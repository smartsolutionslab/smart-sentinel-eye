# Spec 201 — A reservation that lets go

**Issue:** #2290 — *A crashed idempotent attempt wedges its key at 409 forever, and the table has no reaper*
**Branch:** `2290-idempotency-reaper`
**Phase-4a colour:** **RED** (behaviour-changing) — a test that arrives green is a phase-4 failure (ADR-0139).
**ADRs:** **ADR-0142** (idempotency keys — the mechanism being repaired, and the source of the "keys are durable and not swept" constraint that §*Why this is not a TTL* answers), ADR-0143 (retry safety — why `POST` retries reach these endpoints at all), ADR-0103 (integration tests against the Aspire fixture, no Testcontainers), ADR-0105 (`Ensure.That`), ADR-0050 (OpenTelemetry), ADR-0084 (readable parameter lists), ADR-0139 (new behaviour starts red), ADR-0144 (autonomous lane), ADR-0037 (phases), ADR-0109 (parallel markers)
**Severity:** **Permanent loss of service for the recommended usage of the mechanism.** A client that derives its `Idempotency-Key` deterministically from the request — which is what ADR-0142 exists to serve — can never again perform that operation. No API, no log line and no operator action clears the row.

**No new ADR is required.** §*Why this is not a TTL* shows the change lives inside ADR-0142's existing state machine rather than amending it.

## Problem

Verified by reading the files at HEAD `9f192b2b`; every line number below was
re-checked against the working tree, not copied from the issue.

`src/ServiceDefaults/Idempotency/IdempotencyStore.cs:37-74` claims a key with

```sql
INSERT INTO idempotency_key (key, endpoint, caller, reserved_at)
VALUES ({0}, {1}, {2}, NOW())
ON CONFLICT (key, endpoint, caller) DO NOTHING;
```

and, on conflict, reads the row back. A row whose `resource_identifier` is null
is reported `InProgress` (`:68-71`). `IdempotentRequest.ClaimAsync`
(`:143-155`) then polls **10 x 500 ms** and, if the row still has no
identifier, `ExecuteAsync` answers **409 `IDEMPOTENT_REQUEST_IN_PROGRESS`**
(`:92-98`).

**Nothing ever removes a reservation that will never complete.** The only
`DELETE` in the system is `ReleaseAsync` (`IdempotencyStore.cs:99-107`), and it
is reachable only from inside the request that holds the reservation
(`IdempotentRequest.cs:178, 189`). `reserved_at` and its index
`ix_idempotency_key_reserved_at` (`IdempotencyKeyTable.cs:48, 53-56`) have
**zero readers** anywhere in `src/` or `tests/` — re-confirmed by grep at HEAD.
The column and the index were written for a reaper that was never built.

### Two ways a row wedges

1. **The process dies between reserve and complete.** The `INSERT` is committed
   the moment it runs; `CompleteAsync` and `ReleaseAsync` are separate
   statements issued later. A pod restart, an OOM kill, a `k3s` eviction, or a
   stack brought down in the middle of a create leaves the row exactly as the
   `INSERT` left it: identifier null, `completed_at` null, `reserved_at` set.
   Every later request with that key reads `InProgress`, waits five seconds,
   and is refused.

2. **`ReleaseAsync` itself throws** (`IdempotentRequest.cs:172-181`):

   ```csharp
   catch
   {
       await execution.Store.ReleaseAsync(scope, CancellationToken.None);

       throw;
   }
   ```

   If the release throws — and the connection that just failed the work is the
   same connection the release uses, so this is correlated, not independent —
   that exception propagates **out of the catch block** and `throw;` never
   runs. Two things go wrong at once: the caller is told about a database error
   instead of the failure it actually hit, and the reservation is still there.

### What it costs

The blast radius is one `(key, endpoint, caller)` triple, so a human retries
with a different key and moves on. **A client that derives its key
deterministically from the request cannot** — it re-derives the same string
forever. That is the usage ADR-0142 recommends, because it is the only
derivation under which a *transparent* retry (the retry nobody wrote, from the
resilience handler) presents the same key as the original.

**Ten keyed call sites across seven bounded contexts** share this store —
measured at HEAD, not taken from the issue, which says nine:

| Context | Endpoint | Call site |
|---|---|---|
| Automation | `POST /rules` | `RulesEndpoints.cs:327` |
| CameraCatalog | `POST /cameras` | `CameraEndpoints.cs:182` |
| EventIngestion | `POST /events` (manual) | `EventsEndpoints.Writes.cs:98` |
| EventIngestion | `POST /event-types` | `EventTypesEndpoints.cs:120` |
| Identity | `POST /devices/register` | `DevicesEndpoints.cs:138` |
| Identity | `POST /kiosks/enroll` | `KiosksEndpoints.cs:140` |
| Identity | `POST /webhooks/{name}/rotate` | `WebhookRotationEndpoints.cs:158` |
| LayoutComposition | `POST /layouts` | `LayoutEndpoints.Commands.cs:165` |
| OverlayDesigner | `POST /overlays` | `OverlayEndpoints.Commands.cs:56` |
| SystemVariables | `POST /system-variables` | `SystemVariableEndpoints.cs:206` |

