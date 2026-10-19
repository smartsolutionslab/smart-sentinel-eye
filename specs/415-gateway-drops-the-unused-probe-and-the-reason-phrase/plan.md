# Plan 415: The MediaMTX gateway drops its unused single-path probe, and a refused call carries no reason phrase

**Spec**: [spec.md](./spec.md). **Tasks**: [tasks.md](./tasks.md). **Issue**: #3013 (feature-level).

## 1. Context and layers

- **Bounded context**: StreamDistribution only. No `Shared.Contracts`, `Shared.Kernel`, AppHost or
  other-context change. No messaging change (no domain or integration event is added, removed or
  reshaped). No persistence, no migration.
- **Domain**: `src/StreamDistribution/Domain/Stream/IRtspGateway.cs` loses one member (US1).
- **Infrastructure**: `src/StreamDistribution/Infrastructure/Gateways/MediaMtxRtspGateway.cs` and
  `MediaMtxRtspGateway.PageReading.cs` (both stories).
- Boundary rules unaffected: the port stays in Domain, the adapter in Infrastructure.
- No value object or entity changes; constitution §II is untouched (no domain state is added).

## 2. US1 blast radius (verified with `grep -rn GetPathHealthAsync`, develop `98834b73`)

| File | Line | Change |
|---|---|---|
| `src/StreamDistribution/Domain/Stream/IRtspGateway.cs` | 46 | member removed |
| same | 6, 50-51, 68-70 | doc text: drop the `cref`s, say "one round trip per path"; drop the stale ADR-0061 citation |
| `src/StreamDistribution/Infrastructure/Gateways/MediaMtxRtspGateway.cs` | 157-174 | method removed |
| same | 266-270 | `NotRegistered` removed (only reader was `:165`) |
| `tests/StreamDistribution.Application.Tests/Fakes/FakeRtspGateway.cs` | 71-82 | member removed |
| `tests/StreamDistribution.Application.Tests/Fakes/RecordingStreamDistributionPorts.cs` | 106-109 | forwarding member removed |
| `tests/StreamDistribution.Infrastructure.Tests/Fakes/SourceRepairFakes.cs` | 83-84 | member removed |
| `tests/StreamDistribution.Infrastructure.Tests/HealthWatcher/StreamHealthWatcherAbsenceHoldInvariantTests.cs` | 211-212 | member removed |
| `tests/StreamDistribution.Infrastructure.Tests/HealthWatcher/StreamHealthWatcherAbsenceHoldTests.cs` | 162, 269, 279-284 | member, `PerPathCalls` and its assertion removed (spec FR-104) |
| `tests/StreamDistribution.Infrastructure.Tests/HealthWatcher/StreamHealthWatcherScopeTests.cs` | 33, 78-81, 213, 229, 297-302, 320, 331-335 | member, `PerPathCalls`, the `Sweep` record field and its assertion removed; class doc reworded (FR-104/FR-105) |
| `tests/StreamDistribution.Infrastructure.Tests/Gateways/MediaMtxProbeLogVolumeTests.cs` | 60-99, 101-127, 129-156, 205-237 | the four probe-driven facts deleted **after** their twins land (§5) |
| `tests/Integration.Tests/StreamDistribution/MediaMtxReconcilerPassCharacterisationTests.cs` | 206-207 | member removed |
| `tests/Integration.Tests/StreamDistribution/MediaMtxSourceRepairLogVolumeIntegrationTests.cs` | 249-250, 299-300 | members removed (two doubles) |
| `tests/Integration.Tests/StreamDistribution/ProvisionRetireRaceIntegrationTests.cs` | 49, 403-404 | member removed; comment points at `StreamHealthWatcher.cs:150` |
| `tests/Integration.Tests/StreamDistribution/StreamRepositoryVersionAssertionIntegrationTests.cs` | 101 | comment points at `StreamHealthWatcher.cs:150` |

`ProvisionRetireRaceIntegrationTests`' constants `PathNotRegistered` / `PathNotReady` stay correct:
the watcher produces "path not registered" itself (`StreamHealthWatcher.cs:150`) and "not ready"
comes from `ParsePathHealth`, which stays.

No integration class is added, so no `tests/Integration.Tests/ci-shards/shard-N.filter` change.

## 3. US2 design

