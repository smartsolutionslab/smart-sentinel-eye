# Spec 223 — Verification (phase 4b + phase 5 counterfactuals)

**Date**: 2026-09-23 · **Issue**: #2441 · **Engineer**: `backend-engineer`
**Scope**: T006–T011 (phase 4b) plus CF-A/CF-B (tasks.md T012/T013). Phase 5's
§10 six-step manual dev-mode procedure (tasks.md T015) is **out of scope** for
this pass and was not attempted.

All commands were run from the repo root of the `2441-a-429-a-test-can-provoke`
worktree (`D:\Github\sse-2441`). Before every stack boot, the running-process
list was checked for an existing AppHost/testhost — none was found at any
point; every boot below was the only Aspire stack running on the machine at
the time.

---

## T004/T005 — step 1, the new test against the current worktree: RED

Captured by the phase-4a `test-writer` pass, before the AppHost line below
existed. `dotnet test tests/Integration.Tests/ --filter
"FullyQualifiedName~IngestBackpressureIntegrationTests"`, verbatim:

```
Failed SmartSentinelEye.Integration.Tests.EventIngestion.IngestBackpressureIntegrationTests.A_burst_past_the_write_limit_is_shed_with_the_documented_problem_title [4 s]
Error Message:
 Shouldly.ShouldAssertException : refused
  should not be null but was

Additional Info:
    expected at least one 429; observed 201x8. Recent event-ingestion logs:
    [Wolverine/RabbitMQ startup log tail from event-ingestion, per RecentLogs]

Stack Trace:
   at SmartSentinelEye.Integration.Tests.EventIngestion.IngestBackpressureIntegrationTests.A_burst_past_the_write_limit_is_shed_with_the_documented_problem_title() in D:\Github\sse-2441\tests\Integration.Tests\EventIngestion\IngestBackpressureIntegrationTests.cs:line 59

Test Run Failed.
Total tests: 1
     Failed: 1
 Total time: 2,4609 Minutes
```

**`expected at least one 429; observed 201x8`** — the limiter still grants all
64 slots at the production default, so all eight burst POSTs succeed and the
`refused` lookup comes back null. This is exactly the distribution spec §6
predicts for the wiring-absent state, and it is the load-bearing red: a green
here would mean the test is wired to nothing, per tasks.md T004/T005's
explicit stop-and-report instruction. It did not occur.

Independently reproduced by the orchestrator against the same worktree state
before T006 landed, with the identical assertion and distribution.

---

## T006 — the AppHost line

Added inside the existing `if (isE2ETests)` block in `src/AppHost/AppHost.cs`
(today at `:575-588`), alongside the audit-retention tick and the
ingest-breakdown switch:

```csharp
if (isE2ETests)
{
    // Sweep retention every few seconds in the integration suite so the
    // round-trip test isn't waiting on the production daily timer.
    auditObservability.WithEnvironment("AuditObservability__Retention__TickInterval", "00:00:03");

    // The ingest breakdown's stamps, for the integration suite only. The type
    // default stays false (AuditMeasurementSwitchTests) because the stamps sit
    // on a write path and production does not pay for an instrument nobody
    // reads. But a run that has to remember a shell export is a run whose
    // breakdown silently reports zeros, so the fixture turns it on rather than
    // asking (spec 109 US1).
    auditObservability.WithEnvironment("AuditObservability__Measurement__RecordIngestBreakdown", "true");

    // The write limiter's 429 seam is unreachable at the production default of
    // 64 concurrent slots; pinning it to 1 for the integration suite is the
    // only channel the collection fixture has to reach it per-test-lane
    // (spec 223 US1).
    eventIngestion.WithEnvironment("EventIngestion__IngestWrite__Concurrency", "1");
}
```

No other line in `src/AppHost/AppHost.cs` was touched.

---

## T007 — step 2, the new test alone: GREEN

`dotnet test tests/Integration.Tests/ --filter "FullyQualifiedName~IngestBackpressureIntegrationTests"`

First pass (minimal console logger), verbatim tail:

```
Test run for D:\Github\sse-2441\tests\Integration.Tests\bin\Debug\net10.0\SmartSentinelEye.Integration.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: 6 s - SmartSentinelEye.Integration.Tests.dll (net10.0)
```