The three Identity rows are the ones ADR-0142 was written for, and they are the
worst case: a wedged `POST /devices/register` means a device that can be
neither registered nor re-registered under its deterministic key.

### Why no existing test could have caught this

`tests/ServiceDefaults.Tests/Idempotency/IdempotentRequestTests.cs` drives a
hand-written `RecordingStore`. Its
`A_first_attempt_that_never_lands_is_refused_rather_than_waited_on_forever`
asserts the 409 **as the correct answer** — which it is, for a genuinely live
attempt. The fake never expresses time, so "still running" and "abandoned by a
dead process" are the same state to it.

The three integration suites that touch a real `idempotency_key` table
(`IdempotentCameraRegistrationIntegrationTests`,
`IdempotentRegistrationIntegrationTests`,
`EventTypeRegistryIdempotencyIntegrationTests`) only ever exercise rows their
own request created and completed. None writes a row directly, so none has ever
seen a reservation that outlives its owner.

**The fix therefore cannot be proved by a unit test.** The defect is in one
`ON CONFLICT` clause; a fake store that ignores the SQL proves nothing about
it. US1's red test is an integration test against the Aspire fixture
(ADR-0103).

## Locked technical choices

Nothing here is a new choice; each is the existing repo decision this spec is
bound by.

| Concern | Choice | Source |
|---|---|---|
| Where reservation state lives | `idempotency_key` in **each context's own schema** — no shared database | ADR-0142 §Decision.4 |
| How a claim is made atomic | one `INSERT ... ON CONFLICT` statement, raw SQL, never read-then-write | `IdempotencyStore.cs:22-27` |
| Clock for the staleness comparison | **Postgres `NOW()`**, not the injected `TimeProvider` | the existing SQL already uses it; see plan §*Testability* |
| Test stack | Aspire fixture, no Testcontainers | ADR-0103 |
| Guards | `Ensure.That(x).IsNotNull()` | ADR-0105 |
| Observability | OpenTelemetry; `Activity` for span-scoped failures | ADR-0050, `JourneyOrigin.cs:56` |
| Background workers | `BackgroundService` + `PeriodicTimer` over `TimeProvider`, with a public `RunOnceAsync` tests can drive | `AuditRetentionHostedService.cs:38-58` |

## Latency-budget impact

**N/A.** Every affected path is an HTTP `POST` create or rotation. None of the
six legs in constitution §IV is touched: no camera, SFU, presentation-buffer,
event-to-overlay, or composite work is involved, and §VII's dashboard
obligation is not engaged.

Worth stating because US1 changes a statement on a request path: the claim
stays **one round trip**. `ON CONFLICT DO UPDATE ... WHERE` is the same single
statement the code issues today, with a conflict action attached. No extra
query, no extra connection, no new index lookup.

## Why this is not a TTL

ADR-0142 §Consequences says, in terms:

> Keys are durable and not swept. A create's key is worth keeping for as long
> as the thing it created; a TTL would reintroduce the "same key, different
> answer" hazard the mechanism exists to remove.

**This spec does not weaken that, and reading it as a contradiction is the one
mistake that would turn a fix into a regression.** The sentence is about
**completed** keys — rows carrying a `resource_identifier`, which are exactly
the rows that make a replay possible. Nothing in this spec touches one. Every
statement added here is guarded on `resource_identifier IS NULL`, the same
guard `ReleaseAsync` already carries (`IdempotencyStore.cs:96-102`) and for the
same stated reason: *"so a release arriving late cannot delete a reservation
that has since completed — which would turn a replayable answer back into a
fresh registration."*

What this spec changes is **who** may perform the `absent <- reserved`
transition that ADR-0142's own state table already contains and that
`ReleaseAsync` already performs. Today only the request holding the reservation
may do it. After this, so may a request arriving long enough afterwards that
the holder cannot still be alive. The state machine gains no state and loses
none.

## User stories

### US1 (P1) — A reservation nobody is holding is reclaimed by the next caller

**Why first:** it is the whole of the user-visible harm, and it is
independently shippable. A deterministic-key client recovers on its next
attempt after the bound, with no operator action and no other story merged.

