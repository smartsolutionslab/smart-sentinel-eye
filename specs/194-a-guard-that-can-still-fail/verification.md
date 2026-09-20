# Verification 194 — A guard that can still fail (#2293)

**Latency: N/A.** Test-only change; no production assembly modified, no leg
of constitution §IV's event→overlay path touched.
`src/EventIngestion/Infrastructure/Ingress/PersistenceLoopHostedService.cs`
is byte-identical to `origin/develop`
(`git hash-object` = `d5a1fbf7a1a31fd758aa203c665530d70fd5fceb`, both
sides) — independently re-confirmed by the orchestrator after phase 4a.

## What this verifies

Two tests in `PersistenceLoopHostedServiceTests.cs` could pass for the
wrong reason. Before touching anything, the architect proved by real
counterfactual that the production loop is already correct — the fix is
entirely in the tests' own discriminating power, not in `src/`.

## Finding 1 — the four-run sequence, quoted verbatim

`One_delivery_that_never_stores_does_not_hold_up_the_others` used the
harness default `MaximumRetryWindow = 500ms`. At that window, the healthy
event's storage is explained equally well by "the loop moved past the
failure" (correct) or "the poison was abandoned out of the way before the
outer assertion's patience ran out" (the defect the test exists to catch).
`Window = 30s` (matching the sibling test's own pattern) removes that
ambiguity.

**T001 — baseline, unmodified test, unmodified loop:**
```
Passed!  - Failed:     0, Passed:     8, Skipped:     0, Total:     8, Duration: 1 s
```

**T003 — a real defect injected into `RetryAsync`** (stop attempting after
the first `Outcome.Failed`, carrying the remainder untried — the exact
shape spec 018 originally fixed), **test as it stood (500ms) → PASS, the
bug**:
```
Passed SmartSentinelEye...One_delivery_that_never_stores_does_not_hold_up_the_others [629 ms]
Passed!  - Failed: 0, Passed: 8, ...
```
629ms — well past the 500ms window, meaning abandonment (not correctness)
let the healthy event through. The test could not tell the difference.

**T004 — same injected defect, `Window = 30s` → FAIL, the guard now has
teeth:**
```
Shouldly.ShouldAssertException : healthy.Stored
    should be
1
    but was
0
Additional Info: a good event waited for a bad one
Failed ...One_delivery_that_never_stores_does_not_hold_up_the_others [10 s]
Total tests: 8   Passed: 7   Failed: 1
```

**Sibling confirmation, same run, same injected defect** —
`An_event_arriving_behind_a_failing_one_does_not_wait_for_it` **PASSED**
`[181 ms]`: the sibling already guards a different thing (arrivals vs.
retries across cycles), and this defect is invisible to it. Only
`One_delivery_that_never_stores_does_not_hold_up_the_others` sees it —
confirming the two tests are not redundant. (Independently re-confirmed at
phase 6 with a fresh mutation of the same shape: same two outcomes.)

**T005 — defect reverted** (`git checkout`, hash confirmed
`d5a1fbf7...` — matches both the pre-mutation state and `origin/develop`),
forced rebuild (a restored file keeps its old mtime), `Window = 30s`,
correct loop → **PASS**:
```
Passed ...One_delivery_that_never_stores_does_not_hold_up_the_others [69 ms]
Test Run Successful.
Total tests: 8   Passed: 8
```
69ms — stored in cycle 1 at the fallback pass, exactly as the architect's
original investigation found. Confirms the fix doesn't break the real
(correct) production behaviour.

## Finding 2 — deterministic flake reproduction, quoted verbatim

`Retries_a_failed_delivery_and_acknowledges_nothing_until_it_lands` flaked
once under parallel load (2026-09-11) — the only test racing a **wall-clock**
500ms budget to complete `FailuresBeforeSuccess = 2` retries.
`TimeProvider` virtualization was considered and rejected: no project
references `Microsoft.Extensions.TimeProvider.Testing`, every
`TimeProvider` usage in this file passes `TimeProvider.System` (real time),
and the loop's real `await Task.Delay(backoff, ct)` means virtualizing the
clock alone would not remove the race. Widening the window is the fix.

**Red** (window squeezed to 1ms, forcing the exact real flake's failure
mode deterministically):
```
Shouldly.ShouldAssertException : completion.Stored
    should be
1
    but was
0
Failed ...Retries_a_failed_delivery_and_acknowledges_nothing_until_it_lands [10 s]
```
Exact signature of the 2026-09-11 flake (`completion.Stored should be 1
but was 0`), reproduced deterministically rather than waited for.

**Green** (`Window = 30s`, the real fix):
```
Passed ...Retries_a_failed_delivery_and_acknowledges_nothing_until_it_lands [173 ms]
Test Run Successful.
Total tests: 8   Passed: 8
```
`Attempts.ShouldBe(3)` and every other assertion in this test are
byte-identical to before — only the window value changed, confirmed by
diff.

## Characterisation — the other six tests, unmodified, green throughout

Present and green identically in T001 and every subsequent full-class run:
`Acknowledges_each_delivery_in_a_stored_batch`,
`Records_and_releases_a_delivery_that_never_stores`,
`An_event_arriving_behind_a_failing_one_does_not_wait_for_it`,
`A_batch_is_stored_in_one_save_not_one_per_event`,
`An_envelope_no_rule_will_accept_is_recorded_before_it_is_released`,
`An_acknowledgement_that_throws_does_not_take_the_loop_down`. Byte-for-byte
unmodified in the diff — confirmed independently by the orchestrator via
`git diff origin/develop -- tests/EventIngestion.Infrastructure.Tests/PersistenceLoopHostedServiceTests.cs`.

## Independent re-verification, this pass

```
$ dotnet test tests/EventIngestion.Infrastructure.Tests
Passed!  - Failed: 0, Passed: 59, Skipped: 0, Total: 59, Duration: 10 s

$ git diff origin/develop --stat
 tests/EventIngestion.Infrastructure.Tests/PersistenceLoopHostedServiceTests.cs | 23 +-
 (plus three new spec docs)

$ git hash-object src/EventIngestion/Infrastructure/Ingress/PersistenceLoopHostedService.cs
d5a1fbf7a1a31fd758aa203c665530d70fd5fceb   (== origin/develop's blob)
```

`dotnet build -c Release`: 0 errors, only pre-existing advisory S107/S138
warnings unrelated to this diff.

## Phase 6 — one review round, no blockers

`backend-reviewer` independently reproduced the core counterfactual with
its own fresh mutation of `RetryAsync` (not a re-run of phase 4a's),
confirmed the sibling's independence under that separate mutation, ran the
30s retry-test value five consecutive times (8/8 each), and — the check
phase 4a's own tools couldn't easily make — confirmed via the tests'
actual 10s `CancellationTokenSource` bound that a 30s window can never
produce a hang: every observed failure lands cleanly at the 10s deadline
with a real Shouldly assertion, never a timeout. One should-fix (the new
docstring's first sentence read as though it condemned four unrelated,
correct tests in the same file for "taking the default" — reworded to
"relying on" it for their own abandonment assertion) and one nit (stale
line-number references in this file, now fixed to name tests instead),
both fixed. Also confirmed: `Abandoned.ShouldBe(0)` in the retry test
isn't hollowed out by widening its window — a loop that abandons by
attempt count rather than duration still trips it, and the window
mechanism itself stays positively asserted by the untouched
`Records_and_releases_a_delivery_that_never_stores`.

**Governance note narrowed at phase 6**: the uncovered act isn't "this
kind of red" in general (ADR-0139's obligation — observed failing, quoted
verbatim — is met on its own terms) but specifically *temporarily
mutating known-correct production code to manufacture a red*, since a
botched revert is the one way this technique could ship a defect. T007's
three-check revert proof (diff, hash, forced rebuild) is the mitigation,
and it was independently reproduced twice — once by phase 4a, once by
phase 6, with two different mutations.

## Root-cause documentation

`MaximumRetryWindow`'s 500ms harness default gained a doc paragraph noting
only a test whose own assertion depends on abandonment actually happening
should rely on the short default — the trap this whole investigation
found was that 500ms is the *default*, and the two tests that needed
something longer had drifted onto it silently.

## What was NOT changed, and why

Both findings are entirely test-file changes. There is no phase 4b: the
architect confirmed no production code needed changing (the loop is
already correct), so there is nothing for a backend-engineer to receive.
The window-value edits themselves were made by phase 4a's test-writer,
since the evidence sequence *is* the justification for the edit — there
is no separate "implementation" step once the four-run proof exists.

## Governance note, carried forward from phase 3, not resolved here

The architect flagged that ADR-0144's two phase-4a colours both assume the
test's own *subject* is what changes, and neither explicitly covers "a
test-only change whose red can only be produced by temporarily mutating
correct production code to prove the test's discriminating power." This
delivery resolved it pragmatically as red-by-counterfactual (satisfying
ADR-0139's intent — a real failure was observed and is quoted above) but
flagged that if this shape recurs, it may be worth its own ADR — which the
autonomous lane may not write. Delivery did not depend on resolving this;
recorded so it isn't silently lost.
