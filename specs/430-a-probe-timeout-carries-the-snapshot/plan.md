# Implementation Plan: A probe timeout carries the resource snapshot

**Spec**: `./spec.md` | **Issue**: #3076 | **Branch**: `test/3076-fixture-probe-snapshot`

## Constitution / ADR check

| Gate | Result |
|---|---|
| Bounded context / layers | None — `tests/Integration.Tests/Fixtures/` only. No `src/` change, no `Shared.Contracts`, no cross-context reference. |
| §II primitives | N/A — test infrastructure, not a domain model. |
| §IV latency | N/A — no leg. |
| §Testing | Behaviour-changing (the failure message changes) → phase 4a **red**. |
| ADR-0103 / 0068 | Applied, not amended: a diagnostic on an existing failure path. **No ADR** (owner: "No ADR"). |
| ADR-0105 | `Ensure.That(...)` guards on the new helper's reference arguments, matching the file. |

## Design

One new static pair in `AspireFixture.cs`, beside `FormatTimeoutMessage`, reusing
spec 424's capture and formatter unchanged:

```csharp
internal static async Task<TimeoutException> ProbeExhaustedAsync(
    string probeFailure,
    ResourceNotificationService notifications,
    TimeSpan window)
// = new TimeoutException(FormatProbeTimeoutMessage(probeFailure,
//       await CaptureResourceStatesAsync(notifications, window)))

internal static string FormatProbeTimeoutMessage(
    string probeFailure,
    CapturedResourceStates captured)
// = $"{probeFailure}\nResource states:\n{FormatResourceStates(captured.States, captured.ExitCodes, captured.Health)}"
```

Each of the three probe tails becomes

```csharp
throw await ProbeExhaustedAsync(
    "<unchanged sentence>",
    App.ResourceNotifications,
    ProbeSnapshotWindow).ConfigureAwait(false);
```

with `ProbeSnapshotWindow = TimeSpan.FromSeconds(3)` (spec A2).

**Why static, taking the notification service:** it is the seam that lets the
behaviour run with no Docker — the instance path needs a built `app`. It is the
same shape `CaptureResourceStatesAsync` was given in PR #3075 for the same reason.

**Why not `CaptureResourceStateMapAsync`:** it also writes the instance
`exitCodes` map the eight-minute catch reads. A probe timeout escapes
`InitializeAsync` without entering that catch, so nothing would read the write.

**Untouched:** attempt counts, delays, the caught exception types per probe
(`HttpRequestException`; `TimeoutRejectedException` in the health probe), the
`InitializeAsync` catch, `FormatTimeoutMessage`. A probe cancelled by the shared
`cts` still raises `OperationCanceledException` from `Task.Delay` and still lands
in the existing catch.

## Tests (Docker-free, `Category=FixtureLogic`)

Added to the **existing** `AspireFixtureResourceCaptureTests` (already in
`ci-shards/shard-3.filter`, already has `NewNotificationService()`), so no shard
edit and no new class for `IntegrationTestSelectionTests` to classify:

1. `A_probe_that_runs_out_of_attempts_reports_every_resources_state_and_health`
2. `A_probe_timeout_bounds_health_lines_like_the_startup_timeout`
3. `A_probe_timeout_with_no_snapshots_still_reports_and_does_not_throw`
4. `Every_probe_timeout_in_the_fixture_goes_through_the_snapshot` — source scan of
   `AspireFixture*.cs`: exactly one `throw new TimeoutException(` (the catch);
   three `ProbeExhaustedAsync(` call sites whose first argument is a
   `"... after 60 attempts..."` literal. Locate the directory with the repo-root
   walk an existing source-reading test in this project uses; compare paths
   with `/`-normalisation (memory: source-scanning tests need slash normalising).

Test 4 proves wiring, not runtime behaviour — tests 1-3 prove behaviour on the
helper the wiring calls. Together they cover what a booted stack would show,
without one.

## Files

- `tests/Integration.Tests/Fixtures/AspireFixture.cs` — helper pair; three throw sites.
- `tests/Integration.Tests/Fixtures/AspireFixtureResourceCaptureTests.cs` — tests 1-4.
