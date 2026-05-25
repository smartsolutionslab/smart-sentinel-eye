# Tasks: 001 — Register a Camera

**Input:** Design documents at `specs/001-register-camera/`

**Prerequisites:** [spec.md](./spec.md) (Phase 2 closed), [plan.md](./plan.md) (Phase 3 closed)

**Status:** Draft (Phase 4 — Tasks)

## Format: `[ID] [P?] [Story] Description`

- **[P]** — independent of any task above it in the same phase; safe to parallelise.
- **[Story]** — US1 (register), US2 (list), SETUP, FOUND (foundational), POLISH.
- File paths in descriptions reference the layout from [plan.md](./plan.md).

## Path conventions

Per [plan.md](./plan.md):

- Backend: `src/CameraCatalog/{Domain,Application,Infrastructure,Api}/`, `src/Shared.{Kernel,CQRS,Contracts}/`, `src/MigrationRunner/`, `src/AppHost/`, `src/ServiceDefaults/`
- Frontend: `apps/shared/`, `apps/management-web/`
- Tests: `tests/CameraCatalog.Domain.Tests/`, `tests/CameraCatalog.Application.Tests/`, `tests/Integration.Tests/`

---

## Phase 1: Setup — `Shared.Kernel` / `Shared.CQRS` / `Shared.Contracts` foundations

These types are referenced by every later task. Karpathy guideline #2: minimum code that solves the problem — implement only what US1 and US2 actually need. Future features add to these projects per their own specs.

- [ ] **T001 [P] [SETUP]** `Option<T>` value type in `src/Shared.Kernel/Option.cs` (per ADR-0048). Includes `Some(T)`, `None`, `HasValue`, `Match`, `Map`, `GetOrDefault`. Reference-tests in next phase.
- [ ] **T002 [P] [SETUP]** `Result` + `Result<T>` + `Result<T, TError>` in `src/Shared.Kernel/Result.cs` (per ADR-0047). Includes `Match`, `IsSuccess`, `Value`, `Error`. Static `Result.Success(T)` / `Result.Failure(TError)` factories.
- [ ] **T003 [P] [SETUP]** `ApiError` abstract record in `src/Shared.Kernel/ApiError.cs` (per ADR-0089): `record ApiError(string Code, string Message, HttpStatusCode Status)`.
- [ ] **T004 [P] [SETUP]** `IValueObject` + `IValueObject<TValue>` marker interfaces in `src/Shared.Kernel/Primitives/IValueObject.cs` (per ADR-0066).
- [ ] **T005 [P] [SETUP]** `StringValueObject` abstract record in `src/Shared.Kernel/Primitives/StringValueObject.cs` (per ADR-0046). Implements `IValueObject<string>`; equality/ToString built in.
- [ ] **T006 [P] [SETUP]** `IStronglyTypedId<TValue>` marker in `src/Shared.Kernel/Primitives/IStronglyTypedId.cs` (per ADR-0090). Constrains identifier types.
- [ ] **T007 [P] [SETUP]** `IClock` + `SystemClock` in `src/Shared.Kernel/IClock.cs`. Used for deterministic `RegisteredAt` in tests.
- [ ] **T008 [P] [SETUP]** `Ensure` validator-chain helper in `src/Shared.Kernel/Ensure.cs` (per ADR-0059). Methods: `IsNotNullOrWhiteSpace`, `HasMaxLength`, `HasMinLength`, `StartsWith`, `Matches(Regex)`, `AndReturn`.
- [ ] **T009 [P] [SETUP]** `IDomainEvent` marker in `src/Shared.Kernel/IDomainEvent.cs` + `AggregateRoot<TIdentifier>` base class in `src/Shared.Kernel/AggregateRoot.cs`. Carries `TIdentifier Id`, `int Version`, `IReadOnlyList<IDomainEvent> PendingEvents`, protected `Raise(IDomainEvent)`, `ClearEvents()`.
- [ ] **T010 [P] [SETUP]** `ICommand<TResult>`, `IQuery<TResult>`, `ICommandHandler<TCommand, TResult>`, `IQueryHandler<TQuery, TResult>` interfaces in `src/Shared.CQRS/` (per ADR-0042, ADR-0057).
- [ ] **T011 [P] [SETUP]** `IIntegrationEvent` marker in `src/Shared.Contracts/IIntegrationEvent.cs` (per ADR-0040).
- [ ] **T012 [P] [SETUP]** `OperatorIdentifier` value object in `src/Shared.Kernel/OperatorIdentifier.cs`. Backed by `Guid` (typed wrapper); used cross-context for "the operator who did X". Lives in Shared.Kernel because every context needs it.

