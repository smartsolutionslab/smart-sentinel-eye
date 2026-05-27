# Tasks: 004 — OverlayDesigner (walking-skeleton "1 overlay")

**Input:** Design documents at `specs/004-overlay-designer/`

**Prerequisites:** [spec.md](./spec.md) (Phase 1 closed, PR #308 merged), [plan.md](./plan.md) (Phase 2 closed, PR #309 merged)

**Status:** Draft (Phase 3 — Tasks)

## Format: `[ID] [P?] [Story] Description`

- **[P]** — independent of any task above it in the same phase; safe to parallelise.
- **[Story]** — US1 (author+publish), US2 (binding+kiosk render), US3 (republish push), US4 (revisions), FOUND (foundational), LAYOUTEXT (cross-context extension to LayoutComposition), POLISH.
- File paths in descriptions reference the layout from [plan.md](./plan.md).

## Path conventions

Per [plan.md](./plan.md):

- Backend: `src/OverlayDesigner/{Domain,Application,Infrastructure,Api}/`, `src/Shared.Contracts/OverlayDesigner/`, `src/MigrationRunner/`, `src/AppHost/`
- LayoutComposition extension: `src/LayoutComposition/{Domain,Application,Infrastructure}/Layout/`
- Frontend: `apps/shared/src/{api,ui/composites}/`, `apps/management-web/src/features/overlays/`, `apps/kiosk-web/src/features/cell/`
- Tests: `tests/OverlayDesigner.{Domain,Application}.Tests/`, `tests/Integration.Tests/OverlayDesigner/`

Setup tasks from specs 001-003 (Option, Result, Ensure, AggregateRoot, AspireFixture, SignalR hub, CameraViewer, etc.) are NOT repeated — they exist and are reused.

---

## Phase 1: Foundational — Aspire overlay-designer + react-rnd + integration events

Blocks every user-story task. Adds OverlayDesigner-specific infrastructure without touching the aggregate's shape.

- [ ] **T001 [P] [FOUND]** Add ``react-rnd@10.5.2`` to ``apps/shared/package.json`` as a dependency. Run ``pnpm install``; commit the lockfile.
- [ ] **T002 [FOUND]** Add ``overlay-designer-db`` database to the existing ``postgres`` resource in ``src/AppHost/AppHost.cs``: ``var overlayDesignerDb = postgres.AddDatabase("overlay-designer-db");`` + ``migrations.WithReference(overlayDesignerDb).WaitFor(overlayDesignerDb)``.
- [ ] **T003 [FOUND]** Wire the ``overlay-designer`` API project in ``AppHost.cs``: ``builder.AddProject<Projects.SmartSentinelEye_OverlayDesigner_Api>("overlay-designer")`` with ``WithHttpEndpoint()``, ``WithReference(overlayDesignerDb)``, ``WithReference(rabbitmq)``, ``WithReference(keycloak)``, ``WaitForCompletion(migrations)``. Both management-web and kiosk-web get ``WithReference(overlayDesigner)``.
- [ ] **T004 [P] [FOUND]** ``OverlayDesigner.Infrastructure`` NuGet refs mirror ``LayoutComposition.Infrastructure``: EFCore + Npgsql + WolverineFx stack + FrameworkReference Microsoft.AspNetCore.App (for IHubContext later when we share the LayoutLifecycle hub).
- [ ] **T005 [P] [FOUND]** ``OverlayDesigner.Application`` project refs: Domain + Shared.Kernel + Shared.CQRS + Shared.Contracts. PackageReference Microsoft.Extensions.Logging.Abstractions + Microsoft.EntityFrameworkCore (the IQueryable.ToListAsync seam).
- [ ] **T006 [P] [FOUND]** ``OverlayRevisionPublishedV1`` integration event in ``src/Shared.Contracts/OverlayDesigner/OverlayRevisionPublishedV1.cs``: ``(Guid Overlay, int RevisionNumber, string Name, string Text, decimal NormalizedX, decimal NormalizedY, decimal NormalizedWidth, decimal NormalizedHeight, int FontSizePx, DateTimeOffset PublishedAt, Guid PublishedBy) : IIntegrationEvent``.
- [ ] **T007 [P] [FOUND]** ``OverlayRevisionArchivedV1``: ``(Guid Overlay, int RevisionNumber, DateTimeOffset ArchivedAt, Guid ArchivedBy) : IIntegrationEvent``.

**Checkpoint:** ``aspire run`` brings up ``overlay-designer`` (failing to start — OK; the goal is connection-string availability + project resource appearing in the dashboard).

---

## Phase 2: User Story 1 — Admin authors and publishes an Overlay (P1) 🎯

**Goal:** Authenticated admin creates a Draft overlay (text + drag-positioned label), publishes it, sees ``OverlayRevisionPublishedV1`` on the bus.

**Independent Test:** ``OverlayLifecycleIntegrationTests.Create_and_publish_an_overlay_emits_OverlayRevisionPublishedV1_within_500_ms``.

### Tests first (TDD per Karpathy guideline #4)

- [ ] **T008 [P] [US1]** ``OverlayIdentifierTests`` — ``New()`` returns Guid v7, ``From(Guid.Empty)`` throws.
- [ ] **T009 [P] [US1]** ``OverlayRevisionIdentifierTests``.
- [ ] **T010 [P] [US1]** ``OverlayNameTests`` — non-empty, ≤ 80 chars, no newlines (clone of LayoutName rules).
- [ ] **T011 [P] [US1]** ``OverlayRevisionNumberTests`` — ≥ 1 invariant, ``One`` singleton, ``Next()``.
- [ ] **T012 [P] [US1]** ``OverlayRevisionStateTests`` — three canonical singletons + ``From(string)`` round-trip.
- [ ] **T013 [P] [US1]** ``LabelTests`` — text validation (non-empty trim, ≤ 256), normalized bounds (0..1, width/height > 0), font-size bounds (8..256), all-fields-required.
- [ ] **T014 [P] [US1]** ``OverlayRevisionStateMachineTests`` — every allowed + forbidden transition (clone of LayoutRevisionStateMachineTests shape).
- [ ] **T015 [P] [US1]** ``OverlayTests`` — chain invariants (at-most-one-Published, atomic swap on publish, BranchDraft copies prior Published's label, etc.).
- [ ] **T016 [P] [US1]** ``OverlayBuilder`` fluent test helper (ADR-0054).
- [ ] **T017 [P] [US1]** Application test fakes: ``InMemoryOverlayRepository``, ``InMemoryOverlayQuerySource`` (clone the TestAsyncEnumerable from LayoutComposition.Application.Tests), ``FakeOverlayLifecycleBroadcaster``, ``FakeClock``.
- [ ] **T018 [P] [US1]** ``CreateOverlayDraftCommandHandlerTests`` — happy path, name-collision returns ``OverlayNameTaken``.
- [ ] **T019 [P] [US1]** ``PublishRevisionCommandHandlerTests`` — happy path + 4 error cases (NotFound / RevisionNotFound / InvalidStateTransition / atomic-swap-archives-prior).
- [ ] **T020 [P] [US1]** ``OverlayRevisionPublishedDomainEventHandlerTests`` — fires V1 + calls broadcaster's overlay-method.
- [ ] **T021 [US1]** Extend ``AspireFixture`` (in ``tests/Integration.Tests/Fixtures/AspireFixture.cs``): wait for ``overlay-designer``; expose ``OverlayDesigner`` HttpClient; ``CreateOverlayDesignerDbContextAsync``; ``ResetOverlayDesignerAsync``.
- [ ] **T022 [US1]** ``OverlayLifecycleIntegrationTests.Create_and_publish_an_overlay_emits_OverlayRevisionPublishedV1_within_500_ms`` in ``tests/Integration.Tests/OverlayDesigner/``.

### Domain layer

- [ ] **T023 [P] [US1]** ``OverlayIdentifier`` value object (Guid v7 IStronglyTypedId).
- [ ] **T024 [P] [US1]** ``OverlayRevisionIdentifier``.
- [ ] **T025 [P] [US1]** ``OverlayName`` string VO.
- [ ] **T026 [P] [US1]** ``OverlayRevisionNumber`` int VO; ``One`` static; ``Next()``.
- [ ] **T027 [P] [US1]** ``OverlayRevisionState`` enum-backed VO (Draft / Published / Archived).
- [ ] **T028 [P] [US1]** ``Label`` value object — text + normalized x/y/width/height + fontSizePx; all validated via ``Ensure.That(...)``. Decimal bounds via ``.Satisfies(...)``.
- [ ] **T029 [P] [US1]** ``OverlayRevisionPublishedDomainEvent`` (in-process); carries identifier + revision number + Label + timestamps + operator.
- [ ] **T030 [P] [US1]** ``OverlayRevisionArchivedDomainEvent``.
- [ ] **T031 [US1]** ``Revision`` entity in ``src/OverlayDesigner/Domain/Overlay/Revision.cs`` — internal mutators for Publish/Revert/EditLabel/Archive; mirrors Layout's Revision shape.
- [ ] **T032 [US1]** ``Overlay`` aggregate root — CreateDraft / BranchDraft / EditDraft / Publish / Revert / ArchiveRevision (mirrors Layout aggregate signatures with Label replacing Camera).
- [ ] **T033 [P] [US1]** ``IOverlayRepository`` interface: GetByIdentifier / GetByName (filters out fully-archived chains) / Add / SaveAsync.

### Application layer

- [ ] **T034 [P] [US1]** ``CreateOverlayDraftCommand`` + ``CreateOverlayDraftErrors`` (``OverlayNameTaken``).
- [ ] **T035 [US1]** ``CreateOverlayDraftCommandHandler``.
- [ ] **T036 [P] [US1]** ``PublishRevisionCommand`` + ``PublishRevisionErrors`` (LayoutNotFound / RevisionNotFound / InvalidStateTransition — names mirror Layout).
- [ ] **T037 [US1]** ``PublishRevisionCommandHandler``.
- [ ] **T038 [P] [US1]** ``OverlayRevisionPublishedDomainEventHandler`` — publishes V1 + calls ``ILayoutLifecycleBroadcaster.OverlayPublishedAsync`` (cross-context broadcaster reference; see T053).
- [ ] **T039 [P] [US1]** ``OverlayRevisionArchivedDomainEventHandler`` — publishes V1 + calls ``ILayoutLifecycleBroadcaster.OverlayArchivedAsync``.
- [ ] **T040 [P] [US1]** ``OverlayDto`` + ``OverlayRevisionDto`` (DTOs carry full Label).

### Infrastructure layer

- [ ] **T041 [P] [US1]** ``OverlayDesignerDbContext`` + ``ApplyConfigurationsFromAssembly``.
- [ ] **T042 [P] [US1]** ``OverlayConfiguration`` — aggregate + ``OwnsMany(o => o.Revisions)``; the Label is mapped via individual columns on each revision (label_text / label_x / label_y / label_width / label_height / label_font_size_px). Indexes: ``ux_overlay_revisions_number (overlay_id, revision_number)`` + partial ``ux_overlay_revisions_one_published (overlay_id) WHERE state='Published'``.
- [ ] **T043 [P] [US1]** ``OverlayRepository`` (IOverlayRepository impl) — same SaveAsync-then-dispatch pattern as LayoutRepository.
- [ ] **T044 [P] [US1]** ``OverlayQuerySource`` (``IOverlayQuerySource`` impl, ``AsNoTracking``).
- [ ] **T045 [P] [US1]** ``OverlayDesignerMigrator`` + ``DesignTimeDbContextFactory``.
- [ ] **T046 [US1]** EF migration ``<timestamp>_InitialOverlayDesigner.cs`` — creates the two tables + the partial unique index.
- [ ] **T047 [P] [US1]** ``OverlayDesignerPersistenceModule.AddOverlayDesignerPersistence`` (slim, used by MigrationRunner).
- [ ] **T048 [US1]** ``OverlayDesignerInfrastructureModule.AddOverlayDesignerInfrastructure`` — registers ``IOverlayRepository``, domain-event handlers, ``IDomainEventDispatcher``, ``IClock``, ``IEventBus``, ``IOverlayQuerySource``. Wires ``AddWolverineForContext`` with ``configureMore``. **Does NOT register ``ILayoutLifecycleBroadcaster`` — that's consumed from LayoutComposition's container registration via the shared abstraction.**
- [ ] **T049 [US1]** ``MigrationRunner.Program.cs`` adds ``builder.AddOverlayDesignerPersistence();``.

### Application + Domain → Broadcaster bridge (the cross-context exception)

- [ ] **T050 [US1]** Extend ``ILayoutLifecycleBroadcaster`` (in ``src/LayoutComposition/Domain/Layout/ILayoutLifecycleBroadcaster.cs``) with two new methods: ``OverlayPublishedAsync(OverlayLifecyclePublishedNotification, CancellationToken)`` and ``OverlayArchivedAsync(OverlayLifecycleArchivedNotification, CancellationToken)``. Notification records live alongside the interface.

  > Cross-context note: ``OverlayLifecyclePublishedNotification`` carries primitive types only (Guid + int + string + decimals). No reference to ``OverlayDesigner.Domain.Label`` — the broadcaster contract stays free of OverlayDesigner types so the architecture rule allowing this reference doesn't widen.

- [ ] **T051 [US1]** Update ``SignalRLayoutLifecycleBroadcaster`` (Infrastructure) to implement the two new methods; map the notification to ``OverlayRevisionPublishedHubMessage`` / ``OverlayRevisionArchivedHubMessage`` and call ``hub.Clients.All.OverlayRevisionPublished/Archived(message)``.
- [ ] **T052 [US1]** Add ``LayoutRevisionPublishedHubMessage`` siblings: ``OverlayRevisionPublishedHubMessage`` (primitive types only, mirrors the V1 shape) and ``OverlayRevisionArchivedHubMessage`` in ``src/LayoutComposition/Infrastructure/Broadcasting/``.
- [ ] **T053 [US1]** ``ILayoutLifecycleClient`` (in ``Infrastructure/Broadcasting``) grows ``OverlayRevisionPublished(OverlayRevisionPublishedHubMessage)`` and ``OverlayRevisionArchived(OverlayRevisionArchivedHubMessage)`` methods.

### Api layer

- [ ] **T054 [P] [US1]** ``OverlayEndpoints.MapOverlayEndpoints`` — POST /overlays, GET /overlays/{id}, GET /overlays?state=..., POST /overlays/{id}/revisions/{n}/publish, POST /overlays/{id}/revisions/{n}/archive (the four additional revisions endpoints land in PR F). Admin policy + Result.Match → Created/OK/Problem.
- [ ] **T055 [P] [US1]** ``OverlayDesignerApiModule.AddOverlayDesignerApi`` — registers the concrete command/query handler classes.
- [ ] **T056 [P] [US1]** ``CreateOverlayRequest`` body record + ``LabelRequest`` shared body for label payloads.
- [ ] **T057 [US1]** ``Program.cs``: ``AddServiceDefaults`` + ``AddBearerAuthentication`` + ``AddOverlayDesignerInfrastructure`` + ``AddOverlayDesignerApi`` + ``MapOverlayEndpoints``.

### Frontend (management-web)

- [ ] **T058 [P] [US1]** ``overlays.api.ts`` in ``apps/shared/src/api/``: RTK Query slice — createOverlayDraft / getOverlay / listOverlays / publishRevision / archiveRevision (branch/edit/revert land in PR F). Tag types ``Overlay`` + ``OverlayList``. ``./api/overlays.api`` export added to shared package.json.
- [ ] **T059 [P] [US1]** ``OverlayEditor.tsx`` in ``apps/shared/src/ui/composites/``: react-rnd-based composite. Renders a fixed-aspect placeholder background; ``<Rnd>`` wraps the label preview; ``<input type="range">`` for font size; ``<input type="text">`` for label text. All four normalized values clamped to [0,1] before ``onChange``. ``./ui/composites/OverlayEditor`` export.
- [ ] **T060 [US1]** ``app/store.ts`` (management-web): add ``overlaysApi`` reducer + middleware.
- [ ] **T061 [P] [US1]** ``OverlayEditorDialog.tsx`` in ``apps/management-web/src/features/overlays/``: Dialog wrapping ``<OverlayEditor>`` + a name field. Reused for new-chain (CreateOverlayDraftMutation) and new-revision (BranchDraftRevisionMutation in PR F) flows.
- [ ] **T062 [P] [US1]** ``OverlayEditorDialog.test.tsx`` — render, submit, validation.
- [ ] **T063 [US1]** ``OverlaysPage.tsx`` in ``apps/management-web/src/features/overlays/``: DataTable + state-filter chips + per-row Publish / Archive actions. Same shape as LayoutsPage.
- [ ] **T064 [P] [US1]** ``OverlaysPage.test.tsx`` — empty state, populated list, Publish fires mutation, retry control.
- [ ] **T065 [US1]** ``App.tsx`` (management-web): add **Overlays** to the nav toggle.
- [ ] **T066 [P] [US1]** Update ``App.test.tsx`` smoke test to mock the four ``overlaysApi`` hooks.

**Checkpoint:** Admin can create + publish an overlay end-to-end. The bus carries ``OverlayRevisionPublishedV1``. No kiosk-side rendering yet.

---

## Phase B': User Story 1.5 — LayoutComposition extension (P1)

**Goal:** Layout revisions gain an optional ``OverlayIdentifier`` so US2 can bind one. Lands behind Phase 2 in the PR sequence so OverlayDesigner.Domain exists when the value-copied identifier is introduced.

**Independent Test:** ``LayoutComposition.Domain.Tests/Layout/LayoutTests.cs`` extended with ``CreateDraft_with_an_overlay_carries_it_into_the_first_revision``.

- [ ] **T067 [LAYOUTEXT]** New value-copy struct ``OverlayIdentifier`` in ``src/LayoutComposition/Domain/Layout/OverlayIdentifier.cs`` (Guid v7 IStronglyTypedId). Same shape as ``CameraIdentifier`` — a value-copy of the OverlayDesigner identifier; **no project reference**.
- [ ] **T068 [LAYOUTEXT]** ``Revision`` (in LayoutComposition.Domain) gains ``public OverlayIdentifier? Overlay { get; private set; }`` + ``internal void AttachOverlay(OverlayIdentifier?)``.
- [ ] **T069 [LAYOUTEXT]** ``Layout.CreateDraft`` + ``BranchDraft`` + ``EditDraft`` signatures grow an optional ``OverlayIdentifier?`` parameter. ``BranchDraft`` copies the overlay from the current Published.
- [ ] **T070 [LAYOUTEXT]** Update ``CreateLayoutDraftCommand`` + ``EditDraftRevisionCommand`` to carry ``OverlayIdentifier?``; matching DTOs + API request bodies. ``null`` clears the binding on EditDraft.
- [ ] **T071 [LAYOUTEXT]** EF migration ``<timestamp>_AddLayoutOverlayBinding.cs``: ``ALTER TABLE layout_revisions ADD COLUMN overlay_id uuid NULL`` + EF mapping update in ``LayoutConfiguration``.
- [ ] **T072 [LAYOUTEXT]** Extend ``LayoutComposition.Domain.Tests`` and ``LayoutComposition.Application.Tests`` to cover the new field. Existing tests stay green (the default for the new arg is ``null``).

**Checkpoint:** Layouts can carry an overlay binding (no kiosk rendering yet).

---

## Phase 3: User Story 2 — Kiosk renders the bound overlay (P1)

**Goal:** Kiosk fetches the bound overlay + renders the label over the camera frame at the authored coordinates.

**Independent Test:** ``OverlayBindingIntegrationTests.Bound_overlay_appears_in_GET_layout_and_in_GET_overlay`` + a kiosk-web vitest that asserts ``<CameraViewer overlay={...}>`` renders the label span.

- [ ] **T073 [P] [US2]** Extend ``CameraViewer.tsx`` (``apps/shared/src/ui/composites/CameraViewer.tsx``): add optional ``overlay?: { text, normalizedX, normalizedY, normalizedWidth, normalizedHeight, fontSizePx }`` prop. When set, render an absolutely-positioned ``<span>`` over the ``<video>`` with the label; CSS ``clamp(...)`` ties fontSizePx to the viewport.
- [ ] **T074 [P] [US2]** ``CameraViewer.test.tsx`` extends to cover the overlay rendering case.
- [ ] **T075 [P] [US2]** ``LayoutDto`` + ``LayoutRevisionDto`` carry the new ``OverlayIdentifier?`` field (from Phase B'); the kiosk-web layouts.api types learn it.
- [ ] **T076 [US2]** ``CellPage.tsx`` (kiosk-web): when ``data.revisions[Published].overlayIdentifier`` is set, call ``useGetOverlayQuery(overlayIdentifier)`` (new hook from ``overlays.api.ts``); pass the Published revision's ``Label`` into ``<CameraViewer overlay={...}>``.
- [ ] **T077 [P] [US2]** ``CellPage.test.tsx`` extends — bound overlay renders the label; unbound layout renders the viewer alone (no regression).
- [ ] **T078 [US2]** Extend ``LayoutEditorDialog.tsx`` (management-web) with an **Overlay** dropdown fed by ``useListOverlaysQuery('Published')``. ``(none)`` clears binding; otherwise sets ``overlayIdentifier``.
- [ ] **T079 [P] [US2]** ``LayoutEditorDialog.test.tsx`` extension — overlay-picker submits the right body.
- [ ] **T080 [US2]** ``OverlayBindingIntegrationTests`` in ``tests/Integration.Tests/OverlayDesigner/``: publish overlay, publish layout that references it, ``GET /layouts/{id}`` carries the overlayIdentifier, ``GET /overlays/{id}`` returns the Published label.

**Checkpoint:** Operator picks the layout on the kiosk and sees the camera + overlay composited.

---

## Phase 4: User Story 3 — Republish push (P1)

**Goal:** Editing + republishing an overlay propagates the new label to connected kiosks within 1 s via the existing SignalR hub.

**Independent Test:** ``OverlayPushIntegrationTests.Overlay_republish_pushes_to_connected_clients_within_one_second``.

- [ ] **T081 [P] [US3]** ``useLayoutLifecycle`` hook (kiosk-web) grows ``onOverlayPublished`` / ``onOverlayArchived`` callbacks. On reconnect, invalidates the overlay-by-identifier cache the same way layout-list is invalidated.
- [ ] **T082 [US3]** ``CellPage.tsx`` subscribes to ``onOverlayPublished`` for the bound overlay identifier; on match, invalidates the overlay cache so RTK Query re-fetches.
- [ ] **T083 [US3]** ``CellPage.tsx`` ``onOverlayArchived`` for the bound overlay: hide the label + show a small banner ("overlay unavailable").
- [ ] **T084 [P] [US3]** ``OverlayPushIntegrationTests`` — two SignalR clients; admin publishes overlay revision 2 (via the API); both clients receive ``OverlayRevisionPublished`` within 1 s; payload carries the new Label fields.
- [ ] **T085 [P] [US3]** Update ``apps/shared/src/realtime/layoutHub.ts`` (the SignalR client wrapper) — add typed ``onOverlayPublished`` / ``onOverlayArchived`` to the callbacks contract; subscribe via ``connection.on("OverlayRevisionPublished", ...)``.

**Checkpoint:** Admin republishes an overlay; every kiosk rendering a Layout that references it updates the label without page reload.

---

## Phase 5: User Story 4 — Revisions (branch / edit / revert) (P1)

**Goal:** Edit-after-publish creates a new Draft revision in the same chain; publishing the new revision auto-archives the prior one.

**Independent Test:** ``OverlayLifecycleIntegrationTests.Publish_a_new_revision_atomically_archives_the_previous_published_revision``.

- [ ] **T086 [P] [US4]** ``BranchDraftRevisionCommand`` + ``BranchDraftRevisionErrors`` (LayoutNotFound / NoPublishedRevisionToBranchFrom — mirror Layout naming).
- [ ] **T087 [US4]** ``BranchDraftRevisionCommandHandler``.
- [ ] **T088 [P] [US4]** ``EditDraftRevisionCommand`` (carries the full Label) + Errors.
- [ ] **T089 [US4]** ``EditDraftRevisionCommandHandler``.
- [ ] **T090 [P] [US4]** ``RevertRevisionCommand`` + Errors + Handler.
- [ ] **T091 [US4]** Map the three new endpoints in ``OverlayEndpoints.cs``: ``POST /overlays/{id}/draft``, ``PATCH /overlays/{id}/revisions/{n}``, ``POST /overlays/{id}/revisions/{n}/revert``.
- [ ] **T092 [P] [US4]** Extend ``overlays.api.ts`` with ``branchDraftRevision`` / ``editDraftRevision`` / ``revertRevision`` mutations.
- [ ] **T093 [US4]** Extend ``OverlaysPage.tsx`` with **Edit (new draft)** + **Revert** + **Archive** actions per state. The Edit action opens ``OverlayEditorDialog`` in branch-from-Published mode (pre-fills with the current Label).
- [ ] **T094 [US4]** ``OverlayLifecycleIntegrationTests.Publish_a_new_revision_atomically_archives_the_previous_published_revision``.

**Checkpoint:** Full revision chain works end-to-end on overlays.

---

## Phase 6: Polish

- [ ] **T095 [P] [POLISH]** Coverage gates: extend ``scripts/coverage-check.ps1`` with ``OverlayDesigner.Domain >= 90%`` and ``OverlayDesigner.Application >= 80%``. Backfill any missing handler/query tests.
- [ ] **T096 [P] [POLISH]** ``OverlayRevisionPublishedV1Tests`` + ``OverlayRevisionArchivedV1Tests`` in ``tests/Shared.Contracts.Tests/`` — positional ctor, IIntegrationEvent, equality, JSON round-trip.
- [ ] **T097 [P] [POLISH]** Architecture tests confirm: ``OverlayDesigner.Domain`` no SignalR / EF / Wolverine refs; no cross-context project references except the documented ``OverlayDesigner.Application → LayoutComposition.Domain.ILayoutLifecycleBroadcaster`` allow-rule (add an explicit assertion exercising the rule).
- [ ] **T098 [POLISH]** ``ReconnectReconcileIntegrationTests`` (still owed from spec 003 PR G, tracked by #306) lands here as well — drop+reconnect a SignalR client; archive an overlay's bound layout; assert force-disconnect within 5 s of reconnect.
- [ ] **T099 [POLISH]** Update ``README.md`` quickstart: append a **"Compose an overlay on the layout"** section after the existing "Publish a layout and view it on a kiosk" step.
- [ ] **T100 [POLISH]** Phase-5 verification gate (per ADR-0037): start ``aspire run``, sign in as admin, register a camera, create+publish an overlay, edit a Layout to bind it, observe the kiosk render the overlay over the live frame, edit+republish the overlay, observe the kiosk update within 1 s. Capture a screenshot or describe clearly in the PR.

---

## Dependencies and execution order

### Cross-phase

- **Phase 1 (Foundational):** blocks every user-story task.
- **Phase 2 (US1):** depends on Phase 1.
- **Phase B' (LayoutComposition extension):** depends on Phase 2's OverlayDesigner.Domain being present so ``OverlayIdentifier`` can be value-copied. Blocks Phase 3.
- **Phase 3 (US2):** depends on Phase B' + Phase 2's Api endpoints.
- **Phase 4 (US3):** depends on Phase 2 (broadcaster bridge) + Phase 3 (kiosk binding wired).
- **Phase 5 (US4):** depends on Phase 2's domain aggregate; independent of Phase 3/4.
- **Phase 6 (Polish):** depends on Phases 2–5.

### Within Phase 2 (US1)

Tests-first (T008–T022) before implementation (T023–T066). VOs (T023–T028) + events (T029, T030) in parallel; ``Revision`` (T031) + ``Layout`` aggregate (T032) sequential.

The broadcaster-bridge tasks (T050–T053) span the LayoutComposition boundary but stay inside Phase 2 because OverlayDesigner.Application can't compile without them.

### Parallel opportunities

- All [P] tests in Phase 2 (T008–T020).
- All VO/event tasks (T023–T030).
- Application command/error/handler pairs (T034, T036, T038, T039) in parallel.
- Polish tasks T095–T097 in parallel.

### Coverage-gate dependency

- T095 cannot pass until Phase 2 + Phase 5 handler tests land.

---

## Implementation strategy

**MVP first.** Phase 1 → Phase 2 → Phase B' → Phase 3 is the MVP — admin creates an overlay, binds it to a layout, kiosk renders it. Phase 4 (push) closes the consistency story; Phase 5 (revisions) closes the editorial story; Phase 6 (polish) closes the quality story.

**PR sequence (mirrors spec 003 plus an extra B′):**

1. **PR A — Phase 1 foundational** (T001–T007): Aspire wiring, project plumbing, integration-event records, react-rnd.
2. **PR B — Phase 2 Domain + tests** (T008–T016, T023–T033).
3. **PR B′ — LayoutComposition extension** (T067–T072): the cross-context overlay-identifier field + EF migration.
4. **PR C — Phase 2 Application + Infrastructure + broadcaster bridge** (T017–T020, T034–T053, T057).
5. **PR D — Api + management-web Overlays page + WYSIWYG** (T054–T066, plus T058 + T059 from shared). Includes the Aspire fixture extension (T021) + first integration test (T022).
6. **PR E — Kiosk render + SignalR push extension** (T073–T085).
7. **PR F — Revisions** (T086–T094).
8. **PR G — Polish** (T095–T100).

## Notes

- All file paths are absolute under the repo root.
- All NuGet versions reference ``Directory.Packages.props``; ``react-rnd`` joins ``apps/shared/package.json`` once.
- T050–T053 implement the **single documented cross-context exception** from plan.md — OverlayDesigner.Application calling into ``LayoutComposition.Domain.ILayoutLifecycleBroadcaster``. NetArchTest gets an explicit allow-rule in T097.
- T098 picks up the deferred ``ReconnectReconcileIntegrationTests`` originally scoped for spec 003 PR G (tracked by #306) — overlay-aware reconciliation makes the test more useful so we land it here.
