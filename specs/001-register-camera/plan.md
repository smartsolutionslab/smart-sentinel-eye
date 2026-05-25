# Implementation Plan: 001 — Register a Camera

**Branch:** `001-register-camera` | **Date:** 2026-05-25 | **Spec:** [spec.md](./spec.md)

**Status:** Draft (Phase 3 — Plan)

**Input:** Feature specification from
`specs/001-register-camera/spec.md` (Phase 2 closed; zero
`[NEEDS CLARIFICATION]` markers).

## Summary

Implements the smallest end-to-end vertical slice in the Camera
Catalog bounded context:

- Backend: a new `Camera` aggregate with EF Core persistence,
  `RegisterCameraCommand` / `ListCamerasQuery` handlers, two minimal-
  API endpoints, the Wolverine outbox publishing
  `CameraRegisteredV1`, and an EF Core migration.
- Frontend: a `cameras` RTK Query slice in `management-web`, a
  React Hook Form + Zod registration form, and a list view.
- Tests: per-layer test projects for Camera Catalog created here
  (deferred per ADR-0063 until a real feature lands). First
  AspireFixture-based integration test wires the Aspire AppHost to a
  Postgres+Keycloak+RabbitMQ Testcontainers stack and exercises the
  full vertical.

## Technical Context

| Concern | Decision | Source |
|---|---|---|
| Backend language | C# / .NET 10 | ADR-0001, ADR-0024 |
| Frontend language | TypeScript / React 19 | ADR-0001, ADR-0074 |
| Persistence | **EF Core** on Postgres (Wolverine outbox shares `DbContext`) | spec FR-005, ADR-0009, ADR-0088 |
| Messaging | RabbitMQ via Wolverine (per-module queue isolation, eager transactions) | ADR-0010, ADR-0042, ADR-0088 |
| Identity | Keycloak; admin scope required | ADR-0007, ADR-0023 |
| API style | Minimal APIs only | ADR-0070 |
| Errors | `Result<T, ApiError>` (sealed-record hierarchy with HTTP status) | ADR-0047, ADR-0089 |
| Frontend state | Redux Toolkit + RTK Query | ADR-0075 |
| Frontend forms | React Hook Form + Zod | ADR-0079 |
| Auth (browser) | `react-oidc-context` | ADR-0080 |
| Tests | xUnit + Shouldly + Moq + Testcontainers (via AspireFixture) | ADR-0052, ADR-0068 |
| Test naming | Sentence-style with underscores | ADR-0053 |
| Performance goals | Registration ≤ 200 ms p95 (FR-012). Integration event delivery ≤ 1 s (SC-004). | spec |
| Latency budget | This feature is on the COMMAND path; not on the event-to-overlay SLO (ADR-0015). | n/a |
| Scale | 250 cameras per fab production target; ~20 in pilot | ADR-002 |

## Constitution Check

Verifying alignment with each load-bearing principle before
implementation begins. Re-checked after data model is drafted.

| Principle | Check | Status |
|---|---|---|
| §I On-prem first, cloud-ready | Camera aggregate uses idempotent `CameraIdentifier` (Guid v7, client-generatable per ADR-0090). All state survives without cloud dependency. | ✅ |
| §II DDD + value objects | `CameraIdentifier`, `CameraName`, `RtspUrl` are maximalist value objects per ADR-0038, hand-written per ADR-0046, with `IValueObject<T>` markers per ADR-0066. | ✅ |
| §III Bounded-context isolation | All work confined to `SmartSentinelEye.CameraCatalog.*`. Only `Shared.Contracts.CameraRegisteredV1` crosses the boundary (per ADR-0040). NetArchTest enforces. | ✅ |
| §IV Latency budget sacred | Not on the event-to-overlay path. Command-path sub-budget cited in spec FR-012 (≤200 ms p95). PR will report measured value. | ✅ |
| §V Spec-driven | This plan exists, the spec exists, tasks come next. | ✅ |
| §VI Aspire is composition root | EF Core, Postgres, RabbitMQ, Keycloak all registered in `AppHost.cs` via Aspire resources; no hand-rolled wiring. | ✅ |
| §VII Observability mandatory | `RegisterCameraCommandHandler` emits structured logs via `ILogger<T>` (ADR-0050). OpenTelemetry traces capture the command path. | ✅ |
| §VIII Safe at trust boundaries | `[Authorize(Policy = "admin")]` on the endpoint group. Validation rejects malformed input at the API edge AND at the value-object constructor. | ✅ |
| §IX Forward-compatible interfaces | `ICommandHandler<,>` / `IQueryHandler<,>` interfaces stay framework-agnostic; Wolverine dispatcher behind them (ADR-0057). | ✅ |