**Checkpoint:** Shared building blocks compile. No tests yet (each setup task is small; tests for these primitives ship via the value-object tests in Phase 3).

---

## Phase 2: Foundational — auth, Aspire resources, Wolverine defaults

Blocks every user-story task. Add only what US1 needs; broader pieces (e.g. richer Identity setup) wait for their own specs.

- [ ] **T013 [FOUND]** Aspire AppHost resources in `src/AppHost/AppHost.cs`: add `AddPostgres("postgres")`, `AddRabbitMQ("rabbitmq")`, `AddKeycloak("keycloak")` (Aspire community integrations) with development-mode credentials. Reference them from the existing `camera-catalog` Api and from `MigrationRunner`.
- [ ] **T014 [FOUND]** Aspire NuGet packages added to `Directory.Packages.props`: `Aspire.Hosting.PostgreSQL`, `Aspire.Hosting.RabbitMQ`, `Aspire.Hosting.Keycloak` (latest 9.x-line versions matching `Aspire.Hosting.NodeJs`). PackageReferences in `src/AppHost/SmartSentinelEye.AppHost.csproj`.
- [ ] **T015 [FOUND]** `AddWolverineForContext` extension method in `src/ServiceDefaults/WolverineDefaults.cs` (per ADR-0088): per-module queue isolation prefix, `TransactionMiddlewareMode.Eager`, Postgres outbox configuration, `AutoBuildMessageStorageOnStartup`. Single method consumed by every context's Infrastructure module.
- [ ] **T016 [FOUND]** `AddBearerAuthentication` extension method in `src/ServiceDefaults/AuthenticationDefaults.cs`: configures JWT bearer authn against the Keycloak realm exposed by Aspire. Adds the `"admin"` authorization policy requiring scope `sse.management`. (Minimal — full Identity context lands in its own spec.)
- [ ] **T017 [FOUND]** Camera Catalog NuGet refs in `Directory.Packages.props`: `Microsoft.EntityFrameworkCore`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore.Design` (for migrations CLI), `WolverineFx.EntityFrameworkCore`. PackageReferences in `src/CameraCatalog/Infrastructure/SmartSentinelEye.CameraCatalog.Infrastructure.csproj`.

**Checkpoint:** Aspire stack starts via `aspire run`. Postgres, RabbitMQ, Keycloak come up. Authentication scaffold compiles. No domain code yet.

---

## Phase 3: User Story 1 — Register a Camera (P1) 🎯 MVP

**Goal:** Authenticated admin can POST to `/cameras` and receive a `CameraIdentifier` for a newly-persisted camera. The catalog persists the row, raises the domain event, translates to `CameraRegisteredV1`, and stages it in the Wolverine outbox for at-least-once delivery.

**Independent Test:** `RegisterCameraIntegrationTests.Register_a_camera_end_to_end_persists_the_row_and_publishes_CameraRegisteredV1` (via AspireFixture).

### Tests first (TDD per Karpathy guideline #4)

- [ ] **T018 [P] [US1]** `CameraIdentifierTests` in `tests/CameraCatalog.Domain.Tests/Camera/CameraIdentifierTests.cs`: `New()` returns a non-empty sortable Guid v7; `From(Guid.Empty)` fails.
- [ ] **T019 [P] [US1]** `CameraNameTests` in `tests/CameraCatalog.Domain.Tests/Camera/CameraNameTests.cs`: trimming, empty/whitespace rejection, 200-char cap, case-insensitive equality, original-casing display.
- [ ] **T020 [P] [US1]** `RtspUrlTests` in `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlTests.cs`: `rtsp://` required, 1–2048 chars, `user:password@` rejected, scheme-case-insensitive.
- [ ] **T021 [P] [US1]** `CameraTests` in `tests/CameraCatalog.Domain.Tests/Camera/CameraTests.cs`: `Register` assigns identifier, raises `CameraRegisteredDomainEvent` exactly once, sets `Status = Registered`.
- [ ] **T022 [P] [US1]** `CameraBuilder` in `tests/CameraCatalog.Domain.Tests/Camera/Builders/CameraBuilder.cs` (per ADR-0054).
- [ ] **T023 [P] [US1]** `InMemoryCameraRepository` + `InMemoryClock` fakes in `tests/CameraCatalog.Application.Tests/Fakes/`.
- [ ] **T024 [P] [US1]** `RegisterCameraCommandHandlerTests` in `tests/CameraCatalog.Application.Tests/Commands/RegisterCameraCommandHandlerTests.cs`: success returns identifier + raised event + outbox-marked aggregate; duplicate name returns `NameAlreadyTaken`; persistence failure surfaces as exception (not Result.Failure).
- [ ] **T025 [US1]** `AspireFixture` + `AspireCollection` in `tests/Integration.Tests/Fixtures/` (first integration fixture in the repo per ADR-0068). Boots `Projects.SmartSentinelEye_AppHost`, exposes `HttpClient CameraCatalog`, `IDbContextFactory<CameraCatalogDbContext>`.
- [ ] **T026 [US1]** `RegisterCameraIntegrationTests.Register_a_camera_end_to_end_persists_the_row_and_publishes_CameraRegisteredV1` in `tests/Integration.Tests/CameraCatalog/`. Depends on T025 and the full Phase 3 implementation chain.
- [ ] **T027 [US1]** `RegisterCameraIntegrationTests.Register_a_camera_with_a_duplicate_name_returns_409_via_HTTP`.

