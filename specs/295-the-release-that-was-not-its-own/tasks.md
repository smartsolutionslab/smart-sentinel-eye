# Tasks 295: The release that was not its own

Spec: [`spec.md`](./spec.md) · Plan: [`plan.md`](./plan.md) · Issue #2491

## Phase-3 declarations (for `/next-issue`)

| Declaration | Value |
|---|---|
| Engineer | **`backend-engineer`** only (ServiceDefaults persistence + Postgres SQL). No frontend, no infra: no AppHost, CI workflow or migration change. The shard-filter line is a one-line test-selection edit that travels with its test class. |
| Reviewer (phase 6) | `backend-reviewer`, against plan §*Review focus*. `security-reviewer` **narrowly**: the fence must not weaken the `caller` boundary (every statement keeps `caller` in its `WHERE`) and nothing new is stored or logged. |
| New ADR? | **No.** ADR-0142's state machine is unchanged; `IIdempotencyStore` is an in-repo seam no ADR specifies; no schema change. Spec §*Why no ADR*. A reviewer who disagrees blocks the run — the lane does not write one. |
| Phase 4a colour | **Behaviour-changing → RED** for T004–T006. **Guards / characterisation → observed green, unmodified** for T007–T009 and for every pre-existing idempotency suite (T002). T001 is a behaviour-preserving shape change whose evidence is T002. |
| Board | Feature-level issue #2491 is on Project #13 (`agent:ready`); no per-task issues (CLAUDE.md, Phase 3 row). |
| Follow-up to file | The sweep-registration architecture test (spec §*Out of scope*, first bullet) — **file before the PR merges**, because the PR's `Closes #2491` would otherwise close the only record of it. |

**Blocking order:** T001 → T002 → (T003 → T004–T009 → T010) → T011 → T012 → T013. T014 (US2 docs)
is `[P]` with everything. T004–T009 are one file, so they are **not** `[P]` among themselves.
T011 and T012 are the same file, sequential.

**Foundational:** T001 (the shape) blocks every test task, because the red tests compile against
`IdempotencyClaim`. Nothing else fans out — this is a one-folder change.

## Phase 3 (architect — done here)

- [x] **T000** Spec, plan, tasks at `specs/295-the-release-that-was-not-its-own/`.

## US1 (P1) — Only the current holder of a reservation may complete or release it

### Phase 4a — shape, then RED (`test-writer`)

- [ ] **T001** [US1] **Shape only, behaviour-preserving** (plan §*The shape*, §*Commit order* 1).
  In `src/ServiceDefaults/Idempotency/IIdempotencyStore.cs`: add
  `public sealed record IdempotencyClaim(IdempotencyScope Scope, DateTime ReservedAt)` with a doc
  comment stating (a) it is the only thing entitled to complete or release a reservation, and
  (b) its uniqueness rests on every claim writing `reserved_at = NOW()` and on
  `IdempotencyReclamation.StaleAfter` — if either stops being true, the fence is unsound.
  `IdempotencyReservation` gains `Option<IdempotencyClaim> Claim`; `Reserved` becomes
  `ReservedAs(IdempotencyClaim claim)`. `CompleteAsync(IdempotencyClaim claim, Guid resourceIdentifier, CancellationToken)`
  and `ReleaseAsync(IdempotencyClaim claim, CancellationToken)`.
  `IdempotencyStore.cs`: `BeginAsync` returns `ReservedAs(new IdempotencyClaim(scope, default))`
  from its **unchanged** `ExecuteSqlRawAsync` path; the other two read `claim.Scope`. **No SQL text
  changes.** `IdempotentRequest.cs`: thread `reservation.Claim.Value` into `RunAndRecordAsync` /
  `ReleaseQuietlyAsync` in place of `scope`. `IdempotentRequestTests.cs`: `RecordingStore`'s
  member signatures and `Next` default only.
  *Depends on:* nothing. **Blocks everything.**

- [ ] **T002** [US1] **Characterise T001, observed green, unmodified.** Run and capture verbatim:
  `ServiceDefaults.Tests` (`FullyQualifiedName~Idempotency`), and the Aspire suites
  `StaleIdempotencyReservationIntegrationTests`, `IdempotentCameraRegistrationIntegrationTests`,
  `IdempotentRegistrationIntegrationTests`, `EventTypeRegistryIdempotencyIntegrationTests`.
  All green. Confirm from `git diff` that **no test method body** changed. A failure here means
  T001 moved behaviour — block, do not adjust.
  *Depends on:* T001.

- [ ] **T003** [US1] New file `tests/Integration.Tests/ServiceDefaults/IdempotencyFencingIntegrationTests.cs`,
  namespace `SmartSentinelEye.Integration.Tests.ServiceDefaults`,
  `[Collection(AspireCollection.Name)]`, `AspireFixture` by primary constructor. Helpers only in
  this task: the run-unique scope (key `$"fence-{Guid.CreateVersion7():N}"`, endpoint label
  `"TEST /idempotency-fencing"`, a fixed caller label **with a comment saying why a literal is
  correct here** — plan §*The recipe* step 1); a store-per-context factory over
  `aspire.CreateCameraCatalogDbContextAsync()`; the in-`work()` ageing statement; row readers
  (`reserved_at`, `resource_identifier`, existence) in the `SqlQueryRaw … AS "Value"` shape of
  `StaleIdempotencyReservationIntegrationTests`; a copy of its `CreateSweepAsync`.
  Add `FullyQualifiedName~SmartSentinelEye.Integration.Tests.ServiceDefaults.IdempotencyFencingIntegrationTests.`
  to `tests/Integration.Tests/ci-shards/shard-4.filter` (beside its siblings; a missing entry
  fails deterministically).
  *Depends on:* T002.