Re-run with `--logger "console;verbosity=detailed"` to capture the test's own
observed status-distribution write-line (the minimal logger does not surface
`ITestOutputHelper` output for a passing test):

```
Passed SmartSentinelEye.Integration.Tests.EventIngestion.IngestBackpressureIntegrationTests.A_burst_past_the_write_limit_is_shed_with_the_documented_problem_title [2 s]
  Standard Output Messages:
POST /events/manual x8 (concurrency 1) -> 201x1, 429x7

Test Run Successful.
Total tests: 1
     Passed: 1
 Total time: 2,3511 Minutes
```

(The 2.35-minute wall time is the AppHost boot, not the test itself — the
test's own duration is 2s; a `minio_check`/`keycloak_management` health-check
warning noise appears during boot and is unrelated.)

**Observed distribution: `201x1, 429x7`.** Both clauses hold: at least one 429
(seven of them), and at least one 201. **GREEN, as expected at step 2.**

---

## Counterfactual CF-A — the contract, not the status code

**Prediction, written before running**: RED on the title assertion only (body
carries `"EVENT_INGEST_OVERLOAD"` instead of `"EVENT_INGEST_BACKPRESSURE"`),
with the 429-status clause (`refused.ShouldNotBeNull`) still passing.

Injection: `src/EventIngestion/Api/EventsEndpoints.Writes.cs:348`,
`title: "EVENT_INGEST_BACKPRESSURE"` → `title: "EVENT_INGEST_OVERLOAD"`.

Verbatim result:

```
[xUnit.net 00:01:51.47]     SmartSentinelEye.Integration.Tests.EventIngestion.IngestBackpressureIntegrationTests.A_burst_past_the_write_limit_is_shed_with_the_documented_problem_title [FAIL]
[xUnit.net 00:01:51.47]       Shouldly.ShouldAssertException : problem.GetProperty("title").GetString()
[xUnit.net 00:01:51.48]           should be
[xUnit.net 00:01:51.48]       "EVENT_INGEST_BACKPRESSURE"
[xUnit.net 00:01:51.48]           but was
[xUnit.net 00:01:51.48]       "EVENT_INGEST_OVERLOAD"
...
Additional Info:
    the 429's problem title did not match the documented contract; observed 201x1, 429x7. Recent event-ingestion logs:
...
  Failed SmartSentinelEye.Integration.Tests.EventIngestion.IngestBackpressureIntegrationTests.A_burst_past_the_write_limit_is_shed_with_the_documented_problem_title [3 s]
```

Observed distribution: `201x1, 429x7` — identical to T007's, confirming the
429-status clause (the first assertion, `refused.ShouldNotBeNull`) was
satisfied and execution proceeded to the title assertion, which is where it
failed.

**Actual matches predicted exactly.** RED on the title clause; the status
clause is unaffected. The two clauses are not coupled.

Reverted: `git checkout -- src/`. This reverted **both** the CF-A injection
**and** T006's `AppHost.cs` line (as tasks.md's own warning anticipated,
`git checkout -- src/` reverts all of `src/`, AppHost included). Re-applied
T006's line immediately and re-verified with `git diff src/AppHost/AppHost.cs`
before proceeding — confirmed present, `git diff --stat src/` showed only
`AppHost.cs | 6 ++++++`.

---

## Counterfactual CF-B — the 429 comes from this limiter

**Prediction, written before running**: RED with zero 429s observed — the
same shape as the original step-1 red (`201x8, 429x0`).

Injection: `src/EventIngestion/Application/Ingress/IngestWriteLimiter.cs:43-44`,

```csharp
public IngestWriteLease TryAcquire() =>
    slots.Wait(0) ? new IngestWriteLease(slots) : IngestWriteLease.Refused;
```

→

```csharp
public IngestWriteLease TryAcquire() =>
    new IngestWriteLease(slots);
```

Verbatim result:

```
[xUnit.net 00:01:50.94]     SmartSentinelEye.Integration.Tests.EventIngestion.IngestBackpressureIntegrationTests.A_burst_past_the_write_limit_is_shed_with_the_documented_problem_title [FAIL]
[xUnit.net 00:01:50.94]       Shouldly.ShouldAssertException : refused
[xUnit.net 00:01:50.94]           should not be null but was
Additional Info:
    expected at least one 429; observed 500x8. Recent event-ingestion logs:
...
Standard Output Messages:
POST /events/manual x8 (concurrency 1) -> 500x8
```

