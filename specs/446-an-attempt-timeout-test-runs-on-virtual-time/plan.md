# Plan 446: An attempt-timeout test runs on virtual time

[spec](spec.md) · [tasks](tasks.md) · Issue #3078

## Slice

One project, `tests/ServiceDefaults.Tests`. No bounded context, no layer, no production code, no messaging, no
migration. Boundary rules: unchanged (no new project reference; test projects do not reference one another, so the
fake is copied, as `StreamDistribution.Infrastructure.Tests/Fakes/SchedulingTimeProvider.cs` itself was).

| File | Change |
|---|---|
| `tests/ServiceDefaults.Tests/Fakes/SchedulingTimeProvider.cs` | **new** — hand-written timer-capable clock |
| `tests/ServiceDefaults.Tests/Resilience/PerAuthorityPipelineRiskTests.cs` | the attempt-timeout fact; `HangingServer` gains a per-attempt signal |
| `tests/ServiceDefaults.Tests/Resilience/RetryEveryMethodAdversarialTests.cs` | the opted-in-POST timeout fact; `Compose` gains an optional clock; `HangingServer` per-attempt signal |

No new test class, so no `ci-shards/shard-N.filter` entry. The fake is not a test class.

## The fake

Start from `tests/StreamDistribution.Infrastructure.Tests/Fakes/SchedulingTimeProvider.cs` (lock-guarded list,
`Advance` fires due one-shots earliest-first on the calling thread). Two differences, both required by how a
`CancellationTokenSource` uses a custom provider, and both to be **observed**, not assumed (T001):

1. **`ITimer.Change` reschedules** (`DueAt = now + dueTime`, `Fired = false`; `InfiniteTimeSpan` disarms). Every
   existing fake in the repo returns `true` and ignores it. `CancellationTokenSource.CancelAfter` on a
   provider-backed CTS arms the timer through `Change`; if Polly's pool takes that path (Polly.Core as pinned by
   Microsoft.Extensions.Http.Resilience 10.11.0), an ignoring fake never times out and the fact hangs to its guard.
