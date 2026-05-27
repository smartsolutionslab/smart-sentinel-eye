# Implementation Plan: 004 — OverlayDesigner (walking-skeleton "1 overlay")

**Branch:** `004-overlay-designer` | **Date:** 2026-05-27 | **Spec:** [spec.md](./spec.md)

**Status:** Draft (Phase 2 — Plan)

**Input:** Feature specification from
`specs/004-overlay-designer/spec.md` (Phase 1 closed, zero
`[NEEDS CLARIFICATION]` markers, eight Q&A clarifications resolved).

## Summary

Implements the first end-to-end slice through the **OverlayDesigner**
bounded context plus the cross-context binding into **LayoutComposition**:

- **Backend (OverlayDesigner):** New `Overlay` aggregate (logical
  chain) containing a collection of `Revision` sub-entities, mirroring
  the spec-003 Layout shape exactly — same state machine, same
  at-most-one-Published invariant, same revision-on-edit semantics.
  Each Revision owns a single `Label` value object
  (text + normalized position + size + font size). Postgres-backed
  CRUD; standard Wolverine outbox + integration events.
- **Backend (LayoutComposition extension):** Layout revision gains
  an optional `OverlayIdentifier`. The existing
  `BranchDraftRevision` / `EditDraftRevision` flow grows to carry it.
  No new Layout state, no new endpoints — just one field on the
  revision row + the DTOs.
- **Backend (SignalR):** The existing `LayoutLifecycleHub` gains two
  new typed client methods: `OverlayRevisionPublished` and
  `OverlayRevisionArchived`. The `SignalRLayoutLifecycleBroadcaster`
  is extended to fan-out overlay-lifecycle events; the hub stays at
  `/hubs/layouts` (no second hub).