A caller presenting a key whose reservation is **older than the reclamation
bound** and still carries no `resource_identifier` takes the reservation over
and does the work, instead of being refused.

**The bound is 10 minutes.** The asymmetry decides it: too long is an annoyance
(a client waits), **too short is a correctness failure** — a reservation
reclaimed from an attempt that is still legitimately running means the work
executes twice, which is the exact guarantee ADR-0142 sells. So the bound is
set far above any request this system can make, not tightly above the typical
one:

- 120x `IdempotentRequest`'s own 5 s in-progress wait (`InProgressPolls` x
  `InProgressPollInterval`, `:61-64`).
- 60x the *"10 s attempt timeout"* that `:63`'s comment names as the window
  that wait is sized to sit inside.
- The slowest legitimate `work()` among the ten call sites is
  `POST /devices/register`, which creates a Keycloak client and reads its
  secret back — seconds under load, never minutes.
- A client stuck on a deterministic key recovers within one 10-minute window,
  which is inside the patience of both a human operator and a retry loop.

**A reclaim is not a promise about the dead attempt's side effects**, and this
is the honest limit of the story. A process that died mid-`work()` may have
committed part of its work. Re-running is exactly what `ReleaseAsync` already
causes on the exception path, and what protects the caller is the same thing
that protects it there: the domain's own uniqueness rules, which answer `409`
for a genuine duplicate. The key guarantees at most one *live* attempt; it has
never guaranteed anything about a half-applied dead one.

### US2 (P2) — A failed release does not replace the failure that caused it

A request whose work threw, and whose reservation-release then also threw,
surfaces **the original exception**. The release failure is recorded on the
current activity rather than discarded.

**Why second:** its harm is real but narrower — a misleading error, plus a
wedged row that US1 now heals within the bound. It is also in a different file
from US1 (`IdempotentRequest.cs` vs `IdempotencyStore.cs`) with a different
test level, so it runs `[P]` alongside it.

### US3 (P3) — Reservations nobody ever retries do not accumulate

A periodic sweep deletes unfinished reservations older than the same bound, so
the table does not grow without limit in the one case US1 cannot reach: a key
that is never presented again.

**Why last, and why at all.** US1 makes every *retried* wedge self-healing, so
US3 adds no recovery a client can observe. What it adds is a bound on growth,
and one honest structural thing: `ix_idempotency_key_reserved_at` is an index
on `reserved_at` alone, which pays off only for a range scan — US1 finds its
row by primary key and never touches it. **US3 is what makes the index a
reader.** Leaving it unread would preserve the exact "zero readers" signal that
found this defect in the first place. It is shippable on its own and must not
block US1.

## Acceptance scenarios

### US1 — reclaim

```gherkin
Scenario: a key whose reservation outlived its process is reclaimed
  Given the idempotency_key table holds a row for key "K", endpoint
        "POST /cameras" and caller "admin@fab"
    And that row has no resource_identifier and no completed_at
    And its reserved_at is 30 minutes in the past
   When an operator POSTs /cameras with Idempotency-Key "K"
   Then the response is 201 Created
    And the camera is registered
    And the row for "K" carries the new camera's identifier
```

```gherkin
Scenario: a reservation that is still young is still refused
  Given the idempotency_key table holds a row for key "K" with no
        resource_identifier and a reserved_at of 10 seconds ago
   When an operator POSTs /cameras with Idempotency-Key "K"
   Then the response is 409 with title IDEMPOTENT_REQUEST_IN_PROGRESS
    And no camera is registered
```

```gherkin
Scenario: a completed key is replayed no matter how old it is
  Given the idempotency_key table holds a completed row for key "K"
        whose reserved_at is 30 days in the past
   When the same operator POSTs /cameras with Idempotency-Key "K"
   Then the response is 201 Created carrying the original camera identifier
    And no second camera is registered
```

*This is the ADR-0142 §Consequences guard, expressed as a test. It is the
scenario a careless reaper breaks, and it must be asserted against a row aged
far past the bound — a fresh completed row proves nothing about it.*

```gherkin
Scenario: two callers reclaiming one stale reservation, only one wins
  Given a stale unfinished reservation for key "K" and caller "admin@fab"
   When two requests with Idempotency-Key "K" from that caller arrive together
   Then exactly one of them does the work
    And the other is either refused with 409 or replays the first one's answer
    And exactly one camera exists
```

```gherkin
Scenario: a stale reservation is not another caller's to reclaim
  Given a stale unfinished reservation for key "K" and caller "dresden@fab"
   When a different operator POSTs /cameras with Idempotency-Key "K"
   Then that operator's request is processed on its own row
    And it never receives the first caller's answer
```

*The `caller` column is a security boundary, not bookkeeping
(`IdempotencyScope.cs`). Reclamation must not become a way across it.*