- [ ] **T004** [US1] **RED:** `A_reclaimed_attempt_that_fails_does_not_release_the_new_holders_reservation`.
  Inside A's `work()`: age A's row 30 minutes, `storeB.BeginAsync` → assert `Reserved`, keep
  `claimB`, throw `InvalidOperationException`. After `ExecuteAsync` throws: assert **first** that
  an unfinished row exists (today: deleted — the red), **then** that its `reserved_at` equals
  `claimB.ReservedAt`.
  *Depends on:* T003.

- [ ] **T005** [US1] **RED:** `A_reclaimed_attempt_that_succeeds_does_not_complete_the_new_holders_reservation`.
  Same takeover; `work()` returns `IdempotentOutcome.Created(ra, …)`. Then
  `storeB.CompleteAsync(claimB, rb)`. Assert the row carries **`rb`** (today: `ra` — the ADR-0142
  "same key, different answer" case the issue did not name).
  *Depends on:* T003.

- [ ] **T006** [US1] **RED:** `A_swept_attempt_that_fails_does_not_release_the_next_arrivals_reservation`.
  Inside `work()`: age A's row, run the sweep's `RunOnceAsync`, `storeC.BeginAsync` → `Reserved`,
  throw. Assert **first** that an unfinished row exists (today: deleted), then that its
  `reserved_at` equals C's claim.
  *Depends on:* T003.

- [ ] **T007** [US1] **Guard, green:** `An_attempt_nobody_overtook_still_releases_its_own_reservation`
  — `work()` throws with no takeover; assert no row remains. *This is what fails if the fence
  matches nothing (mis-threaded claim, lossy round trip).*
  *Depends on:* T003.

- [ ] **T008** [US1] **Guard, green:** `An_attempt_nobody_overtook_still_completes_its_own_reservation`
  — `work()` returns `ra` with no takeover; assert the row carries `ra`.
  *Depends on:* T003.

- [ ] **T009** [US1] **Characterisation, green:** `A_late_completion_after_the_new_holder_completed_is_still_a_no_op`
  — inside `work()`: takeover **and** `storeB.CompleteAsync(claimB, rb)`; `work()` returns `ra`.
  Assert the row carries `rb`. (#2290's existing guard.)
  *Depends on:* T003.

- [ ] **T010** [US1] **Run T004–T009 and capture the verbatim output.** T004, T005, T006 fail **on
  their first assertion** (row missing / row carries `ra`); T007, T008, T009 pass. A red caused
  only by the placeholder `default` token is the wrong red — fix the assertion order, not the
  code. Quote the output in the PR body.
  *Depends on:* T004–T009.

### Phase 4b — GREEN (`backend-engineer`)

- [ ] **T011** [US1] `IdempotencyStore.cs` `BeginAsync`: add `RETURNING reserved_at AS "Value"`
  to the claim and execute it with `SqlQueryRaw<DateTime>(…).ToArrayAsync(…)` — **no LINQ
  operator** EF could compose (plan §*`BeginAsync`*). One row →
  `ReservedAs(new IdempotencyClaim(scope, rows[0]))`; zero → the existing fall-through,
  untouched. The `default` placeholder is deleted. Re-run T002's suites: green, unmodified
  (`Two_concurrent_reclaims_of_one_stale_reservation_only_one_wins` is the atomicity check).
  If EF composes the statement, use a `DbCommand` instead and say so in the PR.
  *Depends on:* T010.

- [ ] **T012** [US1] `IdempotencyStore.cs`: `CompleteAsync` gains `AND reserved_at = {4}`,
  `ReleaseAsync` gains `AND reserved_at = {3}`, parameters from `claim.ReservedAt`. **Keep** the
  `resource_identifier IS NULL` guards. Rewrite each statement's comment to give the fence's
  reason (a late call from an overtaken attempt must not act on the new holder's row, completed
  or not). Run T010's suite plus T002's: **everything green**.
  *Depends on:* T011.

## US2 (P3, docs) — Spec 201's statement of the limit is true

- [ ] **T014** [P] [US2] `specs/201-a-reservation-that-lets-go/spec.md` §US1, paragraph "A reclaim
  is not a promise about the dead attempt's side effects": replace the "answer `409` for a genuine
  duplicate" clause per plan §*US2*, citing `EventsEndpoints.Writes.cs:94-97` for `POST /events`,
  and add *Corrected 2026-09-30 by spec 295 (#2491).* No list of the other endpoints. Docs-only;
  no test.
  *Depends on:* nothing.

## Phase 5 — Verify (`verify`)

- [ ] **T013** Run spec §*Independent end-to-end test procedure*: half A by hand against the booted
  stack (own completion replays immediately; reclaimer's completion replays its own identifier;
  own release leaves no row), half B as the T010 suite on the final commit. **Write the observed
  values into `verification.md`.** Latency: **N/A**, stated explicitly (spec §*Latency-budget
  impact*).
  *Depends on:* T012.

## Phase 6 — QA

- [ ] **T015** [P] `backend-reviewer` over the full diff, against plan §*Review focus for phase 6*.
- [ ] **T016** [P] `security-reviewer`, narrowly: `caller` in every `WHERE`; no key, secret or
  response body added to the row or to any log.

## Phase 7 — PR

- [ ] **T017** `gh pr create --base develop`. Body: T002 and T010 verbatim output, `Closes #2491`,
  the phase-5 values, the link to the **filed** sweep-registration follow-up issue, and the
  premise correction (spec §*Problem*: the `CompleteAsync` half was only fixed for one ordering;
  the sweep is a second route in).