- **Frontend (management-web):** New **Overlays** page reusing the
  `<DataTable>` composite. New **OverlayEditor** composite — WYSIWYG
  drag + resize via **``react-rnd``** (Phase-1 Q&A #4/#8), a font-size
  slider, and a placeholder camera-frame background. The existing
  ``LayoutEditorDialog`` grows an overlay-picker dropdown.
- **Frontend (kiosk-web):** ``CameraViewer`` composite extended with
  an optional ``overlay`` prop; renders the label as an absolutely-
  positioned ``<span>`` over the video element. The ``useLayoutLifecycle``
  hook learns ``onOverlayPublished`` / ``onOverlayArchived`` callbacks.
- **Tests:** Two new per-layer test projects
  (``OverlayDesigner.Domain.Tests``, ``OverlayDesigner.Application.Tests``),
  Overlay-related integration tests reusing ``AspireFixture``.
  Architecture tests pick up the new context automatically via
  assembly-level wildcards.

## Technical Context

| Concern | Decision | Source |
|---|---|---|
| Backend language | C# / .NET 10 | ADR-0001, ADR-0024 |
| Frontend language | TypeScript / React 19 | ADR-0001, ADR-0074 |
| Persistence | EF Core on Postgres (per-context DB ``overlay-designer-db``) | ADR-0009, ADR-0071 |
| Messaging | RabbitMQ via Wolverine; per-module queue isolation; Postgres outbox | ADR-0010, ADR-0042, ADR-0088 |
| Real-time push | **Existing** ASP.NET Core SignalR hub at ``/hubs/layouts`` (spec 003). Grows two new typed-client methods. No second hub. | spec FR-011 + ADR-0076 |
| Identity | Keycloak — same realm; ``sse.management`` scope required for HTTP + the hub | ADR-0007, ADR-0023 |
| API style | Minimal APIs only | ADR-0070 |
| Errors | ``Result<T, ApiError>`` with sealed-record ``OverlayError`` / ``OverlayRevisionError`` hierarchies | ADR-0047, ADR-0089 |
| Frontend state | Redux Toolkit + RTK Query; new ``overlaysApi`` slice (in shared) consumed by both apps | ADR-0075 |
| WYSIWYG library | **``react-rnd@10.5.2``** — drag + resize built-in, ~30 KB gzipped, single-component primitive. ``@dnd-kit`` lacks resize; hand-rolling is meaningful net-new work for no clear win. | spec Assumptions + Phase-1 Q&A #4/#8 |
| Tests | xUnit + Shouldly + Moq + Testcontainers via ``AspireFixture`` (extended for the new context) | ADR-0052, ADR-0068 |
| Performance goals | ``GET /overlays/{id}`` ≤ 100 ms p95 (SC-005). Overlay republish → connected kiosks update ≤ 1 s p95 (SC-002 / FR-011). | spec |
| Scale | Walking-skeleton: ≤ 20 overlays at the gate. Plenty of headroom; the 250-camera target stays a downstream concern. | spec Assumptions |

## Constitution Check

| Principle | Check | Status |
|---|---|---|
| §I On-prem first, cloud-ready | OverlayDesigner runs on the same fab host; SignalR signalling stays on the fab LAN; no cloud calls. | ✅ |
| §II DDD + value objects | ``OverlayIdentifier``, ``OverlayRevisionIdentifier``, ``OverlayName``, ``Label``, ``LayoutRevisionNumber`` (reused), ``LayoutRevisionState`` shape (own VO ``OverlayRevisionState``). Maximalist hand-written per ADR-0038 + ADR-0046; ``IValueObject<T>`` markers per ADR-0066. | ✅ |
| §III Bounded-context isolation | All new work in ``SmartSentinelEye.OverlayDesigner.*``. Cross-context contracts via ``Shared.Contracts/OverlayDesigner/`` (out). ``OverlayIdentifier`` value-copies into LayoutComposition exactly like ``CameraIdentifier``. NetArchTest enforces. | ✅ |
| §IV Latency budget sacred | Spec 004 provides the **render substrate** for the 800 ms event-to-overlay budget. The budget itself starts ticking with spec 005's variable binding. PR will report measured ``publish-overlay → kiosk-render`` on SC-002. | ✅ |
| §V Spec-driven | Spec exists (PR #308). This plan exists. Tasks follow. | ✅ |
| §VI Aspire is composition root | ``overlay-designer`` is a new Aspire project resource. ``overlay-designer-db`` is a ``postgres.AddDatabase`` per-context resource. | ✅ |
| §VII Observability mandatory | Overlay state-machine transitions logged via ``ILogger<T>`` with structured fields ``{ overlayIdentifier, revisionNumber, fromState, toState }``. OpenTelemetry traces span the ``PublishRevisionCommand → outbox commit → SignalR broadcast`` path. | ✅ |
| §VIII Safe at trust boundaries | ``[Authorize(Policy = AuthenticationDefaults.AdminPolicy)]`` on ``/overlays/*`` endpoints. Existing hub authorization covers the new server-to-client methods. Validation rejects malformed input at the API edge AND at value-object constructors. | ✅ |
| §IX Forward-compatible interfaces | ``ICommandHandler<,>`` / ``IQueryHandler<,>`` framework-agnostic. The ``ILayoutLifecycleBroadcaster`` (spec 003) gains two methods; alternative SignalR transports stay a one-class swap. | ✅ |

**Result:** No constitutional violations. No Complexity Tracking entries.

## Project Structure

### Documentation (this feature)

```
specs/004-overlay-designer/
├── spec.md          ← Phase 1 (PR #308, merged)
├── plan.md          ← this file (Phase 2)
└── tasks.md         ← Phase 3 (next; created by /speckit-tasks)
```

### Source Code — files added / modified

```
src/OverlayDesigner/Domain/                              ← scaffold exists; populated here
└── Overlay/                                              ← new aggregate folder (ADR-0092)
    ├── Overlay.cs                                        ← aggregate root (logical chain)
    ├── OverlayIdentifier.cs                              ← Guid v7-backed IStronglyTypedId<Guid>
    ├── OverlayName.cs                                    ← string VO (≤ 80 chars, non-empty, no newlines)
    ├── Revision.cs                                       ← entity inside the aggregate
    ├── OverlayRevisionIdentifier.cs
    ├── OverlayRevisionNumber.cs                          ← int VO (≥ 1, monotonic)
    ├── OverlayRevisionState.cs                           ← enum VO (Draft|Published|Archived)
    ├── Label.cs                                          ← VO carrying text + normalized geometry + fontSizePx
    ├── IOverlayRepository.cs
    └── Events/
        ├── OverlayRevisionPublishedDomainEvent.cs
        └── OverlayRevisionArchivedDomainEvent.cs

src/OverlayDesigner/Application/
├── Commands/                                             ← mirror Layout: Create/Branch/Edit/Publish/Revert/Archive
│   ├── CreateOverlayDraftCommand.cs        + Errors + Handler
│   ├── BranchDraftRevisionCommand.cs       + Errors + Handler
│   ├── EditDraftRevisionCommand.cs         + Errors + Handler  (label payload)
│   ├── PublishRevisionCommand.cs           + Errors + Handler
│   ├── RevertRevisionCommand.cs            + Errors + Handler
│   └── ArchiveRevisionCommand.cs           + Errors + Handler
├── Queries/
│   ├── GetOverlayQuery.cs                  + Errors + Handler
│   ├── ListOverlaysQuery.cs                + Errors + Handler
│   └── IOverlayQuerySource.cs              ← read-side IQueryable seam
├── EventHandlers/
│   ├── OverlayRevisionPublishedDomainEventHandler.cs   ← outbox V1 + broadcast
│   └── OverlayRevisionArchivedDomainEventHandler.cs    ← outbox V1 + broadcast
└── DTOs/
    ├── OverlayDto.cs                                    ← { overlayIdentifier, name, revisions: [...] }
    └── OverlayRevisionDto.cs                            ← carries the full Label payload

src/OverlayDesigner/Infrastructure/
├── OverlayDesignerInfrastructureModule.cs               ← AddOverlayDesignerInfrastructure()
├── OverlayDesignerPersistenceModule.cs                  ← slim, used by MigrationRunner
├── Persistence/
│   ├── OverlayDesignerDbContext.cs                      ← DbSet<Overlay>; Wolverine outbox
│   ├── Configurations/
│   │   ├── OverlayConfiguration.cs                      ← aggregate + OwnsMany(Revisions)
│   │   └── (label is mapped as owned value object on each Revision)
│   ├── OverlayRepository.cs                             ← IOverlayRepository impl + event dispatch
│   ├── OverlayQuerySource.cs                            ← IOverlayQuerySource impl (AsNoTracking)
│   ├── OverlayDesignerMigrator.cs
│   └── DesignTimeDbContextFactory.cs
└── Migrations/
    └── <timestamp>_InitialOverlayDesigner.cs

src/OverlayDesigner/Api/
├── OverlayDesignerApiModule.cs                          ← AddOverlayDesignerApi()
├── OverlayEndpoints.cs                                  ← /overlays minimal API (8 routes per FR-007)
├── Requests/
│   ├── CreateOverlayRequest.cs                          ← { name, label: {...} }
│   └── EditDraftRequest.cs                              ← { label: {...} }
└── Program.cs                                           ← AddOverlayDesignerInfrastructure + Api + auth + endpoints

src/Shared.Contracts/                                    ← cross-context (no project ref between contexts)
└── OverlayDesigner/
    ├── OverlayRevisionPublishedV1.cs                    ← carries the full Label payload + RevisionNumber
    └── OverlayRevisionArchivedV1.cs

src/LayoutComposition/                                   ← cross-context extension
├── Domain/Layout/Revision.cs                            ← MODIFIED: + OverlayIdentifier? property
├── Domain/Layout/OverlayIdentifier.cs                   ← NEW: value-copy of OverlayDesigner.OverlayIdentifier
├── Domain/Layout/Layout.cs                              ← MODIFIED: BranchDraft + EditDraft + CreateDraft accept optional overlay
├── Application/Commands/
│   ├── CreateLayoutDraftCommand.cs                      ← MODIFIED: + optional OverlayIdentifier
│   ├── EditDraftRevisionCommand.cs                      ← MODIFIED: + optional OverlayIdentifier (clear via null)
│   └── Handlers/*.cs                                    ← MODIFIED accordingly
├── Application/DTOs/LayoutDto.cs                        ← MODIFIED: LayoutRevisionDto gets OverlayIdentifier?
├── Infrastructure/Persistence/Configurations/LayoutConfiguration.cs ← MODIFIED: maps overlay_id column (nullable)
├── Infrastructure/Persistence/Migrations/<ts>_AddOverlayBinding.cs   ← NEW: ALTER TABLE layout_revisions ADD overlay_id uuid NULL
└── Infrastructure/Broadcasting/
    ├── ILayoutLifecycleClient.cs                        ← MODIFIED: + OverlayRevisionPublished/Archived methods
    └── SignalRLayoutLifecycleBroadcaster.cs             ← MODIFIED: + PublishedOverlayAsync / ArchivedOverlayAsync (called by OverlayDesigner's domain-event handler via a shared abstraction)

src/MigrationRunner/
└── Program.cs                                           ← +builder.AddOverlayDesignerPersistence();

src/AppHost/
└── AppHost.cs                                           ← add overlay-designer-db database + overlay-designer Api project + WithReference graph

apps/shared/src/
├── api/
│   └── overlays.api.ts                                  ← new RTK Query slice (create/branch/edit/publish/revert/archive/list/get)
└── ui/composites/
    ├── CameraViewer.tsx                                 ← MODIFIED: + optional overlay prop; renders <span> over the <video>
    └── OverlayEditor.tsx                                ← NEW: react-rnd-based WYSIWYG (drag + resize + font-size slider)

apps/management-web/src/
├── features/overlays/                                   ← NEW feature folder
│   ├── OverlaysPage.tsx                                 ← DataTable + per-row actions (Publish / Edit-as-new-draft / Revert / Archive)
│   ├── OverlayEditorDialog.tsx                          ← wraps OverlayEditor composite + name field
│   ├── OverlaysPage.test.tsx
│   └── OverlayEditorDialog.test.tsx
├── features/layouts/LayoutEditorDialog.tsx              ← MODIFIED: + overlay-picker dropdown (fed by useListOverlaysQuery)
├── app/
│   ├── store.ts                                         ← +overlaysApi reducer + middleware
│   └── (App.tsx) — nav adds Overlays toggle
└── App.test.tsx                                         ← mock useListOverlaysQuery

apps/kiosk-web/src/
└── features/cell/CellPage.tsx                           ← MODIFIED: passes Published overlay (via useGetOverlayQuery) to CameraViewer

apps/kiosk-web/src/features/revocation/useLayoutLifecycle.ts ← MODIFIED: + onOverlayPublished / onOverlayArchived callbacks

tests/OverlayDesigner.Domain.Tests/                      ← new test project
├── Overlay/
│   ├── OverlayTests.cs
│   ├── OverlayRevisionStateMachineTests.cs
│   ├── LabelTests.cs
│   ├── OverlayNameTests.cs
│   ├── OverlayIdentifierTests.cs
│   ├── OverlayRevisionIdentifierTests.cs
│   ├── OverlayRevisionNumberTests.cs                    ← same as Layout's; or reuse if we move VO to Shared.Kernel later
│   ├── OverlayRevisionStateTests.cs
│   ├── OverlayGuardTests.cs
│   └── Builders/
│       └── OverlayBuilder.cs

tests/OverlayDesigner.Application.Tests/                 ← new test project
├── Commands/                                            ← handler tests, one per command (6 files)
├── Queries/                                             ← Get + List handler tests
├── EventHandlers/                                       ← V1 + broadcast assertions
└── Fakes/
    ├── InMemoryOverlayRepository.cs
    ├── InMemoryOverlayQuerySource.cs
    ├── FakeOverlayLifecycleBroadcaster.cs
    └── FakeClock.cs                                     ← (duplicated locally, mirrors LayoutComposition.Application.Tests)

tests/LayoutComposition.Application.Tests/               ← extend existing
└── Commands/                                            ← handler tests for the OverlayIdentifier carry

tests/Integration.Tests/OverlayDesigner/                 ← new directory
├── OverlayLifecycleIntegrationTests.cs                  ← create + publish + list filter + edit + revert + archive
├── OverlayBindingIntegrationTests.cs                    ← bind to a Layout revision; assert kiosk-side fetch returns the overlay
└── OverlayPushIntegrationTests.cs                       ← publish overlay rev → existing SignalR clients receive overlay event (US-3)

tests/Integration.Tests/Fixtures/AspireFixture.cs        ← MODIFIED: wait for overlay-designer + HttpClient
tests/Integration.Tests/Fixtures/AspireFixture.Db.cs     ← MODIFIED: CreateOverlayDesignerDbContextAsync + ResetOverlayDesignerAsync

tests/Architecture.Tests/                                ← no source changes; existing wildcards cover the new context

tests/Shared.Contracts.Tests/
├── OverlayRevisionPublishedV1Tests.cs                   ← parallels LayoutRevisionPublishedV1Tests
└── OverlayRevisionArchivedV1Tests.cs

scripts/
└── coverage-check.ps1                                   ← MODIFIED: +OverlayDesigner.Domain >= 90%, .Application >= 80%
```

**Structure Decision:** Backend follows the per-aggregate Domain folder layout (ADR-0092) and the per-message-kind Application layout (ADR-0093). Most of OverlayDesigner is a near-clone of LayoutComposition's shape, which is intentional — the user agreed in Phase-1 Q&A that overlays should mirror Layout's revision-chain model. A shared revision-chain base class is a tempting refactor we'll resist; landing two near-identical aggregates and watching the third confirm the pattern is the right Karpathy-style time to abstract.

## Backend Design

### OverlayDesigner.Domain

```csharp
public sealed class Overlay : AggregateRoot<OverlayIdentifier>
{
    private readonly List<Revision> _revisions = new();

    public OverlayName Name { get; private set; } = null!;
    public IReadOnlyList<Revision> Revisions => _revisions;
    public DateTimeOffset CreatedAt { get; private set; }
    public OperatorIdentifier CreatedBy { get; private set; }

    public static Overlay CreateDraft(OverlayName name, Label label, OperatorIdentifier by, IClock clock) { /* mirrors Layout */ }
    public Revision BranchDraft(OperatorIdentifier by, IClock clock) { /* copies prior Published's label */ }
    public void EditDraft(LayoutRevisionNumber n, Label newLabel, IClock clock) { ... }
    public void Publish(LayoutRevisionNumber n, OperatorIdentifier by, IClock clock) { /* atomic swap */ }
    public void Revert(LayoutRevisionNumber n, OperatorIdentifier by, IClock clock) { ... }
    public void ArchiveRevision(LayoutRevisionNumber n, OperatorIdentifier by, IClock clock) { ... }
}

