# Spec 448: An attempt-timeout test runs on virtual time

**Issue:** #3078 (feature-level, on Project #13) · **Branch:** `test/3078-attempt-timeout`
**ADRs:** ADR-0143 (retry only idempotent methods; the opt-in whose timed-out attempt is re-sent), ADR-0052 / ADR-0054
(hand-written fakes; `FakeTimeProvider` is deliberately not referenced anywhere), ADR-0053 (sentence-style names),
ADR-0139 / ADR-0144 (phase-4a colours; autonomous lane). #2959 (per-authority pipeline selection) is the behaviour the
first test guards.
**Lane:** autonomous · **Phase-4a colour:** **characterisation** (behaviour-preserving; no production file changes) —
the red evidence is the planted-violation counterfactual set in plan §Counterfactuals.
**Engineer:** test-writer (authors), backend-engineer (counterfactuals + stress verification; no production edit kept).
**Security-sensitive:** no. **New ADR needed:** no.
**Scope:** the two attempt-timeout facts that have flaked in CI. `The_total_timeout_still_bounds_the_whole_request_to_a_new_authority`
(asserts `< 4`, a direction starvation cannot break) and `CameraSimProvisionerResilienceScopeLeakTests` (asserts no
secret in the log, not a count) are read and **out of scope**.
**Written 2026-10-24 against `develop` at `9b8b33a3`.**

---

## Premise, checked at `9b8b33a3`

1. **Holds — the flake reproduces.** `tests/ServiceDefaults.Tests` (full suite, `DOTNET_PROCESSOR_COUNT=1`, 16 CPU
   burners, 6 parallel `dotnet test` loops × 4 = 24 runs) failed 3/24, then 2/24 on a second batch:
   ```
   Failed SmartSentinelEye.ServiceDefaults.Tests.Resilience.PerAuthorityPipelineRiskTests.The_attempt_timeout_still_bounds_each_attempt_to_a_new_authority [770 ms]
     Shouldly.ShouldAssertException : server.Attempts
       should be
   4
       but was
   3
   Failed SmartSentinelEye.ServiceDefaults.Tests.Resilience.RetryEveryMethodAdversarialTests.An_opted_in_POST_whose_attempt_timed_out_is_sent_again [1 s]
     Shouldly.ShouldAssertException : server.Attempts
       should be
   4
       but was
   0
   ```
   With the same 4 parallel loops, no CPU limit and 8 burners: 0/20. Filtered runs never failed — it needs the
   whole suite's parallelism.
2. **Root cause — not jitter, not the retry delay.** Both harnesses set `Retry.Delay = Zero`, `UseJitter = false`.
   A diagnostic `Retry.OnRetry` counter in a failing run reported `DIAG pipeline attempts=4` against
   `server.Attempts = 3`: **Polly made four attempts; one never reached the server.** The attempt timeout is a real
   timer (50 ms / 100 ms) that starts when the attempt starts; on a starved thread pool the continuation that carries
   the request through the handler chain to the primary handler runs after the timer fired, a token-observing step on
   the way throws, and the attempt is counted by Polly but not by `HangingServer`. `0` is all four attempts losing
   that race.
3. **Deterministic reproduction.** A token-observing `await Task.Delay(60 ms, cancellationToken)` planted in front of
   `HangingServer`'s increment fails the first test every run with the CI message (`should be 4 but was 3`). The same
   delay ignoring the token (`CancellationToken.None`) passes — Polly's timeout is cooperative and waits for the
   attempt, so only a step that observes the cancelled token before the server loses the count.
4. **A deterministic design exists.** The defaults pipeline takes the container's `TimeProvider` (the class's
   coupling tests already rely on it for the breaker's break duration). With a hand-written timer-capable fake, an
   attempt timeout fires only when the test advances time, and the test advances only after the server has seen the
   attempt — the race disappears instead of being widened.

## User story (P1, the only one)

As a maintainer, I want the two attempt-timeout facts to count attempts on virtual time, so a loaded CI runner can no
longer turn a correct pipeline red, while a pipeline that stops bounding each attempt still turns them red.

### Acceptance scenarios

```gherkin
Scenario: each attempt to a new authority is bounded by the attempt timeout (happy)
  Given the defaults pipeline on a virtual clock, attempt timeout 50 ms, total 10 s, a hanging upstream
  When a GET is sent and, for each of 4 attempts, the server sees the attempt and the clock advances 50 ms
  Then the server saw exactly 4 attempts and the call fails with TimeoutRejectedException

Scenario: an attempt is not cut short (boundary)
  Given an attempt has reached the hanging server
  When the clock advances 1 tick less than the attempt timeout
  Then the call is still pending and no further attempt has started

Scenario: an opted-in POST whose attempt timed out is sent again (ADR-0143)
  Given an opted-in client on a virtual clock, attempt timeout 100 ms
  When each of 4 attempts reaches the server and the clock advances 100 ms
  Then the server saw exactly 4 POSTs and the call fails with TimeoutRejectedException

Scenario: a slow road to the server no longer changes the count (the flake, made deterministic)
  Given a token-observing 60 ms real delay in front of the server's count
  Then both facts stay green, because virtual time does not move while the request travels

Scenario: a pipeline that no longer bounds attempts fails loudly, not by hanging (conflict)
  Given the attempt timeout is planted out of reach (PostConfigure, plan §Counterfactuals C1)
  Then the fact fails within its real-time guard naming the attempt that never arrived, rather than hanging the suite
```

Bad-request and auth scenarios: N/A — no endpoint, no caller identity; a test-harness change.

## Functional requirements

- **FR-001** Both facts drive the attempt and total timeouts from a hand-written timer-capable `TimeProvider`
  registered in the test's container; no real-time assertion remains in either.
- **FR-002** Each fact still asserts exactly 4 server-observed attempts and a `TimeoutRejectedException`.
- **FR-003** The per-authority fact additionally asserts the boundary (pending at timeout − 1 tick).
- **FR-004** The only real-time element is a hang guard (10 s, the class's existing `WaitAsync` convention) whose
  failure message names the attempt number that did not arrive.
- **FR-005** No production file changes; no other assertion in either class changes.

## Independent end-to-end test procedure

1. `dotnet test tests/ServiceDefaults.Tests` green.
2. Stress loop of premise 1 (script in plan §Verification): **0 failures in ≥ 48 full-suite runs** (baseline 5/48).
3. Counterfactuals C1-C4 of plan, each run, quoted, reverted.

## Locked choices

xUnit + Shouldly, hand-written fake (no `FakeTimeProvider`, ADR-0052/0054), real `AddServiceDefaults()` composition
kept (the facts must exercise the production pipeline, not a hand-built one).

## Latency budget

N/A — test-only; no leg of constitution §IV is touched.
