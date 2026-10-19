# Spec 418: MediaMTX list reads stop at twenty pages and five seconds, and a configured-paths page without an items array warns

**Feature Branch**: `fix/3021-list-read-cap-deadline-warning` (cut from `origin/develop` at `398e61a0`)

**Created**: 2026-10-19

**Status**: Specified, planned and tasked. No `[NEEDS CLARIFICATION]` remains. Judgement calls are
marked **[ASSUMPTION]** in §7.

**Input**: Issue [#3021](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/3021) items
2, 3 and 4, under the owner's decisions recorded on the issue (comment 2026-10-19T19:02Z), which
answer spec 410 §8 Q1-Q3:

- **Q1 (item 2)**: the page cap is **20 pages**, one value for all three list reads.
- **Q2 (item 3)**: a **whole-read deadline of 5 s** covering the whole paged read; expiry **fails**
  the read with `HttpRequestException(HttpRequestError.InvalidResponse)`, not a partial result;
  applies to all three reads.
- **Q3 (item 4)**: a configured-paths page whose `items` is absent or not an array **warns**.
- **Q4 (spec 002 FR-008)** is **not** decided and is out of this spec.

**Lane.** Autonomous (ADR-0144). **Behaviour-changing.** Phase 4a is **red** (ADR-0139,
constitution §Testing). The lane implements these decisions; it makes none. Every number in this
spec is the owner's (20, 5 s) or already in the repo (the 10-minute repeat of spec 409's
`WarningRepeatGate`). No ADR. No configuration knob: both figures are constants, as `MaxListPages`
is today and as spec 388 §5 kept it ("making the 1,000 pages configurable" is out of scope).

**Existing tests that pin the old behaviour are edited, by owner decision.** Spec 410 §2 recorded
that lowering the cap means editing the "exactly 1,000 pages" assertions of specs 388/391, which the
lane may not do without the owner. The owner has now decided; those edits are the red half of this
change, not a weakened gate. The same holds for the one test that asserts a configured page without
`items` is silent (§2). Every other existing test in the touched area must pass **unmodified**.