public sealed record Label(
    string Text,
    decimal NormalizedX,
    decimal NormalizedY,
    decimal NormalizedWidth,
    decimal NormalizedHeight,
    int FontSizePx) : IValueObject<Label>
{
    public static Label From(...) { /* Ensure.That validation, all six fields */ }
}
```

State-transition table is **identical** to Layout's (spec 003 plan §Domain Design); refer there. The only structural difference: ``Label`` replaces Layout's ``Camera`` as the per-revision payload.

### LayoutComposition extension

```csharp
public sealed class Revision  // existing entity
{
    // ...existing fields
    public OverlayIdentifier? Overlay { get; private set; }  // NEW
}

public sealed class Layout
{
    public static Layout CreateDraft(
        LayoutName name,
        CameraIdentifier camera,
        OverlayIdentifier? overlay,  // NEW, optional
        OperatorIdentifier createdBy,
        IClock clock) { ... }

    public Revision BranchDraft(OperatorIdentifier by, IClock clock) { /* copies camera + overlay */ }

    public void EditDraft(
        LayoutRevisionNumber n,
        CameraIdentifier camera,
        OverlayIdentifier? overlay,  // NEW (null = clear binding)
        IClock clock) { ... }
}
```

``CreateLayoutDraftCommand`` and ``EditDraftRevisionCommand`` gain an optional ``OverlayIdentifier?``. Existing tests stay green (the optional argument defaults to ``null``).

### SignalR hub extension

The existing ``ILayoutLifecycleClient`` (spec 003 PR E) grows two methods:

```csharp
public interface ILayoutLifecycleClient
{
    // Existing:
    Task LayoutRevisionPublished(LayoutRevisionPublishedHubMessage message);
    Task LayoutRevisionArchived(LayoutRevisionArchivedHubMessage message);

