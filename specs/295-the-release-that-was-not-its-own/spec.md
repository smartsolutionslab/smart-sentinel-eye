# Spec 295 — The release that was not its own

**Issue:** [#2491](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2491)
— *Idempotency reclamation has no fencing token: a merely-slow (not dead) attempt can still
race a reclaimer*
**Branch:** `fix/2491-idempotency-fencing-token` (cut from `origin/develop` @ `419f4f20`)
**Created:** 2026-09-30
**Lane:** autonomous (ADR-0144)
**Phase-4a colour:** **RED** (behaviour-changing) — a fenced write refuses what an unfenced one
performed today. A red test that arrives green is a phase-4 failure (ADR-0139).
**Follows:** spec 201 (`specs/201-a-reservation-that-lets-go/`, #2290), whose
`verification.md` §*Follow-ups filed* names this issue.

**ADRs this spec is bound by (it applies them; it decides nothing they left open):**
**ADR-0142** (idempotency keys — the state machine this protects; its "same key, different
answer" hazard is the defect), ADR-0143 (retry safety — why a retry reaches these endpoints),
ADR-0103 (integration tests against the Aspire fixture, no Testcontainers), ADR-0105
(`Ensure.That`), ADR-0084 (readable parameter lists), ADR-0036 (smallest change), ADR-0139
(new behaviour starts red), ADR-0144 (autonomous lane), ADR-0037 (phases), ADR-0109 (parallel
markers).

**No new ADR is required.** §*Why no ADR* gives the reasoning; if a reviewer disagrees, the
lane blocks rather than writing one.

## Problem

Verified by reading the files at `419f4f20`, not copied from the issue. Line numbers are from
that tree.

`IdempotencyStore<TDbContext>` (`src/ServiceDefaults/Idempotency/IdempotencyStore.cs`) has three
statements. Only the claim knows *which* reservation it made; the other two identify a row by
`(key, endpoint, caller)` alone:

| Method | Statement | `WHERE` (beyond the scope triple) | Lines |
|---|---|---|---|
| `BeginAsync` | `INSERT … ON CONFLICT DO UPDATE SET reserved_at = NOW()` | conflict action only when `resource_identifier IS NULL AND reserved_at < NOW() - StaleAfter` | `:55-68` |
| `CompleteAsync` | `UPDATE … SET resource_identifier, completed_at` | `resource_identifier IS NULL` | `:194-202` |
| `ReleaseAsync` | `DELETE` | `resource_identifier IS NULL` | `:212-219` |

`IdempotencyReservation` (declared in `IIdempotencyStore.cs:22-32`, not a file of its own)
carries an outcome and an optional resource identifier; `Reserved` is a **static singleton**
(`:24-25`), so a won claim carries nothing that distinguishes it from any other won claim of
the same scope.

Since spec 201, two things other than the owning request can take a row out from under it
after `IdempotencyReclamation.StaleAfter` (10 minutes):

1. **A reclaimer** — `BeginAsync`'s conflict action re-stamps a stale unfinished row and hands
   it to a new request.
2. **The sweep** — `IdempotencyReservationSweepHostedService<T>` deletes a stale unfinished row;
   the next arrival then inserts a fresh one. *(The issue names only the reclaimer; the sweep is
   a second route into the same defect and is closed by the same fix.)*

Neither can tell a dead attempt from a merely slow one, and nothing in `src/` bounds how long
`work()` may run (no request-timeout middleware, no Kestrel request limit — the issue's premise,
re-confirmed). Call the original attempt **A** and the new owner **B**. What A's late
bookkeeping does to B's live reservation today:

| A's late call | Condition | Effect today | Fixed? |
|---|---|---|---|
| `CompleteAsync` | B has **already completed** | no-op — the `IS NULL` guard added in #2290's phase 6 | yes |
| `CompleteAsync` | B has reserved, **not yet completed** | **writes A's identifier onto B's row**; B's own `CompleteAsync` then updates 0 rows. B's caller was told B's identifier, every replay answers A's | **no** |
| `ReleaseAsync` | B has reserved, not yet completed | **deletes B's reservation**. A third request can start concurrently with B; B's `CompleteAsync` updates 0 rows; the key ends with no record, and the next retry creates again | **no** |

**The issue's premise is correct but incomplete.** It says the `CompleteAsync` half "is now
fixed". Only the *B-already-completed* ordering is. The middle row — A completing while B is
still working — is the exact "same key, different answer" hazard ADR-0142 §Consequences names
as the thing the mechanism exists to remove, and no guard covers it.

**Blast radius, unchanged from the issue:** `caller` is in every `WHERE`, so this stays within
one authenticated subject's own key. A duplicate resource, or a key that replays the wrong one
of two resources the same caller created — not a boundary crossing.

**Likelihood:** low by construction (A must outlive a 10-minute bound derived to be 60–120x any
real request, spec 201 §US1). The fix is taken anyway because the defect, when it occurs, is
silent and permanent for that key, and the fix is one predicate per statement.

### Why no existing test caught it

`StaleIdempotencyReservationIntegrationTests` exercises reclaim, concurrent reclaim and the sweep
— each from the reclaimer's side. None of them lets the *original* attempt act after it has been
reclaimed. `IdempotentRequestTests` drives `RecordingStore`, which ignores the SQL. The defect is
in two `WHERE` clauses, so, as in spec 201, only an integration test against the real table can
show it.

## Why no ADR

- **ADR-0142's state machine is unchanged.** The three states (absent / reserved / completed)
  and their answers stay exactly as the ADR's table gives them. The fence changes *who may
  perform* the reserved → completed and reserved → absent transitions — only the holder of the
  current reservation — which is what the ADR's table already assumes and the code failed to
  enforce.
- **`IIdempotencyStore` is not a decided contract.** No ADR specifies its signature; it has one
  implementation (`IdempotencyStore<T>`), one test double (`RecordingStore`) and one caller
  (`IdempotentRequest`), all in this repository. It is not in `Shared.Contracts`, carries no
  version suffix and crosses no process boundary. Spec 201 kept it frozen as a scope decision
  for that slice (its §Out of scope), not because a decision record required it.
- **No schema change.** The token is a column that already exists (`reserved_at`), so no
  migration, no DDL change in `IdempotencyKeyTable`, no change across the seven context schemas.
- **Precedent:** spec 201 added the reclaim, the sweep and the `CompleteAsync` guard under the
  same reasoning without an ADR.

## Locked technical choices

Nothing here is a new choice; each is an existing repo decision this spec is bound by.

| Concern | Choice | Source |
|---|---|---|
| Fencing token | **`reserved_at` as the claim wrote it**, returned by the claim statement (`RETURNING`) | the issue's proposed fix; plan §*The token* for why not a new column |
| Where the comparison happens | in the `WHERE` of the same single statement — never read-then-write | `IdempotencyStore.cs:22-27` |
| Clock | Postgres `NOW()`, as today; the token is compared, never computed client-side | spec 201 plan §*Testability* |
| Where reservation state lives | `idempotency_key` in each context's own schema | ADR-0142 §Decision.4 |
| Test stack | Aspire fixture, no Testcontainers | ADR-0103 |
| Guards | `Ensure.That(x).IsNotNull()` | ADR-0105 |

## Latency-budget impact

**N/A.** Every affected path is an HTTP `POST` create or rotation; none of constitution §IV's
six legs is touched and §VII's dashboard obligation is not engaged. The claim stays **one round
trip** (`RETURNING` is part of the same statement); `CompleteAsync` and `ReleaseAsync` gain one
predicate on a row already located by primary key.

## User stories

### US1 (P1) — Only the current holder of a reservation may complete or release it

A request whose reservation has since been reclaimed (or swept and re-reserved) by another
request cannot complete or release the new holder's reservation. Its late `CompleteAsync` and
`ReleaseAsync` become no-ops; the current holder's bookkeeping lands as if the original had
never existed.

**Why one story.** Completion and release are fenced by the same token, carried the same way,
proved by the same test recipe; splitting them would ship half a fence and change
`IIdempotencyStore`'s shape twice.

**What the fence does not do — stated so no reader infers more.** A fenced-off attempt still ran
its `work()`: if it created something, that resource exists, and its caller was answered with
it. The fence decides which of two resources the **key** remembers (the current holder's), and
guarantees the key is never left with *no* record for work that was done. It does not, and
cannot, un-create the zombie's resource. That is the same honest limit spec 201 §US1 records
for reclamation itself — corrected by US2 below.

### US2 (P3, docs) — Spec 201's statement of that limit is true

Spec 201 §US1 says, of re-running a dead attempt's work, that "the domain's own uniqueness rules
… answer `409` for a genuine duplicate". **That is false for at least one keyed endpoint**:
`POST /events` builds a fresh `EventIdentifier` per request, and
`src/EventIngestion/Api/EventsEndpoints.Writes.cs:94-97` says in terms that a retry "would file
a second event under a second identifier — no conflict to notice, just a duplicate". The
sentence is corrected in place with a dated note pointing here.

**Why in this spec and not split:** it is a paragraph, not code; it states the same limit this
spec's US1 has to state; and leaving it would put spec 201 and spec 295 in contradiction about
what reclamation guarantees. It carries no test colour and blocks nothing.

## Acceptance scenarios

All US1 scenarios run against the real `idempotency_key` table through
`IdempotentRequest.ExecuteAsync` and a real `IdempotencyStore<CameraCatalogDbContext>`, with the
takeover performed from *inside* A's `work()` — which is the only place a slow attempt can be
overtaken.

```gherkin
Scenario: a reclaimed attempt that fails does not release the new holder's reservation
  Given request A holds the reservation for key "K"
    And while A's work is running its reservation ages past the bound
    And request B reclaims key "K"
   When A's work throws
   Then the row for "K" still exists, unfinished
    And its reserved_at is the value B's claim returned
```

```gherkin
Scenario: a reclaimed attempt that succeeds does not complete the new holder's reservation
  Given request A holds the reservation for key "K"
    And while A's work is running its reservation ages past the bound
    And request B reclaims key "K"
   When A's work returns resource "RA"
    And B then completes with resource "RB"
   Then the row for "K" carries "RB"
```

*This is the ADR-0142 "same key, different answer" case the issue did not name. Today the row
carries "RA" while B's caller was told "RB".*

```gherkin
Scenario: a swept attempt that fails does not release the next arrival's reservation
  Given request A holds the reservation for key "K"
    And while A's work is running its reservation ages past the bound
    And the sweep runs once, removing it
    And request C then reserves key "K" afresh
   When A's work throws
   Then the row for "K" still exists, unfinished, with C's reserved_at
```

```gherkin
Scenario: an attempt nobody overtook still releases its own reservation
  Given request A holds the reservation for key "K"
   When A's work throws
   Then no row for "K" remains
```

```gherkin
Scenario: an attempt nobody overtook still completes its own reservation
  Given request A holds the reservation for key "K"
   When A's work returns resource "RA"
   Then the row for "K" carries "RA"
```

*The last two are guards, green before and after. They are what fails if the token is threaded
wrongly or loses precision on its round trip: a fence that matched nothing would pass the three
red scenarios above by doing nothing at all.*

```gherkin
Scenario: a late completion after the new holder completed is still a no-op
  Given B reclaimed key "K" from A and completed it with "RB"
   When A's work returns resource "RA"
   Then the row for "K" still carries "RB"
```

*Characterisation of #2290's existing guard — must stay green.*

Bad-request and auth paths are unchanged and already covered: a malformed key is refused at
`IdempotencyHeaders.TryRead` before any store call, and an unauthenticated request never reaches
the endpoint (spec 201's two boundary scenarios, which run unmodified). A **cross-caller** fence
needs no scenario: `caller` is in every `WHERE` before and after, and
`A_stale_reservation_is_not_another_callers_to_reclaim` continues to prove the reclaim side.

### US2 — the corrected sentence

```gherkin
Scenario: spec 201's limit statement matches the code
  Given specs/201-a-reservation-that-lets-go/spec.md §US1
   Then it no longer claims every keyed endpoint answers 409 for a re-run
    And it names POST /events as one that files a duplicate instead
    And it points at spec 295
```

## Independent end-to-end test procedure

The race needs an attempt that outlives the 10-minute bound; it cannot be provoked through
HTTP on demand. So the end-to-end check has two halves, and each proves something the other
cannot.

**A. The running service still completes and releases its *own* reservations.** This is the
half a fence gets wrong silently: a token that does not round-trip exactly through Npgsql in a
real service (precision, `DateTime` kind, offset) makes every fenced write match nothing — and
every normal request then wedges. Against the booted AppHost, `psql` on `camera-catalog-db`:

1. `POST /cameras` with `Idempotency-Key: fence-probe-<run>` and a fresh name `N1` → `201`.
   Repeat it unchanged → `201` with **the same identifier**, immediately (not after ~5 s of
   polling). *A fenced `CompleteAsync` that matched nothing would leave the row unfinished and
   the repeat would answer `409 IDEMPOTENT_REQUEST_IN_PROGRESS`.*
2. Age that row to stale-unfinished (spec 201's recipe:
   `UPDATE idempotency_key SET resource_identifier = NULL, completed_at = NULL, reserved_at = NOW() - INTERVAL '30 minutes' WHERE key = 'fence-probe-<run>';`),
   then `POST` the same key with name `N2` → `201`; repeat → `201` with **`N2`'s identifier**.
   *The reclaimer's completion matched the token its own claim returned.*
3. `POST /cameras` with `Idempotency-Key: fence-fail-<run>` and **`N1`'s name again** — refused
   inside `work()` as a duplicate, which takes the release path. Then
   `SELECT count(*) FROM idempotency_key WHERE key = 'fence-fail-<run>';` → **0**.
   *A fenced `ReleaseAsync` that matched nothing would leave this row behind.*

**B. The race itself** — a late completion or release from an overtaken attempt — is the US1
integration suite, which drives `IdempotentRequest` over a real store and performs the takeover
from inside A's `work()`. Phase 5 runs it and quotes the output.

Write both halves' observed values into `verification.md`, not only into the report.

## Out of scope

- **The sweep-registration architecture test** (issue's "also worth closing" #1). Split to its
  own issue: it is a declaration fence that **arrives green** (all seven registrations exist at
  `419f4f20` — `AutomationInfrastructureModule.cs:48`, `CameraCatalogInfrastructureModule.cs:74`,
  `EventIngestionInfrastructureModule.cs:59`, `IdentityInfrastructureModule.cs:105`,
  `LayoutCompositionInfrastructureModule.cs:57`, `OverlayDesignerInfrastructureModule.cs:54`,
  `SystemVariablesInfrastructureModule.cs:58`), so it has the opposite phase-4a colour from
  this spec; it touches a disjoint project (`Architecture.Tests`); and it guards a different
  defect class (wiring regression, not a race). Bundling it would put a green-arriving test
  inside a red slice with nothing to say which is which.
- **Reporting a fenced no-op.** A fenced `CompleteAsync`/`ReleaseAsync` is silent, exactly as the
  existing `IS NULL` guard's no-op is today. Surfacing it (an `Activity` event, a log line) is a
  separate observability question with no requester (ADR-0036).
- **#2492** — `IdempotencyScope` has no fab. Also touches the idempotency scope; not touched here.
- **Removing or relaxing the existing `resource_identifier IS NULL` guards.** The token makes
  them largely redundant; removing them is a refactor, not this fix, and they stay.
- **Any endpoint file, `IdempotencyScope`, `IdempotencyKey`, `IdempotencyHeaders`,
  `IdempotencyKeyTable`, the sweep, or any migration.** The 11 keyed call sites (spec 201 listed
  ten; `POST /walls`, `WallEndpoints.Commands.cs:66`, has since joined) call `IdempotentRequest`,
  never the store, so none changes. A task that edits one has misread the design.

## File contention

Checked at dispatch (2026-09-30): the only open PR, **#2689** (`ci/2337-render-leg-gate-completion`),
touches `e2e/` and `specs/225-*` only. Spec number **295** is free: 294 is the highest directory
on `origin/develop` and on every remote branch (`git ls-remote`, each branch's `specs/` listed).
**#2492** is open and would edit `IdempotencyScope` — not in flight, but whichever lands second
rebases over the other in `src/ServiceDefaults/Idempotency/`.