**Result:** No constitutional violations. No Complexity Tracking
entries needed.

## Project Structure

### Documentation (this feature)

```
specs/001-register-camera/
├── spec.md          ← Phase 1–2 (this PR commits)
├── plan.md          ← this file (Phase 3)
├── tasks.md         ← Phase 4 (next; created by /speckit-tasks)
└── data-model.md    ← optional; could inline below
```

### Source Code — files added / modified

```
src/CameraCatalog/Domain/
└── Camera/                                     ← new (ADR-0092 per-aggregate folder)
    ├── Camera.cs                               ← aggregate root, AggregateRoot<CameraIdentifier>
    ├── CameraIdentifier.cs                     ← Guid v7-backed IValueObject<Guid>
    ├── CameraName.cs                           ← IValueObject<string>, case-insensitive uniqueness
    ├── RtspUrl.cs                              ← IValueObject<string>, rejects userinfo
    ├── CameraStatus.cs                         ← enum-backed VO: Registered (Decommissioned reserved)
    ├── ICameraRepository.cs                    ← Domain repository contract
    └── Events/
        └── CameraRegisteredDomainEvent.cs      ← in-process domain event

src/CameraCatalog/Application/
├── Commands/
│   ├── RegisterCameraCommand.cs                ← record : ICommand<Result<CameraIdentifier, RegisterCameraError>>
│   ├── RegisterCameraErrors.cs                 ← sealed-record hierarchy : ApiError
│   └── Handlers/
│       └── RegisterCameraCommandHandler.cs
├── Queries/
│   ├── ListCamerasQuery.cs
│   ├── ListCamerasErrors.cs
│   └── Handlers/
│       └── ListCamerasQueryHandler.cs
├── EventHandlers/
│   └── CameraRegisteredDomainEventHandler.cs   ← translates to CameraRegisteredV1 + outbox
└── DTOs/
    ├── CameraSummaryDto.cs                     ← list-row shape
    └── CameraListPageDto.cs                    ← { items, count, offset, limit }

src/CameraCatalog/Infrastructure/
├── CameraCatalogInfrastructureModule.cs        ← AddCameraCatalogInfrastructure() (ADR-0051)
├── Persistence/
│   ├── CameraCatalogDbContext.cs               ← EF Core; Wolverine outbox tables included
│   ├── Configurations/
│   │   └── CameraConfiguration.cs              ← IEntityTypeConfiguration<Camera>; LOWER(name) unique index
│   ├── CameraRepository.cs                     ← ICameraRepository impl
│   └── CameraCatalogMigrator.cs                ← IMigrator implementation
└── Migrations/
    └── <timestamp>_InitialCameraCatalog.cs

src/CameraCatalog/Api/
├── CameraCatalogApiModule.cs                   ← AddCameraCatalogApi() + endpoint group
├── CameraEndpoints.cs                          ← POST /cameras, GET /cameras (ADR-0070)
└── Requests/
    └── RegisterCameraRequest.cs                ← record with Deconstruct(CameraName, RtspUrl) per ADR-0069

src/Shared.Contracts/
└── CameraCatalog/
    └── CameraRegisteredV1.cs                   ← versioned integration event (ADR-0073)

src/MigrationRunner/
└── Program.cs                                  ← wire CameraCatalogMigrator into the run list

src/AppHost/
└── AppHost.cs                                  ← add Postgres + RabbitMQ + Keycloak resources;
                                                  WithReference to camera-catalog Api + MigrationRunner;
                                                  WithReference from management-web to camera-catalog

apps/shared/src/
├── api/
│   └── cameras.api.ts                          ← RTK Query api with listCameras + registerCamera
└── ui/
    └── tokens/colors.css                       ← (existing; no change)

apps/management-web/src/
├── features/cameras/
│   ├── CamerasPage.tsx                         ← list + "Register" button
│   ├── RegisterCameraDialog.tsx                ← RHF + Zod form in a Radix Dialog
│   └── camerasSlice.ts                         ← UI state (selected camera, dialog open)
├── app/store.ts                                ← add camerasApi reducer + middleware
└── app/router.tsx                              ← new route /cameras (admin scope guard)

tests/CameraCatalog.Domain.Tests/               ← new test project (ADR-0063 per-feature)
├── Camera/
│   ├── CameraTests.cs
│   ├── CameraNameTests.cs
│   ├── RtspUrlTests.cs
│   └── Builders/
│       └── CameraBuilder.cs

tests/CameraCatalog.Application.Tests/          ← new test project
├── Commands/
│   └── RegisterCameraCommandHandlerTests.cs
└── Queries/
    └── ListCamerasQueryHandlerTests.cs

tests/Integration.Tests/
├── Fixtures/
│   ├── AspireFixture.cs                        ← AspireFixture pattern lands here (ADR-0068)
│   └── AspireCollection.cs
└── CameraCatalog/
    └── RegisterCameraIntegrationTests.cs       ← end-to-end through Aspire stack

tests/Architecture.Tests/
└── BoundaryTests.cs                            ← (existing; no change needed — same boundary rules
                                                  cover the new Camera Catalog types automatically)
```