    // NEW (spec 004 FR-011):
    Task OverlayRevisionPublished(OverlayRevisionPublishedHubMessage message);
    Task OverlayRevisionArchived(OverlayRevisionArchivedHubMessage message);
}
```

``ILayoutLifecycleBroadcaster`` (the domain abstraction in
``LayoutComposition.Domain``) gains:

```csharp
Task OverlayPublishedAsync(OverlayRevisionPublishedNotification notification, CancellationToken ct);
Task OverlayArchivedAsync(OverlayRevisionArchivedNotification notification, CancellationToken ct);
```

The OverlayDesigner Application layer takes a dependency on
``ILayoutLifecycleBroadcaster`` (the abstraction lives in
``LayoutComposition.Domain``; consuming it from a sibling context's
Application layer is allowed because it's a Domain *abstraction*, not
a concrete type). The concrete ``SignalRLayoutLifecycleBroadcaster``
in LayoutComposition.Infrastructure stays the implementation; its
container registration is unchanged.

**This is the one place spec 004 crosses a context boundary.** The
broadcaster abstraction is the shared seam; the concrete impl stays
inside LayoutComposition. NetArchTest will be checked against this
exception explicitly.

### Persistence schema (OverlayDesigner)

```sql
CREATE TABLE overlays (
  overlay_id    uuid PRIMARY KEY,
  name          varchar(80) NOT NULL,
  created_at    timestamptz NOT NULL,
  created_by    uuid NOT NULL,
  version       integer NOT NULL                       -- optimistic concurrency
);

