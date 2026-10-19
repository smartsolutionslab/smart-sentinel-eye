# Plan 419: The MediaMTX gateway drops its unused single-path health probe

**Spec**: [spec.md](./spec.md). **Tasks**: [tasks.md](./tasks.md). **Issue**: #3013 (feature-level,
already on Project #13). Supersedes spec 415 plan §2/§5/§7 PR-A for this change; line numbers below
are develop `28bcec96`.

## 1. Context and layers

- **Bounded context**: StreamDistribution only. No `Shared.Contracts`, `Shared.Kernel`, AppHost,
  migration, messaging (no domain or integration event touched) or other-context change.
- **Domain**: `src/StreamDistribution/Domain/Stream/IRtspGateway.cs` loses one member. No entity or
  value object changes; constitution §II untouched.
- **Infrastructure**: `src/StreamDistribution/Infrastructure/Gateways/MediaMtxRtspGateway.cs` loses
  the method and one field.
- Boundary rules unaffected (port in Domain, adapter in Infrastructure; NetArchTest rules unchanged).

## 2. Blast radius (every file; `grep -rn "GetPathHealthAsync\|PerPathCalls" src tests`)

| # | File | Lines | Change | Kind |
|---|---|---|---|---|
| S1 | `src/StreamDistribution/Domain/Stream/IRtspGateway.cs` | 46 | member removed | removal |
| S2 | same | 6; 50-51; 68-70 | doc: drop "(ADR-0061 forward-compatible interfaces)"; ":50" becomes "rather than one round trip per path"; ":69" becomes "reads that as 'not registered'" | doc |
| S3 | `src/StreamDistribution/Infrastructure/Gateways/MediaMtxRtspGateway.cs` | 171-188 | method removed | removal |
| S4 | same | 280-284 | `NotRegistered` removed (only reader `:179`) | removal |
| T1 | `tests/StreamDistribution.Application.Tests/Fakes/FakeRtspGateway.cs` | 71-82 | member removed (no direct caller) | mechanical |
| T2 | `tests/StreamDistribution.Application.Tests/Fakes/RecordingStreamDistributionPorts.cs` | 106-109 | forwarding member removed | mechanical |
| T3 | `tests/StreamDistribution.Infrastructure.Tests/Fakes/SourceRepairFakes.cs` | 83-84 | member removed (it threw `NotSupportedException`) | mechanical |
| T4 | `tests/StreamDistribution.Infrastructure.Tests/HealthWatcher/StreamHealthWatcherAbsenceHoldInvariantTests.cs` | 211-212 | member removed (it threw) | mechanical |
| T5 | `tests/StreamDistribution.Infrastructure.Tests/HealthWatcher/StreamHealthWatcherAbsenceHoldTests.cs` | 162; 269; 279-284 | **D7** assertion; `PerPathCalls` property; member | D7 + mechanical |
| T6 | `tests/StreamDistribution.Infrastructure.Tests/HealthWatcher/StreamHealthWatcherScopeTests.cs` | 31-35; 78-81; 213; 229; 296-302; 320; 331-335 | class doc reworded; **D6** assertion; `Sweep` constructor arg and record field `PerPathCalls`; stub doc (drop the `PerPathCalls` sentences/`cref`); property; member | D6 + mechanical + doc |
| T7 | `tests/StreamDistribution.Infrastructure.Tests/Gateways/MediaMtxProbeLogVolumeTests.cs` | 60-99; 101-121; 123-150; 197-237 (current) | **D1-D4** deleted; helpers they alone used (B2); C1-C4 added in an earlier commit | D1-D4 |
| T8 | `tests/StreamDistribution.Infrastructure.Tests/Gateways/MediaMtxRtspGatewayReasonPhraseTests.cs` | 162-169 | **D5** (R9) deleted | D5 |
| T9 | `tests/Integration.Tests/StreamDistribution/MediaMtxReconcilerPassCharacterisationTests.cs` | 206-207 | member removed (`RecordingGateway`) | mechanical |
| T10 | `tests/Integration.Tests/StreamDistribution/MediaMtxSourceRepairLogVolumeIntegrationTests.cs` | 249-250; 299-300 | member removed (`RefusingGateway`, `ListFailingGateway`) | mechanical |
| T11 | `tests/Integration.Tests/StreamDistribution/ProvisionRetireRaceIntegrationTests.cs` | 49-50; 403-404 | comment re-pointed to `StreamHealthWatcher.cs:150`; forwarding member removed (`WindowedGateway`) | mechanical + doc |
| T12 | `tests/Integration.Tests/StreamDistribution/StreamRepositoryVersionAssertionIntegrationTests.cs` | 100-102 | comment re-pointed (the watcher, not the probe, produces "path not registered") | doc |

