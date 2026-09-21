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

`dotnet build -c Release` on the full solution: **0 errors**, pre-existing
advisory SonarAnalyzer warnings only (`BeginAsync`'s S138 line-count, unrelated
files), none introduced blocking.

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

## Phase 6 — pending

`backend-reviewer` (T021) and `security-reviewer` (T022, narrow: the `caller`
boundary must not be crossable by reclamation, per T006's test).
