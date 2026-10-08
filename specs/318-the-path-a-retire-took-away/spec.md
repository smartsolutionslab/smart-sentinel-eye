# Spec 318 — The path a retire took away

**Issue:** [#2743](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2743)
— *A retire racing a redelivered provision can re-add a path the retire just removed*. Labels
`agent:ready`; Project #13, status **In Progress** (verified 2026-10-08,
`gh issue view 2743 --json projectItems`).
**Branch:** `fix/2743-retire-provision-race` (cut from `origin/develop` @ `ba37ca5a`)
**Created:** 2026-10-08
**Lane:** autonomous (ADR-0144).

**Spec number.** On 2026-10-08 `origin/develop` tops out at **315**. Local, unmerged branches
already claim **316** (`feat/316-operator-mfe-shell-and-first-remote`) and **317** three times
(`feat/2325-…`, `fix/2077-…`, `fix/2170-…`). **318** is unclaimed on every local and remote branch
today. Re-check after every parked-PR merge and immediately before opening the PR (memory: *spec
number: origin/develop isn't enough*).

**The design decision was taken by a human before this spec**, recorded on the issue (2026-10-08):

> Fix with optimistic concurrency — add a concurrency token on the stream row so a stale redelivery
> loses the race against a retire instead of silently re-adding the removed path.

This spec decides nothing beyond that. It pins where the race window is (§1.1), why the token that
already exists does not close it on its own (§1.2), the one form of the check that closes it
without creating a worse failure (§1.3), and what the losing provision does (§1.4).

## 1. The issue's premise, re-checked on this tree (`ba37ca5a`)

### 1.1 Confirmed — the window

`ProvisionStreamCommandHandler.HandleAsync`
(`src/StreamDistribution/Application/Commands/Handlers/ProvisionStreamCommandHandler.cs`):

| Step | Line | What |
|---|---|---|
| P1 | :40 | `GetByCameraAsync` — reads the row. |
| P2 | :47-50 | Row `Retired` → `Success`, no gateway call. |
| P3 | :57 → :78 | Row not `Retired` → `RegisterPathAsync` → `rtsp.AddPathAsync`. **Nothing is written.** |
| P3′ | :65-69 → :78 | No row → `Stream.Provision` → `SaveAsync` (insert) → `AddPathAsync`. |

`RetireStreamCommandHandler.HandleAsync` (`…/RetireStreamCommandHandler.cs`): R1 `GetByCameraAsync`
(:23) → R2 `stream.Retire` (:33) → R3 `SaveAsync` (:42, commits `Retired`) → R4
`rtsp.RemovePathAsync` (:46).

The bad interleaving is **P1 → R1…R4 → P3**: the provision read a live row, the retire committed and
removed the path, then the provision — past its only read — re-adds the path. Confirmed by reading;
the provision never re-consults the row after P1. The same window exists on the **first** delivery
too (P3′: the insert commits, a retire arriving right behind it retires the row and removes the
path before P3′'s `AddPathAsync`), so the issue's "redelivered" is the common case, not the only
one.

**Impact, confirmed:** `AuthorizeWhepCommandHandler` does not consult `StreamState` (no `Retired`
reference in the file), so a re-added path for a `Retired` row is watchable by that row's fab until
`MediaMtxReconciler` removes it at the next StreamDistribution start
(`MediaMtxReconciler.cs:16-22`). Not cross-fab — the row stays fab-scoped — exactly as the issue
says.

### 1.2 The concurrency token already exists, and cannot fire here

The issue's decision says "add a concurrency token". **There already is one**, fully wired:

- `AggregateRoot.Version` (`src/Shared.Kernel/AggregateRoot.cs:16`), mapped
  `.IsConcurrencyToken()` on the `version` column
  (`src/StreamDistribution/Infrastructure/Persistence/Configurations/StreamConfiguration.cs:102-106`).
- `AggregateVersionInterceptor` (`src/ServiceDefaults/Persistence/AggregateVersionInterceptor.cs`)
  is registered for this context (`StreamDistributionPersistenceModule.cs:30`), so the retire's
  `UPDATE` at R3 is `… WHERE version = @loaded` and bumps it (ADR-0113 Layer 2).

It does not help because **the provision writes nothing on P3**. EF puts a concurrency token only in
the `WHERE` of a write it issues, and the interceptor bumps only roots that are `Modified` or have a
dirty owned child (`AggregateVersionInterceptor.cs:79-90`). An unmodified, tracked row produces no
statement at all. No column, attribute or migration changes that — the fix is to make the provision
**consult the token after its side effect**, not to add another token. **No migration.**

### 1.3 The form of the check — why not the standard Layer-2 write

Three shapes were considered; only one closes the window without opening a worse one.

1. **Check before `AddPathAsync`** (bump-write or re-read, then add). Does not close it: the retire
   can commit and remove the path between the check and the add — the same window, moved.
2. **A version-bumping write after `AddPathAsync`** (mark the row modified so the interceptor bumps
   it). Closes the provision's side, but makes the provision a **competing writer**: in the
   interleaving P1, R1, P3, *P-confirm commits v+1*, R3, the **retire** now fails with
   `DbUpdateConcurrencyException`. ADR-0113 forbids retrying a conflicting write ("automatic retry
   is forbidden in both the backend and the SPAs"), and spec 296 §1.10 applied that to Wolverine
   message handlers (`WallSceneSwitchFailurePolicy.cs:35` dead-letters the loser). A dead-lettered
   retire leaves the stream live and the path reachable **permanently** — strictly worse than the
   bounded window this issue is about. Rejected.
3. **A non-bumping version assertion after `AddPathAsync`** — a conditional `UPDATE streams SET
   version = version WHERE stream_id = @id AND version = @loaded`, reporting whether a row matched.
   - It is optimistic and uses the existing token as its predicate (the human decision).
   - It changes no column, so a retire that loaded the same version still commits — **the retire
     can never lose to a provision**.
   - It is an `UPDATE`, so in PostgreSQL READ COMMITTED it **waits on the row lock of any
     uncommitted writer** and re-evaluates its predicate after that writer commits. So a retire
     whose `UPDATE` preceded the assertion is committed by the time the assertion answers, whether
     or not the retire's transaction stays open across its `RemovePathAsync` (§9 A1). A plain
     re-read would not wait, and would miss exactly that case.
   **Chosen.**

**Correctness argument (the gate reviewers should check).** Let A be the provision's `AddPathAsync`
and V its assertion, A before V. If V matches, no retire's `UPDATE` preceded V, so every retire's
`UPDATE` — and the `RemovePathAsync` after it — comes after V, hence after A: the removal wins. If V
does not match, some write committed since P1; the provision then reads the committed state (§1.4).
If that is `Retired`, the removal may have preceded A, so the provision removes the path itself. If
it is not `Retired`, the write was a health report or re-point, and any later retire's `UPDATE` (and
removal) follows V, hence A: the path is still wanted.

### 1.4 What the losing provision does

| Assertion | Committed state read after it | Provision does | Result |
|---|---|---|---|
| matches | — | nothing more | `Success(id)` (unchanged) |
| does not match | `Retired` | `RemovePathAsync` (compensation; 404 tolerated, `MediaMtxRtspGateway.cs:76-80`) | `Success(id)` |
| does not match | `Retired`, compensation throws `HttpRequestException` | logs | `Failure(RtspGatewayUnavailable)` (503) |
| does not match | not `Retired` | nothing more — the version moved for a health report or re-point | `Success(id)` |

**Why `Success` for the loser, not a failure:** the command's intent is "provision this camera's
stream unless it is retired" — P2 (`:47-50`) already answers a retired row with `Success(id)`. A
provision that learns *late* that the row is retired reaches the same outcome by a longer route.
Returning a failure would make `CameraRegisteredIntegrationEventHandler` re-throw (`:52-58`) and
Wolverine redeliver — an automatic retry triggered by a lost race, which ADR-0113 forbids, and
which would only replay P2.

**Why not dead-letter like spec 296:** there the loser *was* the write, so not retrying meant
dropping it. Here the provision's loss is detected **before** anything it wrote is wrong, and it can
finish correctly in-process by undoing its own side effect. No retry, no dead letter.

**Why the health watcher matters to the design:** `ReportStreamHealthCommandHandler` saves on every
sweep, and `ReportHealthy` sets `LastSuccessAt` each time (`Stream.cs:211-214`), so a healthy row's
version moves roughly every two seconds. A version mismatch therefore does **not** imply a retire;
compensating on mismatch alone would tear down live streams. That is why the mismatch branch reads
the committed state before acting.

**Compensation failure** has no in-process recovery; US2 makes the redelivery that follows the
503 finish it.

## 2. User stories

### US1 (P1): A retired camera stays unwatchable even if a provision was in flight

As a fab operator, I need a camera I retired to stay off the wall, so a registration that happened to
be in flight — or redelivered — when I retired it cannot quietly make its video watchable again.

**Why P1:** it is the issue.
**Independent test:** §7 steps 1-4.

### US2 (P2): A redelivery onto a retired stream finishes its teardown

As a fab operator, I need any MediaMTX path a retired camera still has to be removed the next time
StreamDistribution hears about that camera, so a compensation that failed (or a crash between the
add and the assertion) is not left for the next restart.

**Why P2, and why in this feature:** US1 ships without it, but US1 introduces the compensation call,
and its failure path (§1.4 row 3) would otherwise have no recovery short of a restart. One gateway
call in the branch P2 already has; separable into its own commit.
**Independent test:** §7 step 5.

## 3. Acceptance scenarios

```gherkin
Feature: A retire always wins over a concurrent or redelivered provision

  Background:
    Given a camera registered in fab "munich" with RTSP source "rtsp://10.0.5.1/h264"

  # Happy path — unchanged
  Scenario: Provisioning with no concurrent writer
    When StreamDistribution provisions the camera's stream
    Then MediaMTX has path "cam-<camera>"
    And the stream row's version is unchanged by the provision's check
    And the result is the stream's identifier

  # Conflict: the race in the issue (existing row, redelivery)
  Scenario: A retire commits and removes the path while a redelivered provision is past its read
    Given the camera's stream exists and is Healthy
    And a redelivered CameraRegisteredV1 has read the row as not Retired
    When the retire commits Retired and removes "cam-<camera>"
    And the provision then adds "cam-<camera>"
    Then the provision's version check does not match
    And the provision reads the committed state as Retired
    And the provision removes "cam-<camera>"
    And MediaMTX does not have path "cam-<camera>"
    And the provision's result is Success with the stream's identifier
    And the stream row is still Retired

  # Conflict: the same race on the first delivery
  Scenario: A retire lands between the provision's insert and its path registration
    Given no stream exists for the camera
    When the provision inserts the row
    And a retire commits Retired and removes "cam-<camera>"
    And the provision then adds "cam-<camera>"
    Then MediaMTX does not have path "cam-<camera>"
    And the provision's result is Success with the stream's identifier

  # Conflict: the retire's transaction is still open when the provision checks
  Scenario: The provision's check waits for an uncommitted retire
    Given a retire has updated the row to Retired but not yet committed
    When the provision's version check runs
    Then the check does not answer until the retire commits
    And then it does not match

  # Conflict: the provision must never make the retire lose
  Scenario: A retire that read the row before the provision's check still commits
    Given the provision and a retire have both read the row at the same version
    When the provision's version check matches
    And the retire then saves Retired
    Then the retire's save succeeds without a concurrency conflict

  # Conflict: a version moved by something other than a retire
  Scenario: A health report moved the version during the provision
    Given the camera's stream exists and is Healthy
    When a health report commits between the provision's read and its check
    Then the provision's version check does not match
    And the provision reads the committed state as Healthy
    And the provision does not remove "cam-<camera>"
    And MediaMTX has path "cam-<camera>"
    And the result is Success

  # Failure: compensation cannot reach MediaMTX
  Scenario: The compensating removal fails
    Given the provision lost the race to a retire
    And MediaMTX refuses the removal
    When the provision compensates
    Then the result is RtspGatewayUnavailable (503)
    And the failure propagates so the outbox redelivers

  # US2: redelivery finishes the teardown
  Scenario: A redelivery for a retired stream removes any leftover path
    Given the camera's stream is Retired
    And MediaMTX still has path "cam-<camera>"
    When CameraRegisteredV1 is redelivered
    Then MediaMTX is asked to remove "cam-<camera>"
    And MediaMTX is not asked to add "cam-<camera>"
    And the result is the retired stream's identifier

  Scenario: A redelivery for a retired stream when MediaMTX is down
    Given the camera's stream is Retired
    And MediaMTX refuses the connection
    When CameraRegisteredV1 is redelivered
    Then the result is RtspGatewayUnavailable (503)

  # Bad request: unchanged
  Scenario: A blank RTSP source
    When StreamDistribution provisions with RTSP source "   "
    Then the result is InvalidRtspSource
    And MediaMTX is not called

  # Auth: unchanged, and stated so nobody "fixes" it here
  Scenario: The WHEP hook still does not consult stream state
    Given a stream that is Retired
    Then AuthorizeWhepCommandHandler and its tests are byte-identical to develop
```

## 4. Scope

### 4.1 In

- `ProvisionStreamCommandHandler.RegisterPathAsync`: the post-add assertion and its §1.4 branches
  (US1); the `Retired` branch at `:47-50` removes the path (US2).
- `IStreamRepository` + `StreamRepository`: two members (plan §3).
- Two `[LoggerMessage]` entries in `src/StreamDistribution/Application/Log.cs`.
- Tests per §6; the `InMemoryStreamRepository` fake models a committed version.

### 4.2 Out, and why

- **`RetireStreamCommandHandler`** — untouched. Its write is already version-checked, and the
  design deliberately keeps it unable to lose to a provision.
- **A retire losing to a health-watcher write.** Both bump the version, so today a retire racing a
  sweep can already throw `DbUpdateConcurrencyException` and fall to Wolverine's unpinned default
  (spec 296 §1.10: unverified on WolverineFx 6.40.0). Pre-existing, not widened here (the
  assertion bumps nothing), and fixing it needs a decision on retrying a conflicting terminal write
  under ADR-0113. **Recommend a follow-up issue** (§9 R2); not built here.
- **`MediaMtxReconciler`** — its startup pass has a similar read-then-act shape against concurrent
  retires; it runs once per start and is out of the issue. Noted, not changed.
- **`RepointStreamCommandHandler` vs a stale provision** (a provision adding a path with the
  pre-repoint URL while the path is absent) — same family, different issue; §9 R3.
- **`AuthorizeWhepCommandHandler`** — not consulting `StreamState` is the reason the race matters,
  but refusing retired rows in the hook is a different decision nobody took.
- **Any migration, column or token change** (§1.2).
- **A new ADR** (§5).

## 5. No new ADR — with one point flagged for the reviewer

This applies ADR-0113's Layer 2 — the in-transaction concurrency token — to a race between two
message handlers, which is the layer ADR-0113 assigns to "the true race". `If-Match` (Layer 1) is
not involved: nothing here is cross-request and StreamDistribution has no mutating HTTP surface
(ADR-0113 §Scope). No retry is introduced (ADR-0113 correction 2 holds), and the precedent for
"a message handler that loses a Layer-2 race is not retried" is spec 296.

**Flagged, not hidden:** ADR-0113 describes Layer 2 only as a *bumping write* surfaced as
`DbUpdateConcurrencyException`. This spec uses the same token as a **non-bumping assertion** and
answers a loss with **compensation of an external side effect**. Neither is described by the ADR,
and neither contradicts it — §1.3 item 2 shows the literal form would contradict ADR-0113's own
no-retry rule here. The architect judges this an application of the existing decision, scoped to one
handler with no shared mechanism, convention or cross-context effect. If a reviewer judges it an
amendment, the lane must stop (ADR-0144: it may not write an ADR) and hand it back.

## 6. Phase-4a colour: **RED**

Behaviour-changing: today the race re-adds the path; afterwards it does not. New tests must be
observed failing for the stated reason first.

**This is a failure-mode issue (a race), so phase 4a runs `test-adversary` alongside
`test-writer`** (tasks.md). The race is provoked **deterministically**, never by timing: the
retire is run from inside the gateway's `AddPathAsync` hook, which is the window by construction.

**Declared assertion edits:** none. Every existing fact in `ProvisionStreamCommandHandlerTests`,
`RetireStreamCommandHandlerTests`, `CameraRegisteredIntegrationEventHandlerTests` and the
StreamDistribution integration classes is **characterisation** and must pass unmodified —
including `Provision_for_a_retired_stream_does_not_re_register_its_path`, which asserts on
`AddCalls` only and stays true under US2.

## 7. Independent end-to-end test procedure (phase 5)

1. Boot the Aspire stack.
2. Run `ProvisionRetireRaceIntegrationTests` (plan §6.3): real Postgres, real MediaMTX
   `1.21.0-ffmpeg`, the real `StreamRepository`, the retire driven from inside the provision's
   `AddPathAsync`. Quote: MediaMTX answers **404** for `cam-<camera>` afterwards, the row is
   `Retired`, the provision returned `Success`.
3. Run `StreamRepositoryVersionAssertionIntegrationTests` (plan §6.2), including the lock-wait fact.
4. Register a camera via `POST /cameras`, retire it via the camera retire endpoint, then replay its
   `CameraRegisteredV1` (or re-run the redelivery integration fact); confirm the row stays
   `Retired` and `GET {mediamtx:api}/v3/config/paths/get/cam-<id>` answers 404.
5. US2: with the camera retired, `POST {mediamtx:api}/v3/config/paths/add/cam-<id>` by hand to
   plant a leftover path, replay `CameraRegisteredV1`, confirm the path answers 404 afterwards.
6. Quote outputs in the verification note. Latency: **N/A** (§8).

## 8. Latency budget

**N/A.** Provisioning and retirement are control-plane work triggered by camera registration and
retirement; no leg of the event → overlay path (constitution §IV) runs through them. The added
statement is one conditional `UPDATE` per provision (plus one read on a mismatch).

## 9. Risks and assumptions

- **A1 (design is robust to it either way; verify, don't rely):** whether Wolverine's EF
  transaction middleware keeps the retire's transaction open across `RemovePathAsync`. ADR-0088
  documents eager mode; `WolverineDefaults.cs:88` calls `UseEntityFrameworkCoreTransactions()`
  with no mode argument. The assertion is an `UPDATE` precisely so that it waits on the retire's
  row lock in both cases (§1.3 item 3); the lock-wait integration fact proves that property
  directly instead of depending on which mode is live.
- **A2 (verify at phase 4b):** EF Core translates `ExecuteUpdateAsync` with a self-assignment of
  the value-converted `Version` property and an equality predicate on it. If it does not, the
  fallback is `ExecuteSqlInterpolatedAsync` with the same statement — same semantics, same tests.
- **A3 (accepted):** the version also moves for re-points and health reports, so the mismatch
  branch costs a read in those cases. Rare: the window is one MediaMTX `add/` round-trip.
- **R1:** spec-number collision (header). Re-check before the PR.
- **R2 (pre-existing, recommend filing):** a retire can lose to a health-watcher write today
  (§4.2). Distinct from #2743; check the board for an existing issue before filing.
- **R3 (pre-existing, out):** a stale provision racing a re-point while the path is absent can add
  the path with the pre-repoint URL. Rare; noted for a follow-up if anyone observes it.