**Why each mechanical edit is not a weakening.** T1-T4, T9-T11's members are interface
implementations that no test calls directly (grep: no caller outside the ten doubles and D1-D5); once
the interface member is gone a leftover public method compiles but is dead, so SC-1's grep, not the
compiler, is what catches it. The two `PerPathCalls` counters (T5, T6) are incremented only by the
removed member and read only by D6/D7. Comments (S2, T6 docs, T11-T12) change no executable line.

**Not touched (verified):** `ParsePathHealth`, `TryReadIsoTimestamp`, `DetectTranscodeMode`
(read by `AddPageToHealth`); the `GetAllPathsHealthAsync` comment at `:194` ("shaped exactly like a
single /v3/paths/get response") describes MediaMTX's API, not our method, and stays;
`StreamDistributionRegistrationCharacterisationTests`; ScenarioSimulator's `CameraSimPathClient` and
its tests; `SfuIngestMetricsTests` (calls MediaMTX's `/v3/paths/get` directly); `ci-shards/*.filter`
(no integration class added or removed); `MediaMtxRtspGateway.PageReading.cs` (the shared
`EnsureSuccess` keeps four call sites).

## 3. Observable-behaviour check

The removed method has no caller in any process, so no request, log record, metric, trace, hook call
(ADR-0171) or health outcome originates from it. The watcher's sweep, the source repair and the
reconciler call unchanged members. `MediaMtxRtspGateway.cs` drops from 334 to ~311 lines (ADR-0084
advisory 300; still over, unchanged in kind). **Behaviour-preserving.**

## 4. Characterisation facts (new, in `MediaMtxProbeLogVolumeTests`, written first)

Through the existing `BuildGatewayHost` (shipped appsettings, real `AddServiceDefaults`, real
`AddMediaMtxGateway`, stub primary handler). A list responder answers
`/v3/paths/list?page=0&itemsPerPage=100` with
`{"pageCount":1,"itemCount":1,"items":[{"name":"cam-<guid>","ready":<bool>}]}`:

- **C1** `A_fab_scale_run_of_healthy_health_sweeps_writes_no_information_record` (Theory: Production,
  Development): 250 `GetAllPathsHealthAsync` reads, each returns one ready path; `stub.Requests` 250;
  first URI starts with `http://mediamtx.test/v3/paths/list`; `CountRoutine == 0`.
- **C2** `A_health_sweep_answered_not_ready_writes_no_information_record`: `ready:false`; result's one
  entry `IsReady == false`; `CountRoutine == 0`.
- **C3** `A_health_sweep_that_needs_retries_still_logs_them_at_warning_and_its_failure_at_error`: 503,
  `zeroRetryDelay: true`; throws `HttpRequestException`; Polly `ExecutionAttempt`@Warning,
  `OnRetry`@Warning, `ExecutionAttempt`@Error present. Guards against silencing the whole Polly
  category (P2). The Error record exists only when the retries fit inside `ListReadDeadline` (5 s);
  under the shipped backoff the deadline cancels the read first (no Error `ExecutionAttempt`, the
  gateway throws the deadline message), a property of the sweep after spec 418, not of the removed
  probe. Hence the zero delay, which needs the harness fix of configuring the options name
  `-standard` (D3 never applied it and ran the real backoff for 7 s; without the fix C3 fails in 5 s
  on develop).
- **C4** `An_environment_override_brings_the_health_sweep_exchange_back_without_a_rebuild`: overrides
  as D4; four gateway handler records at Information; the single Polly `ExecutionAttempt` at Debug.

Each must be **green on develop** before the removal commit (the properties already hold: spec 331's
severity override is per named client, not per route). If any is red on develop, stop: that is a
finding, not a test to adjust.

Characterisation set that must pass **unmodified** (beyond C1-C4): every test in
StreamDistribution.Infrastructure.Tests, StreamDistribution.Application.Tests and Architecture.Tests
other than D1-D7, with only §2's mechanical lines changed; Integration.Tests must build (its four
touched classes run in CI, Docker-backed).

## 5. Commits (one PR, each commit builds alone)

1. `test(3013): pin the gateway client's log volume through the health sweep` - C1-C4 only.
2. `refactor(3013): remove the unused single-path health probe` - S1-S4, T1-T12 (incl. D1-D7).
3. `docs(3013): tick the tasks of spec 419` (+ a one-line pointer in spec 415's status/SC-1 that US1
   was delivered by spec 419).

## 6. Risks

| Risk | Mitigation |
|---|---|
| C1-C4 encode something other than what D1-D4 pinned | Same harness, same assertions, only the method and responder change; reviewer compares pairwise |
| An edit lands outside the authorised set | SC-4 diff check; tasks forbid any assertion edit outside D6/D7 |
| A dead member survives on a double | SC-1 grep over `src` and `tests` |
| An unused helper or `using` triggers IDE0005/IDE0051 in Release | Release build in T005; remove only what D1-D4 orphaned (B2) |
| XML `cref` to a removed member (CS1574) | S2 and T6 doc rewordings |
