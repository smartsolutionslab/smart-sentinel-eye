# Tasks: 005 — SystemVariables

**Input:** Design documents at `specs/005-system-variables/`

**Prerequisites:** [spec.md](./spec.md) (Phase 1 closed, PR #424 merged),
[plan.md](./plan.md) (Phase 2 closed, PR #425 merged)

**Status:** Draft (Phase 3 — Tasks)

## Format: `[ID] [P?] [Story] Description`

- **[P]** — independent of any task above it in the same phase; safe to parallelise.
- **[Story]** — US1 (define), US2 (set value + push), US3 (resolve at fetch), US4 (archive), FOUND (foundational), POLISH.
- File paths in descriptions reference the layout from [plan.md](./plan.md).

## Path conventions

- Backend: `src/SystemVariables/{Domain,Application,Infrastructure,Api}/`, `src/Shared.Contracts/SystemVariables/`, `src/MigrationRunner/`, `src/AppHost/`
- LayoutComposition bridge: `src/LayoutComposition/{Domain,Infrastructure}/`
- Frontend: `apps/shared/src/{api,realtime}/`, `apps/management-web/src/features/systemVariables/`, `apps/kiosk-web/src/features/cell/`
- Tests: `tests/SystemVariables.{Domain,Application}.Tests/`, `tests/Integration.Tests/SystemVariables/`

Setup primitives from specs 001–004 (Option, Result, Ensure, AggregateRoot, AspireFixture, /hubs/layouts, CameraViewer, OverlayEditor, ILayoutLifecycleBroadcaster pattern, etc.) are NOT repeated — they exist and are reused.

---

## Phase 1: Foundational — Aspire + integration events + allow-rule

Blocks every user-story task. Adds context-specific infrastructure without touching the aggregate's shape.

- [ ] **T001 [P] [FOUND]** Add `system-variables-db` to the existing `postgres` resource in `src/AppHost/AppHost.cs`: `var systemVariablesDb = postgres.AddDatabase("system-variables-db");` + `migrations.WithReference(systemVariablesDb).WaitFor(systemVariablesDb)`.
- [ ] **T002 [FOUND]** Wire the `system-variables` API project in `AppHost.cs`: `builder.AddProject<Projects.SmartSentinelEye_SystemVariables_Api>("system-variables")` with `WithHttpEndpoint()`, `WithReference(systemVariablesDb)`, `WithReference(rabbitmq)`, `WithReference(keycloak)`, `WithReference(overlayDesigner)` (for the seed call), `WaitForCompletion(migrations)`. Both management-web and kiosk-web get `WithReference(systemVariables)`.
- [ ] **T003 [P] [FOUND]** `SystemVariables.Infrastructure` csproj refs mirror `OverlayDesigner.Infrastructure`: EFCore + Npgsql + WolverineFx stack + FrameworkReference `Microsoft.AspNetCore.App` (for IHubContext via the bridge) + ProjectReference to `LayoutComposition.Domain` (documented cross-context allow-rule).
- [ ] **T004 [P] [FOUND]** `SystemVariables.Application` csproj refs: Domain + Shared.Kernel + Shared.CQRS + Shared.Contracts + `LayoutComposition.Domain` (broadcaster bridge). PackageReference `Microsoft.Extensions.Logging.Abstractions` + `Microsoft.EntityFrameworkCore` (IQueryable seam).
- [ ] **T005 [P] [FOUND]** `SystemVariableDefinedV1` in `src/Shared.Contracts/SystemVariables/SystemVariableDefinedV1.cs`: `(Guid Variable, string Name, string Type, DateTimeOffset DefinedAt, Guid DefinedBy) : IIntegrationEvent`.
- [ ] **T006 [P] [FOUND]** `SystemVariableValueChangedV1`: `(Guid Variable, string Name, string Type, string Value, DateTimeOffset ChangedAt, Guid ChangedBy) : IIntegrationEvent`. `Value` is the wire-string per FR-007 (culture-invariant decimal for Number, raw text for String, "true"/"false" for Boolean).
- [ ] **T007 [P] [FOUND]** `SystemVariableArchivedV1`: `(Guid Variable, string Name, DateTimeOffset ArchivedAt, Guid ArchivedBy) : IIntegrationEvent`.
- [ ] **T008 [FOUND]** Extend `tests/Architecture.Tests/BoundaryTests.AllowedCrossContext` table with two entries: `("SmartSentinelEye.SystemVariables", "Application")` and `("SmartSentinelEye.SystemVariables", "Infrastructure")` both allowed `["SmartSentinelEye.LayoutComposition"]`.

**Checkpoint:** `aspire run` brings up `system-variables` (failing to start — OK; the goal is connection-string availability + project resource appearing in the dashboard). Architecture tests pass.

---

## Phase 2: User Story 1 — Admin defines a system variable (P1)

**Goal:** Authenticated admin creates a `Defined` variable (typed; optionally with initial value).

**Independent Test:** `VariableLifecycleIntegrationTests.Define_a_variable_lands_on_GET_listing_within_500_ms`.

### Tests first (TDD per Karpathy guideline #4)

- [ ] **T009 [P] [US1]** `VariableIdentifierTests`.
- [ ] **T010 [P] [US1]** `VariableNameTests` — grammar `^[A-Za-z][A-Za-z0-9_]{0,63}$`, case-sensitive equality, rejection of empty / too-long / starting-with-digit / containing-space.
- [ ] **T011 [P] [US1]** `VariableTypeTests` — three singletons + `From(string)` round-trip + invalid-string rejection.
- [ ] **T012 [P] [US1]** `VariableStateTests` — two singletons + `From(string)` round-trip.
- [ ] **T013 [P] [US1]** `VariableValueTests` — `Unset`, `StringValue`, `NumberValue`, `BooleanValue` constructors + equality + `Render` per FR-006/FR-007 (culture-invariant Number, BooleanLabels-driven Boolean).
- [ ] **T014 [P] [US1]** `BooleanLabelsTests` — non-empty truthy/falsy strings, default `("Yes", "No")`.
- [ ] **T015 [P] [US1]** `VariableStateMachineTests` — `Define` → `Defined`; `Archive` from `Defined` → `Archived`; `SetValue` from `Defined` → `Defined`; `Archive` from `Archived` is idempotent.
- [ ] **T016 [P] [US1]** `VariableTests` aggregate-level invariants (BooleanLabels only on Boolean type; type immutable; name uniqueness handled by the application layer).
- [ ] **T017 [P] [US1]** `VariableBuilder` fluent test helper (ADR-0054).
- [ ] **T018 [P] [US1]** Application test fakes: `InMemoryVariableRepository`, `InMemoryVariableQuerySource` (TestAsyncEnumerable pattern), `FakeReverseIndex`, `FakeClock`.
- [ ] **T019 [P] [US1]** `DefineVariableCommandHandlerTests` — happy path, name-collision returns `VariableNameTaken`, BooleanLabels-required-on-Boolean.

### Domain layer

- [ ] **T020 [P] [US1]** `VariableIdentifier` Guid v7 strongly-typed id.
- [ ] **T021 [P] [US1]** `VariableName` StringValueObject.
- [ ] **T022 [P] [US1]** `VariableType` enum-backed VO.
- [ ] **T023 [P] [US1]** `VariableState` enum-backed VO.
- [ ] **T024 [P] [US1]** `BooleanLabels` VO (`(TruthyLabel, FalsyLabel)`).
- [ ] **T025 [P] [US1]** `VariableValue` discriminated VO (`Unset` / `StringValue` / `NumberValue` / `BooleanValue`) + `From(type, rawString)` factory + `Render(BooleanLabels)` method.
- [ ] **T026 [P] [US1]** `VariableDefinedDomainEvent`.
- [ ] **T027 [US1]** `Variable` aggregate root (`Define` static factory + private setters; `SetValue(VariableValue)` and `Archive(by, clock)` internal mutators).
- [ ] **T028 [P] [US1]** `IVariableRepository` interface (GetByIdentifier / GetByName ignoring Archived / Add / SaveAsync).

### Application layer

- [ ] **T029 [P] [US1]** `DefineVariableCommand` + `DefineVariableErrors` (`VariableNameTaken`, `BooleanLabelsRequired`).
- [ ] **T030 [US1]** `DefineVariableCommandHandler`.
- [ ] **T031 [P] [US1]** `VariableDto` + `VariableValueDto` (wire shape; `value: string | null`, `type: "String"|"Number"|"Boolean"`).

**Checkpoint:** Defining a variable via the application handler produces a `Defined` row in `InMemoryVariableRepository`. No infra wiring yet.

---

## Phase 3: User Story 2 — Admin sets a value + push fan-out (P1)

**Goal:** Setting a value publishes V1 + invokes the resolver + pushes resolved text to kiosks within ≤ 200 ms p95.

**Independent Test:** `VariablePushIntegrationTests.Set_value_pushes_resolved_overlay_text_within_200_ms`.

### Tests first

- [ ] **T032 [P] [US2]** `SetVariableValueCommandHandlerTests` — happy path, unknown variable returns `VariableNotFound`, archived returns `VariableArchived`, type mismatch returns `VariableTypeMismatch`.
- [ ] **T033 [P] [US2]** `PlaceholderParserTests` — extracts `{{name}}` tokens; ignores `{{ name }}` with whitespace; ignores `{{1foo}}` (starts with digit); ignores nested `{{{{foo}}}}`.
- [ ] **T034 [P] [US2]** `ResolverTests` — substitutes set values; leaves unset / unknown / archived placeholders literal; culture-invariant Number formatting; BooleanLabels.
- [ ] **T035 [P] [US2]** `InMemoryReverseIndexTests` — Add/Remove/Lookup are concurrent-safe; re-publishing an overlay with a different label cleans up stale entries.
- [ ] **T036 [P] [US2]** `VariableValueChangedDomainEventHandlerTests` — publishes V1, calls resolver for each affected overlay, calls `broadcaster.ResolvedOverlayTextChangedAsync` once per overlay, increments version monotonically.
- [ ] **T037 [P] [US2]** `OverlayRevisionPublishedV1HandlerTests` — updates the reverse-index for the affected overlay; replaces previous entries when the label changes.

### Domain layer

- [ ] **T038 [P] [US2]** `VariableValueChangedDomainEvent`.
- [ ] **T039 [P] [US2]** `VariableArchivedDomainEvent`.

### Application layer

- [ ] **T040 [P] [US2]** `SetVariableValueCommand` + `SetVariableValueErrors`.
- [ ] **T041 [US2]** `SetVariableValueCommandHandler`.
- [ ] **T042 [P] [US2]** `IReverseIndex` interface (`UpsertOverlayReferences(overlayId, label)`, `RemoveOverlay(overlayId)`, `LookupOverlays(name) → IReadOnlyCollection<OverlayIdentifier>`, `LookupLabelText(overlayId) → string`).
- [ ] **T043 [US2]** `PlaceholderParser` static class (`IEnumerable<string> ExtractNames(string labelText)`).
- [ ] **T044 [US2]** `IResolver` interface + `Resolver` implementation (pure function).
- [ ] **T045 [P] [US2]** `OverlayRevisionPublishedV1Handler` (Wolverine subscriber — updates reverse-index).
- [ ] **T046 [P] [US2]** `OverlayRevisionArchivedV1Handler` (Wolverine subscriber — removes from reverse-index).
- [ ] **T047 [P] [US2]** `VariableValueChangedDomainEventHandler` — publishes V1, fan-out via resolver + broadcaster.
- [ ] **T048 [P] [US2]** `VariableArchivedDomainEventHandler` — publishes V1, fan-out (re-resolve every affected overlay → text now contains the literal placeholder again).

### Broadcaster bridge (LayoutComposition extension)

- [ ] **T049 [US2]** Extend `ILayoutLifecycleBroadcaster` (LayoutComposition.Domain) with `ResolvedOverlayTextChangedAsync(ResolvedOverlayTextChangedNotification, CancellationToken)` + the notification record (primitives only: `{ Guid OverlayIdentifier, string ResolvedText, long Version }`).
- [ ] **T050 [US2]** Update `SignalRLayoutLifecycleBroadcaster` (Infrastructure) to implement the new method; map to `ResolvedOverlayTextChangedHubMessage`.
- [ ] **T051 [P] [US2]** Add `ResolvedOverlayTextChangedHubMessage` (primitives only) in `LayoutComposition.Infrastructure.Broadcasting`.
- [ ] **T052 [P] [US2]** Extend `ILayoutLifecycleClient` with `ResolvedOverlayTextChanged(ResolvedOverlayTextChangedHubMessage)`.

### Infrastructure layer

- [ ] **T053 [P] [US2]** `SystemVariablesDbContext` + `ApplyConfigurationsFromAssembly`.
- [ ] **T054 [P] [US2]** `VariableConfiguration` — flat columns (no owned types this time; `value` stored as nullable string + `value_kind` discriminator).
- [ ] **T055 [P] [US2]** `VariableRepository` (IVariableRepository impl) — same SaveAsync-then-dispatch pattern as Layout/Overlay.
- [ ] **T056 [P] [US2]** `VariableQuerySource` (`AsNoTracking`).
- [ ] **T057 [P] [US2]** `SystemVariablesMigrator` + `DesignTimeDbContextFactory`.
- [ ] **T058 [US2]** EF migration `<ts>_InitialSystemVariables.cs` — single `system_variables` table.
- [ ] **T059 [P] [US2]** `SystemVariablesPersistenceModule.AddSystemVariablesPersistence` (slim, used by MigrationRunner).
- [ ] **T060 [P] [US2]** `InMemoryReverseIndex` — singleton, `ConcurrentDictionary<string, HashSet<Guid>>` for the inverse map + parallel `ConcurrentDictionary<Guid, string>` for the cached label text.
- [ ] **T061 [US2]** `ReverseIndexSeederHostedService` — on `IHostedService.StartAsync`, authenticates with Keycloak (service account), HTTP-GETs `/overlays?state=Published` from `overlay-designer`, parses each label, populates the reverse-index, marks itself ready.
- [ ] **T062 [US2]** `SystemVariablesInfrastructureModule.AddSystemVariablesInfrastructure` — registers `IVariableRepository`, `IVariableQuerySource`, `IReverseIndex` (singleton), `IResolver`, domain-event handlers, `IDomainEventDispatcher`, `IClock`, `IEventBus`, the hosted service. Wires `AddWolverineForContext`.
- [ ] **T063 [US2]** `MigrationRunner.Program.cs` adds `builder.AddSystemVariablesPersistence();`.

**Checkpoint:** `aspire run` boots `system-variables`; reverse-index seeds from `overlay-designer`. Setting a variable's value pushes `ResolvedOverlayTextChanged` on the hub (verifiable via a SignalR client).

---

## Phase 4: User Story 3 — Overlay label resolves at fetch time (P1)

**Goal:** Kiosk gets the resolved text from `GET /system-variables/snapshot?overlayIdentifier=X` on cold load; subsequent updates arrive via SignalR.

**Independent Test:** `VariableResolutionIntegrationTests.GET_snapshot_returns_resolved_label_with_current_values`.

- [ ] **T064 [P] [US3]** `GetOverlaySnapshotQuery` + `GetOverlaySnapshotError` (`OverlayNotInReverseIndex`).
- [ ] **T065 [US3]** `GetOverlaySnapshotQueryHandler` — reads cached label from the reverse-index, resolves with the current variable snapshot, returns `ResolvedOverlaySnapshotDto`. Increments `version` per resolution? **No** — version is monotonic per-push, not per-read. Reads return the current effective version.
- [ ] **T066 [P] [US3]** `GetOverlaySnapshotQueryHandlerTests`.
- [ ] **T067 [P] [US3]** `ResolvedOverlaySnapshotDto` (`{ Guid OverlayIdentifier, string ResolvedText, long Version }`).
- [ ] **T068 [US3]** `VariableResolutionIntegrationTests` — publish overlay referencing two variables, set values, GET snapshot, assert resolved text.

---

## Phase 5: API + management-web (P1)

**Goal:** Admin can define / set / archive variables through the management UI.

- [ ] **T069 [P] [US1]** `DefineVariableRequest` + `SetVariableValueRequest` body records.
- [ ] **T070 [US1]** `SystemVariableEndpoints.MapSystemVariableEndpoints` — POST /system-variables, GET /system-variables, GET /system-variables/{name}, PUT /system-variables/{name}/value, POST /system-variables/{name}/archive, GET /system-variables/snapshot. Admin policy on writes; authenticated-user on reads.
- [ ] **T071 [P] [US1]** `SystemVariablesApiModule.AddSystemVariablesApi`.
- [ ] **T072 [US1]** `Program.cs`: `AddServiceDefaults` + `AddBearerAuthentication` + `AddSystemVariablesInfrastructure` + `AddSystemVariablesApi` + `MapSystemVariableEndpoints`.
- [ ] **T073 [P] [US1]** `systemVariables.api.ts` RTK Query slice in `apps/shared/src/api/` — `defineVariable` / `getVariable` / `listVariables` / `setVariableValue` / `archiveVariable` / `getOverlaySnapshot`. Tag types `Variable` + `VariableList` + `OverlaySnapshot`.
- [ ] **T074 [P] [US1]** `systemVariables.schema.ts` Zod input validation matching the VariableName grammar + per-type value validation.
- [ ] **T075 [P] [US1]** Wire `systemVariablesApi` reducer + middleware into `apps/management-web/src/app/store.ts`.
- [ ] **T076 [US1]** `SystemVariablesPage.tsx` — DataTable, state-filter chips, per-row inline value edit (different input control per type), Archive action.
- [ ] **T077 [P] [US1]** `SystemVariablesPage.test.tsx`.
- [ ] **T078 [US1]** `SystemVariableDialog.tsx` — Define a new variable (Name + Type + optional initial value + BooleanLabels when Type=Boolean).
- [ ] **T079 [P] [US1]** `SystemVariableDialog.test.tsx`.
- [ ] **T080 [US1]** `App.tsx` (management-web): add **System variables** nav entry.

**Checkpoint:** Admin can create + set + archive variables end-to-end. No kiosk-side rendering of resolved values yet.

---

## Phase 6: Kiosk snapshot consume + push (P1)

**Goal:** Kiosks render resolved overlay text on cold load + react to live updates within 200 ms.

- [ ] **T081 [P] [US3]** Wire `systemVariablesApi` reducer + middleware into `apps/kiosk-web/src/app/store.ts`.
- [ ] **T082 [P] [US2]** Update `apps/shared/src/realtime/layoutHub.ts` — add typed `onResolvedOverlayTextChanged(message)` to the callbacks contract; subscribe via `connection.on("ResolvedOverlayTextChanged", ...)`.
- [ ] **T083 [US2]** `useLayoutLifecycle` (kiosk-web) grows `onResolvedOverlayTextChanged` callback. On reconnect, invalidates the `OverlaySnapshot` cache so RTK Query re-fetches.
- [ ] **T084 [US3]** `CellPage.tsx` (kiosk-web): when a Layout has a bound overlay, call `useGetOverlaySnapshotQuery(overlayIdentifier)`. Pass `resolvedText` (instead of the raw label text) into `<CameraViewer overlay={...}>`.
- [ ] **T085 [US2]** `CellPage.tsx` subscribes to `onResolvedOverlayTextChanged` filtered to the bound overlay id; on match, dispatches `systemVariablesApi.util.upsertQueryData` to update the snapshot in place (version-checked: ignore frames with version ≤ current).
- [ ] **T086 [P] [US3]** `CellPage.test.tsx` — resolved text renders; live update via push updates the label without a full re-render.
- [ ] **T087 [P] [US2]** Extend `CameraViewer` — no changes needed; it already accepts an `overlay.text` prop. Document this assumption.
- [ ] **T088 [US2]** `VariablePushIntegrationTests` — define + reference + set value; assert two SignalR clients receive `ResolvedOverlayTextChanged` within 200 ms p95 carrying the new resolved text.

**Checkpoint:** Operator sees live variable values on the wall, updates in ≤ 200 ms.

---

## Phase 7: User Story 4 — Archive a variable (P2)

**Goal:** Archived variable cannot be updated; placeholders revert to literal rendering.

- [ ] **T089 [P] [US4]** `ArchiveVariableCommand` + `ArchiveVariableErrors` (`VariableNotFound`, already-archived idempotent).
- [ ] **T090 [US4]** `ArchiveVariableCommandHandler`.
- [ ] **T091 [P] [US4]** `ArchiveVariableCommandHandlerTests`.
- [ ] **T092 [US4]** `VariableLifecycleIntegrationTests.Archive_releases_name_for_reuse_and_reverts_overlay_to_literal_placeholder`.

---

## Phase 8: Polish

- [ ] **T093 [P] [POLISH]** Coverage gates: extend `scripts/coverage-check.ps1` with `SystemVariables.Domain >= 90%` and `SystemVariables.Application >= 80%`. Backfill any missing handler/query tests.
- [ ] **T094 [P] [POLISH]** `SystemVariableDefinedV1Tests` + `SystemVariableValueChangedV1Tests` + `SystemVariableArchivedV1Tests` in `tests/Shared.Contracts.Tests/`.
- [ ] **T095 [P] [POLISH]** Architecture tests: positive assertion that `SystemVariables.Application` event handlers depend on `ILayoutLifecycleBroadcaster` (mirrors the spec-004 T097 assertion). `SystemVariables.Domain` has no infrastructure-framework deps.
- [ ] **T096 [POLISH]** NFR-001 dedicated latency integration test (separate from T088): warm 20 iterations of value-change → push observed; assert p95 ≤ 200 ms.
- [ ] **T097 [POLISH]** Update `README.md` quickstart: append a **"Bind a variable to an overlay"** section after the existing "Compose an overlay" step.
- [ ] **T098 [POLISH]** Phase-5 verification gate (per ADR-0037): start `aspire run`, sign in as admin, define an `oeeLine1` Number variable, edit an overlay's label to include `OEE: {{oeeLine1}}%`, publish; observe the kiosk show the literal placeholder; set the variable to `82.4`; observe the kiosk update to `OEE: 82.4%` within 1 s. Capture a screenshot or describe clearly in the PR.

---

## Dependencies and execution order

### Cross-phase

- **Phase 1 (Foundational):** blocks every user-story task.
- **Phase 2 (US1):** depends on Phase 1.
- **Phase 3 (US2):** depends on Phase 2 + the cross-context allow-rule from T008.
- **Phase 4 (US3):** depends on Phase 3 (reverse-index + resolver in place).
- **Phase 5 (Api + management-web):** depends on Phase 2 + Phase 4 (snapshot endpoint).
- **Phase 6 (Kiosk):** depends on Phase 5 (RTK Query slice) + Phase 3 (push events) + Phase 4 (snapshot).
- **Phase 7 (US4):** depends on Phase 2's aggregate + Phase 3's V1 events.
- **Phase 8 (Polish):** depends on Phases 2–7.

### PR sequence (~7 PRs, mirrors spec 004)

1. **PR A — Phase 1 foundational** (T001–T008): Aspire wiring, project plumbing, V1 records, Architecture allow-rule.
2. **PR B — Domain + tests** (T009–T028): Variable aggregate + every VO + state-machine tests.
3. **PR C — Application + Infrastructure + broadcaster bridge** (T029–T063): commands + handlers + tests, Wolverine subscribers, resolver, reverse-index, DbContext + EF migration, seeder hosted service, broadcaster bridge extension, MigrationRunner wiring.
4. **PR D — Snapshot query + Api + management-web** (T064–T080): GetOverlaySnapshot, endpoints, ApiModule, RTK Query slice, management-web page + dialog.
5. **PR E — Kiosk snapshot + push** (T081–T088): kiosk store wiring, layoutHub extension, useLayoutLifecycle, CellPage rewrite, push integration test.
6. **PR F — Archive flow** (T089–T092): Archive command + handler + integration test.
7. **PR G — Polish** (T093–T098): coverage gates, V1 contract tests, architecture positive assertion, NFR-001 latency test, README, Phase-5 verification gate.

### Coverage-gate dependency

- T093 cannot pass until Phase 2 + Phase 3 handler tests land.

---

## Implementation strategy

**MVP first.** Phase 1 → Phase 2 → Phase 3 → Phase 4 → Phase 5 → Phase 6 is the MVP — admin creates an unset variable, references it in an overlay, sets a value, kiosk renders live. Phase 7 (archive) closes the cleanup story; Phase 8 (polish) closes the quality story.