**Structure Decision:** Backend follows the per-aggregate Domain
folder layout (ADR-0092) and the per-message-kind Application layout
(ADR-0093) that we already documented in CONTRIBUTING.md. Frontend
follows a per-feature folder under `apps/management-web/src/features/`
to keep page+slice+form together for the small admin surface.

## Backend Design

### Domain layer

```csharp
public sealed class Camera : AggregateRoot<CameraIdentifier>
{
    public CameraName Name { get; private set; } = default!;
    public RtspUrl Url { get; private set; } = default!;
    public CameraStatus Status { get; private set; } = default!;
    public DateTimeOffset RegisteredAt { get; private set; }
    public OperatorIdentifier RegisteredBy { get; private set; } = default!;

    private Camera() { }   // EF Core / Marten ctor

    public static Camera Register(
        CameraName name,
        RtspUrl url,
        OperatorIdentifier registeredBy,
        IClock clock)
    {
        var camera = new Camera
        {
            Id = CameraIdentifier.New(),
            Name = name,
            Url = url,
            Status = CameraStatus.Registered,
            RegisteredAt = clock.UtcNow,
            RegisteredBy = registeredBy,
        };
        camera.Raise(new CameraRegisteredDomainEvent(camera.Id, name, url, camera.RegisteredAt, registeredBy));
        return camera;
    }
}
```

### Value objects

- `CameraIdentifier`: `readonly record struct CameraIdentifier(Guid Value) : IValueObject<Guid>` with `New()` and `From(Guid)` factories.
- `CameraName`: `sealed record CameraName : StringValueObject`. Validates trim + 1–200 chars; exposes `NormalizedValue` (lowercased) for uniqueness comparison.
- `RtspUrl`: `sealed record RtspUrl : StringValueObject`. Validates `rtsp://` scheme, 1–2048 chars, no `Uri.UserInfo`.
- `CameraStatus`: enum-backed value object with two members; only `Registered` reachable in this slice.

### Application layer

`RegisterCameraCommand` is a record implementing
`ICommand<Result<CameraIdentifier, RegisterCameraError>>`. The
handler:

1. Validates the operator scope (Keycloak claims) — fail with `ApiError(..., HttpStatusCode.Forbidden)`.
2. Loads the in-memory `Camera` collection via `ICameraRepository`; checks `ExistsByNameAsync(name)`.
3. Calls `Camera.Register(...)` → aggregate validates invariants and raises domain event.
4. `repository.Add(camera)` then `unitOfWork.SaveChangesAsync(ct)` — same EF transaction commits aggregate state, raised domain event (consumed in-process by `CameraRegisteredDomainEventHandler`), and Wolverine outbox row carrying `CameraRegisteredV1`.

`ListCamerasQueryHandler` reads from `CameraCatalogDbContext.Cameras` directly using `AsNoTracking()`, applies sort + pagination (validated by the API layer first), and maps to `CameraSummaryDto`.

### Infrastructure layer

`CameraCatalogDbContext` defines `DbSet<Camera>` plus the Wolverine outbox tables. `CameraConfiguration : IEntityTypeConfiguration<Camera>` registers:

- Owned-type mappings for the three string-backed VOs (stored as plain `varchar` columns).
- Unique index `ux_cameras_name_lower` on `LOWER(name)` to enforce case-insensitive uniqueness at the DB layer.
- `Version` column with EF Core concurrency token attribute (per ADR-0043).

`CameraRepository : ICameraRepository` wraps the DbContext. `CameraCatalogMigrator : IMigrator` exposes a single `RunAsync(ct)` method invoked by `MigrationRunner` before the Api starts.

### Api layer

`CameraEndpoints` exposes two routes inside `/cameras` with `RequireAuthorization("admin")`:

```csharp
group.MapPost("/", Register).Produces<CameraIdentifier>(StatusCodes.Status201Created);
group.MapGet("/", List).Produces<CameraListPageDto>(StatusCodes.Status200OK);
```

`Register` deconstructs the `RegisterCameraRequest` into typed VOs (via `Deconstruct` per ADR-0069), builds the command, invokes the handler. `List` parses + validates `sort`, `order`, `offset`, `limit` query parameters via Zod-equivalent C# guards before dispatching to the query handler.

### Wolverine wiring

In `CameraCatalog.Infrastructure.CameraCatalogInfrastructureModule`:

```csharp
services.AddWolverineForCameraCatalog(
    outboxConnectionName: "postgres",
    outboxSchema: "wolverine_camera_catalog",
    moduleQueuePrefix: "camera-catalog",
    eventHandlerAssemblies: [typeof(CameraCatalogApplicationModule).Assembly]);
```

`AddWolverineForCameraCatalog` is a small wrapper in
`ServiceDefaults` (added in this PR) that applies ADR-0088 defaults:
per-module queue isolation + eager transaction mode + Postgres outbox.

## Frontend Design

### RTK Query slice (`apps/shared/src/api/cameras.api.ts`)

```typescript
export const camerasApi = createApi({
  reducerPath: 'camerasApi',
  baseQuery: fetchBaseQuery({ baseUrl: '/api/cameras', prepareHeaders: attachKeycloakToken }),
  tagTypes: ['Camera'],
  endpoints: (b) => ({
    listCameras: b.query<CameraListPage, ListCamerasArgs>({ ... }),
    registerCamera: b.mutation<CameraIdentifier, RegisterCameraInput>({
      query: (body) => ({ url: '', method: 'POST', body }),
      invalidatesTags: ['Camera'],
    }),
  }),
});
```

### Zod schema (shared between form and API client)

```typescript
export const registerCameraSchema = z.object({
  name: z.string().trim().min(1).max(200),
  rtspUrl: z
    .string()
    .min(1)
    .max(2048)
    .regex(/^rtsp:\/\//, 'Must start with rtsp://')
    .refine((u) => !/^rtsp:\/\/[^@\s]+@/.test(u), 'Credentials in URL are not allowed'),
});
export type RegisterCameraInput = z.infer<typeof registerCameraSchema>;
```

### Form (`apps/management-web/src/features/cameras/RegisterCameraDialog.tsx`)

React Hook Form with `zodResolver(registerCameraSchema)`. On submit, invokes the RTK Query mutation; on `201 Created`, closes the dialog and the list query auto-refetches via `invalidatesTags`. RFC 7807 errors from the backend are mapped per-field by reading the `errors[].field` array on the Problem Details body.

### Page (`apps/management-web/src/features/cameras/CamerasPage.tsx`)

Renders a `<DataTable>` from `apps/shared/ui/composites/` (first composite shipped in this PR; built on a custom hook around Radix `<Table>`). Toolbar has a sort dropdown (name / registeredAt), an order toggle (asc / desc), pagination controls, and a "Register" button that opens the dialog.

## Tests

### Domain unit tests

- `CameraTests.Register_with_valid_input_assigns_a_new_identifier_and_raises_the_registered_event`
- `CameraNameTests.From_with_empty_string_fails`
- `CameraNameTests.From_with_201_characters_fails`
- `CameraNameTests.Two_names_differing_only_in_case_are_equal`
- `RtspUrlTests.From_with_http_scheme_fails`
- `RtspUrlTests.From_with_userinfo_segment_fails`
- `RtspUrlTests.From_with_2049_characters_fails`

### Application handler tests

- `RegisterCameraCommandHandlerTests`:
  - `Register_a_camera_with_valid_input_returns_the_new_identifier`
  - `Register_a_camera_with_a_duplicate_name_returns_NameAlreadyTaken`
  - `Register_a_camera_persists_the_camera_and_stages_the_outbox_message`
- `ListCamerasQueryHandlerTests`:
  - `List_cameras_returns_the_registered_cameras_ordered_by_registeredAt_desc_by_default`
  - `List_cameras_with_invalid_sort_field_returns_an_invalid_sort_error`
  - `List_cameras_respects_offset_and_limit`

Hand-written `InMemoryCameraRepository` + `InMemoryClock` + `InMemoryMessageBus` fakes (ADR-0052).

### Integration test (Aspire fixture lands in this PR per ADR-0068)

- `RegisterCameraIntegrationTests`:
  - `Register_a_camera_end_to_end_persists_the_row_and_publishes_CameraRegisteredV1`
  - `Register_a_camera_with_a_duplicate_name_returns_409_via_HTTP`
  - `List_cameras_returns_paginated_results_via_HTTP`