2. **(Dropped after probing: no zero due time is ever created by these facts, so the branch was removed. The original rationale:)** a zero due time fires at once. If any zero-length delay
   (the retry's `Delay = Zero`) goes through the provider, a fake that waits for `Advance` would stall between
   attempts.

Doc comment states why it is a copy and why it differs (the why, per CLAUDE.md), nothing else. `ManualClock` in
`PerAuthorityPipelineRiskTests` stays as is — the coupling/hazard facts use it and need no timers.

## Test shape (both facts)

`HangingServer` keeps `Attempts` and adds `Task<bool> AttemptArrivedAsync(TimeSpan guard)` backed by a
`SemaphoreSlim` released after the increment (one release per attempt, so attempts cannot be missed or
double-counted). The fact:

```
clock = new SchedulingTimeProvider(start); compose with clock (attempt T = 1 h, total 10 h, SamplingDuration 3 h: durations real time cannot reach; the validator caps SamplingDuration at 1 day and requires it >= 2 x AttemptTimeout)
call = client.GetAsync(...)            // or PostAsync for the opted-in fact; not awaited
for n in 1..4:
    (await server.AttemptArrivedAsync(Guard)).ShouldBeTrue($"attempt {n} never reached the server")
    [per-authority fact only] clock.Advance(T - 1 tick); attemptToken(n).IsCancellationRequested.ShouldBeFalse()
    clock.Advance(1 tick)   // [per-authority fact only] then attemptToken(n).IsCancellationRequested.ShouldBeTrue()
await Should.ThrowAsync<TimeoutRejectedException>(() => call.WaitAsync(Guard))
server.Attempts.ShouldBe(4)
```

Why it is race-free: the attempt timer is created by the timeout strategy before the inner handler runs, so "the
server saw attempt n" implies attempt n's timer exists; nothing can fire until the test advances, and the test
advances only after arrival. The boundary check cannot race either — no timer is due, so no new attempt can start.
`HangGuard` = 60 s real time (observed worst scheduling delay 2.4 s; see `evidence.md` §Hang-guard sizing); it only bounds a
hang and asserts nothing about duration. Existing class-level comments stay; the fact's `<summary>` gains one sentence
saying the clock is virtual so a starved runner cannot reorder the timer and the request (the why).

What is preserved, explicitly: real `AddServiceDefaults()`; per-authority selection (`SelectPipelineByUpstreamAuthority`)
on a cold authority; exactly 4 attempts; `TimeoutRejectedException`; the opted-in POST being re-sent after a timeout.
What is **added**: the "not cut short" boundary in the per-authority fact. The `RetryEveryMethod` fact keeps its
original scope (re-sent, 4) and does not gain the boundary — that is the other fact's job.

## Phase 4a — colour: CHARACTERISATION (declared, ADR-0144)

Behaviour-preserving: production untouched, so the subject is the pipeline's existing behaviour. Evidence chain:
(a) baseline flake rate recorded in spec premise 1 (5/48); (b) rewritten facts green; (c) every counterfactual below
red for the stated reason; (d) stress loop 0/≥48. A rewritten fact arriving green is expected here — the red half is
(c), and a counterfactual that stays green blocks the slice.

### Counterfactuals (each planted, run, failure quoted in the PR, reverted; `git diff --stat src` empty at the end)

- **C1 — attempts not bounded.** In `src/ServiceDefaults/Extensions.cs` add
  `PostConfigure<HttpStandardResilienceOptions>("-standard", o => o.AttemptTimeout.Timeout = o.TotalRequestTimeout.Timeout - 1 ms)`.
  Expected: both facts red at the guard, `attempt 2 never reached the server` (per-authority: or at the boundary
  check). If options validation rejects the value instead, adjust the plant until the red is the guard, and say so —
  a validation exception is the wrong red.
- **C2 — a timed-out attempt is not retried.** PostConfigure `Retry.ShouldHandle` to exclude
  `TimeoutRejectedException`. Expected: `attempt 2 never reached the server`.
- **C3 — opt-in lost.** Make `IdempotentRetry.IsMarked` return `false`. Expected: the opted-in fact red
  (`attempt 2 never reached the server`); the per-authority (GET) fact green — the control.
- **C4 — the flake, made harmless.** Plant `await Task.Delay(60 ms, cancellationToken)` before `HangingServer`'s
  increment in both classes. Before the change this gave `should be 4 but was 3` every run (spec premise 3); after it
  both facts must be **green**. This is the proof the race is gone rather than widened.

- **C5 — boundary (S2).** `PostConfigure` `AttemptTimeout = AttemptTimeout / 2`. Expected: the per-authority fact red
  at `attempt 1 was cut short one tick before its timeout`; the opted-in fact (no boundary check) stays green.
- **C6 — the clock is bypassed.** Register no `TimeProvider` in `Compose` (Polly falls back to `TimeProvider.System`).
  With hour-long timeouts nothing can fire in real time: both facts red.
- C1 note: the options validator caps `SamplingDuration` at 1 day, so a C1 plant that sets the attempt timeout to
  `Total - 1 ms` must also set `SamplingDuration = 24 h` (>= 2 x attempt) or the red is a validation exception.

Not plantable here, recorded so nobody looks for it: a selector that shares one pipeline across authorities does not
affect these facts (one provider, one authority per fact); isolation is pinned by the coupling facts in the same class.

## Verification

Stress loop (premise 1), scratch script, not committed: `DOTNET_PROCESSOR_COUNT=1`, 16 busy-loop burners, 6 parallel
`dotnet test tests/ServiceDefaults.Tests --no-build` loops × 8 iterations. Pass = 0 failures of either fact. Report
the run count and any unrelated failure verbatim (do not fix it here).

## Risks

- **Polly bypasses the provider for the timeout** (pool keyed on `TimeProvider.System` only?). T001 observes it: a
  fact that hangs to its guard on the unplanted pipeline means the fake is not reached — stop and report; do not
  fall back to widening the real timeout (that weakens the gate, ADR-0144).
- **Inline continuations.** `Advance` cancels on the test thread; Polly's retry may run the next attempt inline up to
  the server. The semaphore makes that harmless; do not add `Task.Yield`/sleeps to "settle".
- **Other `TimeProvider` consumers** in the composed container (token provider, revocation refresher) are not started
  — the host is never run, only `BuildServiceProvider()`, as today.