### Domain layer

- [ ] **T028 [P] [US1]** `CameraIdentifier` value object in `src/CameraCatalog/Domain/Camera/CameraIdentifier.cs` (per ADR-0090).
- [ ] **T029 [P] [US1]** `CameraName` value object in `src/CameraCatalog/Domain/Camera/CameraName.cs`. Case-insensitive equality via `NormalizedValue`.
- [ ] **T030 [P] [US1]** `RtspUrl` value object in `src/CameraCatalog/Domain/Camera/RtspUrl.cs`. `Uri.UserInfo` check rejects credentials.
- [ ] **T031 [P] [US1]** `CameraStatus` enum-backed VO in `src/CameraCatalog/Domain/Camera/CameraStatus.cs`. Only `Registered` reachable in this slice.
- [ ] **T032 [P] [US1]** `CameraRegisteredDomainEvent` in `src/CameraCatalog/Domain/Camera/Events/CameraRegisteredDomainEvent.cs`.
- [ ] **T033 [US1]** `Camera` aggregate root in `src/CameraCatalog/Domain/Camera/Camera.cs` (depends on T028–T032 + T009 `AggregateRoot<TId>`). Carries private setters; static `Register(CameraName, RtspUrl, OperatorIdentifier, IClock)` factory.
- [ ] **T034 [P] [US1]** `ICameraRepository` interface in `src/CameraCatalog/Domain/Camera/ICameraRepository.cs`. Methods: `Task<Option<Camera>> GetByIdentifierAsync(CameraIdentifier camera, CancellationToken ct)`, `Task<bool> ExistsByNameAsync(CameraName name, CancellationToken ct)`, `void Add(Camera camera)`, `Task SaveAsync(CancellationToken ct)`.

### Application layer

- [ ] **T035 [P] [US1]** `RegisterCameraCommand` record in `src/CameraCatalog/Application/Commands/RegisterCameraCommand.cs`. Implements `ICommand<Result<CameraIdentifier, RegisterCameraError>>`. Fields: `CameraName Name`, `RtspUrl Url`, `OperatorIdentifier RegisteredBy`.
- [ ] **T036 [P] [US1]** `RegisterCameraError` sealed-record hierarchy in `src/CameraCatalog/Application/Commands/RegisterCameraErrors.cs`. Cases: `NameAlreadyTaken`, `InvalidName(string Reason)`, `InvalidUrl(string Reason)`. Each carries an `ApiError(Code, Message, HttpStatusCode)`.
- [ ] **T037 [US1]** `RegisterCameraCommandHandler` in `src/CameraCatalog/Application/Commands/Handlers/RegisterCameraCommandHandler.cs`. Depends on T033, T034, T035, T036 and `IClock`. Logs at Information level on success (per ADR-0050).
- [ ] **T038 [P] [US1]** `CameraRegisteredV1` integration event in `src/Shared.Contracts/CameraCatalog/CameraRegisteredV1.cs` (per ADR-0073). Fields: `CameraIdentifier Camera, CameraName Name, RtspUrl Url, DateTimeOffset RegisteredAt, OperatorIdentifier RegisteredBy`. Implements `IIntegrationEvent`.
- [ ] **T039 [US1]** `CameraRegisteredDomainEventHandler` in `src/CameraCatalog/Application/EventHandlers/CameraRegisteredDomainEventHandler.cs`. Translates domain event → `CameraRegisteredV1` and publishes via `IMessageBus` (Wolverine outbox).