**Spec number.** 418. Checked 2026-10-19 after `git fetch`: `origin/develop` tops out at 417; no
remote head holds `specs/418-*` or `specs/419-*`; the only other worktree is the main checkout on
`develop`; the one open PR (#2811, devcontainer) holds no spec.

**ADRs.** ADR-0144 / ADR-0139 (lane, red colour). ADR-0171 (every MediaMTX control-API call spends a
permit of the shared `api` partition of `/streams/authorize`; this is why the cap matters).
ADR-0050 (`[LoggerMessage]` source-gen, three new events). ADR-0049 (`CancellationToken` passthrough).
ADR-0052 / ADR-0053 / ADR-0054 (xUnit + Shouldly, sentence-style names, hand-written fakes; no
`FakeTimeProvider`). ADR-0084 (advisory metrics; `MediaMtxRtspGateway.cs` is already over 300 LOC,
so new code goes in the `PageReading` partial). ADR-0087 (each commit builds on its own). ADR-0105
(`Ensure.That` guards). Specs implemented rather than re-decided: **388** (the cap and its stop
rules), **391** (bounded readers, health read fails rather than returns part), **405** (one loop,
`ReadPagesAsync`), **409** (the configured-sources read throttle `MediaMtxConfiguredSourcesReadWarnings`;
FR-104/FR-113/FR-114: no remote text in a built exception, cancellation passes through, no
`InnerException`), **410** (§8 Q1-Q3), **415** (`EnsureSuccess` carries no reason phrase).

**Latency budget (constitution §IV)**: **N/A.** All three reads are the control plane (health sweep,
startup reconcile, source repair). None is on the event-to-overlay path; `StreamHealthChangedV1` is
not an overlay input.

**Security review**: not required. No trust boundary, scope, secret or auth rule changes. The change
*reduces* the `api` partition spend a hostile MediaMTX can force; the new messages carry no MediaMTX
text (FR-305).

---

## 1. Premise check (develop at `398e61a0`)

| # | Item | Holds? | Evidence |
|---|---|---|---|
| 2 | Page cap is 1,000 | **Holds** | `MediaMtxRtspGateway.cs:21` `private const int MaxListPages = 1000;` — one constant, one loop (`ReadPagesAsync`, `MediaMtxRtspGateway.PageReading.cs:10-44`) shared by `GetAllPathsHealthAsync`, `ListConfiguredPathsAsync`, `ListConfiguredPathSourcesAsync` (spec 405 removed `MaxConfiguredSourcePages`; `grep MaxConfiguredSourcePages src` is empty). At the cap: health read throws `InvalidResponse` "still reported more pages after 1000 pages" (`CompleteHealthOrThrow`, `.cs:223-236`); the configured-paths read returns what it read and Warns `MediaMtxConfiguredPathsPageCapReached` (unthrottled, runs once per startup); the configured-sources read returns what it read and Warns `MediaMtxConfiguredSourcesPageCapReached` through spec 409's throttle (`WarnOnUnfinishedRead`, `PageReading.cs:196-220`). `api` partition: `MediaMtxApiAuthorizeRateLimiting:PermitLimit` default 2000 / 1 min (`StreamDistribution/Api/Program.cs:41-44`). |
| 3 | No whole-read deadline | **Holds** | `ReadPagesAsync` passes the caller's token straight to each `http.GetAsync`; no linked CTS, no timer anywhere in the gateway. Each page GET runs under the ServiceDefaults standard resilience handler (10 s per attempt, retries for idempotent GET, 30 s total, spec 410 §1 row 3). Worst case of one read today: 1,000 pages x 30 s. |
| 4 | Configured page with absent / non-array `items` is silent | **Holds** | `AddPageToPaths` / `AddPageToSources` (`PageReading.cs:128-191`) return `false` when `items` is absent or not an array, exactly as for an empty array; `ReadPagesAsync` maps both to `PageReadEnd.Complete` (`PageReading.cs:19-22`), so neither caller logs. Pinned silent by `MediaMtxRtspGatewayPageShapeTests.An_object_page_without_items_still_ends_the_configured_reads_normally` (asserts no Warning). The health read already **fails** on the same shape (`GetAllPathsHealthAsync`'s `itemsUnreadablePage`, `.cs:195-218`); that stays as is. |
| Q4 | Spec 002 FR-008 | **Out** | Not decided by the owner. Untouched; reported, not built. |

**Callers, as they stand (unchanged by this spec):**

- Health: `StreamHealthWatcher.DispatchAsync` catches `HttpRequestException` → `HealthSweepFailed`
  (Warning, unthrottled, one per tick), dispatches nothing that tick. Ticks never overlap:
  `PollInterval` (2 s) is a `Task.Delay` *after* each tick completes (`StreamHealthWatcher.cs:39-63`).
- Startup reconcile: `ReconcileOnceAsync` lets the read's exception escape to `RunDeferredAsync`,
  which logs `ReconcilerStartupPassFailed` once (unless the host is stopping); the next restart retries.
- Source repair: `MediaMtxSourceRepair.RepairAsync` lets the read's exception escape; the periodic
  `MediaMtxSourceRepairSweep.RunPassAsync` logs it through its own `WarningRepeatGate<SweepFailure>`
  (keyed on type, status, `HttpRequestError`; 10-minute repeat); the startup call logs
  `ReconcilerStartupSourceRepairFailed` once.
- Every caller filters cancellation on its own token, so a caller-cancelled read must stay an
  `OperationCanceledException` (spec 409 FR-113).

## 2. Behaviour-changing vs behaviour-preserving

| Part | Colour | Existing tests edited (owner-authorised) |
|---|---|---|
| Cap 1,000 → 20 | behaviour-changing → **red** | the seven assertions pinning 1,000 (tasks.md T001) |
| 5 s whole-read deadline | behaviour-changing → **red** | none (new tests) |
| Configured page without an items array warns | behaviour-changing → **red** | `An_object_page_without_items_still_ends_the_configured_reads_normally` (asserts silence) |
| Health read on a page without items; caller cancellation; empty-page end; non-object page / element failures; reason-phrase rule | preserved → **characterisation** | none; must pass unmodified |

## 3. User stories

All three stories live in one class pair (`MediaMtxRtspGateway` + `.PageReading`), so they ship as
one slice: three red/green commit pairs in one PR. Each story is still independently observable.

### US1 (P1): a list read issues at most 20 page requests

*As the operator of a fab, I need a hostile or broken MediaMTX that keeps reporting more pages to
cost at most 20 control-API calls per read, so the health sweep cannot spend the whole `api`
rate-limit partition and starve provisioning, repoint, retire and source repair.*

**Why P1**: it is the permit budget. At 20 pages the worst case is 20 GETs per health tick (≤ 600/min
at the 2 s cadence) plus 20 per 10 s repair pass (120/min): ≤ 720 of the 2,000/min partition, before
transient-failure retries, against "the whole partition" today. 250 cameras need 3 pages of 100.

**Independent test**: a stub MediaMTX answering every page with one item and `pageCount` 2147483647;
count requests per read.

```gherkin
Scenario: the health read fails at the cap rather than returning part of the list
  Given MediaMTX answers every /v3/paths/list page with one item and pageCount 2147483647
  When the gateway reads all paths' health
  Then exactly 20 pages are requested, pages 0 to 19
  And the read throws HttpRequestException with HttpRequestError InvalidResponse and no status code
  And the message says "still reported more pages after 20 pages"
  And nothing is logged at Warning by the gateway

Scenario: the startup reconcile's read stops at the cap and warns once
  Given MediaMTX answers every /v3/config/paths/list page with one item and pageCount 2147483647
  When the gateway lists configured paths
  Then exactly 20 pages are requested and 20 paths are returned
  And exactly one Warning MediaMtxConfiguredPathsPageCapReached says "20"

Scenario: the source repair's read stops at the cap and warns through the existing throttle
  Given the same configured-paths stub
  When the gateway lists configured path sources
  Then exactly 20 pages are requested and 20 sources are returned
  And exactly one Warning MediaMtxConfiguredSourcesPageCapReached says "after 20 pages"

Scenario Outline: the cap warns only when MediaMTX still reports more pages (boundary)
  Given every configured-paths page holds one item and pageCount is <pageCount>
  When the gateway lists configured path sources
  Then <requests> pages are requested and <warnings> Warning is logged
  Examples:
    | pageCount | requests | warnings |
    | 19        | 19       | 0        |
    | 20        | 20       | 0        |
    | 21        | 20       | 1        |

Scenario: the health sweep loses the tick at the cap
  Given the health stub of the first scenario behind the real StreamHealthWatcher
  When one sweep runs
  Then 20 pages are requested, HealthSweepFailed is logged once and no camera is dispatched
```

Bad-request / auth scenarios: **N/A** (no inbound request; the reads are outbound control-API
calls whose auth, ADR-0171's Basic credential, is unchanged).

### US2 (P1): a list read that takes longer than 5 s fails

*As the operator, I need one stalled or slow-dripping MediaMTX to cost a health tick at most 5 s,
reported as a failure, rather than silently holding the sweep for minutes with nothing logged.*

**Independent test**: a stub that answers page 0 and holds page 1 open until cancelled, a
timer-capable hand-written `TimeProvider`, and the clock advanced to 5 s.

```gherkin
Scenario Outline: a read still running at 5 s fails as an invalid response
  Given MediaMTX answers page 0 of <endpoint> with pageCount 3 and holds page 1 open
  And the read has been running for 5 seconds on the gateway's TimeProvider
  When <reader> is awaited
  Then it throws HttpRequestException with HttpRequestError InvalidResponse
  And StatusCode is null and InnerException is null
  And the message names <endpoint> and the 5 s deadline
  And no partial result is returned
  Examples:
    | reader                          | endpoint               |
    | GetAllPathsHealthAsync          | /v3/paths/list         |
    | ListConfiguredPathsAsync        | /v3/config/paths/list  |
    | ListConfiguredPathSourcesAsync  | /v3/config/paths/list  |

Scenario: the deadline covers the whole read, not each page
  Given every page of a 10-page list answers, each taking 2 s on the gateway's TimeProvider
  When the health read runs
  Then it throws HttpRequestException InvalidResponse before page 4 is requested

Scenario: a read that finishes inside the deadline is unaffected
  Given page 1 is held open and the clock advances 4.9 s, then page 1 answers as the last page
  When the health read is awaited
  Then it returns every path of both pages and nothing is thrown

Scenario: a caller's cancellation is still a cancellation (conflict with the deadline)
  Given a list read in progress and the caller's token cancelled before the deadline
  When the read is awaited
  Then it throws OperationCanceledException, not HttpRequestException

Scenario: the deadline reaches the gateway through the DI registration
  Given a timer-capable TimeProvider registered before AddMediaMtxGateway
  And the gateway resolved from that registration over a stub that holds page 0 open
  When the clock advances 5 s
  Then the read throws HttpRequestException InvalidResponse
```

### US3 (P2): a configured-paths page without an items array is reported

*As the operator, I need a configured-paths page that is not shaped like a list to be visible in the
logs, because both consumers then act on part of the list (the startup reconcile removes orphans
only among paths it read; the repair compares only those) and today nothing says so.*

**Why P2**: both consumers are already fail-safe; this is visibility, not correctness.

```gherkin
Scenario Outline: a page without an items array ends the read with one Warning naming the page
  Given configured-paths page 0 holds one path, pageCount 5, and page 1 is <malformed>
  When the gateway lists configured path sources
  Then pages 0 and 1 are requested and the one path of page 0 is returned
  And exactly one Warning MediaMtxConfiguredSourcesItemsUnreadable says "page 1"
  And the Warning carries no text from the page body
  Examples:
    | malformed                                 |
    | {"pageCount":5}                           |
    | {"pageCount":5,"items":null}              |
    | {"pageCount":5,"items":{"name":"x"}}      |
    | {"pageCount":5,"items":"[]"}              |

Scenario: the startup reconcile's read warns too, unthrottled like its siblings
  Given configured-paths page 0 is {"pageCount":5}
  When the gateway lists configured paths
  Then no path is returned and exactly one Warning MediaMtxConfiguredPathsItemsUnreadable says "page 0"

Scenario: the source repair's Warning repeats at most every 10 minutes (spec 409 throttle)
  Given a gateway sharing one MediaMtxConfiguredSourcesReadWarnings over a manual clock
  And every read meets a page without an items array
  When it reads at t=0, t=9m59s and t=10m
  Then the log levels are Warning, Debug, Warning

Scenario: a complete read re-arms the throttle
  Given an items-unreadable read, then a complete read, then an items-unreadable read, within one minute
  Then two Warnings are logged

Scenario: a changed outcome warns at once
  Given a read that reaches the page cap, then a read with an unreadable items page, within one minute
  Then two Warnings are logged, MediaMtxConfiguredSourcesPageCapReached then MediaMtxConfiguredSourcesItemsUnreadable

Scenario: an empty items array is still the normal end (guard)
  Given page 1 is {"pageCount":5,"items":[]}
  When either configured read runs
  Then nothing is logged at Warning

Scenario: the health read on the same shape still fails, with its existing message (guard)
  Given /v3/paths/list page 1 has no items array
  When the health read runs
  Then it throws InvalidResponse saying "page 1" and "no readable items array", as today
```

## 4. Functional requirements

- **FR-301** `MaxListPages` is **20**. It stays one `private const int` used by all three reads.
  Every stop rule is unchanged: empty page, absent / non-numeric `pageCount`, `page + 1 >= pageCount`,
  unreadable `pageCount`, cap. At the cap each reader keeps its current outcome (health throws;
  configured-paths returns partial + unthrottled Warning; configured-sources returns partial +
  throttled Warning). Messages interpolate the constant, so they now say 20.
- **FR-302** One whole-read deadline of **5 s**, a `private static readonly TimeSpan` constant (no
  options, no configuration), starts when `ReadPagesAsync` begins and covers every page request,
  every body read, every resilience-handler retry and retry delay inside it.
- **FR-303** If the deadline fires while the caller's token is **not** cancelled, the read throws
  `new HttpRequestException(HttpRequestError.InvalidResponse, <message>)`: `StatusCode` null,
  **no `InnerException`** (spec 409 FR-114's rule for exceptions this gateway builds), no partial
  result. All three readers fail; none returns what it had read.
- **FR-304** If the caller's token is cancelled, an `OperationCanceledException` passes through
  unchanged, including when the deadline has fired as well (the caller's token is checked first).
  Spec 409 FR-113 and spec 415's cancellation tests hold unmodified.
- **FR-305** The deadline message is fixed text plus values this service owns: the list path, the
  deadline in seconds and the zero-based page being read, formatted with the invariant culture.
  It carries nothing MediaMTX sent (spec 409 FR-104, spec 415).
- **FR-306** The deadline is measured on the gateway's `TimeProvider`: `TimeProvider.System` by
  default and in production, the DI-registered one when resolved through `AddMediaMtxGateway`
  (which already `TryAddSingleton(TimeProvider.System)`).
- **FR-307** A configured-paths page (`/v3/config/paths/list`) whose payload is a JSON object with
  `items` absent or not a JSON array ends the read at that page, keeping the items read so far
  (unchanged), and now records a new end, `PageReadEnd.ItemsUnreadable`, at that page index. An
  `items` that is an empty array is still `Complete` and silent.
- **FR-308** `ListConfiguredPathsAsync` logs a new Warning `MediaMtxConfiguredPathsItemsUnreadable`
  (page) once per read, unthrottled, like its two siblings (the startup read runs once).
- **FR-309** `ListConfiguredPathSourcesAsync` routes `ItemsUnreadable` through the existing
  `WarnOnUnfinishedRead` and spec 409's `MediaMtxConfiguredSourcesReadWarnings` gate: first or
  changed outcome → Warning `MediaMtxConfiguredSourcesItemsUnreadable` (page); the same outcome
  within 10 minutes → Debug `MediaMtxConfiguredSourcesItemsStillUnreadable` (page); a `Complete`
  read clears the gate. **No new throttle, interval or number.**
- **FR-310** The health read is unchanged: a runtime page without an items array still throws its
  existing message from `GetAllPathsHealthAsync`, before `read.End` is consulted.
- **FR-311** No caller changes (`StreamHealthWatcher`, `MediaMtxReconciler`, `MediaMtxSourceRepair`,
  `MediaMtxSourceRepairSweep`). Their existing catches and throttles absorb the new failure.

## 5. Edge cases

- **Exactly 2,000 paths** (20 full pages, `pageCount` 20): complete, silent. **2,001**: the cap
  (health fails every tick; configured reads partial + Warning). Far above the 250-camera target.
- **Deadline vs per-attempt timeout.** The resilience handler's attempt timeout is 10 s; the 5 s
  deadline is shorter, so one page attempt slower than 5 s now fails the read where it used to
  succeed, and retries never get past the deadline. This is the owner's trade (Q2).
- **Deadline vs the 2 s health tick.** No overlap is possible (the delay follows the tick). A
  persistently slow MediaMTX now yields one `HealthSweepFailed` Warning per ~7 s (5 s + 2 s). That
  Warning is unthrottled today; spec 410 §8 Q2 named exactly this consequence ("health:
  `HealthSweepFailed`, one Warning per tick") and the owner chose fail. Throttling it is #3013's
  open question, not this spec's.
- **Deadline at the startup reconcile.** The read throws, so the whole startup pass (orphan removal,
  re-add, the startup source repair) is skipped and logged once as `ReconcilerStartupPassFailed`,
  as for any other read failure today. The periodic source repair still runs every 10 s.
- **Deadline fires after the last page has been folded.** The read has already returned; nothing is
  thrown (the deadline is not re-checked after a complete read).
- **Deadline fires between two pages.** The next `GetAsync` observes the cancelled token at once;
  same mapping.
- **Circuit breaker.** A request cancelled by the deadline surfaces as `OperationCanceledException`
  inside the pipeline, which the standard breaker does not count **[ASSUMPTION A2]**.
- **An items-unreadable page that is page 0** returns an empty list; for the startup reconcile that
  means no orphan is removed and every expected path is re-added idempotently (unchanged).
- **Throttle key.** `MediaMtxConfiguredSourcesReadWarnings` keys on `PageReadEnd` only, not the page
  index; an unreadable page moving from page 1 to page 2 is the same outcome (Debug). Matches the
  existing `PageCountUnreadable` behaviour.

## 6. Locked tech choices

.NET 10; `CancellationTokenSource(TimeSpan, TimeProvider)` linked with the caller's token via
`CancellationTokenSource.CreateLinkedTokenSource`; `[LoggerMessage]` (ADR-0050); xUnit + Shouldly,
hand-written stubs and a hand-written timer-capable `TimeProvider` (ADR-0052/0054: no
`FakeTimeProvider`); no Testcontainers (ADR-0103; these are unit tests). No new package.

## 7. Assumptions

- **[ASSUMPTION A1] (FR-303) No `InnerException` on the deadline failure.** The cancellation's own
  exception carries only framework text, so including it would be harmless; it is left out to keep
  one rule for every exception the gateway builds (spec 409 FR-114) and because the message already
  states the cause.
- **[ASSUMPTION A2] (§5)** The standard resilience handler's circuit breaker does not count an
  outer-token cancellation as a failure. The spec does not depend on it; if it were wrong, a stall
  would open the breaker sooner, which fails safe.
- **[ASSUMPTION A3] (FR-307)** "Configured-paths page" means both readers of
  `/v3/config/paths/list` (startup reconcile and source repair), since both read the same page shape
  and the owner's Q3 named the page, not a caller.
- **[ASSUMPTION A4] (FR-306)** A fourth constructor parameter (`TimeProvider`) on the
  `[ActivatorUtilitiesConstructor]` constructor is the seam; the two- and three-argument constructors
  keep compiling by chaining to `TimeProvider.System`, so the 14 existing direct constructions are
  untouched.

## 8. Out of scope (reported)

- **Q4 / spec 002 FR-008** (sustained ≥ 10 s before `Healthy -> Degraded`): not decided.
- Throttling `HealthSweepFailed` (#3013, open; spec 415 §5 dropped it for want of a number).
- Making the cap or the deadline configurable; a different cap per reader.
- Issue #3021 item 1 (spec 405) and item 5 (spec 410) are delivered.

## 9. Neighbours

- #3013 / specs 409, 415: same files (`PageReading.cs`, `Log.cs`); both merged. No open branch
  touches them.
- #3029 (S138 on `GetAllPathsHealthAsync`): untouched; this spec does not edit that method.