CREATE TABLE overlay_revisions (
  revision_id        uuid PRIMARY KEY,
  overlay_id         uuid NOT NULL REFERENCES overlays(overlay_id) ON DELETE CASCADE,
  revision_number    integer NOT NULL,
  state              varchar(16) NOT NULL,             -- 'Draft' | 'Published' | 'Archived'
  label_text         varchar(256) NOT NULL,
  label_x            numeric(6,5) NOT NULL,
  label_y            numeric(6,5) NOT NULL,
  label_width        numeric(6,5) NOT NULL,
  label_height       numeric(6,5) NOT NULL,
  label_font_size_px integer NOT NULL,
  created_at         timestamptz NOT NULL,
  created_by         uuid NOT NULL,
  published_at       timestamptz NULL,
  archived_at        timestamptz NULL,
  UNIQUE (overlay_id, revision_number)
);

CREATE UNIQUE INDEX ux_overlay_revisions_one_published
  ON overlay_revisions (overlay_id)
  WHERE state = 'Published';
```

LayoutComposition gets one additive migration:

```sql
ALTER TABLE layout_revisions ADD COLUMN overlay_id uuid NULL;
```

No foreign key (cross-context isolation per ADR-0027 — the FK would
imply database-level coupling). Integrity is application-level.

### Wolverine subscription wiring

OverlayDesigner has no inbound integration events in v1. The standard
``AddWolverineForContext<OverlayDesignerDbContext>`` is wired in
infrastructure with the OverlayDesigner.Application assembly included
via ``configureMore`` (tech-debt #200 still pending).

## Frontend Design

### management-web (additive)

- New route ``/overlays`` (nav adds a third toggle button alongside
  Cameras / Layouts).
- ``OverlaysPage.tsx`` lists all chains with state filter chips, per-
  row Publish / Edit / Revert / Archive actions — same shape as
  ``LayoutsPage``.
- ``OverlayEditorDialog.tsx`` is the create + edit form: a name
  ``<input>`` plus the ``<OverlayEditor>`` composite (see below). The
  dialog is reused for new-chain and new-revision flows the same way
  ``LayoutEditorDialog`` is.
- ``LayoutEditorDialog.tsx`` (existing) grows an **Overlay** dropdown
  fed by ``useListOverlaysQuery('Published')``. Picking ``(none)``
  clears the binding; picking an entry sets the layout revision's
  ``overlayIdentifier``.

### Shared composite: ``OverlayEditor``

```tsx
import { Rnd } from 'react-rnd';

