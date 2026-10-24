# Evidence 446: an attempt-timeout test runs on virtual time

Every block below is runner output, quoted as observed (filtered with `grep` for the failure lines; nothing edited).
Commands: `dotnet test tests/ServiceDefaults.Tests -c Debug --filter "FullyQualifiedName~The_attempt_timeout_still_bounds|FullyQualifiedName~An_opted_in_POST_whose_attempt_timed_out"`.
Facts under test use attempt timeout 1 h, total 10 h, SamplingDuration 3 h (all virtual; real time cannot reach them).

## Green on the unplanted tree

```
Passed!  - Failed:     0, Passed:     2, Skipped:     0, Total:     2, Duration: 590 ms - SmartSentinelEye.ServiceDefaults.Tests.dll (net10.0)
```

## C1 — attempts not bounded (red)

Plant (`src/ServiceDefaults/Extensions.cs`, before `ConfigureHttpClientDefaults`):
`PostConfigure<HttpStandardResilienceOptions>("-standard", o => { o.AttemptTimeout.Timeout = o.TotalRequestTimeout.Timeout - 1 ms; o.CircuitBreaker.SamplingDuration = 24 h; })`.
The first attempt of this plant (SamplingDuration 100 days) produced the *wrong* red, an options-validation exception
(`SamplingDuration: The field -standard.CircuitBreaker.SamplingDuration must be between 00:00:00.5000000 and 1.00:00:00.`),
which is why the plant sets 24 h. With it:

```
  Failed SmartSentinelEye.ServiceDefaults.Tests.Resilience.PerAuthorityPipelineRiskTests.The_attempt_timeout_still_bounds_each_attempt_to_a_new_authority [574 ms]
   Shouldly.ShouldAssertException : attemptToken.IsCancellationRequested
    should be
True
    but was
False
    attempt 1 outlived its timeout
  Failed SmartSentinelEye.ServiceDefaults.Tests.Resilience.RetryEveryMethodAdversarialTests.An_opted_in_POST_whose_attempt_timed_out_is_sent_again [1 m]
   Shouldly.ShouldAssertException : await server.AttemptArrivedAsync(HangGuard)
    should be
True
    but was
False
    attempt 2 never reached the server
Failed!  - Failed:     2, Passed:     0, Skipped:     0, Total:     2, Duration: 1 m - SmartSentinelEye.ServiceDefaults.Tests.dll (net10.0)
```

The per-authority fact is now caught earlier, by the token check, than by the guard; the opted-in fact has no
boundary check and reaches the guard message.

## C2 — a timed-out attempt is not retried (red)

Plant: `IdempotentRetry.RetryIdempotentMethodsOnly`'s final `return` gains
`&& arguments.Outcome.Exception is not Polly.Timeout.TimeoutRejectedException`.

```
  Failed ...PerAuthorityPipelineRiskTests.The_attempt_timeout_still_bounds_each_attempt_to_a_new_authority [1 m]
    attempt 2 never reached the server
  Failed ...RetryEveryMethodAdversarialTests.An_opted_in_POST_whose_attempt_timed_out_is_sent_again [1 m]
    attempt 2 never reached the server
Failed!  - Failed:     2, Passed:     0, Skipped:     0, Total:     2, Duration: 1 m - SmartSentinelEye.ServiceDefaults.Tests.dll (net10.0)
```

(each preceded by `await server.AttemptArrivedAsync(HangGuard) should be True but was False`)

## C3 — opt-in lost (red on the opted-in fact, GET fact green = control)

Plant: `IsMarked` returns `false && ...`.

```
  Failed ...RetryEveryMethodAdversarialTests.An_opted_in_POST_whose_attempt_timed_out_is_sent_again [1 m]
   Shouldly.ShouldAssertException : await server.AttemptArrivedAsync(HangGuard)
    should be
True
    but was
False
    attempt 2 never reached the server
Failed!  - Failed:     1, Passed:     1, Skipped:     0, Total:     2, Duration: 1 m - SmartSentinelEye.ServiceDefaults.Tests.dll (net10.0)
```

The one pass is the per-authority (GET) fact.

## C4 — the flake, made harmless

Plant: a token-observing `await Task.Delay(60 ms, cancellationToken)` before the increment in both `HangingServer`s.

New facts: green on both.

```
Passed!  - Failed:     0, Passed:     2, Skipped:     0, Total:     2, Duration: 799 ms - SmartSentinelEye.ServiceDefaults.Tests.dll (net10.0)
```

