# Tasks: 002 — Watch a Camera Live

**Input:** Design documents at `specs/002-watch-camera-live/`

**Prerequisites:** [spec.md](./spec.md) (Phase 1 closed, PR #94 merged), [plan.md](./plan.md) (Phase 2 closed, PR #95 merged)

**Status:** Draft (Phase 3 — Tasks)

## Format: `[ID] [P?] [Story] Description`

- **[P]** — independent of any task above it in the same phase; safe to parallelise.
- **[Story]** — US1 (watch live), US2 (health badge), FOUND (foundational), POLISH.
- File paths in descriptions reference the layout from [plan.md](./plan.md).

## Path conventions

Per [plan.md](./plan.md):

- Backend: `src/StreamDistribution/{Domain,Application,Infrastructure,Api}/`, `src/Shared.Contracts/StreamDistribution/`, `src/MigrationRunner/`, `src/AppHost/`
- Frontend: `apps/shared/src/{api,streaming,ui/composites}/`, `apps/management-web/src/features/cameras/`
- Tests: `tests/StreamDistribution.Domain.Tests/`, `tests/StreamDistribution.Application.Tests/`, `tests/Integration.Tests/StreamDistribution/`

Setup tasks from spec 001 (Option, Result, Ensure, AggregateRoot, etc.) are NOT repeated here — they already exist in `Shared.Kernel` and are reused.

---

## Phase 1: Foundational — Aspire MediaMTX + per-context plumbing

Blocks every user-story task. Adds the StreamDistribution-specific infrastructure that doesn't depend on the Stream aggregate's shape.

- [ ] **T001 [FOUND]** `Aspire.Hosting.Containers` is already present; no new NuGet packages needed for the MediaMTX wiring. Verify in `Directory.Packages.props` and document the version pin in the PR body.
- [ ] **T002 [P] [FOUND]** Add the MediaMTX YAML config at `src/AppHost/Resources/mediamtx.yml` with `authMethod: http`, `externalAuthenticationURL: http://stream-distribution:8080/streams/{path}/authorize`, RTSP-on-8554, WHEP-on-8889, API-on-9997. Empty `paths:` section — paths get added at runtime by `StreamDistribution.Api`.
- [ ] **T003 [FOUND]** Wire MediaMTX in `src/AppHost/AppHost.cs`: `builder.AddContainer("mediamtx", "bluenviron/mediamtx", "latest-ffmpeg").WithBindMount("Resources/mediamtx.yml", "/mediamtx.yml").WithHttpEndpoint(port: 8889, name: "whep").WithHttpEndpoint(port: 9997, name: "api").WithEndpoint(port: 8554, name: "rtsp", scheme: "tcp")`. Persistent lifetime in dev, ephemeral in E2ETests mode.
- [ ] **T004 [FOUND]** Add `stream-distribution-db` database to the existing `postgres` resource: `postgres.AddDatabase("stream-distribution-db");`.
- [ ] **T005 [FOUND]** Wire the `stream-distribution` API project in `AppHost.cs` with `WithHttpEndpoint()`, `WithReference(streamDistributionDb)`, `WithReference(rabbitmq)`, `WithReference(keycloak)`, `WithReference(mediamtx)`, `WaitForCompletion(migrations)`, `WaitFor(mediamtx)`. Replace the existing placeholder `.AddProject<...StreamDistribution_Api>()` line.
- [ ] **T006 [FOUND]** `Migrations` project ref: `migrations.WithReference(streamDistributionDb)` in `AppHost.cs` so `MigrationRunner` gets the new connection string.
- [ ] **T007 [P] [FOUND]** `StreamDistribution` Infrastructure NuGet refs in `src/StreamDistribution/Infrastructure/SmartSentinelEye.StreamDistribution.Infrastructure.csproj`: `Microsoft.EntityFrameworkCore`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore.Design`, `WolverineFx.EntityFrameworkCore`. Identical to `CameraCatalog.Infrastructure`.
- [ ] **T008 [P] [FOUND]** `StreamDistribution.Application` project gets `ProjectReference` to `Shared.CQRS` + `Shared.Contracts` + `StreamDistribution.Domain` + `Shared.Kernel`. Already scaffolded; verify and adjust if missing.
- [ ] **T009 [P] [FOUND]** `StreamHealthChangedV1` integration event in `src/Shared.Contracts/StreamDistribution/StreamHealthChangedV1.cs` (per ADR-0073). Fields: `Guid Camera, string FromState, string ToState, DateTimeOffset ChangedAt, string? Error`. Implements `IIntegrationEvent`. Primitive types at the wire boundary per ADR-0040.

**Checkpoint:** `aspire run` brings up MediaMTX alongside the existing stack. `stream-distribution` Api fails to start (no implementation yet) — that's OK; the goal is connection-string availability + container health.

---

## Phase 2: User Story 1 — Watch a Camera Live (P1) 🎯

**Goal:** Authenticated admin clicks **Watch** on a registered camera; the WebRTC viewer opens and renders live video. The Stream aggregate persists its state machine through RTSP outages and recoveries; `StreamHealthChangedV1` events are published on every transition.

**Independent Test:** `ProvisionStreamIntegrationTests.Register_a_camera_provisions_a_stream_within_30_seconds_and_marks_it_Healthy` + `WhepAuthIntegrationTests.WHEP_with_a_valid_admin_token_succeeds_and_negotiates_an_SDP_answer`.

### Tests first (TDD per Karpathy guideline #4)

- [ ] **T010 [P] [US1]** `StreamIdentifierTests` in `tests/StreamDistribution.Domain.Tests/Stream/StreamIdentifierTests.cs`: `New()` returns non-empty sortable Guid v7; `From(Guid.Empty)` fails.
- [ ] **T011 [P] [US1]** `StreamStateTests` in `tests/StreamDistribution.Domain.Tests/Stream/StreamStateTests.cs`: each of the four singletons returns its canonical value; `From("Provisioning")` returns the singleton; unknown value throws.
- [ ] **T012 [P] [US1]** `TranscodeModeTests` in `tests/StreamDistribution.Domain.Tests/Stream/TranscodeModeTests.cs`: three singletons; `From` round-trip; unknown value throws.
- [ ] **T013 [P] [US1]** `MediaMtxPathTests` in `tests/StreamDistribution.Domain.Tests/Stream/MediaMtxPathTests.cs`: `For(cameraIdentifier)` produces `cam-{guid}`; regex guard rejects arbitrary strings; case-sensitive.
- [ ] **T014 [P] [US1]** `StreamTests` in `tests/StreamDistribution.Domain.Tests/Stream/StreamTests.cs`. Covers the state machine: `Provision_creates_a_provisioning_stream_and_raises_event`, `Report_healthy_from_provisioning_transitions_and_raises_event`, `Report_healthy_when_already_healthy_does_not_raise`, `Report_degraded_from_healthy_raises_event_with_error`, `Report_degraded_when_already_degraded_updates_LastError_only`, `Report_offline_from_degraded_raises_event`, `Report_offline_directly_from_healthy_throws`.
- [ ] **T015 [P] [US1]** `StreamBuilder` fluent builder in `tests/StreamDistribution.Domain.Tests/Stream/Builders/StreamBuilder.cs` (per ADR-0054).
- [ ] **T016 [P] [US1]** Test-project fakes in `tests/StreamDistribution.Application.Tests/Fakes/`: `InMemoryStreamRepository`, `InMemoryStreamQuerySource` (reuses `TestAsyncEnumerable` pattern from `CameraCatalog.Application.Tests`), `FakeRtspGateway` (scripted MediaMTX responses), `FakeClock`.
- [ ] **T017 [P] [US1]** `ProvisionStreamCommandHandlerTests`: `Provision_for_a_new_camera_creates_the_stream_and_registers_the_path`, `Provision_for_an_existing_camera_returns_the_existing_identifier_and_does_not_re_register` (idempotency), `Provision_when_MediaMTX_is_unreachable_returns_RtspGatewayUnavailable_and_does_not_save`.
- [ ] **T018 [P] [US1]** `ReportStreamHealthCommandHandlerTests`: transitions Healthy↔Degraded↔Offline, no-event-when-no-transition, validates state-machine guard rails.
- [ ] **T019 [P] [US1]** `AuthorizeWhepCommandHandlerTests`: valid admin → 200, missing `sse.management` scope → 403, Offline stream → 403 with `StreamUnavailable` code.
- [ ] **T020 [P] [US1]** `CameraRegisteredIntegrationEventHandlerTests`: first receipt dispatches `ProvisionStreamCommand`; redelivery is a no-op (handler delegates idempotency to command handler).
- [ ] **T021 [US1]** Extend `AspireFixture` (in `tests/Integration.Tests/Fixtures/AspireFixture.cs`): wait for `mediamtx` to reach `Running`; poll MediaMTX `/v3/paths/list` for 200; add `CreateStreamDistributionDbContextAsync()`; add `StartRtspTestSourceAsync()` that boots a side-car MediaMTX container as an RTSP publisher and returns the source URL.
- [ ] **T022 [US1]** `ProvisionStreamIntegrationTests.Register_a_camera_provisions_a_stream_within_30_seconds_and_marks_it_Healthy` in `tests/Integration.Tests/StreamDistribution/ProvisionStreamIntegrationTests.cs`. End-to-end through Aspire stack.
- [ ] **T023 [US1]** `ProvisionStreamIntegrationTests.Provisioning_is_idempotent_on_event_redelivery_via_Wolverine_replay`.
- [ ] **T024 [US1]** `StreamHealthIntegrationTests.Stopping_the_RTSP_source_transitions_to_Degraded_within_15_seconds` + `StreamHealthIntegrationTests.Restarting_the_RTSP_source_transitions_back_to_Healthy_within_15_seconds`.
- [ ] **T025 [US1]** `WhepAuthIntegrationTests.WHEP_with_a_valid_admin_token_succeeds_and_negotiates_an_SDP_answer` + `WHEP_without_a_token_returns_401` + `WHEP_with_a_token_missing_sse_management_returns_403`.

### Domain layer

- [ ] **T026 [P] [US1]** `StreamIdentifier` value object in `src/StreamDistribution/Domain/Stream/StreamIdentifier.cs` (per ADR-0090). `Guid` v7-backed `IStronglyTypedId<Guid>`.
- [ ] **T027 [P] [US1]** `StreamState` enum-backed VO in `src/StreamDistribution/Domain/Stream/StreamState.cs`. Four singletons (`Provisioning`, `Healthy`, `Degraded`, `Offline`) + `From(string)` factory. Mirrors `CameraStatus`'s shape.
- [ ] **T028 [P] [US1]** `TranscodeMode` enum-backed VO in `src/StreamDistribution/Domain/Stream/TranscodeMode.cs`. Three singletons (`Passthrough`, `Software`, `Unknown`).
- [ ] **T029 [P] [US1]** `MediaMtxPath` string-backed VO in `src/StreamDistribution/Domain/Stream/MediaMtxPath.cs`. Regex-guarded constructor; `For(CameraIdentifier)` factory.
- [ ] **T030 [P] [US1]** `StreamProvisionedDomainEvent` in `src/StreamDistribution/Domain/Stream/Events/StreamProvisionedDomainEvent.cs`. Carries the stream + camera identifiers + path + timestamp + operator.
- [ ] **T031 [P] [US1]** `StreamHealthChangedDomainEvent` in `src/StreamDistribution/Domain/Stream/Events/StreamHealthChangedDomainEvent.cs`. Carries `from`, `to`, `at`, optional `error`.
- [ ] **T032 [US1]** `Stream` aggregate root in `src/StreamDistribution/Domain/Stream/Stream.cs` (depends on T026–T031 + `AggregateRoot<TId>`). Private setters; `Provision(CameraIdentifier, OperatorIdentifier, IClock)` factory; `ReportHealthy(TranscodeMode, IClock)`, `ReportDegraded(string error, IClock)`, `ReportOffline(string error, IClock)` behaviours with state-machine guards.
- [ ] **T033 [P] [US1]** `IStreamRepository` interface in `src/StreamDistribution/Domain/Stream/IStreamRepository.cs`. Methods: `Task<Option<Stream>> GetByIdentifierAsync(StreamIdentifier stream, CancellationToken ct)`, `Task<Option<Stream>> GetByCameraAsync(CameraIdentifier camera, CancellationToken ct)`, `void Add(Stream stream)`, `Task SaveAsync(CancellationToken ct)`.
- [ ] **T034 [P] [US1]** `IRtspGateway` interface in `src/StreamDistribution/Domain/Stream/IRtspGateway.cs` (per plan §Backend Design / Infrastructure). Methods: `AddPathAsync`, `RemovePathAsync`, `GetPathHealthAsync` returning a `RtspPathHealth` record.

### Application layer

- [ ] **T035 [P] [US1]** `ProvisionStreamCommand` in `src/StreamDistribution/Application/Commands/ProvisionStreamCommand.cs`. `ICommand<Result<StreamIdentifier, ProvisionStreamError>>`. Fields: `CameraIdentifier Camera, string RtspSourceUrl, OperatorIdentifier ProvisionedBy`.
- [ ] **T036 [P] [US1]** `ProvisionStreamError` sealed-record hierarchy in `src/StreamDistribution/Application/Commands/ProvisionStreamErrors.cs`: `RtspGatewayUnavailable`, `InvalidRtspSource(string reason)`, `Conflict`. Each carries an `ApiError` with HTTP status.
- [ ] **T037 [US1]** `ProvisionStreamCommandHandler` in `src/StreamDistribution/Application/Commands/Handlers/ProvisionStreamCommandHandler.cs`. Depends on T032, T033, T034, `IClock`. Idempotency: `GetByCameraAsync` first; if present, return existing `StreamIdentifier` as `Success` (FR-011).
- [ ] **T038 [P] [US1]** `ReportStreamHealthCommand` in `src/StreamDistribution/Application/Commands/ReportStreamHealthCommand.cs`. Fields: `CameraIdentifier Camera, RtspPathHealth Observation`.
- [ ] **T039 [P] [US1]** `ReportStreamHealthError` sealed-record hierarchy in `src/StreamDistribution/Application/Commands/ReportStreamHealthErrors.cs`: `StreamNotFound`, `InvalidStateTransition(string from, string to)`.
- [ ] **T040 [US1]** `ReportStreamHealthCommandHandler`. Loads stream, calls the matching `ReportXxx` aggregate method. Exception-to-Result mapping for invalid transitions.
- [ ] **T041 [P] [US1]** `AuthorizeWhepCommand` in `src/StreamDistribution/Application/Commands/AuthorizeWhepCommand.cs`. Fields: `MediaMtxPath Path, string BearerToken`.
- [ ] **T042 [P] [US1]** `AuthorizeWhepError` sealed-record hierarchy in `src/StreamDistribution/Application/Commands/AuthorizeWhepErrors.cs`: `Unauthorized`, `Forbidden`, `StreamUnavailable`.
- [ ] **T043 [US1]** `AuthorizeWhepCommandHandler`. Validates the bearer JWT (delegated to the same `JwtBearer` validator that the rest of the API uses; resolved via DI). Checks scope `sse.management`. Loads the stream; rejects if `Offline`.
- [ ] **T044 [P] [US1]** `GetStreamQuery` in `src/StreamDistribution/Application/Queries/GetStreamQuery.cs`. `IQuery<Result<StreamHealthDto, StreamNotFoundError>>`.
- [ ] **T045 [P] [US1]** `ListStreamsQuery` in `src/StreamDistribution/Application/Queries/ListStreamsQuery.cs`. Fields: `IReadOnlyList<CameraIdentifier> Cameras`. Errors in `ListStreamsErrors.cs`: `InvalidBatchSize(int requested, int maximum)`.
- [ ] **T046 [P] [US1]** `ICameraQuerySource`-equivalent for streams: `IStreamQuerySource` interface in `src/StreamDistribution/Application/Queries/IStreamQuerySource.cs`. Exposes `IQueryable<Stream> Streams { get; }`.
- [ ] **T047 [US1]** `GetStreamQueryHandler` and `ListStreamsQueryHandler` in `src/StreamDistribution/Application/Queries/Handlers/`. Map to `StreamHealthDto`; compute `whepUrl` from the configured MediaMTX WHEP base URL + path.
- [ ] **T048 [P] [US1]** `StreamHealthDto` + `StreamListItemDto` in `src/StreamDistribution/Application/DTOs/`. Primitive types only at the API boundary.
- [ ] **T049 [US1]** `CameraRegisteredIntegrationEventHandler` in `src/StreamDistribution/Application/EventHandlers/CameraRegisteredIntegrationEventHandler.cs`. Wolverine handler that maps `CameraRegisteredV1` → `ProvisionStreamCommand`.
- [ ] **T050 [US1]** `StreamHealthChangedDomainEventHandler` in `src/StreamDistribution/Application/EventHandlers/StreamHealthChangedDomainEventHandler.cs`. Translates domain event → `StreamHealthChangedV1` and publishes via `IEventBus` (Wolverine outbox).
- [ ] **T051 [US1]** `StreamProvisionedDomainEventHandler` in `src/StreamDistribution/Application/EventHandlers/StreamProvisionedDomainEventHandler.cs`. Logs at Information level on provision (per ADR-0050). No integration event yet — that fires on first state transition.

### Infrastructure layer

- [ ] **T052 [US1]** `StreamDistributionDbContext` in `src/StreamDistribution/Infrastructure/Persistence/StreamDistributionDbContext.cs`. `DbSet<Stream> Streams`.
- [ ] **T053 [US1]** `StreamConfiguration : IEntityTypeConfiguration<Stream>` in `src/StreamDistribution/Infrastructure/Persistence/Configurations/StreamConfiguration.cs`. VO conversions; `Option<T>` → nullable column conversions; unique index on `camera_id`; `Version` concurrency token.
- [ ] **T054 [US1]** `StreamRepository : IStreamRepository` in `src/StreamDistribution/Infrastructure/Persistence/StreamRepository.cs`. Mirrors `CameraRepository`.
- [ ] **T055 [US1]** `StreamQuerySource : IStreamQuerySource` in `src/StreamDistribution/Infrastructure/Persistence/StreamQuerySource.cs`. `AsNoTracking()`.
- [ ] **T056 [US1]** `StreamDistributionMigrator : IMigrator` in `src/StreamDistribution/Infrastructure/Persistence/StreamDistributionMigrator.cs`. Mirrors `CameraCatalogMigrator`.
- [ ] **T057 [US1]** `DesignTimeDbContextFactory` in `src/StreamDistribution/Infrastructure/Persistence/DesignTimeDbContextFactory.cs` so `dotnet ef migrations add` works without Aspire.
- [ ] **T058 [US1]** EF Core migration `InitialStreamDistribution` via `dotnet ef migrations add InitialStreamDistribution --project src/StreamDistribution/Infrastructure --startup-project src/MigrationRunner --output-dir Persistence/Migrations`. Commit generated files; remove any bogus `Npgsql:IndexInclude` annotation if generator emits it (regression caught in PR #90).
- [ ] **T059 [P] [US1]** `MediaMtxOptions` in `src/StreamDistribution/Infrastructure/Gateways/MediaMtxOptions.cs`. Bound from `IConfiguration` section `MediaMtx`; fields: `ManagementUrl`, `WhepBaseUrl`.
- [ ] **T060 [US1]** `MediaMtxRtspGateway : IRtspGateway` in `src/StreamDistribution/Infrastructure/Gateways/MediaMtxRtspGateway.cs`. Typed `HttpClient` against the Aspire-resolved MediaMTX endpoint. Polly retry policy: 1 s, 2 s, 5 s, 10 s, 30 s cap.
- [ ] **T061 [US1]** `StreamHealthWatcher : BackgroundService` in `src/StreamDistribution/Infrastructure/HealthWatcher/StreamHealthWatcher.cs`. Polls every 2 s; lists active streams; calls `GetPathHealthAsync` per stream; dispatches `ReportStreamHealthCommand` only when state would change.
- [ ] **T062 [US1]** `StreamDistributionPersistenceModule.AddStreamDistributionPersistence(IHostApplicationBuilder)` in `src/StreamDistribution/Infrastructure/StreamDistributionPersistenceModule.cs`. Slim — `DbContext` + `IMigrator` only (consumed by `MigrationRunner`, mirroring `AddCameraCatalogPersistence` from PR #90).
- [ ] **T063 [US1]** `StreamDistributionInfrastructureModule.AddStreamDistributionInfrastructure(IHostApplicationBuilder)` in `src/StreamDistribution/Infrastructure/StreamDistributionInfrastructureModule.cs`. Calls `AddStreamDistributionPersistence()`, then registers `IStreamRepository`, `IStreamQuerySource`, `IRtspGateway → MediaMtxRtspGateway`, the typed `HttpClient` for MediaMTX, the `StreamHealthWatcher` `BackgroundService`, the domain-event handler, `IEventBus → WolverineEventBus`, and `AddWolverineForContext(moduleQueuePrefix: "stream-distribution", outboxSchema: "wolverine_stream_distribution", postgresConnectionName: "stream-distribution-db")`.
- [ ] **T064 [US1]** Register `StreamDistributionMigrator` in `src/MigrationRunner/Program.cs` via `builder.AddStreamDistributionPersistence();`. MigrationRunner now runs both context migrators sequentially before any Api starts.

### Api layer

- [ ] **T065 [US1]** `StreamEndpoints.MapStreamEndpoints(IEndpointRouteBuilder)` in `src/StreamDistribution/Api/StreamEndpoints.cs` (per ADR-0070). Three routes: `GET /streams/{cameraIdentifier:guid}`, `GET /streams?cameraIdentifiers=...`, `POST /streams/{path}/authorize` (the MediaMTX callback — `AllowAnonymous` at routing layer; the handler validates the forwarded bearer).
- [ ] **T066 [US1]** `StreamDistributionApiModule.AddStreamDistributionApi(IServiceCollection)` in `src/StreamDistribution/Api/StreamDistributionApiModule.cs`. Registers all command/query handlers as scoped services.
- [ ] **T067 [US1]** Wire endpoints in `src/StreamDistribution/Api/Program.cs`: `builder.AddBearerAuthentication(); builder.AddStreamDistributionInfrastructure(); builder.Services.AddStreamDistributionApi(); app.MapStreamEndpoints();`.

### Frontend

- [ ] **T068 [P] [US1]** `streamsApi` RTK Query slice in `apps/shared/src/api/streams.api.ts`. `getStream(cameraIdentifier)` and `listStreams(cameraIdentifiers[])`. `pollingInterval: 5000` exposed via `useListStreamsQuery` consumer.
- [ ] **T069 [P] [US1]** Add export path `./api/streams.api` to `apps/shared/package.json` `exports` (mirrors the cameras.api wiring).
- [ ] **T070 [US1]** Wire `streamsApi.reducerPath` reducer + middleware into `apps/management-web/src/app/store.ts`.
- [ ] **T071 [P] [US1]** `WhepClient` in `apps/shared/src/streaming/WhepClient.ts`. Thin wrapper over `RTCPeerConnection` + `fetch(whepUrl, ...)`. Add export path `./streaming` to `apps/shared/package.json` `exports`.
- [ ] **T072 [P] [US1]** `WhepClient.test.ts` in `apps/shared/src/streaming/`. Mocks `fetch` + `RTCPeerConnection` to assert the SDP offer is sent and a 401 surfaces as `WhepError`.
- [ ] **T073 [US1]** `CameraViewer` composite in `apps/shared/src/ui/composites/CameraViewer.tsx`. Accepts `{ cameraIdentifier }` prop; polls `useGetStreamQuery`; mounts `WhepClient`; renders the `<video>` element + `ViewerOverlay` for non-live status (Provisioning, Connecting, Reconnecting, Error). Add export path to `apps/shared/package.json`.
- [ ] **T074 [P] [US1]** `CameraViewerPanel` in `apps/management-web/src/features/cameras/CameraViewerPanel.tsx`. Side panel that mounts `<CameraViewer cameraIdentifier={...} />` when a camera row is selected.
- [ ] **T075 [US1]** Update `CamerasPage.tsx` to add a per-row **Watch** button that opens the panel.
- [ ] **T076 [P] [US1]** `CameraViewerPanel.test.tsx` in `apps/management-web/src/features/cameras/`. Mocks `useGetStreamQuery` to return a `Healthy` stream + a fake `whepUrl`; asserts the `<CameraViewer />` mounts and unmounts on close.

---

## Phase 3: User Story 2 — Stream health badges in the cameras list (P1)

**Goal:** The cameras list shows each camera's stream health as a coloured badge with a tooltip carrying `lastSuccessAt` and (if non-Healthy) the `error`. The list polls `/streams?cameraIdentifiers=...` every 5 seconds.

**Independent Test:** `StreamHealthIntegrationTests.Five_cameras_with_mixed_reachability_show_correct_states_in_the_list_API_within_30_seconds`.

### Backend

- [ ] **T077 [US2]** `ListStreamsByCamerasIntegrationTests` in `tests/Integration.Tests/StreamDistribution/`. Register five cameras with mixed RTSP reachability; assert `GET /streams?cameraIdentifiers=...` returns the expected state per identifier within 30 seconds.
- [ ] **T078 [US2]** No new backend code — `ListStreamsQueryHandler` (T047) already handles the batch case. Just verify FR-006 / FR-017 against the integration test.

### Frontend

- [ ] **T079 [P] [US2]** `StreamHealthBadge` in `apps/management-web/src/features/cameras/StreamHealthBadge.tsx`. Renders a coloured pill per state (green/yellow/red/grey) with a Radix tooltip showing `lastSuccessAt` and `error`.
- [ ] **T080 [US2]** Extend `CamerasPage.tsx` to add a `Stream` column to the DataTable; cell renders `<StreamHealthBadge cameraIdentifier={row.cameraIdentifier} />`. Page-level `useListStreamsQuery(visibleCameraIds, { pollingInterval: 5000 })` feeds the per-row hook via context, avoiding N independent polls.
- [ ] **T081 [P] [US2]** `StreamHealthBadge.test.tsx`: renders each state with the correct colour + tooltip text.
- [ ] **T082 [US2]** Update `App.test.tsx` to mock `useListStreamsQuery` so the smoke test renders the cameras page including the new column.

---

## Phase 4: Polish

- [ ] **T083 [P] [POLISH]** Verify per-layer coverage gates pass for the new context via `pwsh scripts/coverage-check.ps1`. Domain ≥ 90 %, Application ≥ 80 %, `Shared.Contracts.StreamDistribution` ≥ 90 %. Add tests for any gaps; never relax thresholds. The script already enforces ADR-0065.
- [ ] **T084 [P] [POLISH]** Verify `Architecture.Tests` still pass — StreamDistribution.Domain has no EF / Marten / Wolverine / MediaMTX references; no cross-context project references between StreamDistribution and CameraCatalog. Existing rules cover the new types automatically (assembly-level wildcards).
- [ ] **T085 [POLISH]** Reconcile-on-startup pass in `StreamDistribution.Api`: a hosted service that runs once on app start, lists streams from the DB, ensures each path exists in MediaMTX, drops orphans. Documented as a risk-mitigation in plan.md. Add an integration test that restarts MediaMTX mid-test and asserts reconciliation.
- [ ] **T086 [POLISH]** Measure click-to-first-frame latency: integration test asserts WebRTC peer connection reaches `connected` within **3 s p95** over 20 sequential viewer opens against the AspireFixture stack. Report measured value in the PR body's "Latency budget impact" section (per ADR-0031). Headless browser deferred per plan.md — measure the WHEP handshake completion time instead.
- [ ] **T087 [POLISH]** Update repo `README.md` quickstart with the **Watch a camera** step appended to the existing "Register your first camera" walkthrough (point the browser at the new viewer panel; verify a live frame).
- [ ] **T088 [POLISH]** Verification gate (Phase 5 of the 7-phase workflow per ADR-0037): start the system via `aspire run`, sign in as admin, register a real camera through the UI, click **Watch**, observe a live frame, disconnect the camera, observe the Degraded banner, reconnect, observe recovery. Capture a screenshot or describe the verification clearly in the PR.

---

## Dependencies and execution order

### Cross-phase

- **Phase 1 (Foundational):** depends only on spec 001 being merged (which it is). T001–T009 are mostly `[P]`; T003 must follow T002 (config file before container resource); T005 must follow T003/T004 (resources before project references).
- **Phase 2 (US1):** depends on Phase 1. T021 (AspireFixture extension) blocks T022–T025; tests T010–T020 don't block implementation but should land before per Karpathy guideline #4.
- **Phase 3 (US2):** depends on Phase 2 (the `streamsApi` slice and `GET /streams` endpoint must exist). T077 depends on the full Phase 2 implementation chain.
- **Phase 4 (Polish):** depends on Phases 2 + 3. T086 needs at least one Healthy stream; T087 + T088 need the full UI flow.

### Within Phase 2 (US1)

```
Tests first (T010–T020) — all [P]
T021 AspireFixture extension — blocks T022–T025
Domain VOs (T026–T031) — all [P]
T032 Stream aggregate — depends on T026–T031
T033–T034 interfaces — [P] with T032
Application records (T035, T036, T038, T039, T041, T042, T044, T045, T046, T048) — [P]
T037 ProvisionStream handler — depends on T032, T033, T034, T035, T036
T040 ReportStreamHealth handler — depends on T032, T033, T038, T039
T043 AuthorizeWhep handler — depends on T032, T033, T041, T042
T047 query handlers — depends on T044, T045, T046, T048
T049 Wolverine subscriber — depends on T037
T050, T051 domain-event handlers — depend on T032, T009 (StreamHealthChangedV1)
Infrastructure (T052–T064) — mostly sequential within the layer
Api (T065–T067) — depends on Infrastructure layer
Frontend (T068–T076) — T068–T071 [P]; T073 depends on T068, T071; T074 depends on T073;
                       T075 depends on T074; T076 depends on T074; T072 [P]
```

### Parallel opportunities

- All Phase 1 [FOUND] tasks except the dependency chain T002→T003→T005→T006.
- Domain tests (T010–T015) + Application fakes (T016) + handler tests (T017–T020) in parallel.
- Domain value objects (T026–T029) + domain events (T030–T031) in parallel.
- Application records (T035, T036, T038, T039, T041, T042, T044–T046, T048) in parallel with each other.
- Frontend primitives (T068, T069, T071) in parallel.
- Phase 3 frontend (T079, T081) in parallel.
- All Phase 4 [POLISH] tasks in parallel except T086 (needs implementation) and T088 (needs all UI in place).

### Coverage-gate dependency

- T083 cannot pass until Phase 2 tests (T010–T020) and Phase 3 tests (T077, T081) are written and passing.

---

## Implementation strategy

**MVP first.** Phase 1 → Phase 2 (US1) is the MVP. Stop after T076 and validate end-to-end: a registered camera produces a watchable WebRTC stream. Then add US2 (T077–T082) and Polish (T083–T088).

**Per ADR-0087 (rebase-only):** every commit is independently buildable + green-test. Suggested commit groupings for a multi-PR sequence (matches the cadence used in spec 001):

1. **PR A — Phase 1 Foundational:** MediaMTX wiring + per-context plumbing (T001–T009). No Stream aggregate yet. Asserts `aspire run` brings up MediaMTX and migrations succeed.
2. **PR B — Phase 2 Domain + Application:** Stream aggregate, value objects, commands, handlers, in-memory tests (T010–T051). `dotnet test` green; no API exposed yet.
3. **PR C — Phase 2 Infrastructure:** EF Core mapping, repository, `MediaMtxRtspGateway`, `StreamHealthWatcher`, migrations (T052–T064).
4. **PR D — Phase 2 Api + Frontend:** endpoints, RTK Query slice, `WhepClient`, `CameraViewer`, viewer panel (T065–T076).
5. **PR E — Phase 2 Integration tests:** AspireFixture extension + provision/health/WHEP-auth integration tests (T021–T025 implementation, asserting against the full Phase 2 stack).
6. **PR F — Phase 3 US2 list-health badges:** T077–T082.
7. **PR G — Phase 4 Polish:** coverage gate verification, latency measurement, README update, manual verification (T083–T088).

Resulting in ~7 PRs across the spec. Each independently bisectable.

---

## Notes

- `[P]` tasks = independent files, no order constraint within the phase.
- Each task's exact file path is in its description; reviewers can verify scope.
- Tests fail before implementation (TDD per Karpathy guideline #4).
- Spec 001 already shipped the AspireFixture + Setup primitives + per-context plumbing pattern; spec 002 reuses these directly. Net new code is the StreamDistribution context + MediaMTX wiring + WHEP frontend plumbing.
- The MediaMTX integration is the highest-risk part of spec 002 (per plan.md). Phase 1 + the first integration test should land first to de-risk the unknown.
