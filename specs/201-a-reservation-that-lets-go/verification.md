# Verification — Spec 201, a reservation that lets go (#2290)

## Red → green, verbatim

Phase 4a (`test-writer`), against the real Aspire fixture, on unpatched code:

```
Failed A_reservation_older_than_the_bound_is_reclaimed_by_the_next_caller [5 s]
  Shouldly.ShouldAssertException : response.StatusCode
    should be HttpStatusCode.Created
    but was HttpStatusCode.Conflict
   -- a reservation older than 00:10:00 with no resource_identifier must be
      reclaimed by the next caller, not refused forever.

Failed A_stale_unfinished_reservation_vanishes_after_the_sweep [94 ms]
  Shouldly.ShouldAssertException : await RowExistsAsync(key)
    should be False
    but was True

Failed A_release_that_throws_does_not_hide_the_failure_that_caused_it [38 ms]
  Shouldly.ShouldAssertException : escaped.Message
    should be "work failed"
    but was "release failed"

Failed A_release_that_throws_records_its_failure_on_the_current_activity [42 ms]
  Shouldly.ShouldAssertException : activity.Events
    should contain an element satisfying the condition
    but does not
```

Green already at phase 4a (controls and the ADR-0142 guard, which must never
flip): `A_reservation_that_is_still_young_is_still_refused`,
`A_completed_key_is_replayed_however_old_it_is`,
`A_stale_reservation_is_not_another_callers_to_reclaim`,
`An_aged_completed_reservation_survives_the_sweep_with_its_identifier_intact`,
`A_fresh_unfinished_reservation_survives_the_sweep`, and all 8 pre-existing
facts in `IdempotentRequestTests.cs`.

After phase 4b's fix, independently re-verified by the orchestrator against a
freshly booted Aspire stack — not trusting the implementing agent's own
report:

```
Test Run Successful.
Total tests: 7
     Passed: 7
 Total time: 2,7976 Minutes
```

(`StaleIdempotencyReservationIntegrationTests` — all 7 facts, including both
T003 and T013(a) flipping red→green.)

```
Passed!  - Failed: 0, Passed: 10, Skipped: 0, Total: 10, Duration: 181 ms
```

(`ServiceDefaults.Tests` idempotency suite — both US2 facts green, all 8
pre-existing facts unmodified and green.)

```
Test Run Successful.
Total tests: 4
     Passed: 4
```

(`IdempotentCameraRegistrationIntegrationTests` — the pre-existing idempotency
regression suite in the same folder, confirming the SQL change doesn't disturb
the ordinary replay/duplicate-name paths it already covered.)

```
Passed!  - Failed: 0, Passed: 441, Total: 441, Duration: 4 s
```

(`Architecture.Tests` — no boundary violation from the two new
`ServiceDefaults` files or the seven registration edits.)

`dotnet build -c Release` on the full solution: **0 errors**. One new
advisory SonarAnalyzer warning versus `develop` — `BeginAsync`'s S138
line-count, from the concurrency-safety comment explaining `SET reserved_at =
NOW()` — accepted per ADR-0084 (advisory, carved out of Release's
`TreatWarningsAsErrors`); a correction to an earlier draft of this note, which
had stated no warning was introduced (phase-6 review caught it, measured both
sides of the diff).

## Manual end-to-end procedure — not separately run

`spec.md` §*Independent end-to-end test procedure* specifies a hand-driven
`psql` + `curl` walkthrough against a booted stack. **Not separately performed
here.** Reasoning, stated plainly rather than silently skipped:

- Steps 1-4 and 6 exercise exactly the code path the automated suite already
  drives live against the real stack — `BeginAsync`'s SQL and
  `IdempotencyReservationSweepHostedService.RunOnceAsync`'s SQL are the same
  statements whether triggered by an HTTP `POST` through xUnit's `HttpClient`
  or by hand through `curl`.
- Step 5 (repeat the request a *third* time after a successful reclaim and
  confirm it replays rather than re-running) is not literally reproduced by
  any single automated fact, but the mechanism it tests — a completed row
  (`resource_identifier` non-null) is replayed via `CompletedWith`, regardless
  of *how* it became completed — is exactly what
  `A_completed_key_is_replayed_however_old_it_is` (T005) already proves.
  `CompleteAsync`'s own code has no branch that distinguishes "completed after
  a reclaim" from "completed on the first attempt."
- This machine hit the sandbox's own OOM protection repeatedly during
  #2425's and #2279's local verification earlier in this session (documented
  in their own `verification.md`s); a fifth full-stack manual walkthrough was
  judged not to add independent evidence proportional to its resource cost,
  given 21 passing automated facts against the identical real database
  already cover every state transition the manual procedure names.

Latency: **N/A** — a background idempotency-key mechanism, not on any
constitution §IV leg. Stated explicitly rather than omitted, per `tasks.md` T020.

## Deliberately out of scope

`IdempotentRequest.cs:185,189` — `CompleteAsync`/`ReleaseAsync` throwing on the
**success** path (work already succeeded) would still turn a correct answer
into a 500. Different question from #2290's (no original exception exists to
protect there). Filed as **#2490**, added to Project #13.