### Infrastructure layer

- [ ] **T040 [US1]** `CameraCatalogDbContext` in `src/CameraCatalog/Infrastructure/Persistence/CameraCatalogDbContext.cs`. `DbSet<Camera> Cameras`. Wolverine outbox tables auto-added by `AddWolverineForContext`.
- [ ] **T041 [US1]** `CameraConfiguration : IEntityTypeConfiguration<Camera>` in `src/CameraCatalog/Infrastructure/Persistence/Configurations/CameraConfiguration.cs`. Maps owned VO columns; unique index `ux_cameras_name_lower` on `LOWER(name)`; `Version` configured as concurrency token (per ADR-0043).
- [ ] **T042 [US1]** `CameraRepository : ICameraRepository` in `src/CameraCatalog/Infrastructure/Persistence/CameraRepository.cs`.
- [ ] **T043 [US1]** `CameraCatalogMigrator : IMigrator` in `src/CameraCatalog/Infrastructure/Persistence/CameraCatalogMigrator.cs`. Single `RunAsync` invoked by MigrationRunner.
- [ ] **T044 [US1]** EF Core migration `InitialCameraCatalog` via `dotnet ef migrations add InitialCameraCatalog --project src/CameraCatalog/Infrastructure --startup-project src/MigrationRunner`. Commit the generated `Migrations/<timestamp>_InitialCameraCatalog.{cs,Designer.cs}` and snapshot.
- [ ] **T045 [US1]** `CameraCatalogInfrastructureModule.AddCameraCatalogInfrastructure(IServiceCollection)` in `src/CameraCatalog/Infrastructure/CameraCatalogInfrastructureModule.cs` (per ADR-0051). Registers `CameraCatalogDbContext` against the Aspire-provided `postgres` connection, registers `ICameraRepository`, calls `AddWolverineForContext(moduleQueuePrefix: "camera-catalog", outboxSchema: "wolverine_camera_catalog", ...)`.
- [ ] **T046 [US1]** Register `CameraCatalogMigrator` in `src/MigrationRunner/Program.cs`. Migration runs before any Api starts (per ADR-0067).

### Api layer

- [ ] **T047 [P] [US1]** `RegisterCameraRequest` record + `Deconstruct` in `src/CameraCatalog/Api/Requests/RegisterCameraRequest.cs`. Fields: `string Name, string RtspUrl`. `Deconstruct(out CameraName name, out RtspUrl url)` calls the VO factories and aggregates validation errors into an `ApiError`.
- [ ] **T048 [US1]** `CameraEndpoints.MapCameraCatalogEndpoints(IEndpointRouteBuilder)` in `src/CameraCatalog/Api/CameraEndpoints.cs` (per ADR-0070). Group `/cameras` with `RequireAuthorization("admin")`. `POST /cameras` deconstructs the request, builds the command, invokes the handler, maps `Result.Match` to `201 Created`/`Problem`.
- [ ] **T049 [US1]** `CameraCatalogApiModule.AddCameraCatalogApi(IServiceCollection)` in `src/CameraCatalog/Api/CameraCatalogApiModule.cs`. Adds OpenAPI metadata, authentication, calls `AddCameraCatalogInfrastructure()`.
- [ ] **T050 [US1]** Wire endpoint group in `src/CameraCatalog/Api/Program.cs` — `app.MapCameraCatalogEndpoints();` after `AddBearerAuthentication`.

### Frontend

- [ ] **T051 [P] [US1]** `registerCameraSchema` Zod schema in `apps/shared/src/api/cameras.schema.ts` (per ADR-0079). Includes the `user:password@` rejection regex.
- [ ] **T052 [P] [US1]** `camerasApi` RTK Query slice in `apps/shared/src/api/cameras.api.ts`. Single mutation `registerCamera` for now; `listCameras` added in US2.
- [ ] **T053 [US1]** Wire `camerasApi.reducerPath` reducer + middleware into `apps/management-web/src/app/store.ts`.
- [ ] **T054 [P] [US1]** `Button` primitive in `apps/shared/src/ui/primitives/Button.tsx`. Built on Radix Slot, styled with Tailwind tokens.
- [ ] **T055 [P] [US1]** `Input` primitive in `apps/shared/src/ui/primitives/Input.tsx`.
- [ ] **T056 [P] [US1]** `Dialog` primitive in `apps/shared/src/ui/primitives/Dialog.tsx` (Radix Dialog wrapper).
- [ ] **T057 [P] [US1]** `FormField` composite in `apps/shared/src/ui/composites/FormField.tsx`. Integrates with RHF + Radix Label.
- [ ] **T058 [US1]** `RegisterCameraDialog` in `apps/management-web/src/features/cameras/RegisterCameraDialog.tsx`. Uses primitives from T054–T057 and the Zod schema from T051.
- [ ] **T059 [US1]** `CamerasPage` minimal in `apps/management-web/src/features/cameras/CamerasPage.tsx`. Renders a "Register" button + dialog. List view comes in US2.
- [ ] **T060 [US1]** Add `/cameras` route to `apps/management-web/src/app/router.tsx` (lazy-loaded; admin-scope guard via `react-oidc-context`).
- [ ] **T061 [US1]** Frontend test `RegisterCameraDialog.test.tsx` covers happy submit + 409 error mapping.

