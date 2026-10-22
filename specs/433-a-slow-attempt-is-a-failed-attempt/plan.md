# Implementation Plan: A slow probe attempt is a failed attempt

**Spec**: `./spec.md` | **Issue**: #3087 | **Branch**: `test/3087-probe-attempt-timeout`

## Scope and constitution check

- Test infrastructure only: `tests/Integration.Tests/Fixtures/`. No bounded
  context, no `src/` change, no contract, no migration, no AppHost resource.
- §Testing: behaviour-changing → **red first** (ADR-0139). The seam is landed
  behaviour-preserving first so the reds are assertion failures, not compile
  errors (spec 430 / 424 / 084 precedent).
- ADR-0144: no ADR written; the owner ruled "No ADR". ADR-0143 is applied, not
  amended (the fixture's factory clients keep their resilience handler).
- §IV latency: N/A.

## The seam — chosen and why

Two candidates were weighed:

1. **A static predicate** `IsAttemptTimeout(Exception, CancellationToken)` tested
   in isolation. Smallest, but proves only the predicate: deleting the `catch`
   that calls it from either loop leaves every test green. Rejected on its own —
   it is an assertion that cannot see the defect.
2. **The loop itself, extracted as a static helper** and driven by a real
   `HttpClient` over a scripted handler. Proves the loop's behaviour, including
   that a genuine `HttpClient.Timeout` (not a hand-built exception) is retried
   and that the fixture token still wins. **Chosen.**

```csharp
// AspireFixture.cs (internal static, beside ProbeExhaustedAsync)
internal static async Task<bool> PollUntilSuccessAsync(
    HttpClient probe,
    string path,
    int attempts,
    TimeSpan delay,
    CancellationToken cancellationToken)
```

Returns `true` on the first success status, `false` after `attempts` failed
attempts; the `Task.Delay(delay, cancellationToken)` between attempts is kept.
The two callers become:

```csharp
if (await PollUntilSuccessAsync(probe, "/v3/paths/list", 60, TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false))
{
    return;
}

throw await ProbeExhaustedAsync("MediaMTX /v3/paths/list was not reachable after 60 attempts.", …);
```

so the `throw await ProbeExhaustedAsync("… after 60 attempts …")` text that spec
430's source-scan test matches is untouched (A3). Returning `bool` rather than
taking an exhaustion callback keeps the snapshot code out of the helper and out
of the new tests.

The catch inside the helper after the fix:

```csharp
catch (HttpRequestException) { }
catch (TimeoutRejectedException) { }
catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { }
```

A filter on the fixture token, not a separate predicate method — one line the
loop tests already cover; a named predicate would be a second seam with nothing
left to prove (spec A2: never read `ex.CancellationToken`).

`WaitForServiceHealthAsync` is **not** routed through the helper: its catch set
already handles its only timeout shape, and adding `TaskCanceledException` there
is a behaviour change the owner did not rule on. Its loop is the third copy of the
same shape; recorded as a follow-up candidate, not done.

## Tests — `tests/Integration.Tests/Fixtures/AspireFixtureProbeLoopTests.cs` (new)

`[Trait("Category", "FixtureLogic")]`, no `[Collection]`, no Docker. A private
`ScriptedHandler : HttpMessageHandler` counting `Attempts`, each step one of:
`Hang` (`await Task.Delay(Timeout.Infinite, requestToken)`), `Throw(Exception)`,
`Answer(HttpStatusCode)`. `delay = TimeSpan.Zero`, `attempts = 3`.

| # | Test | Red on T001? |
|---|---|---|
| 1 | `A_request_that_hits_the_client_timeout_counts_as_a_failed_attempt` — `HttpClient.Timeout = 50 ms`, all `Hang` → returns `false`, `Attempts == 3` | **Red**: `TaskCanceledException` escapes on attempt 1 |
| 2 | `A_resilience_timeout_counts_as_a_failed_attempt` — all `Throw(new TimeoutRejectedException())` → `false`, 3 | **Red**: escapes on attempt 1 |
| 3 | `An_attempt_after_a_timed_out_one_can_still_succeed` — `Hang`, `Answer(200)`, 50 ms timeout → `true`, 2 | **Red** |
| 4 | `The_fixtures_own_cancellation_still_propagates` — all `Hang`, `HttpClient.Timeout = Infinite`, token cancelled after 100 ms → throws `OperationCanceledException` (`Should.ThrowAsync<OperationCanceledException>`, which accepts the derived `TaskCanceledException`), `Attempts == 1` | Green (characterisation — must stay green) |
| 5 | `A_timeout_shaped_exception_under_a_cancelled_fixture_token_propagates` — the handler cancels the fixture token's source and then throws `TaskCanceledException` (a pre-cancelled token would short-circuit `GetAsync` before the handler and prove nothing about the filter) → throws `OperationCanceledException`, `Attempts == 1` | Green (characterisation) |
| 6 | `A_refused_connection_counts_as_a_failed_attempt` — all `Throw(new HttpRequestException())` → `false`, 3 | Green (characterisation of today's catch) |

Tests 4-6 characterise the surrounding behaviour. They do **not** pin the `when`
filter: removing it leaves all six green (observed, T004). When the fixture token
is cancelled, a swallowed `TaskCanceledException` is followed by
`Task.Delay(delay, cancellationToken)`, which throws its own
`TaskCanceledException` — the same type, `Attempts == 1`. Identity cannot
separate them either: `HttpClient` replaces the handler's exception on a cancelled
caller token even when it carries that token (tried: `new
TaskCanceledException("x", null, token)` then `ShouldBeSameAs` fails with the
filter present; the propagated exception has no inner). The filter is a
documented **equivalent mutant**: it narrows the catch but no observable outcome
differs, so no test is committed for it.

## Files

- `tests/Integration.Tests/Fixtures/AspireFixture.cs` — helper + two callers.
- `tests/Integration.Tests/Fixtures/AspireFixtureProbeLoopTests.cs` — new.
- `tests/Integration.Tests/ci-shards/shard-1.filter` — names
  `FixtureRetryPolicyTests`, so the new class needs an entry beside it (a missing
  entry fails deterministically).
