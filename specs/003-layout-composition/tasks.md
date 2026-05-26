# Tasks: 003 — Layout Composition (walking-skeleton "1 cell")

**Input:** Design documents at `specs/003-layout-composition/`

**Prerequisites:** [spec.md](./spec.md) (Phase 1 closed, PR #201 merged), [plan.md](./plan.md) (Phase 2 closed, PR #202 merged)

**Status:** Draft (Phase 3 — Tasks)

## Format: `[ID] [P?] [Story] Description`

- **[P]** — independent of any task above it in the same phase; safe to parallelise.
- **[Story]** — US1 (admin authors+publishes), US2 (operator picks at kiosk), US3 (force-disconnect on archive), US4 (edit-as-new-revision), FOUND (foundational), POLISH.
- File paths in descriptions reference the layout from [plan.md](./plan.md).

## Path conventions

Per [plan.md](./plan.md):

- Backend: `src/LayoutComposition/{Domain,Application,Infrastructure,Api}/`, `src/Shared.Contracts/LayoutComposition/`, `src/MigrationRunner/`, `src/AppHost/`
- Frontend: `apps/shared/src/{api,realtime}/`, `apps/management-web/src/features/layouts/`, `apps/kiosk-web/` (NEW)
- Tests: `tests/LayoutComposition.Domain.Tests/`, `tests/LayoutComposition.Application.Tests/`, `tests/Integration.Tests/LayoutComposition/`

Setup tasks from specs 001-002 (Option, Result, Ensure, AggregateRoot, AspireFixture, CameraViewer, DataTable, Tooltip, etc.) are NOT repeated here — they already exist and are reused.

---

## Phase 1: Foundational — Aspire layout-composition + kiosk-web + Keycloak kiosk client

Blocks every user-story task. Adds the LayoutComposition-specific infrastructure that doesn't depend on the Layout aggregate's shape, plus the kiosk-web Vite app shell so subsequent UI tasks have a place to land.

- [ ] **T001 [FOUND]** No new backend NuGet packages required beyond what's already in `Directory.Packages.props` (SignalR is `Microsoft.AspNetCore.SignalR`, included in the framework). Verify in the PR body.
- [ ] **T002 [P] [FOUND]** Add the `smart-sentinel-eye-kiosk` Keycloak client to the realm-export JSON at `src/AppHost/Resources/realms/smart-sentinel-eye.json`. **Public client + PKCE**, redirect URI `http://localhost:{kiosk-port}/oidc/callback` for dev + the prod kiosk URL pattern. `sse.management` scope assignable.
- [ ] **T003 [FOUND]** Wire the `layout-composition` API project in `src/AppHost/AppHost.cs`: `builder.AddProject<Projects.SmartSentinelEye_LayoutComposition_Api>("layout-composition")` with `WithHttpEndpoint()`, `WithReference(layoutCompositionDb)`, `WithReference(rabbitmq)`, `WithReference(keycloak)`, `WaitForCompletion(migrations)`.
- [ ] **T004 [FOUND]** Add `layout-composition-db` database to the existing `postgres` resource: `postgres.AddDatabase("layout-composition-db");` + `migrations.WithReference(layoutCompositionDb)` so `MigrationRunner` gets the new connection string.
- [ ] **T005 [FOUND]** Wire the `kiosk-web` JS resource in `AppHost.cs`: `builder.AddNpmApp("kiosk-web", "../../apps/kiosk-web")` with `WithReference` to `camera-catalog`, `stream-distribution`, `layout-composition`, `keycloak`. Skip in E2E mode (same pattern as management-web).
- [ ] **T006 [P] [FOUND]** `LayoutComposition.Infrastructure` NuGet refs in `src/LayoutComposition/Infrastructure/SmartSentinelEye.LayoutComposition.Infrastructure.csproj`: `Microsoft.EntityFrameworkCore`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore.Design`, `WolverineFx.EntityFrameworkCore`. Identical to `StreamDistribution.Infrastructure`.
- [ ] **T007 [P] [FOUND]** `LayoutComposition.Api` NuGet refs (or framework refs) for `Microsoft.AspNetCore.SignalR`. The package is part of `Microsoft.AspNetCore.App` so no explicit `PackageReference` is needed — verify the `FrameworkReference` is present.
- [ ] **T008 [P] [FOUND]** `LayoutComposition.Application` project gets `ProjectReference` to `Shared.CQRS` + `Shared.Contracts` + `LayoutComposition.Domain` + `Shared.Kernel`. Already scaffolded; verify and adjust if missing.
- [ ] **T009 [P] [FOUND]** `LayoutRevisionPublishedV1` integration event in `src/Shared.Contracts/LayoutComposition/LayoutRevisionPublishedV1.cs` (per ADR-0073). Fields: `Guid Layout, int RevisionNumber, string Name, Guid Camera, DateTimeOffset PublishedAt, Guid PublishedBy`. Implements `IIntegrationEvent`.
- [ ] **T010 [P] [FOUND]** `LayoutRevisionArchivedV1` integration event in `src/Shared.Contracts/LayoutComposition/LayoutRevisionArchivedV1.cs`. Fields: `Guid Layout, int RevisionNumber, DateTimeOffset ArchivedAt, Guid ArchivedBy`. Implements `IIntegrationEvent`.

**Checkpoint:** `aspire run` brings up `layout-composition` (failing to start with no implementation yet — OK) alongside the existing stack; `kiosk-web` resource shows in the dashboard with a placeholder index page. Realm import succeeds with the new kiosk client.

---

## Phase 2: User Story 1 — Admin authors and publishes a Layout (P1) 🎯

**Goal:** Authenticated admin creates a Draft layout pointing at a registered camera, then publishes it. `LayoutRevisionPublishedV1` is on the integration bus.

**Independent Test:** `LayoutLifecycleIntegrationTests.Create_and_publish_a_layout_emits_LayoutRevisionPublishedV1_within_500_ms`.

### Tests first (TDD per Karpathy guideline #4)

- [ ] **T011 [P] [US1]** `LayoutIdentifierTests` in `tests/LayoutComposition.Domain.Tests/Layout/LayoutIdentifierTests.cs`: `New()` returns Guid v7; `From(Guid.Empty)` fails.
- [ ] **T012 [P] [US1]** `LayoutRevisionIdentifierTests`: same shape; independent of `LayoutIdentifier`.
- [ ] **T013 [P] [US1]** `LayoutNameTests`: non-empty, ≤ 80 chars, no newlines; trimming behaviour.
- [ ] **T014 [P] [US1]** `LayoutRevisionNumberTests`: ≥ 1 invariant; `One` singleton; `From(int)` factory.
- [ ] **T015 [P] [US1]** `LayoutRevisionStateTests`: four singletons (`Draft|Published|Archived`); `From("Draft")` round-trip; unknown value throws.
- [ ] **T016 [P] [US1]** `LayoutRevisionStateMachineTests` in `tests/LayoutComposition.Domain.Tests/Layout/LayoutRevisionStateMachineTests.cs`: every allowed transition (`Draft→Published`, `Draft→Archived`, `Published→Draft`, `Published→Archived`) and every forbidden transition (`Archived→*` throws, `Published→Published` is no-op-or-throws TBD).
- [ ] **T017 [P] [US1]** `LayoutTests` in `tests/LayoutComposition.Domain.Tests/Layout/LayoutTests.cs`: covers the chain invariants — `CreateDraft_yields_revision_one_in_Draft_state_and_raises_no_events`, `Publish_a_Draft_transitions_to_Published_and_raises_LayoutRevisionPublished`, `BranchDraft_off_Published_yields_revision_N_plus_1_in_Draft_state`, `Publish_a_new_revision_atomically_archives_the_previous_Published`, `At_most_one_Published_revision_per_chain_is_enforced`.
- [ ] **T018 [P] [US1]** `LayoutBuilder` fluent builder in `tests/LayoutComposition.Domain.Tests/Layout/Builders/LayoutBuilder.cs` (ADR-0054).
- [ ] **T019 [P] [US1]** Test-project fakes in `tests/LayoutComposition.Application.Tests/Fakes/`: `InMemoryLayoutRepository`, `FakeLayoutLifecycleBroadcaster` (records broadcast calls), `FakeClock`.
- [ ] **T020 [P] [US1]** `CreateLayoutDraftCommandHandlerTests`: happy path; name-collision returns `LayoutNameTaken`; missing camera returns `LayoutCameraUnknown`.
- [ ] **T021 [P] [US1]** `PublishRevisionCommandHandlerTests`: Draft → Published; auto-archives prior Published in same UoW; revision number not found returns `LayoutRevisionNotFound`; already-Published revision returns `LayoutRevisionInvalidTransition`.
- [ ] **T022 [P] [US1]** `LayoutRevisionPublishedDomainEventHandlerTests`: invocation publishes `LayoutRevisionPublishedV1` via `IEventBus` AND calls `ILayoutLifecycleBroadcaster.PublishedAsync` exactly once.
- [ ] **T023 [US1]** Extend `AspireFixture` (in `tests/Integration.Tests/Fixtures/AspireFixture.cs`): wait for `layout-composition` to reach `Running`; add `LayoutComposition` HttpClient; add `CreateLayoutCompositionDbContextAsync()`; add `ResetLayoutCompositionAsync()`.
- [ ] **T024 [US1]** `LayoutLifecycleIntegrationTests.Create_and_publish_a_layout_emits_LayoutRevisionPublishedV1_within_500_ms` in `tests/Integration.Tests/LayoutComposition/`. End-to-end through Aspire stack.

### Domain layer

- [ ] **T025 [P] [US1]** `LayoutIdentifier` value object in `src/LayoutComposition/Domain/Layout/LayoutIdentifier.cs` (per ADR-0090).
- [ ] **T026 [P] [US1]** `LayoutRevisionIdentifier` value object.
- [ ] **T027 [P] [US1]** `LayoutName` string-backed VO in `src/LayoutComposition/Domain/Layout/LayoutName.cs`.
- [ ] **T028 [P] [US1]** `LayoutRevisionNumber` int-backed VO; `One` static; `From(int)` factory.
- [ ] **T029 [P] [US1]** `LayoutRevisionState` enum-backed VO; three singletons + `From(string)`.
- [ ] **T030 [P] [US1]** `LayoutRevisionPublishedDomainEvent` in `src/LayoutComposition/Domain/Layout/Events/LayoutRevisionPublishedDomainEvent.cs`. Carries layout + revision + name + camera + timestamp + operator.
- [ ] **T031 [P] [US1]** `LayoutRevisionArchivedDomainEvent` in `src/LayoutComposition/Domain/Layout/Events/LayoutRevisionArchivedDomainEvent.cs`. Carries layout + revision + timestamp + operator.
- [ ] **T032 [US1]** `Revision` entity in `src/LayoutComposition/Domain/Layout/Revision.cs` (depends on T026–T029). Private ctor + static `NewDraft`/`Branch` factories; `Publish`, `Archive`, `Revert`, `EditCamera` mutators with state-machine guards.
- [ ] **T033 [US1]** `Layout` aggregate root in `src/LayoutComposition/Domain/Layout/Layout.cs` (depends on T025–T032 + `AggregateRoot<TId>`). Behaviours: `CreateDraft`, `BranchDraft`, `Publish`, `Revert`, `EditDraft`, `ArchiveRevision`. Enforces "at most one Published" invariant via `CurrentPublished()`.
- [ ] **T034 [P] [US1]** `ILayoutRepository` interface in `src/LayoutComposition/Domain/Layout/ILayoutRepository.cs`: `GetByIdentifierAsync`, `GetByNameAsync` (for create-name-uniqueness), `Add`, `SaveAsync`.
- [ ] **T035 [P] [US1]** `ILayoutLifecycleBroadcaster` interface in `src/LayoutComposition/Domain/Layout/ILayoutLifecycleBroadcaster.cs`: `PublishedAsync(LayoutRevisionPublishedNotification)`, `ArchivedAsync(LayoutRevisionArchivedNotification)`. Notification records live alongside the interface.

### Application layer

- [ ] **T036 [P] [US1]** `CreateLayoutDraftCommand` + `CreateLayoutDraftErrors`. Fields: `LayoutName Name, CameraIdentifier Camera, OperatorIdentifier CreatedBy`. Errors: `LayoutNameTaken`, `LayoutCameraUnknown` (FUTURE — not validated against CameraCatalog in v1; the API enforces non-empty Guid only).
- [ ] **T037 [US1]** `CreateLayoutDraftCommandHandler`: name-uniqueness check via repository; `Layout.CreateDraft`; save.
- [ ] **T038 [P] [US1]** `PublishRevisionCommand` + `PublishRevisionErrors`. Errors: `LayoutNotFound`, `LayoutRevisionNotFound`, `LayoutRevisionInvalidTransition`, `LayoutRevisionStale` (concurrency).
- [ ] **T039 [US1]** `PublishRevisionCommandHandler`: load by `LayoutIdentifier`; call `layout.Publish(revisionNumber, by, clock)`; save (the prior Published revision is archived inside the same UoW per FR-003).
- [ ] **T040 [P] [US1]** `LayoutRevisionPublishedDomainEventHandler`: receives the in-process event; publishes `LayoutRevisionPublishedV1` via `IEventBus`; calls `ILayoutLifecycleBroadcaster.PublishedAsync`.
- [ ] **T041 [P] [US1]** `LayoutRevisionArchivedDomainEventHandler`: receives the in-process event; publishes `LayoutRevisionArchivedV1`; calls `ILayoutLifecycleBroadcaster.ArchivedAsync`.

### Infrastructure layer

- [ ] **T042 [P] [US1]** `LayoutCompositionDbContext` in `src/LayoutComposition/Infrastructure/Persistence/`. Includes `Layouts` `DbSet` + Wolverine outbox tables.
- [ ] **T043 [P] [US1]** `LayoutConfiguration` + `RevisionConfiguration` in `src/LayoutComposition/Infrastructure/Persistence/Configurations/`. `OwnsMany(l => l.Revisions, ...)` for the revision collection. Unique indexes per plan §Persistence schema. **Includes the migration-author decision** (function-backed partial index vs denormalized archived flag — pick one; document in PR body).
- [ ] **T044 [P] [US1]** `LayoutRepository : ILayoutRepository`. Implements `GetByIdentifierAsync` with `Include(l => l.Revisions)`; `GetByNameAsync` with non-archived filter.
- [ ] **T045 [P] [US1]** `LayoutCompositionMigrator : IMigrator`. Same pattern as `StreamDistributionMigrator`.
- [ ] **T046 [US1]** EF migration `<timestamp>_InitialLayoutComposition.cs`. Creates `layouts`, `layout_revisions`, partial unique indexes, Wolverine outbox tables. Validates against a fresh Postgres via the integration test.
- [ ] **T047 [P] [US1]** `LayoutCompositionPersistenceModule.AddLayoutCompositionPersistence` (slim — Domain+EF only, used by MigrationRunner).
- [ ] **T048 [US1]** `LayoutCompositionInfrastructureModule.AddLayoutCompositionInfrastructure`: registers `ILayoutRepository`, `IDomainEventHandler<...>` handlers, `IDomainEventDispatcher`, `IEventBus`, `ILayoutLifecycleBroadcaster` → `SignalRLayoutLifecycleBroadcaster`, `IClock`. Calls `AddWolverineForContext<LayoutCompositionDbContext>` with the standard `configureMore` pattern.

### Api layer

- [ ] **T049 [P] [US1]** `LayoutEndpoints.MapLayoutEndpoints` in `src/LayoutComposition/Api/LayoutEndpoints.cs`. Routes per FR-007:
  - `POST /layouts` (create first revision)
  - `GET /layouts/{layoutIdentifier:guid}` (full chain)
  - `GET /layouts?state=...&offset=&limit=` (list with state filter)
  - `POST /layouts/{layoutIdentifier:guid}/revisions/{revisionNumber:int}/publish`
  - `POST /layouts/{layoutIdentifier:guid}/revisions/{revisionNumber:int}/archive`
- [ ] **T050 [P] [US1]** `LayoutCompositionApiModule.AddLayoutCompositionApi` registers the concrete command/query handler classes.
- [ ] **T051 [US1]** `Program.cs` for `LayoutComposition.Api`: `AddLayoutCompositionInfrastructure` + `AddLayoutCompositionApi` + standard `AddAuthentication`/`AddAuthorization` + `MapLayoutEndpoints`.

### Frontend (management-web)

- [ ] **T052 [P] [US1]** `layouts.api.ts` RTK Query slice in `apps/shared/src/api/layouts.api.ts`. Endpoints: `createLayoutDraft`, `branchDraftRevision`, `editDraftRevision`, `publishRevision`, `revertRevision`, `archiveRevision`, `getLayout`, `listLayouts`. Tag types: `Layout` + `LayoutList`.
- [ ] **T053 [P] [US1]** `layouts.schema.ts` zod shapes for the request/response bodies. Mirrors `cameras.schema.ts`.
- [ ] **T054 [US1]** Wire `layoutsApi.reducerPath` reducer + middleware into `apps/management-web/src/app/store.ts`.
- [ ] **T055 [P] [US1]** `LayoutEditorDialog` in `apps/management-web/src/features/layouts/LayoutEditorDialog.tsx`. Form: `name` + camera picker (consumes `useListCamerasQuery`). Used for both new-chain and new-revision flows.
- [ ] **T056 [P] [US1]** `LayoutEditorDialog.test.tsx`: happy path, validation errors, camera-picker empty state.
- [ ] **T057 [US1]** `LayoutsPage.tsx` in `apps/management-web/src/features/layouts/`. DataTable + state-filter chips + per-row action buttons. Mirrors `CamerasPage` structure.
- [ ] **T058 [P] [US1]** `LayoutsPage.test.tsx`: empty state, populated table, action-button wiring (mocked mutations).
- [ ] **T059 [US1]** Register `/layouts` route + navigation link in `apps/management-web/src/App.tsx` and `app/router.tsx`.
- [ ] **T060 [P] [US1]** Update `apps/management-web/src/App.test.tsx` to mock `useListLayoutsQuery` so the smoke test renders the new page.

**Checkpoint:** Admin can create+publish a layout end-to-end via management-web. Integration event on the bus. Kiosk side not yet ready.

---

## Phase 3: User Story 2 — Operator at a kiosk picks a Published layout (P1)

**Goal:** Bring kiosk-web online. Admin signs into kiosk-web, sees a picker of Published layouts, taps one, and the cell view renders the camera's live stream via `<CameraViewer>` (reused unchanged from spec 002).

**Independent Test:** `KioskPickerIntegrationTests.Picker_lists_only_Published_layouts_and_renders_cell_view_on_select`.

- [ ] **T061 [US2]** Scaffold the kiosk-web Vite app at `apps/kiosk-web/`: `package.json` (workspace ref to `@smart-sentinel-eye/shared`; same deps as management-web — react, react-dom, react-redux, react-router-dom, react-oidc-context, @reduxjs/toolkit, react-hook-form, zod, clsx), `tsconfig.json`, `vite.config.ts`, `tailwind.config.js`, `postcss.config.js`, `index.html`, `src/main.tsx`, `src/App.tsx`, `src/styles/`.
- [ ] **T062 [P] [US2]** `apps/kiosk-web/src/app/auth.ts`: OIDC config for `react-oidc-context` pointing at the existing Keycloak realm with `smart-sentinel-eye-kiosk` client; redirect URI `/oidc/callback`.
- [ ] **T063 [P] [US2]** `apps/kiosk-web/src/app/store.ts`: Redux store with `camerasApi` + `streamsApi` + `layoutsApi` (read-only consumption).
- [ ] **T064 [US2]** `apps/kiosk-web/src/app/router.tsx`: routes `/` → `PickerPage`, `/layouts/:layoutIdentifier` → `CellPage`, `/oidc/callback` handled by `react-oidc-context`. Wrap with `<AuthProvider>` + `<RequireAuth>` HOC.
- [ ] **T065 [P] [US2]** `PickerPage.tsx` in `apps/kiosk-web/src/features/picker/`. Calls `useListLayoutsQuery({ state: 'published' })`; renders a card grid; tap navigates to `/layouts/{layoutIdentifier}`.
- [ ] **T066 [P] [US2]** `PickerPage.test.tsx`: empty state, populated list, tap-navigates assertion.
- [ ] **T067 [US2]** `CellPage.tsx` in `apps/kiosk-web/src/features/cell/`. Reads `layoutIdentifier` from the route; `useGetLayoutQuery`; selects the Published revision's `cameraIdentifier`; renders `<CameraViewer cameraIdentifier={...} />`. Force-disconnect path stub (filled in by US3).
- [ ] **T068 [P] [US2]** `CellPage.test.tsx`: renders viewer when Published; redirects to picker when layout is not Published (e.g. only Drafts in the chain).
- [ ] **T069 [P] [US2]** `App.test.tsx` smoke test for kiosk-web (mirrors management-web's).
- [ ] **T070 [US2]** `KioskPickerIntegrationTests.Picker_lists_only_Published_layouts_and_renders_cell_view_on_select` — drives the API + asserts the kiosk-side flow via an HTTP-level test against the layout-composition Api (no headless browser needed).

**Checkpoint:** kiosk-web boots, admin can sign in, picker lists Published layouts, tapping opens the cell view with a live WebRTC frame. SignalR not yet wired — list refreshes on full reload only.

---

## Phase 4: User Story 3 — Force-disconnect on archive via SignalR (P1)

**Goal:** Connected kiosks force-disconnect to the picker within 1 s of an archive command. Includes the SignalR hub + JS client + reconcile-on-reconnect fallback.

**Independent Test:** `SignalRRevocationIntegrationTests.Archive_force_disconnects_connected_kiosks_within_one_second`.

- [ ] **T071 [P] [US3]** `LayoutLifecycleHub` in `src/LayoutComposition/Api/LayoutLifecycleHub.cs`: `Hub<ILayoutLifecycleClient>` with `[Authorize(Policy = AdminPolicy)]`. Empty server-side methods; client-side methods declared via the typed interface.
- [ ] **T072 [P] [US3]** `ILayoutLifecycleClient` typed interface + `LayoutRevisionPublishedNotification` / `LayoutRevisionArchivedNotification` records (in `src/LayoutComposition/Api/` for the client + in `src/LayoutComposition/Domain/Layout/` for the broadcaster contract — mirror shapes via shared types).
- [ ] **T073 [US3]** `SignalRLayoutLifecycleBroadcaster : ILayoutLifecycleBroadcaster` in `src/LayoutComposition/Infrastructure/Broadcasting/`. Wraps `IHubContext<LayoutLifecycleHub, ILayoutLifecycleClient>`; sends `Clients.All.LayoutRevisionPublished(notification)` / `.LayoutRevisionArchived(notification)`. Failures are logged + swallowed (broadcast is best-effort per plan).
- [ ] **T074 [US3]** Wire SignalR in `Program.cs`: `builder.Services.AddSignalR()`; `app.MapHub<LayoutLifecycleHub>("/hubs/layouts")`. Auth flows through the existing JwtBearer pipeline; the bearer is extracted from the WebSocket query-string (`?access_token=...`) per Microsoft's documented pattern — add the `OnMessageReceived` JwtBearerEvents hook.
- [ ] **T075 [P] [US3]** `realtime/layoutHub.ts` in `apps/shared/src/realtime/`. SignalR client wrapper using `@microsoft/signalr`. Exposes `connect(accessTokenFactory): HubConnection`, `subscribePublished(handler)`, `subscribeArchived(handler)`, `onReconnected(handler)`. Add `@microsoft/signalr` to `apps/shared/package.json`.
- [ ] **T076 [P] [US3]** `useLayoutLifecycle` hook in `apps/kiosk-web/src/features/revocation/useLayoutLifecycle.ts`. Connects to the hub using the OIDC access token; exposes the typed callbacks; re-runs `useListLayoutsQuery` invalidation on `onReconnected` (FR-012 reconciliation).
- [ ] **T077 [US3]** Wire `useLayoutLifecycle` into `PickerPage` (live-update the list) and `CellPage` (force-disconnect when the current layout is archived; if hub disconnects, the reconnect-and-reconcile path also triggers force-disconnect).
- [ ] **T078 [P] [US3]** `SignalRRevocationIntegrationTests.Archive_force_disconnects_connected_kiosks_within_one_second` in `tests/Integration.Tests/LayoutComposition/`. Spawns two `HubConnection`s via `Microsoft.AspNetCore.SignalR.Client`; archives a Published revision; asserts both clients receive the notification within 1 s.
- [ ] **T079 [US3]** `ReconnectReconcileIntegrationTests.A_dropped_kiosk_reconciles_on_reconnect_and_force_disconnects_within_five_seconds`. Drops the SignalR connection mid-test; archives a layout; reconnects; asserts the client re-fetches `GET /layouts?state=published` and detects the missing layout (FR-012).
- [ ] **T080 [P] [US3]** Wolverine handler discovery: ensure the `LayoutComposition.Application` assembly is included via `configureMore` (per the pattern from PRs #194/#196 and tech-debt #200).

**Checkpoint:** Archiving a Published layout from management-web causes any connected kiosk's cell view to flip back to the picker within 1 s. Disconnecting + reconnecting the SignalR channel triggers the same force-disconnect on reconnect.

---

## Phase 5: User Story 4 — Revision chain edit + auto-archive (P1)

**Goal:** Editing a Published layout creates a new Draft revision (N+1). Publishing N+1 atomically archives N.

**Independent Test:** `EditRevisionIntegrationTests.Publish_a_new_revision_atomically_archives_the_previous_published_revision`.

- [ ] **T081 [P] [US4]** `BranchDraftRevisionCommand` + `BranchDraftRevisionErrors`. Errors: `LayoutNotFound`, `LayoutHasNoPublishedRevision`, `LayoutRevisionStale`.
- [ ] **T082 [US4]** `BranchDraftRevisionCommandHandler`: load aggregate; call `layout.BranchDraft(by, clock)`; save.
- [ ] **T083 [P] [US4]** `EditDraftRevisionCommand` + `EditDraftRevisionErrors` (only updates `cameraIdentifier` in v1; renames are deferred per spec edge cases).
- [ ] **T084 [US4]** `EditDraftRevisionCommandHandler`: load; call `layout.EditDraft(...)`; save.
- [ ] **T085 [P] [US4]** `RevertRevisionCommand` + handler: Published → Draft. Errors: `LayoutRevisionNotFound`, `LayoutRevisionInvalidTransition`.
- [ ] **T086 [P] [US4]** `ArchiveRevisionCommand` + handler: Draft|Published → Archived. Idempotent on already-Archived.
- [ ] **T087 [US4]** Map the four new endpoints in `LayoutEndpoints.cs`:
  - `POST /layouts/{layoutIdentifier}/draft` (branch new Draft)
  - `PATCH /layouts/{layoutIdentifier}/revisions/{revisionNumber}` (edit draft)
  - `POST /layouts/{layoutIdentifier}/revisions/{revisionNumber}/revert`
  - `POST /layouts/{layoutIdentifier}/revisions/{revisionNumber}/archive`
- [ ] **T088 [P] [US4]** Extend `LayoutsPage.tsx` with **Edit** / **Revert** / **Archive** actions per revision. The Edit action opens `LayoutEditorDialog` in "branch from current Published" mode (pre-fills with current values; submits to `useBranchDraftRevisionMutation`).
- [ ] **T089 [US4]** `EditRevisionIntegrationTests.Publish_a_new_revision_atomically_archives_the_previous_published_revision`. Asserts both transitions land in a single DB transaction (Stream of integration events shows Archived(N) and Published(N+1) within the same `LayoutVersion` bump).

**Checkpoint:** Full revision chain works end-to-end. The audit story holds — every published revision leaves a row trail in `layout_revisions`.

---

## Phase 6: Polish

- [ ] **T090 [P] [POLISH]** Verify per-layer coverage gates pass for the new context via `pwsh scripts/coverage-check.ps1`. `LayoutComposition.Domain ≥ 90 %`, `LayoutComposition.Application ≥ 80 %`, `Shared.Contracts ≥ 90 %` (will require unit tests for the two new V1 records). Add tests for any gaps; never relax thresholds.
- [ ] **T091 [P] [POLISH]** `LayoutRevisionPublishedV1Tests` + `LayoutRevisionArchivedV1Tests` in `tests/Shared.Contracts.Tests/`. Parallel to `StreamHealthChangedV1Tests` from PR #196 — positional ctor, `IIntegrationEvent`, equality, JSON round-trip.
- [ ] **T092 [P] [POLISH]** Update `tests/Architecture.Tests/BoundaryTests.cs` if needed: add explicit assertion that `LayoutComposition.Domain` does not reference `Microsoft.AspNetCore.SignalR.*` (the existing no-Wolverine / no-EF / no-cross-context rules already cover it via wildcards).
- [ ] **T093 [POLISH]** Update `coverage-check.ps1` gated-list with `SmartSentinelEye.LayoutComposition.Domain = 90` and `SmartSentinelEye.LayoutComposition.Application = 80`.
- [ ] **T094 [POLISH]** Update `README.md` quickstart: append a **"Publish a layout and view it on a kiosk"** section after the existing "Watch a camera" step. Walks the reader through opening kiosk-web, picking a layout, and observing the force-disconnect when archived.
- [ ] **T095 [POLISH]** Verification gate (Phase 5 of the 7-phase workflow per ADR-0037): start the system via `aspire run`, sign in as admin, create+publish a layout via management-web, sign in at kiosk-web on a second browser, observe the picker, tap the layout, observe the live frame, archive the layout from management-web, observe the kiosk's force-disconnect to picker within 1 s. Capture a screenshot or describe clearly in the PR.

---

## Dependencies and execution order

### Cross-phase

- **Phase 1 (Foundational):** blocks every user-story task. Must complete first.
- **Phase 2 (US1):** depends on Phase 1.
- **Phase 3 (US2):** depends on Phase 1 + Phase 2's Api endpoints (the kiosk consumes `GET /layouts?state=published` + `GET /layouts/{id}`).
- **Phase 4 (US3):** depends on Phase 2 (broadcaster wiring) + Phase 3 (the kiosk needs the hub client). The SignalR-hub task (T071–T074) is technically Phase-2 infrastructure but is parked in Phase 4 so the broadcaster-from-domain-event-handler tasks (T040, T041) can stub the broadcaster as a no-op until the hub is wired — keeps US1 shippable on its own.
- **Phase 5 (US4):** depends on Phase 2's aggregate work; independent of Phase 4 (revisions don't need SignalR).
- **Phase 6 (Polish):** depends on Phases 2-5.

### Within Phase 2 (US1)

Tests-first (T011–T024) before implementation (T025–T060).

Domain (T025–T035) — leaves T032 (Revision) and T033 (Layout) sequential since Layout depends on Revision; the rest are [P].

Application (T036–T041) — handlers (T037, T039) sequential because they share Layout; commands/errors/events all [P].

Infrastructure (T042–T048) — T046 (migration) depends on T042–T043 (DbContext + Configurations).

Api (T049–T051) sequential — T051 depends on T049 + T050.

Frontend (T052–T060) — T054 depends on T052; T057 depends on T055; the rest are [P].

### Within Phase 4 (US3)

T071–T074 (hub + broadcaster impl) are largely [P] but T074 (Program.cs wiring) depends on T071+T073.

JS-side T075–T077 depend on T074 being deployed (need a working `/hubs/layouts` endpoint).

T078–T079 integration tests depend on the full chain being wired.

### Parallel opportunities

- All [P] tests in Phase 2 (T011–T022) in parallel.
- All [P] VO/event tasks (T025–T031) in parallel.
- Application commands/errors (T036, T038, T081, T083, T085, T086) in parallel.
- Frontend dialog (T055, T056) and page (T057, T058) are largely independent.
- Polish tasks T090–T094 mostly in parallel.

### Coverage-gate dependency

- T090 cannot pass until Phase 2 tests (T011–T022) and Phase 5 handler tests (covered by the integration tests) are all green.

---

## Implementation strategy

**MVP first.** Phase 1 → Phase 2 (US1) is the MVP for the admin side. Stop after T060 and validate end-to-end: an admin can create and publish a layout, the integration event is on the bus, the row appears in management-web. Then add Phase 3 (US2 — the kiosk-web app shell + picker + cell view), then Phase 4 (US3 — SignalR + force-disconnect), then Phase 5 (US4 — revisions), then Phase 6 (polish).

**Suggested PR sequence (mirrors spec 002):**

1. **PR A — Phase 1 foundational:** AppHost wiring, Keycloak kiosk client, project scaffolds, integration-event records (T001–T010).
2. **PR B — Phase 2 Domain + tests:** value objects, Revision, Layout aggregate, ILayoutRepository, ILayoutLifecycleBroadcaster, all domain tests (T011–T035).
3. **PR C — Phase 2 Application + Infrastructure + EF migration:** command/query handlers, DbContext, repository, broadcaster stub (returns Task.CompletedTask — real impl in PR E), migration, ApplicationModule registrations (T036–T048).
4. **PR D — Phase 2 Api + management-web Layouts page:** endpoints, RTK Query slice, LayoutsPage + LayoutEditorDialog + tests (T049–T060).
5. **PR E — Phase 3 kiosk-web app shell + picker + cell view + Phase 4 SignalR:** kiosk-web Vite app, OIDC, picker, cell view, SignalR hub + broadcaster (replaces the PR-C stub), SignalR JS client, force-disconnect wiring (T061–T080).
6. **PR F — Phase 5 revisions:** branch/edit/revert/archive commands + handlers + endpoints + management-web edit-dialog evolution + the chain integration test (T081–T089).
7. **PR G — Phase 6 polish:** coverage gates, arch test confirmation, README quickstart, manual verification (T090–T095).

## Notes

- All file paths are absolute under the repo root.
- All NuGet versions reference the existing `Directory.Packages.props`; no new SDK upgrades.
- The migration-author choice (function-backed partial index vs denormalized `archived` flag) is folded into T043; the PR-C author picks one and documents the choice in the PR body.
- T091 backfills the Shared.Contracts coverage drop that the new V1 records will cause — same lesson as the StreamHealthChangedV1 oversight from PRs D-G of spec 002.
