# Verification 274 — #2490

**Phase**: 5 (Verify) · **Tree**: `841e4083` (phase 4 tip) · **Latency budget: N/A** — error-handling path on idempotency bookkeeping (`IdempotentRequest.RunAndRecordAsync`), not on the event-to-overlay path (constitution §IV). Confirmed: this file has no relation to camera/SFU/overlay rendering; it guards HTTP write endpoints only.

## 1. Fresh, independent test run (not the engineer's report — re-run here)

```
dotnet test tests/ServiceDefaults.Tests -c Release --filter "FullyQualifiedName~IdempotentRequestTests"
...
Passed!  - Failed:     0, Passed:    14, Skipped:     0, Total:    14, Duration: 394 ms - SmartSentinelEye.ServiceDefaults.Tests.dll (net10.0)
```

Release configuration deliberately (CI treats warnings as errors in Release;
Debug hides some analyzer classes). All 14 facts green: the 4 new #2490 facts
(`A_completion_that_throws_after_the_work_succeeded_still_answers_with_the_work_s_response`,
`A_release_that_throws_after_a_refusal_still_answers_with_the_refusal`,
`A_completion_that_throws_records_its_failure_on_the_current_activity`,
`A_completion_that_throws_is_not_followed_by_a_release`) plus the 10
pre-existing facts, including the two #2290 facts, unmodified.

## 2. Counterfactual — the red baseline already is the "does it actually fail without the fix" proof

Per the test-writer's phase-4a transcript (quoted in the PR body), the same
four facts were run against the pre-fix tree and failed with
`System.InvalidOperationException` propagating out of `RunAndRecordAsync` at
the exact `CompleteAsync`/`ReleaseAsync` call sites named in #2490 — i.e. the
uncaught-exception defect was observed directly, not inferred. This
verification's job is the other half: confirm the fix closes exactly that gap
and nothing else. Re-running the identical facts against `841e4083` (above)
shows all four now pass, with `store.Released` staying `0` in the
completion-failure case (FR-003 — no fallback release), and the pre-existing
#2290 facts (`A_release_that_throws_does_not_hide_the_failure_that_caused_it`,
`A_release_that_throws_records_its_failure_on_the_current_activity`) still
pass unmodified, so the sibling failure-path behaviour was not disturbed.

## 3. A real HTTP endpoint still compiles and wires unchanged

`IdempotentRequest.RunAndRecordAsync` is not endpoint-specific — all 11
`IdempotentExecution` call sites (CameraCatalog, Automation, EventIngestion,
Identity ×3, LayoutComposition ×2, OverlayDesigner, SystemVariables) share
this one code path, so a unit-level fix here is load-bearing for all of them
without any call-site change. Rebuilt one representative consumer end to end:

```
dotnet build src/CameraCatalog/Api/SmartSentinelEye.CameraCatalog.Api.csproj -c Release
...
14 Warning(s)
0 Error(s)
```

All 14 warnings are pre-existing SonarAnalyzer advisories (S104/S107/S138) in
files this change does not touch (`CameraEndpoints.cs`, migrations,
`CameraConfiguration.cs`) — none new, none in `IdempotentRequest.cs`. No DI
wiring, endpoint signature, or `IIdempotencyStore` contract changed, so this
confirms the fix is a drop-in behind the existing 11 call sites without
touching them (spec's explicit out-of-scope boundary).

## 4. What was not covered (the honest gap)

- **No live Aspire/Postgres run.** This was not exercised against a real
  `IdempotencyStore<TDbContext>` backed by an actual Postgres connection
  forced to fail (e.g. killing the connection mid-`UPDATE`) — that would be
  the strongest possible evidence but requires booting the full stack for a
  fix that is entirely inside a pure, already-isolated static method with no
  new I/O, DI, or serialization surface. The fake `IIdempotencyStore` in the
  unit tests throws the same `Exception` type a real ADO.NET/Npgsql failure
  would surface as (the store's methods are typed `Task`, any exception
  propagates identically regardless of source), so the boundary being tested
  is the one this fix actually changes.
- **No observation of the `Activity.Current?.AddException` signal actually
  reaching a real OTel exporter/Aspire dashboard trace** — the unit test
  confirms the API call happens (via a test `ActivityListener`, matching the
  existing #2290 test's own pattern), but nobody watched a real trace appear
  in the dashboard for this specific new call. The mechanism is identical to
  the already-shipped #2290 `ReleaseQuietlyAsync` case, which was previously
  verified this way, so the marginal risk is low but unconfirmed here.