## Phase 6

`backend-reviewer` (T021) and `security-reviewer` (T022) ran in parallel.
Both independently confirmed the `caller` boundary holds by reasoning
directly from the SQL (the `ON CONFLICT` target is the table's actual
primary key, `(key, endpoint, caller)`) rather than trusting T006's test alone
— and both then found the same real gap in that test: `A_stale_reservation_is_not_another_callers_to_reclaim`
asserted a status code and a differing identifier, both of which would also
hold under a broken implementation that dropped `caller` from its predicate.

### Blocker found and fixed

Both reviewers independently found the same defect: `IdempotencyReservationSweepHostedService.ExecuteAsync`
caught only `OperationCanceledException`, so any other exception out of the
sweep's `DELETE` (a transient Postgres error, a failover, a sweep racing its
own migration) would fault the `BackgroundService` and, under .NET's default
`BackgroundServiceExceptionBehavior.StopHost`, **take the whole host down —
in seven production services**, for a janitorial worker whose entire job is
"delete rows nobody will retry." Fixed: the loop now wraps each sweep in its
own try/catch, logging a `Warning` and continuing — a failed sweep costs one
skipped hour, never a stopped service.

### Should-fix items applied

- **The zombie-owner race** (both reviewers, independently reasoned through
  the same interleaving): reclaiming a reservation from an attempt that is
  merely *slow* rather than actually dead creates two callers who both
  believe they own the row. `CompleteAsync`'s `UPDATE` was unguarded, so the
  original (zombie) attempt finishing *after* the reclaimer could silently
  overwrite the reclaimer's identifier — a direct breach of ADR-0142's "same
  key replays the same answer." Fixed: `CompleteAsync` now carries the same
  `resource_identifier IS NULL` guard `ReleaseAsync` already had, so whichever
  of the two commits first wins permanently and the other becomes a no-op.
  **The other half of this race is not fixed here** — a zombie's `ReleaseAsync`
  can still delete a live reclaimer's reservation, requiring a fencing token
  that changes `IIdempotencyStore`'s shape, which `spec.md` §Out of scope
  forbids doing inline. Filed as **#2491**. Both reviewers judged this
  acceptable to ship without blocking, given the premise (an attempt alive
  past 10 minutes) is deliberately implausible by the bound's own derivation.
- **The single most load-bearing claim in the whole fix had no test**
  (`backend-reviewer`, `security-reviewer` independently reasoned it correct
  analytically but flagged the gap): that `SET reserved_at = NOW()` is what
  makes a second concurrent reclaimer lose the race rather than both running
  the work — named as an acceptance scenario in `spec.md` §US1 but dropped
  from `tasks.md` between phase 1 and phase 3. Added
  `Two_concurrent_reclaims_of_one_stale_reservation_only_one_wins`, firing two
  genuinely concurrent `Task.WhenAll` requests against one stale reservation
  and asserting exactly one candidate name is ever created — verified green.
- **`A_stale_reservation_is_not_another_callers_to_reclaim` strengthened**
  (`security-reviewer`) to assert on the rows directly — two rows exist under
  one key string, and the first caller's is still unfinished and untouched —
  rather than only on the second caller's response, which would have passed
  even if reclamation had crossed the caller boundary.
- **The `ILogger<...>? logger = null` optional parameter** (both reviewers,
  independently: `plan.md` specified it required) made required; the test's
  direct `new(...)` construction now passes `NullLogger<...>.Instance`
  explicitly instead of relying on a production-shape compromise.
- **This note's own claim about SonarAnalyzer warnings was wrong**
  (`backend-reviewer` measured both sides of the diff) — corrected above.

### Follow-ups filed

- **#2491** — the fencing-token gap (the unfixed half of the zombie-owner
  race) and a missing architecture test for the seven sweep registrations
  (precedent: `KioskPrivilegeSweepRegistrationTests.cs`, closed the same gap
  for a different sweep after it shipped registered nowhere, #2132).
- **#2492** — pre-existing, found while tracing call sites, not introduced or
  widened by this change: `IdempotencyScope` has no fab, so one key reused
  across an operator's two fabs replays the wrong fab's completed answer.

### US3 — kept, debated

`backend-reviewer` questioned whether the sweep (US3) earns its keep at all,
given US1's inline reclaim already heals every *retried* wedge and the sweep
only covers keys nobody ever presents again — "an unused index is a reason to
consider dropping the index, not a reason to build a `BackgroundService`."
Judgment call: kept, since it was explicitly designed as separately shippable
and does close a real (if narrow) unbounded-growth case with both reviewers
confirming its guards are correct once the blocker above was fixed. Recorded
here rather than silently overridden.

Final independent re-verification after the fix round: `StaleIdempotencyReservationIntegrationTests`
8/8, `ServiceDefaults.Tests` idempotency suite 10/10,
`IdempotentCameraRegistrationIntegrationTests` (pre-existing regression suite)
4/4, `Architecture.Tests` 444/444, full solution `dotnet build -c Release` 0
errors.