event-ingestion logs showed, for every one of the eight requests:

```
System.Threading.SemaphoreFullException: Adding the specified count to the semaphore would cause it to exceed its maximum count.
```

**Actual result is RED with zero 429s (matches the "no 429" half of the
prediction), but the shape is `500x8`, not the predicted `201x8, 429x0`.**

**This is a mismatch, reported as observed rather than adjusted or explained
away.** The cause is mechanical: with the AppHost's concurrency pinned to 1,
the `SemaphoreSlim` is constructed with `initialCount: 1, maxCount: 1`. The
injected `TryAcquire` always returns a real lease bound to the semaphore
*without* ever calling `Wait`, so the semaphore's internal count is never
decremented. When each request's `using IngestWriteLease lease = ...` block
ends, `Dispose()` unconditionally calls `slots.Release()` — releasing a
semaphore that was never acquired, against a count already at its max (1),
which `SemaphoreSlim.Release()` throws for. Every one of the eight concurrent
requests hits this and the endpoint answers `500` instead of `201`.

No 429 survived the injection (satisfying the finding's core purpose — the
429 does come from this limiter's refusal path, since removing the refusal
path removes every 429), but the specific distribution differs from what
spec §6.3 predicted, and that difference is recorded here rather than
resolved.

**Phase-6 review strengthened this claim rather than merely accepting the
mismatch.** `EventsEndpoints.Writes.cs:344`'s `using IngestWriteLease lease =
...` is a method-scoped `using` declaration, so it disposes only *after*
`return result.Match(...)` has already computed its return value — meaning
every one of the eight requests traversed the **admitted** branch and ran the
handler to completion (storing its event) before the lease's `Dispose()`
threw on the way out. That is a direct positive observation that no request
took the refusal branch, which is precisely CF-B's claim — so the `500x8`
result is not a weaker proof than the predicted `201x8`, it is a *stronger*
one: it shows the admitted path was reached, not merely that no 429 appeared
for some other reason. Review also ruled out an alternative 429 source:
`Status429TooManyRequests` occurs exactly once in `src/EventIngestion`
(`EventsEndpoints.Writes.cs:350`), gated solely on `!lease.Acquired`, and the
test's client resolves the `event-ingestion` service directly
(`AspireFixture.CreateAuthenticatedClientAsync("event-ingestion", …)`), not
the API gateway's own separate rate limiter.

**The injection itself was under-specified in spec §6.3, not sloppily
executed.** `TryAcquire`'s counterfactual removed the refusal path but broke
the `Wait`/`Release` pairing `IngestWriteLimiter.Dispose()` assumes
(`:60`, unconditional `slots.Release()`), producing a compound defect the
prediction didn't anticipate. An injection that would have matched the
`201x8` prediction and preserved the pairing:

```csharp
public IngestWriteLease TryAcquire()
{
    slots.Wait();                       // never refuses; still balanced
    return new IngestWriteLease(slots);
}
```

Not re-run — the claim CF-B exists to prove already holds by the argument
above, and a corrected re-run would only reproduce a `201x8` distinction
already established qualitatively.

Reverted: `git checkout -- src/`. As with CF-A, this again wiped T006's
`AppHost.cs` line. Re-applied it, re-verified with
`git diff src/AppHost/AppHost.cs` (present) and `git diff --stat src/`
(`AppHost.cs | 6 ++++++` only) before proceeding to T008.

**Comparison per tasks.md T014**: CF-A and CF-B do fail in different shapes
from each other (`FAIL` on the title assertion with 429s present, vs. `FAIL`
on the "at least one 429" assertion with none present) — the qualitative
distinction the spec asks for holds. What does not hold is CF-B's own
predicted distribution shape (`201x8` vs. actual `500x8`); reported as a
finding, not corrected.

---

## T008 — the whole Aspire integration bucket (AS-5)

`dotnet test tests/Integration.Tests/ --filter "Category!=Measurement&Category!=Disruptive&Category!=Maintenance"`

Verbatim summary line:

```
Failed!  - Failed:     1, Passed:   639, Skipped:     0, Total:   640, Duration: 10 m 25 s - SmartSentinelEye.Integration.Tests.dll (net10.0)
```

**One test reddened**: `SmartSentinelEye.Integration.Tests.StreamDistribution.WhepAuthorizeRateLimitTests.A_throttled_authorize_never_reaches_the_handler`

Verbatim failure:

```
[xUnit.net 00:08:49.07]     SmartSentinelEye.Integration.Tests.StreamDistribution.WhepAuthorizeRateLimitTests.A_throttled_authorize_never_reaches_the_handler [FAIL]
  Failed SmartSentinelEye.Integration.Tests.StreamDistribution.WhepAuthorizeRateLimitTests.A_throttled_authorize_never_reaches_the_handler [368 ms]
  Error Message:
   Shouldly.ShouldAssertException : throttledResponse.StatusCode
    should be