`AspireFixture` boots `Projects.SmartSentinelEye_AppHost` in E2ETests mode, exposes `HttpClient camera-catalog`, and provides a `DbContextFactory<CameraCatalogDbContext>` for seeding.

### Architecture tests

No changes. The existing boundary rules in
`tests/Architecture.Tests/BoundaryTests.cs` automatically cover the
new Camera Catalog types (the test uses assembly-level rules).

### Frontend tests

- `apps/management-web/src/features/cameras/RegisterCameraDialog.test.tsx`:
  - `Register form submits valid input and closes on success`
  - `Register form shows field-level error when backend returns 409 CAMERA_NAME_TAKEN`
- `apps/management-web/src/features/cameras/CamerasPage.test.tsx`:
  - `Cameras page renders the list and the Register button`

### Coverage gates (ADR-0065)

- `CameraCatalog.Domain` ≥ 90 % — small, focused, easily achievable.
- `CameraCatalog.Application` ≥ 80 % — both handlers + decorators.
- `Shared.Contracts.CameraCatalog` ≥ 90 % — trivial record, satisfied by integration test reference.

## Migrations

`dotnet ef migrations add InitialCameraCatalog --project src/CameraCatalog/Infrastructure --startup-project src/MigrationRunner --output-dir Persistence/Migrations`

Migration creates the `cameras` table, `wolverine_camera_catalog.*` outbox tables (auto-generated by Wolverine on startup; gated by `AutoBuildMessageStorageOnStartup` per ADR-0088), and the unique index `ux_cameras_name_lower`.

Rollback: `dotnet ef migrations remove` while the migration is still local; once merged, write a new "down" migration.

## Latency budget allocation (command path)

Spec FR-012 sets ≤ 200 ms p95 for `POST /cameras`. Sub-budget for the
walking skeleton:

| Leg | Budget | Notes |
|---|---|---|
| ASP.NET Core minimal-API pipeline (auth + binding + validation) | ≤ 20 ms | mostly Keycloak JWT verify on cold cache |
| Command-handler logic (uniqueness check + aggregate factory) | ≤ 10 ms | in-memory + one indexed Postgres lookup |
| Postgres write (aggregate + outbox row, single transaction) | ≤ 30 ms | localhost in dev; ≤ 50 ms on shared k3s Postgres |
| Wolverine outbox post-commit dispatch (background) | not on the response path | event delivery is async; SC-004 budgets it separately |
| Response serialisation + network | ≤ 10 ms | LAN |
| Headroom | ≤ 130 ms | absorbs jitter, cold starts, telemetry overhead |

CI emits the measured p95 via a benchmark fixture; if any leg drifts
above its budget, the PR is blocked.

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| **EF Core unique index on `LOWER(name)` is provider-specific** | Postgres-specific; we already commit to Postgres (ADR-0009). Validate via integration test. |
| **Wolverine outbox table conflicts with existing Camera Catalog schema** | Outbox schema is `wolverine_camera_catalog` — namespaced separately. `MigrationRunner` runs Wolverine `AutoBuildMessageStorageOnStartup` before EF migrations apply, so order matters; integration test catches startup ordering bugs. |
| **First AspireFixture in repo — pattern unknown** | Adopt Yumney's pattern verbatim (ADR-0068). Spike during implementation; if non-trivial, split into a prep PR. |
| **Keycloak setup adds ~30 s to integration-test cold start** | Cache the Keycloak Testcontainer between test runs (Testcontainers reuse mode). Document the trade-off in the AspireFixture README. |
| **Frontend RTK Query base URL discovery** | Aspire injects `services__camera-catalog__http__0` env var into management-web. RTK Query reads it via Vite's `import.meta.env`. Documented in `apps/shared/src/api/cameras.api.ts`. |
| **Coverage gate on Application barely fails at 80 %** | Application is intentionally thin; offset by integration test coverage. If still close, add property-based tests for handler input validation. |

## Out of scope (deferred to follow-up specs)

- Decommissioning a camera (US-3 in spec; goes in a 0xx-decommission-camera spec).
- Camera credentials (`user:password@` URLs); requires a `CameraSecret` aggregate.
- Camera Health (reachability probe + status reporting).
- Editing a registered camera (update flow).
- ONVIF discovery (the camera lists itself rather than admin entering URL).
- React Hook Form composite primitives in `apps/shared/ui/composites/` beyond the minimum two needed by this form (deferred to a Frontend Foundations spec if/when the composites grow).