**Checkpoint:** `aspire run` starts the stack; an admin can POST to `/cameras` from the management app and see a `201 Created` plus a `CameraRegisteredV1` outbox row. Integration test passes. Domain coverage ≥ 90 %, Application ≥ 80 %.

---

## Phase 4: User Story 2 — List Registered Cameras (P1)

**Goal:** Authenticated admin can `GET /cameras` with sort + pagination and receive the paginated list.

**Independent Test:** `ListCamerasIntegrationTest.List_cameras_returns_paginated_results_via_HTTP`.

### Tests first

- [ ] **T062 [P] [US2]** `ListCamerasQueryHandlerTests` in `tests/CameraCatalog.Application.Tests/Queries/ListCamerasQueryHandlerTests.cs`: default ordering, custom sort, custom order, offset+limit, invalid sort field surfaces as `Result.Failure(InvalidSortField)`.
- [ ] **T063 [US2]** `ListCamerasIntegrationTests.List_cameras_returns_paginated_results_via_HTTP`. Seeds 75 cameras via the DbContextFactory, calls `GET /cameras?offset=50&limit=50`, expects 25 items + `count=75`.

### Application layer

- [ ] **T064 [P] [US2]** `ListCamerasQuery` record in `src/CameraCatalog/Application/Queries/ListCamerasQuery.cs`. Implements `IQuery<Result<CameraListPageDto, ListCamerasError>>`. Fields: `string Sort`, `string Order`, `int Offset`, `int Limit`.
- [ ] **T065 [P] [US2]** `ListCamerasError` sealed-record hierarchy in `src/CameraCatalog/Application/Queries/ListCamerasErrors.cs`. Cases: `InvalidSortField`, `InvalidSortOrder`, `LimitExceeded`.
- [ ] **T066 [P] [US2]** `CameraSummaryDto` and `CameraListPageDto` in `src/CameraCatalog/Application/DTOs/`. The latter: `{ IReadOnlyList<CameraSummaryDto> Items, int Count, int Offset, int Limit }`.
- [ ] **T067 [US2]** `ListCamerasQueryHandler` in `src/CameraCatalog/Application/Queries/Handlers/ListCamerasQueryHandler.cs`. Reads via `AsNoTracking()`, applies validated sort + offset + limit, maps to DTO.

### Api layer

- [ ] **T068 [US2]** `GET /cameras` endpoint in `src/CameraCatalog/Api/CameraEndpoints.cs`. Validates query parameters (`sort`, `order`, `offset`, `limit`) and dispatches to `ListCamerasQueryHandler`.

### Frontend

- [ ] **T069 [P] [US2]** Extend `camerasApi` with `listCameras` query in `apps/shared/src/api/cameras.api.ts`. `invalidatesTags` on `registerCamera` mutation tags the query for refetch.
- [ ] **T070 [P] [US2]** `DataTable` composite in `apps/shared/src/ui/composites/DataTable.tsx` (built on Radix `<Table>` + Tailwind). Generic over row type; sort + pagination as props.
- [ ] **T071 [US2]** Extend `CamerasPage` with the data table, sort dropdown, order toggle, pagination controls.
- [ ] **T072 [US2]** Frontend test `CamerasPage.test.tsx`: page lists cameras returned by mocked API; pagination button updates query params.

**Checkpoint:** After registering a camera, it appears in the list. Pagination works. All FRs from spec satisfied.

---

## Phase 5: Polish