HttpStatusCode.TooManyRequests
    but was
HttpStatusCode.Forbidden

Additional Info:
    request 51 in the window should have been refused by the limiter; it was not, so the absence of a handler log below proves nothing about FR-002.
```

**Reported as a finding, per the task brief — not fixed, not retried, and the
reddened test was not touched.**

**Diagnostic (not a fix) run to characterise the finding**: re-ran only
`WhepAuthorizeRateLimitTests` in isolation (no contention from any other test
class on the shared fixture):
`dotnet test tests/Integration.Tests/ --filter "FullyQualifiedName~WhepAuthorizeRateLimitTests" --logger "console;verbosity=detailed"`.

Result: the same fact failed again, in isolation, with the identical shape:

```
  Failed SmartSentinelEye.Integration.Tests.StreamDistribution.WhepAuthorizeRateLimitTests.A_throttled_authorize_never_reaches_the_handler [414 ms]
  Error Message:
   Shouldly.ShouldAssertException : throttledResponse.StatusCode
    should be
HttpStatusCode.TooManyRequests
    but was
HttpStatusCode.Forbidden

Additional Info:
    request 51 in the window should have been refused by the limiter; it was not, so the absence of a handler log below proves nothing about FR-002.
```

(The other five facts in that class — `A_partition_entering_the_throttled_state_is_logged_once`,
`Health_and_readiness_are_never_throttled`, `Repeated_refusals_within_the_same_window_do_not_add_a_second_log_record`,
`Authorize_declares_the_429_it_can_answer`, `Authorize_refuses_a_source_over_its_window_with_429`
— all passed both in the full bucket and in this isolated re-run.)

The stream-distribution log around the failure shows the rate limiter's own
"entered the throttled state" transition logged at `10:48:00.063`, *after*
the test's request 51 had already been answered and asserted against — the
window's 51st request landed just before the limiter's internal transition
fired, so it was answered `403 Forbidden` (the endpoint's own missing-action
refusal) rather than `429`.

**This reproduces identically with zero contention from the rest of the
suite, on a completely different context (StreamDistribution's WHEP-authorize
rate limiter, sharing no code with `IngestWriteLimiter`) and a completely
different mechanism (a `FixedWindowRateLimiter` policy vs. a `SemaphoreSlim`
gate).** That test's own doc comments (`WhepAuthorizeRateLimitTests.cs:14-53`)
already describe this fixture-shared address partition as timing-sensitive
across multiple spec reviews (S6–S10), independent of spec 223. The evidence
here does not prove this redden is unrelated to spec 223's `Concurrency=1`
override — only that it does **not** require contention from the rest of the
suite to reproduce, which is consistent with (but does not conclusively
establish) a pre-existing flake rather than one introduced by this change.
**Reported as observed; not explained away, not fixed, not retried within
the test, and the concurrency workaround was not lowered.**

**The four tests named as the priority read all passed** (by elimination:
`Total: 640, Failed: 1, Skipped: 0`, and the one failure listed above is not
any of the four):

| Test | Shard | Result |
|---|---|---|
| `EventTypeRegistryConcurrencyIntegrationTests` | 2 | Passed |
| `ManualIngestFabScopingIntegrationTests` | 3 | Passed |
| `MissingPayloadIsRefusedIntegrationTests` | 4 | Passed |
| `AnonymousIngestIsRefusedTests` | 2 | Passed |

(The default console logger at non-detailed verbosity does not print a line
per passing test, so these are not individually quoted verbatim above; their
pass is established by the total/failed/skipped arithmetic — 639 of 640 non-
`WhepAuthorizeRateLimitTests`-failure tests passed, and none of the four
appear in the one `[FAIL]` block the run produced.)

---

## T009 — Architecture.Tests

`dotnet test tests/Architecture.Tests/`

Verbatim summary line:

```
Passed!  - Failed:     0, Passed:   455, Skipped:     0, Total:   455, Duration: 6 s - SmartSentinelEye.Architecture.Tests.dll (net10.0)
```

**Green and unchanged.** All 455 tests passed, including (by the same
zero-failure arithmetic) `LogTailCoverageTests`, `IntegrationTestSelectionTests`,
and `AppHostE2ESwitchTests` — none of the 455 failed, so each of these three
held.

---

## T010 — Release build

Aspire stack confirmed stopped before building (no `dotnet.exe` process with
`AppHost`/`ApiGateway`/`MigrationRunner` in its command line at the time).

`dotnet build -c Release`

Verbatim tail:

```
    82 Warning(s)
    0 Error(s)