One private helper in `MediaMtxRtspGateway.PageReading.cs` (the gateway's main file is over
ADR-0084's advisory 300 LOC; US1 brings it back under):

```csharp
private static void EnsureSuccess(HttpResponseMessage response, string route)
```

- Returns when `response.IsSuccessStatusCode`.
- Otherwise throws `new HttpRequestException(HttpRequestError.Unknown /* A3 */, $"MediaMTX {route} answered {(int)response.StatusCode}.", null, response.StatusCode)`.
  `CultureInfo.InvariantCulture` formatting for the number (the file already imports it).
- `route` is a literal at each call site: `/v3/config/paths/add`, `/v3/config/paths/patch`,
  `/v3/config/paths/delete`, and `listPath` (already the route constant, no query) in
  `ReadPageAsync`.
- Call sites: `MediaMtxRtspGateway.cs:76`, `:92`, `:105` (inside the existing 404 guard);
  `MediaMtxRtspGateway.PageReading.cs:50` (before the `try`, so spec 409 FR-115 holds).
- No `Ensure.That` on `response`/`route`: private, every caller passes a non-null local and literal.
- `ReasonPhrase` and the content are never read.

Why not a shared `Shared.Kernel` helper: one context, one rule (spec 373 FR-008 is about MediaMTX);
generalising is spec §5's follow-up.

## 4. Messaging, persistence, contracts

None. Throttle keys (`SourceRepairFailureLog.cs:33`, `MediaMtxSourceRepairSweep.cs:80`) read type,
`StatusCode` and `HttpRequestError`, all preserved, so their state machines see identical outcomes.

## 5. Test strategy

### US1 (characterisation, observed green on develop before any source change)

New facts in `MediaMtxProbeLogVolumeTests` driving `GetAllPathsHealthAsync` through the same
`BuildGatewayHost` (shipped appsettings, real `AddServiceDefaults`, real `AddMediaMtxGateway`, stub
primary handler). A list responder answers `/v3/paths/list?page=0&itemsPerPage=100` with
`{"pageCount":1,"itemCount":1,"items":[{"name":"cam-<guid>","ready":true}]}` (`ready:false` for C2):

- **C1** `A_fab_scale_run_of_healthy_health_sweeps_writes_no_information_record` (Theory:
  Production, Development): 250 reads, 250 stub requests, first URI starts with
  `http://mediamtx.test/v3/paths/list`, zero routine records.
- **C2** `A_health_sweep_answered_not_ready_writes_no_information_record`.
- **C3** `A_health_sweep_that_needs_retries_still_logs_them_at_warning_and_its_failure_at_error` (503).
- **C4** `An_environment_override_brings_the_health_sweep_exchange_back_without_a_rebuild`: four
  gateway handler records at Information, the Polly `ExecutionAttempt` at Debug.

These must be green on develop **before** the removal commit, and pass **unmodified** after it.
The existing `Another_client_in_the_same_service_still_logs_its_exchanges_at_information` does not
call the probe and is untouched.

### US2 (red)

New unit class `tests/StreamDistribution.Infrastructure.Tests/Gateways/MediaMtxRtspGatewayReasonPhraseTests.cs`
with a hand-written stub handler (ADR-0052) that answers with `new HttpResponseMessage(status) { ReasonPhrase = Hostile }`,
`Hostile = "rtsp://admin:SECRET@10.0.8.1/x"`. An in-memory `ReasonPhrase` is exactly what
`EnsureSuccessStatusCode` formats, so the red is deterministic without sockets. Plus one red and one
guard in the existing `MediaMtxSourceRepairOverRealGatewayTests` (its `MediaMtxConfigDouble` may
gain a reason-phrase option; no existing assertion changes). Tasks list each test.

## 6. Risks

| Risk | Mitigation |
|---|---|
| `HttpRequestError` differs from what `EnsureSuccessStatusCode` gives, splitting a throttle outcome | Guard G1 asserts the value on today's code first (A3) |
| A thrown subclass or a non-null inner changes a `catch` filter | Guards assert exact type and null `InnerException` |
| The 404 classification in the source repair breaks | Guard G4 over the real gateway |
| The removal leaves a double carrying a dead member | A public method left on a class that no longer gets it from the interface still compiles, so the build will not catch it; SC-1's `grep` over `src` and `tests` does |
| A test that should have stayed unmodified is edited | tasks.md forbids every edit outside plan §2's table |

## 7. PR split and order

1. **PR-A (US1, characterisation)**, from `develop`: commit 1 `test(3013): pin the gateway client's log volume through the health sweep` (C1-C4, green on develop); commit 2 `refactor(3013): remove the unused single-path health probe` (plan §2). Each commit builds alone.
2. **PR-B (US2, red)**, cut from `develop` **after PR-A merges** (both touch `MediaMtxRtspGateway.cs`; not stacked): commit 1 `test(3013): …` the R-tests and guards, red; commit 2 `fix(3013): a refused MediaMTX call carries no reason phrase`.

If the owner vetoes PR-A (spec A1), PR-B goes alone and also replaces the `:167` site.