export interface OverlayEditorProps {
  initialLabel?: Label;
  onChange: (label: Label) => void;
}
```

- Wraps a single ``<Rnd>`` instance over a fixed-aspect placeholder
  background (1080p reference, scaled-to-fit the editor pane).
- Drag updates ``normalizedX`` / ``normalizedY``; the resize handles
  update ``normalizedWidth`` / ``normalizedHeight``.
- A small ``<input type="range">`` controls ``fontSizePx``.
- A ``<input type="text">`` controls the label text — placeholder
  syntax (``{{name}}``) is accepted verbatim per FR-013.
- All four normalized values are clamped to ``[0, 1]`` before
  ``onChange`` fires. The aggregate constructor re-validates server-
  side as a backstop (FR-005).

### Shared composite: ``CameraViewer`` extension

```tsx
export interface CameraViewerProps {
  cameraIdentifier: string;
  getToken: () => Promise<string | null>;
  overlay?: Label;                  // NEW
  className?: string;
}
```

When ``overlay`` is set, render an absolutely-positioned ``<span>``
over the ``<video>`` element using ``style={{ left:
\`${overlay.normalizedX * 100}%\`, ... }}``. CSS ``font-size:
clamp(...)`` ties the author-time pixel size to the viewport via
``vh`` so the label scales proportionally (FR-014).

### kiosk-web

- ``CellPage.tsx`` (existing) fetches the Layout's current Published
  revision (spec 003). When the revision has ``overlayIdentifier``,
  the page calls ``useGetOverlayQuery(overlayIdentifier)`` and passes
  the overlay's Published-revision ``Label`` to ``<CameraViewer
  overlay={...}>``.
- ``useLayoutLifecycle`` gains ``onOverlayPublished`` /
  ``onOverlayArchived``. The cell view subscribes; on a published
  event for the bound overlay, it invalidates the overlay cache so
  RTK Query re-fetches. On an archived event for the bound overlay,
  it falls back gracefully (label hidden + tiny banner).

## Cross-cutting

### NetArchTest rules

| Rule | Why |
|---|---|
| ``OverlayDesigner.Domain`` MUST NOT reference EF Core / Wolverine / SignalR | Same Domain freedom as Layout / Stream / Camera |
| ``OverlayDesigner.*`` MUST NOT reference ``LayoutComposition.Application.*`` / ``CameraCatalog.*`` / ``StreamDistribution.*`` | Cross-context isolation (ADR-0027) |
| ``OverlayDesigner.Application`` MAY reference ``LayoutComposition.Domain.ILayoutLifecycleBroadcaster`` (and the notification records) | Documented exception — the broadcaster is a Domain abstraction, intentionally shared as the SignalR seam. NetArchTest adds a single explicit allow-rule with a comment pointing at this plan. |

### Coverage gates

``scripts/coverage-check.ps1`` adds:

```pwsh
'SmartSentinelEye.OverlayDesigner.Domain'      = 90
'SmartSentinelEye.OverlayDesigner.Application' = 80
```

``Shared.Contracts`` gate stays at 90 % — the two new
``OverlayRevision*V1`` records get unit tests in PR G (mirrors the
spec-003 polish).

### Test strategy

**Domain tests** mirror Layout's exactly: full state-machine matrix,
chain invariants, Label VO validation (text bounds, normalized
bounds, font-size bounds).

**Application tests** parallel the Layout suite: handler-per-command,
in-memory fakes, ``FakeOverlayLifecycleBroadcaster`` records its
calls.

**Integration tests:**

- ``OverlayLifecycleIntegrationTests``: create → publish → list →
  branch → edit → publish-new → assert revision 1 archived.
- ``OverlayBindingIntegrationTests``: create + publish overlay; create
  + publish a layout that references it; ``GET /layouts/{id}`` then
  ``GET /overlays/{overlayIdentifier}`` returns the label.
- ``OverlayPushIntegrationTests``: two SignalR clients connected;
  publish a new overlay revision; assert both receive
  ``OverlayRevisionPublished`` within 1 s (mirrors spec-003's
  ``SignalRRevocationIntegrationTests``).

## Risk register

| # | Risk | Mitigation |
|---|---|---|
| 1 | Always-latest binding amplifies blast radius — one overlay change hits N layouts. | Phase-1 Q&A locked this in; trade-off accepted. If it bites in practice, revision-pinned binding is an additive change (LayoutRevision gains an optional ``overlayRevisionNumber``). |
| 2 | OverlayDesigner.Application depends on a LayoutComposition.Domain abstraction (the broadcaster). NetArchTest currently blocks all cross-context references. | One explicit allow-rule with a comment pointing at this plan. The same pattern will recur with future contexts (Automation referencing variable abstractions, etc.); landing the exception infra here pays off downstream. |
| 3 | react-rnd is unmaintained-looking on its surface (last release Jan 2024). | Pinning version + integration test that re-asserts ``onDragStop`` / ``onResizeStop`` give us normalized coordinates within 0.005 of the cursor's reported position. If the library breaks on a future React major, swap to hand-rolled is a per-component change. |
| 4 | ``decimal`` columns for normalized coords. EF Core's default Postgres mapping uses ``numeric`` which is slow for high-frequency updates — but our update rate is "publish a revision" (≤ 1/min/admin), so it's fine. | No mitigation needed; flagging the shape so future high-rate variable streaming uses ``double`` / ``real`` instead. |
| 5 | Drag-and-drop in the editor doesn't compose with the existing dialog ESC-to-close pattern. ``react-rnd`` swallows pointer events. | Editor lives inside a Dialog that listens at the root for ESC; pointer events on the canvas are scoped. Manual test in PR D. |
| 6 | Overlay font-size scaling via ``vh`` looks fine on the 1080p reference but breaks at 4K kiosk resolutions. | CSS uses ``clamp(min, vh-derived, max)`` with sensible caps. Spec doesn't promise 4K parity; that's a follow-up if/when a 4K kiosk lands. |

## Phase hand-off

When the user approves this plan, ``/speckit-tasks`` will:

- Generate atomic tasks per layer, with the same A-G PR pattern as
  spec 003 plus a new **PR B'** for the LayoutComposition extension.
- Bundle the WYSIWYG editor library + the new shared composite into a
  single PR so the editor lands as one cohesive feature.

Suggested PR sequence:

1. **PR A — Phase 1 foundational:** AppHost wiring, project scaffolds,
   integration-event records, react-rnd pin in apps/shared.
2. **PR B — Phase 2 domain + tests:** Overlay aggregate, Label VO,
   per-aggregate tests.
3. **PR B′ — LayoutComposition extension:** add ``OverlayIdentifier`` to
   Layout.Revision + migration + handler/DTO updates. Lands behind
   B so OverlayDesigner.Domain exists first.
4. **PR C — Phase 2 application + infrastructure + EF migration:**
   commands/handlers/queries, DbContext, repository, broadcaster
   wiring (the cross-context abstraction reference).
5. **PR D — Api + management-web Overlays page + WYSIWYG editor:**
   endpoints, RTK Query slice, OverlayEditor composite,
   OverlayEditorDialog, OverlaysPage; LayoutEditorDialog grows the
   overlay-picker dropdown.
6. **PR E — Kiosk overlay rendering + SignalR push:** CameraViewer
   ``overlay`` prop, CellPage wiring, useLayoutLifecycle extension,
   hub-method additions.
7. **PR F — Revisions:** branch / edit / revert (Phase 5 in spec 003
   parlance). Likely smaller than spec 003's PR F because the
   handlers are clones.
8. **PR G — Polish:** coverage gate additions, V1 record tests,
   README quickstart, manual verification gate.

## Open clarifications

None — all design decisions are locked. Ready for Phase 3 (Tasks).