- [ ] **T073 [P] [POLISH]** Verify per-layer coverage gates pass (`coverlet` + threshold script per ADR-0065): Domain ≥ 90 %, Application ≥ 80 %, `Shared.Kernel` ≥ 90 %, `Shared.Contracts` ≥ 90 %. If under, add tests; never relax thresholds.
- [ ] **T074 [P] [POLISH]** Verify `Architecture.Tests` (10/10) still pass — Camera Catalog Domain has no EF / Marten / Wolverine references; no cross-context references.
- [ ] **T075 [POLISH]** Measure command-path latency: integration test asserts `POST /cameras` returns within 200 ms p95 over 100 sequential calls against the AspireFixture stack. Report measured value in the PR body's "Latency budget impact" section (per ADR-0031).
- [ ] **T076 [POLISH]** Update repo `README.md` quickstart with "Register your first camera" (one short paragraph + the `aspire run` + sign-in steps).
- [ ] **T077 [POLISH]** Verification gate (Phase 5 of the 7-phase workflow per ADR-0037): start the system via `aspire run`, sign in as admin, register a camera through the UI, observe it in the list, verify a `CameraRegisteredV1` message in the RabbitMQ admin dashboard. Capture a screenshot or describe the verification clearly in the PR.

---

## Dependencies and execution order

### Cross-phase

- **Phase 1 (Setup):** no dependencies; all 12 tasks `[P]`. Execute first.
- **Phase 2 (Foundational):** depends on Phase 1 completion. Blocks all user-story work.
- **Phase 3 (US1):** depends on Phase 2. P1 — the MVP slice.
- **Phase 4 (US2):** depends on Phase 3 (reuses the same DbContext, same endpoint group, same RTK Query slice).
- **Phase 5 (Polish):** depends on Phases 3 + 4. Final gate before opening PR.

### Within Phase 3 (US1)

```
Tests first (T018-T024, T026-T027) — all [P]
T025 AspireFixture — blocks T026, T027
Domain VOs (T028-T032) — all [P]
T033 Camera aggregate — depends on T028-T032
Application (T035, T036, T038) — all [P]
T037 RegisterCameraCommandHandler — depends on T033, T034, T035, T036
T039 CameraRegisteredDomainEventHandler — depends on T032, T038
Infrastructure (T040-T046) sequential within the layer
Api (T047-T050) — T047 [P]; T048-T050 sequential
Frontend primitives (T054-T057) — all [P]
T058 RegisterCameraDialog — depends on T051, T052, T054-T057
T059, T060 — depends on T058
T053 store wiring — depends on T052
T061 frontend test — depends on T058
```

### Parallel opportunities

- All Phase 1 [SETUP] tasks together (12 in parallel).
- Domain tests (T018-T022) + fakes (T023) + handler test (T024) in parallel.
- Domain value objects (T028-T032) in parallel.
- Application records (T035, T036, T038) in parallel with each other.
- Frontend primitives (T054-T057) in parallel.
- Phase 4 query records (T064-T066) in parallel.

### Coverage-gate dependency

- T073 cannot pass until tests in T018-T024, T062 are written and passing.

---

## Implementation strategy

**MVP first.** Phase 1 → Phase 2 → Phase 3 (US1) is the MVP. Stop after T061 and validate end-to-end. Then add US2 (T062-T072) and Polish (T073-T077).

**Per ADR-0087 (rebase-only):** every commit is independently buildable + green-test. Suggested commit groupings for a single PR:

1. Phase 1 SETUP — 1–3 commits (`feat(shared-kernel): ...`, `feat(shared-cqrs): ...`, `feat(shared-contracts): ...`)
2. Phase 2 FOUND — 1 commit per task or grouped per concern.
3. Phase 3 Domain — 1 commit each for VOs, 1 for aggregate.
4. Phase 3 Application + Infrastructure + Api — separate commits per layer.
5. Phase 3 Frontend — 1 commit each for slice + dialog + page.
6. Phase 4 — 1 commit per layer addition.
7. Phase 5 Polish — final cleanup commit.

Resulting in ~20–25 commits across the PR. Each one independently bisectable.

---

## Notes

- `[P]` tasks = independent files, no order constraint within the phase.
- Each task's exact file path is in its description; reviewers can verify scope.
- Tests fail before implementation (TDD per Karpathy guideline #4).
- Do NOT skip Phase 1 setup tasks — every later task references at least one of `Option<T>`, `Result<T, Error>`, `IValueObject<T>`, `Ensure`, or `AggregateRoot<TId>`.
- The **AspireFixture** lands here (T025); the pattern is reused by every future integration test.
- This is the FIRST feature: the Phase 1 + Phase 2 investment is one-time. Subsequent features add only their domain + application + api code.