Time Elapsed 00:01:43.85
```

`Build succeeded.` (confirmed present in the full log). All 82 warnings are
the advisory SonarAnalyzer code-metric set (S104/S107/S138/S1541 — file
length, parameter count, method length, cyclomatic complexity), carved out of
`TreatWarningsAsErrors` per ADR-0084 — none are `TreatWarningsAsErrors`
failures (no CS8601/CS0618/IDE-class warnings), and none are new: they are
the repository's existing, unrelated advisory baseline in files this slice
did not touch (`OverlayDesigner`, `AuditObservability`, `StreamDistribution`,
`SystemVariables`, `Automation`, `Identity`, `AppHost.cs`'s own pre-existing
S104/S1541 on the whole top-level file, etc.).

**Expected success, observed.**

---

## T011 — diff scope

```
$ git diff --stat tests/Integration.Tests/
 tests/Integration.Tests/ci-shards/shard-3.filter | 2 +-
 1 file changed, 1 insertion(+), 1 deletion(-)

$ git diff --stat src/
 src/AppHost/AppHost.cs | 6 ++++++
 1 file changed, 6 insertions(+)

$ git status --short
 M src/AppHost/AppHost.cs
 M tests/Integration.Tests/ci-shards/shard-3.filter
?? tests/Integration.Tests/EventIngestion/IngestBackpressureIntegrationTests.cs
```

- `tests/Integration.Tests/` diff is exactly the one appended shard clause in
  `shard-3.filter` (the new test file is untracked/new, not a modification —
  `git diff` does not show untracked files, which is why it doesn't appear in
  the `--stat` above; it is confirmed present and unmodified by `git status`).
- `src/` diff is exactly the one `AppHost.cs` block (6 insertions: the 5-line
  comment plus the one `WithEnvironment` call), confirming both CF-A's and
  CF-B's injections in `src/EventIngestion/` are fully reverted with no
  residue.
- No edit to any existing test anywhere in the diff.
- `git status --short` is clean of anything outside the three files this
  slice was scoped to touch:
  `src/AppHost/AppHost.cs`,
  `tests/Integration.Tests/EventIngestion/IngestBackpressureIntegrationTests.cs`,
  `tests/Integration.Tests/ci-shards/shard-3.filter`.

---

## Summary

| Task | Result |
|---|---|
| T006 | AppHost line added, one line + comment, inside `if (isE2ETests)` |
| T007 | GREEN, `201x1, 429x7` |
| CF-A | Matches prediction exactly: RED on title clause only |
| CF-B | RED with zero 429s (half the prediction), but `500x8` not `201x8` — **mismatch, reported** |
| T008 | 639/640 passed; **1 redden, `WhepAuthorizeRateLimitTests.A_throttled_authorize_never_reaches_the_handler`, reproduces in isolation — reported as a finding, not fixed** |
| T009 | 455/455 passed, unchanged |
| T010 | Release build succeeded, 0 errors, 82 pre-existing advisory warnings |
| T011 | Diff scope exactly the three files this slice was scoped to |

**Not attempted**: tasks.md T015 (spec §10's six-step manual dev-mode
procedure) — explicitly out of scope for this pass per the brief.