Pre-change baseline (facts and fake from `66c1c2eb`, real 50 ms / 100 ms timers; delay 60 ms for the per-authority
fact, 150 ms for the opted-in fact because its attempt timeout is 100 ms):

```
  Failed ...PerAuthorityPipelineRiskTests.The_attempt_timeout_still_bounds_each_attempt_to_a_new_authority [824 ms]
    should be
4
    but was
2
  Failed ...RetryEveryMethodAdversarialTests.An_opted_in_POST_whose_attempt_timed_out_is_sent_again [835 ms]
    should be
4
    but was
0
Failed!  - Failed:     2, Passed:     0, Skipped:     0, Total:     2, Duration: 875 ms - SmartSentinelEye.ServiceDefaults.Tests.dll (net10.0)
```

The count lost is whatever the load decides (`2` here, `3` in the CI failure of spec premise 1): the failure shape
`Attempts should be 4 but was N<4` is the same. With a 60 ms delay the opted-in old fact stays green (60 < 100 ms), so the
plant needs to exceed its attempt timeout to reproduce.

## C5 — boundary (S2)

Plant: `PostConfigure` `AttemptTimeout = AttemptTimeout / 2` (30 min).

```
  Failed ...PerAuthorityPipelineRiskTests.The_attempt_timeout_still_bounds_each_attempt_to_a_new_authority [562 ms]
   Shouldly.ShouldAssertException : attemptToken.IsCancellationRequested
    should be
False
    but was
True
    attempt 1 was cut short one tick before its timeout
Failed!  - Failed:     1, Passed:     1, Skipped:     0, Total:     2, Duration: 621 ms - SmartSentinelEye.ServiceDefaults.Tests.dll (net10.0)
```

(the passing fact is the opted-in one, which does not carry the boundary check by design).

## C6 — clock bypassed (S1)

Plant (test side): `if (clock is not null && false)` in both `Compose`, so Polly falls back to `TimeProvider.System`.

```
  Failed ...PerAuthorityPipelineRiskTests.The_attempt_timeout_still_bounds_each_attempt_to_a_new_authority [573 ms]
    attempt 1 outlived its timeout
  Failed ...RetryEveryMethodAdversarialTests.An_opted_in_POST_whose_attempt_timed_out_is_sent_again [1 m]
    attempt 2 never reached the server
```

Verbatim summary line: `Failed!  - Failed:     2, Passed:     0, Skipped:     0, Total:     2, Duration: 1 m - SmartSentinelEye.ServiceDefaults.Tests.dll (net10.0)`.
With the short (50/100 ms) durations this plant would have passed by real time; with hours it cannot.

## Stress tally

Final test code (`d76440b3`), full `tests/ServiceDefaults.Tests` (398 tests), `DOTNET_PROCESSOR_COUNT=1`, 16 CPU burners,
6 parallel `dotnet test --no-build` loops x 8 iterations:

```
48 runs: 48 x "Passed!  - Failed:     0, Passed:   398, Skipped:     0, Total:   398"   (0 failures of any test)
```

(Previous 48/48 on `78d70436`, the 50 ms/100 ms version, was green too; baseline before the rewrite: 5/48.)

## Hang-guard sizing

Question: what does the 60 s `HangGuard` actually guard against? Method: guard temporarily 10 s, timestamped logging
(thread, pool busy/pending/threads) around `Advance`, the timer callback, `SendAsync` entry and the arrival wait, and
the three other 10 s `WaitAsync` guards wrapped to log a failure; same stress configuration, 36 runs (6 loops x 6).

- 36/36 runs green with a 10 s guard; the three other 10 s guards never fired.
- 288 measured "attempt reaches the server" waits: median 11 ms, p99 2060 ms, **max 2443 ms**.
- The slow waits sit between the retry's cancel and `SendAsync` entry (Advance and the timer callbacks take 1-6 ms
  each, on virtual time). Example: attempt 3 -> 4 gap of 2.4 s with `pool(busy=3,pending=6,threads=8)`: the
  continuation that carries the retry to the handler was queued behind other work in a thread pool that grows slowly
  at `DOTNET_PROCESSOR_COUNT=1`. That is thread-pool scheduling latency (starvation observed, not guessed).
- Nothing in the pipeline waits on a real-time event with the clock virtual: no production defect, no issue to file.

So 60 s is a hang detector with a ~25x margin over the worst observed delay; it asserts no duration.

## `git diff --stat -- src`

Empty (verified after each plant was reverted and at the end).
