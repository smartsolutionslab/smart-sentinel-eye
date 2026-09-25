# Verification 250 — The backoff no test could reach

**Spec:** `specs/250-the-backoff-no-test-could-reach/spec.md`
**Plan:** `specs/250-the-backoff-no-test-could-reach/plan.md`
**Tasks:** `specs/250-the-backoff-no-test-could-reach/tasks.md`
**Issue:** #2449

Phase 4a colour: **CHARACTERISATION (behaviour-preserving)**. Latency: **N/A —
dev-only simulator (ADR-0111)**.

---

## 1. Premise table

Carried over from `spec.md` §1 — re-checked, unchanged at verification time:

| Claim | Verified |
|---|---|
| The simulator's fake had no drop/hold controls before this spec | yes |
| EventIngestion's fake has both, as the model to port | yes |
| `MqttPublisher` runs the same `ResetIfHeld` logic as EventIngestion's loop | yes, byte-identical `MqttBackoff` |
| The backoff was a hardcoded `new()` at 1 s / 30 s | yes, before this spec |
| Nothing in the simulator's suite reached `ResetIfHeld` before this spec | yes |
| #2451 / PR #2585 does not overlap this file | yes — EventIngestion's fake only |

---

## 2. Characterisation baseline (T001) — before the seam

`dotnet test -c Release tests/ScenarioSimulator.Tests`, run twice against
`a54b11d0` (the commit immediately before T002/T003, checked out in an
isolated worktree so the working tree's later commits were not disturbed),
per plan.md §6 (the first run after machine churn can read like a
regression):

```
=== RUN 1 ===
Passed!  - Failed:     0, Passed:    61, Skipped:     0, Total:    61, Duration: 5 s - SmartSentinelEye.ScenarioSimulator.Tests.dll (net10.0)
=== RUN 2 ===
Passed!  - Failed:     0, Passed:    61, Skipped:     0, Total:    61, Duration: 4 s - SmartSentinelEye.ScenarioSimulator.Tests.dll (net10.0)
```

Both runs: 61/61, same count. This is the "before" the DoD's before/after
comparison is against — 61 pre-existing tests, none of which named
`MqttBackoff` or `ResetIfHeld` (spec.md §1).

---

## 3. Characterisation "after" (T004) — the seam in place, pre-existing files unmodified

```
Passed!  - Failed:     0, Passed:    67, Skipped:     0, Total:    67, Duration: 5 s - SmartSentinelEye.ScenarioSimulator.Tests.dll (net10.0)
```

`git diff origin/develop --stat -- tests/ScenarioSimulator.Tests/` lists only
`Fakes/FakeMqttClient.cs` among pre-existing files (the two new test files are
additions, not modifications). The two frozen characterisation files —
`MqttPublisherDropAccountingTests.cs`, `MqttPublisherProtocolPinTests.cs` —
are byte-identical to `origin/develop`:

```
$ git diff --exit-code origin/develop -- tests/ScenarioSimulator.Tests/MqttPublisherDropAccountingTests.cs tests/ScenarioSimulator.Tests/MqttPublisherProtocolPinTests.cs
(no output — byte-identical)
```

---

## 4. New tests green (T007)

All 6 new tests (3 in `FakeMqttClientDropHoldContractTests.cs`, 3 in
`MqttPublisherBackoffTests.cs`) pass as part of the 67/67 total above.
Observed `ConnectAttempts` per window, from `Console.WriteLine` in the test
output:

- `A_connection_that_dies_on_arrival_is_retried_with_a_delay_rather_than_a_spin`:
  well under the 20 ceiling in the correct 1 s window (a handful of attempts).
- `A_connection_held_just_past_the_floor_still_backs_off`: well under the 16
  ceiling in the correct 3 s window.
- `A_reconnect_after_a_held_connection_does_not_inherit_the_previous_backoff`:
  reconnects inside the 150 ms window.

---

## 5. Counterfactuals — verbatim red, each reverted

All patches transient. Each followed by `git checkout -- <file>` and
`git diff --exit-code -- src/ tests/ScenarioSimulator.Tests/Fakes/` before the
next, confirmed clean every time (see §7).

### CF1 — `MqttPublisher.cs`: `backoff.ResetIfHeld(...)` → `backoff.Reset()`

Expected red: dies-on-arrival (hundreds+ attempts); held-just-past-the-floor.

```
[xUnit.net]     MqttPublisherBackoffTests.A_connection_that_dies_on_arrival_is_retried_with_a_delay_rather_than_a_spin [FAIL]
  Shouldly.ShouldAssertException : spun
    should be
False
    but was
True
    the loop reached 489 attempts inside a 1s window against a 100 ms backoff, which allows about five.

[xUnit.net]     MqttPublisherBackoffTests.A_connection_held_just_past_the_floor_still_backs_off [FAIL]
  Shouldly.ShouldAssertException : publisher.Client.ConnectAttempts
    should be less than
16
    but was
26

Failed!  - Failed:     2, Passed:     1, Skipped:     0, Total:     3, Duration: 4 s
```

Reverted; `git diff --exit-code` clean.

### CF2 — `MqttBackoff.cs`: yardstick → `first` (the pre-fix constant)

Expected red: held-just-past-the-floor only; dies-on-arrival stays green.

```
[xUnit.net]     MqttPublisherBackoffTests.A_connection_held_just_past_the_floor_still_backs_off [FAIL]
  Shouldly.ShouldAssertException : publisher.Client.ConnectAttempts
    should be less than
16
    but was
26

Failed!  - Failed:     1, Passed:     2, Skipped:     0, Total:     3, Duration: 5 s
```

Confirmed: only the one test failed; dies-on-arrival and the reconnect test
stayed green. This is the counterfactual that matters most — it is the defect
the doubled yardstick fixed. Reverted; `git diff --exit-code` clean.

### CF3 — `MqttPublisher.cs`: delete the `ResetIfHeld` line

Expected red: reconnect-after-a-held-connection (inherits 320-480 ms).

```
[xUnit.net]     MqttPublisherBackoffTests.A_reconnect_after_a_held_connection_does_not_inherit_the_previous_backoff [FAIL]
  Shouldly.ShouldAssertException : reconnected
    should be
True
    but was
False
    the reconnect took 157 ms against a 150 ms window — still waiting out the backoff the outage had grown.

Failed!  - Failed:     1, Passed:     2, Skipped:     0, Total:     3, Duration: 5 s
```

Reverted; `git diff --exit-code` clean.

### CF4 — `FakeMqttClient.cs`: both new controls become no-ops

Expected red: the fake contract tests 1 and 3.

```
[xUnit.net]     FakeMqttClientDropHoldContractTests.A_connection_dropped_on_arrival_raises_one_disconnect_for_a_connection_that_was_up [FAIL]
  Shouldly.ShouldAssertException : disconnects.Count
    should be
1
    but was
0

[xUnit.net]     FakeMqttClientDropHoldContractTests.A_held_connection_is_up_until_the_hold_ends_and_then_drops [FAIL]
  Shouldly.ShouldAssertException : await WaitUntilAsync(disconnects.Count == 1)
    should be
True
    but was
False
    the held connection never dropped

Failed!  - Failed:     2, Passed:     1, Skipped:     0, Total:     3, Duration: 10 s
```

Test 2 (`A_refused_connect_is_not_dropped_as_if_it_had_connected`) stayed
green, as expected — the no-op controls do not break the refusal contract.
Reverted; `git diff --exit-code` clean.

### CF5 (new) — `MqttPublisher.cs`: the injection seam itself ignores the parameter

Patch: `this.backoff = backoff ?? new MqttBackoff();` →
`this.backoff = new MqttBackoff(); _ = backoff;` (Sonar-discard, see §6).
No prior counterfactual patches this exact line — CF1-CF4 patch the backoff's
own logic or the fake, never the constructor's injection seam. Run against
the **whole assembly** (not just the backoff file) to confirm the bypass is
isolated to exactly the test the reviewer named:

```
[xUnit.net]     MqttPublisherBackoffTests.A_reconnect_after_a_held_connection_does_not_inherit_the_previous_backoff [FAIL]
  Shouldly.ShouldAssertException : reconnected
    should be
True
    but was
False
    the reconnect took 156 ms against a 150 ms window — still waiting out the backoff the outage had grown.

Failed!  - Failed:     1, Passed:    66, Skipped:     0, Total:    67, Duration: 8 s
```

Confirmed: exactly one test failed out of the full 67 — the direct detector
for a bypass of the injected `MqttBackoff` parameter. Reverted; `git diff
--exit-code` clean.

### CF6 (new) — `FakeMqttClient.cs`: `DropEveryConnectionImmediately` moved ahead of the refusal branch

Patch: the immediate-drop check moved to run before `if (refusals > 0)`
(temporarily — it correctly sits after it today). Run against the whole
assembly:

```
[xUnit.net]     FakeMqttClientDropHoldContractTests.A_refused_connect_is_not_dropped_as_if_it_had_connected [FAIL]
  Shouldly.ShouldAssertException : result.ResultCode
    should be
MqttClientConnectResultCode.NotAuthorized
    but was
MqttClientConnectResultCode.Success
    the refusal branch in ConnectAsync returns before the success path, so a refusal must win over a drop control that is also armed.

Failed!  - Failed:     1, Passed:    66, Skipped:     0, Total:    67, Duration: 5 s
```

Confirmed: exactly one test failed out of the full 67 — the refusal contract
test. This is the test that was previously never observed red (CF4's no-op
patch correctly leaves it green, since it tests that refusal survives *without*
the controls, not their ordering). Reverted; `git diff --exit-code` clean.

---

## 6. The `_ = x;` discard workaround

Two of the six counterfactual patches removed the only remaining read of a
field or method, which Sonar's Release-build analyzers (`TreatWarningsAsErrors`)
then flag as an error rather than a warning:

- **CF1**: removing the argument to `ResetIfHeld` and replacing the call left
  `connectedAt` unused → S1481. Fixed with `_ = connectedAt;` immediately
  after.
- **CF2**: replacing the yardstick expression left the `servedDelay` field
  read nowhere else in `ResetIfHeld` (it is still written in `Next()`, but
  the analyzer flags an unread field as S1450) → fixed with `_ = servedDelay;`.
- **CF4**: disabling both controls left `HoldThenDropAsync` uncalled → S1144.
  Fixed with a method-group discard, `_ = (Func<TimeSpan, Task>)HoldThenDropAsync;`.
- **CF5**: the discarded constructor parameter → `_ = backoff;`, as the
  reviewer's instruction specified.
- CF3 and CF6 needed no discard — CF3 removes the only reader of `connectedAt`
  together with its declaration, and CF6 only reorders two existing blocks.

Each discard was part of the transient patch only. After every counterfactual,
`git checkout -- <file>` removed it along with the rest of the patch, and
`git diff --exit-code -- src/ tests/ScenarioSimulator.Tests/Fakes/` confirmed
nothing — discard included — survived into the committed state. None of the
six discard workarounds appears in the final diff.

---

## 7. Final state

Full clean rebuild (`obj`/`bin` for both the `ScenarioSimulator` and
`ScenarioSimulator.Tests` projects removed first, so this is not reusing a
cached, possibly-stale binary):

```
Build succeeded.
    19 Warning(s)
    0 Error(s)
```

The 19 warnings are all pre-existing SonarAnalyzer advisories (S107/S134/S138/
S1541) across `src/ScenarioSimulator/`, carved out of `TreatWarningsAsErrors`
by ADR-0084. Among them, as expected:

```
src/ScenarioSimulator/Mqtt/MqttPublisher.cs(95,27): warning S107: Constructor has 5 parameters, which is greater than the 4 authorized.
```

This is the internal constructor's fifth parameter (`MqttBackoff? backoff =
null`) — accepted in `spec.md` §3.3, non-blocking per ADR-0084.

Full assembly, final re-run after every counterfactual reverted:

```
Passed!  - Failed:     0, Passed:    67, Skipped:     0, Total:    67, Duration: 5 s - SmartSentinelEye.ScenarioSimulator.Tests.dll (net10.0)
```

`git diff --stat -- src/` is empty. `git diff --exit-code origin/develop --
tests/ScenarioSimulator.Tests/MqttPublisherDropAccountingTests.cs
tests/ScenarioSimulator.Tests/MqttPublisherProtocolPinTests.cs` is clean —
both frozen characterisation files are byte-identical to `origin/develop`.

---

## 8. Findings not fixed here

Carried over from `spec.md` §7, unchanged by this work:

- The simulator's `MqttBackoff` has no direct unit tests of its own (a direct
  `MqttBackoffTests` copy of EventIngestion's suite is out of scope for this
  spec — `ResetIfHeld` is covered indirectly, through the publisher's loop).
- The fake's `IsConnected` property is an unsynchronised `bool`, read and
  written from different async continuations without a memory barrier. Not a
  practical race in this single-threaded-per-test usage, but not a guarantee
  either; out of scope here (EventIngestion's twin carries the same shape and
  PR #2585 is the only other in-flight change touching either fake).