```gherkin
Scenario: a malformed key is still rejected before any reclamation happens
   When an operator POSTs /cameras with an Idempotency-Key the boundary refuses
   Then the response is 400
    And no row is inserted, reclaimed or deleted
```

```gherkin
Scenario: an unauthenticated caller reaches none of this
   When a request without a bearer token POSTs /cameras with any
        Idempotency-Key
   Then the response is 401
    And no row is inserted, reclaimed or deleted
```

### US2 — the release that fails

```gherkin
Scenario: a release that throws does not hide the work's failure
  Given an idempotent operation whose work throws InvalidOperationException
    And a store whose ReleaseAsync throws a database exception
   When the request runs
   Then the InvalidOperationException reaches the caller
    And the release failure is recorded on the current activity
```

```gherkin
Scenario: a release that succeeds still frees the key, unchanged
  Given an idempotent operation whose work throws
    And a store whose ReleaseAsync succeeds
   When the request runs
   Then the work's exception reaches the caller
    And the reservation has been released
```

*The second is the characterisation half: US2 must not change the path that
already works. It exists today as
`A_failed_attempt_releases_its_key_so_a_retry_can_claim_it` and must pass
unmodified.*

### US3 — the sweep

```gherkin
Scenario: the sweep removes an unfinished reservation past the bound
  Given an unfinished reservation whose reserved_at is 30 minutes in the past
   When the sweep runs once
   Then that row is gone
```

```gherkin
Scenario: the sweep never touches a completed key
  Given a completed row whose reserved_at is 30 days in the past
   When the sweep runs once
   Then that row is still present with its resource_identifier intact
```

```gherkin
Scenario: the sweep never touches a live reservation
  Given an unfinished reservation reserved 10 seconds ago
   When the sweep runs once
   Then that row is still present
```

## Independent end-to-end test procedure

Reproduces the defect and then its absence, by hand, against the running stack
— no test project involved. Run it during phase 5 and **write the observed
figures down in `verification.md`**, not only in a report to the orchestrator.

1. Boot the AppHost. Mint an admin token from **Aspire's proxied endpoint**,
   not the container's mapped port, or everything 401s.
2. Open `psql` against `camera-catalog-db` and insert a reservation that no
   process holds. This is what a crashed attempt leaves behind, written
   directly because there is no way to crash a process on demand:

   ```sql
   INSERT INTO idempotency_key (key, endpoint, caller, reserved_at)
   VALUES ('wedge-probe', 'POST /cameras', '<the token subject>',
           NOW() - INTERVAL '30 minutes');
   ```

3. `POST /cameras` with `Idempotency-Key: wedge-probe` and a fresh camera name.
   - **Before the fix:** `409` with title `IDEMPOTENT_REQUEST_IN_PROGRESS`,
     after roughly five seconds of polling. Repeat it — `409` again, and it
     will be forever.
   - **After the fix:** `201 Created`, immediately.
4. `SELECT resource_identifier FROM idempotency_key WHERE key = 'wedge-probe';`
   — it now carries the created camera's identifier.
5. Repeat step 3 unchanged. It must answer `201` with **the same identifier** —
   a replay, not a second camera. This is the step that proves reclamation did
   not break the replay it lives inside.
6. For US3: insert a second stale row under a key nothing will ever retry, then
   drive one sweep and confirm that row is gone while the completed
   `wedge-probe` row is not.

## Out of scope

- **`CompleteAsync` and the no-identifier `ReleaseAsync` at
  `IdempotentRequest.cs:185, 189` throwing.** Either turns a correct answer
  into a `500`. It is a real defect and it is *not* the one #2290 names: those
  calls sit on the success path, where there is no original exception to be
  masked. Fixing them means deciding what a caller should be told when the work
  succeeded and the bookkeeping did not — a different question with a different
  answer. **File it separately; do not widen this slice.** US1 already removes
  its permanence.
- **Expiring completed keys.** Forbidden by ADR-0142 §Consequences, and nothing
  here needs it.
- **Making the bound configurable per endpoint.** One constant; no knob for a
  need that does not exist (ADR-0036).
- **Any change to `IdempotencyScope`, `IdempotencyKey`, `IdempotencyHeaders`,
  or the ten call sites.** If a task proposes editing an endpoint file, the
  design has gone wrong.

## File contention

Checked at dispatch: **PR #2489** (`2279-granular-console-scopes`, spec 200) is
the only open PR, and it touches no file in this spec — no `src/ServiceDefaults/`,
no `Idempotency/`, no migration. Spec number **201** is free: 200 is the
highest claimed on any remote branch (`git ls-remote origin`, all nine branches
checked individually, not only `origin/develop`).
