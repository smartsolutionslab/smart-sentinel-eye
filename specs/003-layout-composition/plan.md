# Implementation Plan: 003 — Layout Composition (walking-skeleton "1 cell")

**Branch:** `003-layout-composition` | **Date:** 2026-05-26 | **Spec:** [spec.md](./spec.md)

**Status:** Draft (Phase 2 — Plan)

**Input:** Feature specification from
`specs/003-layout-composition/spec.md` (Phase 1 closed; zero
`[NEEDS CLARIFICATION]` markers; eight Phase-1 Q&A clarifications
resolved).

## Summary

Implements the first end-to-end slice through the **LayoutComposition**
bounded context:

- **Backend:** New `Layout` aggregate (logical chain) containing a
  collection of `Revision` sub-entities. State machine on the
  Revision: `Draft → Published → Archived`, with `Draft ↔ Published`
  edges, plus an aggregate-level invariant *at most one Published
  revision per chain at any time*. Postgres-backed CRUD per CLAUDE.md
  (not a Marten / event-sourcing candidate). One `IHostedService`
  publishes lifecycle events to a SignalR hub. Standard outbox-driven
  integration events on the bus.
- **Frontend (management-web):** New **Layouts** page reusing the
  spec 002 `<DataTable>` composite. Editor dialog handles new-chain
  and new-revision flows. State chip + action buttons per row.
- **Frontend (kiosk-web — first-time online):** New Vite/React app at
  `apps/kiosk-web/`. OIDC sign-in via the existing Keycloak realm
  (same flow as management-web — second OIDC client, not a new
  realm). Picker page lists `GET /layouts?state=published`. Cell view
  reuses spec 002's `<CameraViewer>` composite unchanged. SignalR
  client subscribes to layout-lifecycle pushes.
- **Real-time:** ASP.NET Core **SignalR** hub at `/hubs/layouts`,
  broadcast on every Layout-Revision state change. Authenticated via
  the existing JWT bearer pipeline (same Keycloak realm, same
  `sse.management` scope). Kiosks reconcile on reconnect by re-
  fetching the Published list — covers missed pushes.
- **Tests:** Two new per-layer test projects
  (`LayoutComposition.Domain.Tests`, `LayoutComposition.Application.Tests`),
  Layout-related integration tests reusing `AspireFixture` extended
  with a layout-composition resource and a SignalR test-client helper.
  Architecture tests add LayoutComposition.Domain to the
  no-infrastructure-ref rule.

## Technical Context

| Concern | Decision | Source |
|---|---|---|
| Backend language | C# / .NET 10 | ADR-0001, ADR-0024 |
| Frontend language | TypeScript / React 19 | ADR-0001, ADR-0074 |
| Persistence | EF Core on Postgres (per-context DB `layout-composition-db`) | ADR-0009, ADR-0071 |
| Messaging | RabbitMQ via Wolverine; per-module queue isolation; Postgres outbox in the same per-context DB | ADR-0010, ADR-0042, ADR-0088 |
| Real-time push | **ASP.NET Core SignalR** (hub at `/hubs/layouts`); broadcast-to-all in v1 (no SignalR groups yet — every connected admin/kiosk needs every event) | spec FR-009 + ADR-0076 v1 |
| Identity | Keycloak — same `smart-sentinel-eye` realm; `sse.management` scope required for HTTP **and** the SignalR hub | ADR-0007, ADR-0023, spec FR-008/FR-010 |
| API style | Minimal APIs only (the SignalR hub is the one allowed deviation — it's not an API in the REST sense) | ADR-0070 |
| Errors | `Result<T, ApiError>` with sealed-record `LayoutError` / `LayoutRevisionError` hierarchies | ADR-0047, ADR-0089 |
| Frontend state | Redux Toolkit + RTK Query; new `layoutsApi` slice (in shared) consumed by both apps | ADR-0075 |
| Frontend routing | React Router DOM in kiosk-web (picker + cell view); management-web gets one new route under its existing router | ADR-0074 |
| OIDC client | `react-oidc-context`; kiosk-web is a new Keycloak client (`smart-sentinel-eye-kiosk`) sharing the existing realm | ADR-0080 (admin-flow path) |
| Tests | xUnit + Shouldly + Moq + Testcontainers via `AspireFixture` (extended for the SignalR hub) | ADR-0052, ADR-0068 |
| Performance goals | `GET /layouts?state=published` ≤ 100 ms p95 (SC-005). Archive-to-force-disconnect ≤ 1 s p95 (SC-002 / FR-011). Click-to-first-frame ≤ 3 s p95 (SC-004 — reuses spec 002's budget). | spec |
| Scale | Walking-skeleton: ≤ 5 kiosks, ≤ 20 layouts at the gate. The 250-camera target is a downstream concern. | spec Assumptions |

## Constitution Check

Verifying alignment with each load-bearing principle before
implementation begins. Re-checked after data model is drafted.

| Principle | Check | Status |
|---|---|---|
| §I On-prem first, cloud-ready | LayoutComposition runs on the same fab host as the rest of the stack; SignalR signalling stays on the fab LAN. No cloud calls. | ✅ |
| §II DDD + value objects | `LayoutIdentifier`, `LayoutRevisionIdentifier`, `LayoutName`, `LayoutRevisionState`, `LayoutRevisionNumber` are maximalist value objects per ADR-0038, hand-written per ADR-0046, with `IValueObject<T>` markers per ADR-0066. The aggregate enforces transition + chain invariants. | ✅ |
| §III Bounded-context isolation | All new work in `SmartSentinelEye.LayoutComposition.*`. Cross-context contracts via `Shared.Contracts/LayoutComposition/` (out). No inbound integration events in v1 (operator picks layouts directly; no automation triggers yet). NetArchTest enforces. | ✅ |
| §IV Latency budget sacred | Spec 003 reuses spec 002's CameraViewer composite — no regression on click-to-first-frame. The new SignalR-driven revocation path is an *operator-action latency* (≤ 1 s) — not the 800 ms event-to-overlay budget, which only applies to the overlay path (spec 004+). PR will report measured archive-to-force-disconnect from the integration test. | ✅ |
| §V Spec-driven | Spec exists (PR #201). This plan exists. Tasks follow. | ✅ |
| §VI Aspire is composition root | `layout-composition` is a new Aspire project resource with `WithReference(postgres)` and `WithReference(rabbitmq)`. `layout-composition-db` is a `postgres.AddDatabase(...)` per-context resource. SignalR hub lives inside the same API project. | ✅ |
| §VII Observability mandatory | Layout state-machine transitions logged via `ILogger<T>` with structured fields `{ layoutIdentifier, revisionNumber, fromState, toState }`. OpenTelemetry traces span the `ArchiveLayoutRevisionCommand → outbox commit → SignalR broadcast` path. SignalR connection lifecycle (connected / disconnected / reconnected) instrumented for kiosk-health dashboards. | ✅ |
| §VIII Safe at trust boundaries | `[Authorize(Policy = "admin")]` on `/layouts/*` endpoints and on the SignalR hub. The hub's `OnConnectedAsync` rejects connections without the `sse.management` scope. Validation rejects malformed input at the API edge AND at value-object constructors. | ✅ |
| §IX Forward-compatible interfaces | `ICommandHandler<,>` / `IQueryHandler<,>` stay framework-agnostic; Wolverine dispatcher behind them (per ADR-0057). `ILayoutLifecycleBroadcaster` interface lives in `LayoutComposition.Domain` so a future swap from SignalR to a different push transport (per ADR-0076) is a single-class change. | ✅ |

**Result:** No constitutional violations. No Complexity Tracking
entries needed.

## Project Structure

### Documentation (this feature)

```
specs/003-layout-composition/
├── spec.md          ← Phase 1 (PR #201)
├── plan.md          ← this file (Phase 2)
└── tasks.md         ← Phase 3 (next; created by /speckit-tasks)
```

### Source Code — files added / modified

```
src/LayoutComposition/Domain/                          ← scaffold exists; populated here
└── Layout/                                             ← new aggregate folder (ADR-0092)
    ├── Layout.cs                                       ← aggregate root (logical chain)
    ├── LayoutIdentifier.cs                             ← Guid v7-backed IStronglyTypedId<Guid>
    ├── LayoutName.cs                                   ← string VO (≤ 80 chars, non-empty, no newlines)
    ├── Revision.cs                                     ← entity inside the aggregate
    ├── LayoutRevisionIdentifier.cs                     ← Guid v7-backed IStronglyTypedId<Guid>
    ├── LayoutRevisionNumber.cs                         ← int-backed VO (≥ 1, monotonic within chain)
    ├── LayoutRevisionState.cs                          ← enum-backed VO: Draft|Published|Archived
    ├── ILayoutRepository.cs                            ← domain repository contract
    ├── ILayoutLifecycleBroadcaster.cs                  ← domain abstraction over SignalR
    └── Events/
        ├── LayoutRevisionPublishedDomainEvent.cs       ← in-process
        └── LayoutRevisionArchivedDomainEvent.cs

src/LayoutComposition/Application/
├── Commands/
│   ├── CreateLayoutDraftCommand.cs                     ← first revision of a new chain
│   ├── CreateLayoutDraftErrors.cs
│   ├── BranchDraftRevisionCommand.cs                   ← Edit Published → new Draft (N+1)
│   ├── BranchDraftRevisionErrors.cs
│   ├── EditDraftRevisionCommand.cs                     ← PATCH on Draft (name + camera)
│   ├── EditDraftRevisionErrors.cs
│   ├── PublishRevisionCommand.cs                       ← Draft → Published (auto-archives prior Published)
│   ├── PublishRevisionErrors.cs
│   ├── RevertRevisionCommand.cs                        ← Published → Draft
│   ├── RevertRevisionErrors.cs
│   ├── ArchiveRevisionCommand.cs                       ← Draft|Published → Archived
│   ├── ArchiveRevisionErrors.cs
│   └── Handlers/
│       ├── CreateLayoutDraftCommandHandler.cs
│       ├── BranchDraftRevisionCommandHandler.cs
│       ├── EditDraftRevisionCommandHandler.cs
│       ├── PublishRevisionCommandHandler.cs
│       ├── RevertRevisionCommandHandler.cs
│       └── ArchiveRevisionCommandHandler.cs
├── Queries/
│   ├── GetLayoutQuery.cs                               ← full chain by layoutIdentifier
│   ├── ListLayoutsQuery.cs                             ← state filter, paged
│   ├── ListLayoutsErrors.cs
│   └── Handlers/
│       ├── GetLayoutQueryHandler.cs
│       └── ListLayoutsQueryHandler.cs
├── EventHandlers/
│   ├── LayoutRevisionPublishedDomainEventHandler.cs    ← outbox → integration event + SignalR broadcast
│   └── LayoutRevisionArchivedDomainEventHandler.cs     ← same
└── DTOs/
    ├── LayoutDto.cs                                    ← { layoutIdentifier, name, currentRevisionNumber, state, revisions: [...] }
    └── LayoutRevisionDto.cs                            ← { revisionIdentifier, revisionNumber, state, cameraIdentifier, publishedAt?, archivedAt? }

src/LayoutComposition/Infrastructure/
├── LayoutCompositionInfrastructureModule.cs            ← AddLayoutCompositionInfrastructure()
├── LayoutCompositionPersistenceModule.cs               ← AddLayoutCompositionPersistence() (slim — Domain+EF only)
├── Persistence/
│   ├── LayoutCompositionDbContext.cs                   ← EF Core; Wolverine outbox tables included
│   ├── Configurations/
│   │   ├── LayoutConfiguration.cs                      ← IEntityTypeConfiguration<Layout>
│   │   └── RevisionConfiguration.cs                    ← owned-entity collection mapping
│   ├── LayoutRepository.cs                             ← ILayoutRepository impl
│   ├── LayoutCompositionMigrator.cs                    ← IMigrator implementation
│   └── DesignTimeDbContextFactory.cs
├── Broadcasting/
│   └── SignalRLayoutLifecycleBroadcaster.cs            ← ILayoutLifecycleBroadcaster impl
└── Migrations/
    └── <timestamp>_InitialLayoutComposition.cs

src/LayoutComposition/Api/
├── LayoutCompositionApiModule.cs                       ← AddLayoutCompositionApi() + handler registrations
├── LayoutEndpoints.cs                                  ← POST/PATCH/GET routes (FR-007)
├── LayoutLifecycleHub.cs                               ← SignalR Hub at /hubs/layouts
├── Requests/                                           ← CreateLayoutRequest, EditDraftRequest
└── Program.cs                                          ← AddLayoutCompositionInfrastructure + auth + endpoints + SignalR

src/Shared.Contracts/                                   ← cross-context (no project ref between contexts)
└── LayoutComposition/
    ├── LayoutRevisionPublishedV1.cs                    ← new versioned integration event (ADR-0073)
    └── LayoutRevisionArchivedV1.cs                     ← new versioned integration event

src/MigrationRunner/
└── Program.cs                                          ← +builder.AddLayoutCompositionPersistence();

src/AppHost/
└── AppHost.cs                                          ← add layout-composition-db database + layout-composition Api project + kiosk-web JS resource
                                                          + WithReference(layout-composition) on management-web and kiosk-web

apps/shared/src/
├── api/
│   ├── layouts.api.ts                                  ← new RTK Query slice (createLayoutDraft, branchDraftRevision, edit, publish, revert, archive, list, get)
│   └── layouts.schema.ts                               ← zod request/response shapes
├── realtime/
│   ├── layoutHub.ts                                    ← SignalR client wrapper (connect, subscribe, reconnect-and-reconcile callback)
│   └── index.ts
└── (no new UI primitives; CameraViewer + DataTable are unchanged from spec 002)

apps/management-web/src/
├── features/layouts/                                   ← NEW feature folder
│   ├── LayoutsPage.tsx                                 ← DataTable + state filter + action buttons (Publish/Revert/Archive/Edit)
│   ├── LayoutEditorDialog.tsx                          ← name + camera picker, used for new-chain and new-revision flows
│   ├── LayoutsPage.test.tsx
│   └── LayoutEditorDialog.test.tsx
├── app/
│   ├── store.ts                                        ← +layoutsApi.reducer + .middleware
│   └── router.tsx                                      ← +/layouts route
└── App.tsx                                             ← navigation link to Layouts

apps/kiosk-web/                                         ← NEW Vite app (mirrors apps/management-web structure)
├── package.json
├── tsconfig.json
├── vite.config.ts
├── index.html
├── postcss.config.js                                   ← shared tailwind config
├── tailwind.config.js
└── src/
    ├── main.tsx                                        ← OIDC provider + redux Provider + RouterProvider
    ├── App.tsx
    ├── app/
    │   ├── store.ts                                    ← layoutsApi + streamsApi + camerasApi (read-only)
    │   ├── router.tsx                                  ← / → picker, /layouts/{id} → cell view
    │   └── auth.ts                                     ← OIDC config (different client ID, same realm)
    ├── features/
    │   ├── picker/
    │   │   ├── PickerPage.tsx                          ← lists Published layouts; tap to navigate
    │   │   ├── PickerPage.test.tsx
    │   │   └── usePublishedLayouts.ts                  ← layoutsApi + SignalR subscription
    │   ├── cell/
    │   │   ├── CellPage.tsx                            ← <CameraViewer cameraIdentifier=... />; force-disconnect on revocation
    │   │   └── CellPage.test.tsx
    │   └── revocation/
    │       └── useLayoutLifecycle.ts                   ← SignalR hooks; reconnect+reconcile
    ├── App.test.tsx                                    ← smoke test (same pattern as management-web)
    └── styles/                                         ← tailwind entry CSS

tests/LayoutComposition.Domain.Tests/                   ← new test project (ADR-0063 per-feature)
├── Layout/
│   ├── LayoutTests.cs                                  ← chain invariant; revision-number monotonicity
│   ├── LayoutRevisionStateMachineTests.cs              ← every transition (allowed + forbidden)
│   ├── LayoutNameTests.cs
│   ├── LayoutRevisionNumberTests.cs
│   └── Builders/
│       └── LayoutBuilder.cs                            ← fluent per ADR-0054

tests/LayoutComposition.Application.Tests/              ← new test project
├── Commands/
│   ├── CreateLayoutDraftCommandHandlerTests.cs         ← happy path + name-collision
│   ├── BranchDraftRevisionCommandHandlerTests.cs       ← spawns N+1
│   ├── EditDraftRevisionCommandHandlerTests.cs
│   ├── PublishRevisionCommandHandlerTests.cs           ← atomic swap with prior Published
│   ├── RevertRevisionCommandHandlerTests.cs
│   └── ArchiveRevisionCommandHandlerTests.cs
├── Queries/
│   ├── GetLayoutQueryHandlerTests.cs
│   └── ListLayoutsQueryHandlerTests.cs
├── EventHandlers/
│   ├── LayoutRevisionPublishedDomainEventHandlerTests.cs   ← integration event + broadcast both fire
│   └── LayoutRevisionArchivedDomainEventHandlerTests.cs
└── Fakes/
    ├── InMemoryLayoutRepository.cs
    ├── FakeLayoutLifecycleBroadcaster.cs                ← records broadcast calls
    └── FakeClock.cs

tests/Integration.Tests/LayoutComposition/              ← new directory; reuses AspireFixture
├── LayoutLifecycleIntegrationTests.cs                  ← end-to-end: create draft → publish → list shows it → archive → list omits
├── SignalRRevocationIntegrationTests.cs                ← connect 2 SignalR clients; archive; both receive within 1 s
├── ReconnectReconcileIntegrationTests.cs               ← disconnect a client; archive; reconnect; assert force-disconnect within 5 s
└── EditRevisionIntegrationTests.cs                     ← publish N=1; branch + publish N=2; assert N=1 auto-archived

tests/Integration.Tests/Fixtures/
└── AspireFixture.cs                                    ← MODIFIED: wait for layout-composition + HttpClient + SignalR test-client helper

tests/Architecture.Tests/
└── BoundaryTests.cs                                    ← MODIFIED: add LayoutComposition.Domain to no-Microsoft.AspNetCore.SignalR rule
                                                          (existing no-EF / no-Wolverine / no-cross-context rules cover automatically via wildcards)

tests/Shared.Contracts.Tests/
├── LayoutRevisionPublishedV1Tests.cs                    ← positional ctor + IIntegrationEvent + JSON round-trip
└── LayoutRevisionArchivedV1Tests.cs

scripts/
└── coverage-check.ps1                                  ← MODIFIED: add LayoutComposition.Domain >= 90% and Application >= 80%
```

**Structure Decision:** Backend follows the per-aggregate Domain
folder layout (ADR-0092) and the per-message-kind Application layout
(ADR-0093). The Persistence/Infrastructure split mirrors specs 001-002
exactly — MigrationRunner consumes only persistence; the API consumes
the full stack including Wolverine and the SignalR hub. Frontend
introduces `apps/kiosk-web/` as a sibling of `apps/management-web/`
per ADR-0074 (two-app split). Shared types/components live in
`apps/shared/` and both apps consume them via the workspace package.

## Backend Design

### Domain layer

Single aggregate. The chain is the aggregate boundary; revisions are
sub-entities owned by the aggregate so the *at-most-one-Published*
invariant lives inside the aggregate's transaction.

```csharp
public sealed class Layout : AggregateRoot<LayoutIdentifier>
{
    private readonly List<Revision> _revisions = new();

    public LayoutName Name { get; private set; } = default!;
    public IReadOnlyList<Revision> Revisions => _revisions.AsReadOnly();
    public DateTimeOffset CreatedAt { get; private set; }
    public OperatorIdentifier CreatedBy { get; private set; }

    private Layout() { }

    public static Layout CreateDraft(
        LayoutName name,
        CameraIdentifier camera,
        OperatorIdentifier createdBy,
        IClock clock)
    {
        var now = clock.UtcNow;
        var layout = new Layout
        {
            Id = LayoutIdentifier.New(),
            Name = name,
            CreatedAt = now,
            CreatedBy = createdBy,
        };
        layout._revisions.Add(Revision.NewDraft(LayoutRevisionNumber.One, camera, now, createdBy));
        return layout;
    }

    public Revision BranchDraft(OperatorIdentifier by, IClock clock)
    {
        var current = CurrentPublished()
            ?? throw new InvalidOperationException("can only branch from a Published revision");
        var next = LayoutRevisionNumber.From(_revisions.Max(r => r.Number.Value) + 1);
        var draft = Revision.Branch(next, current.Camera, clock.UtcNow, by);
        _revisions.Add(draft);
        return draft;
    }

    public void Publish(LayoutRevisionNumber number, OperatorIdentifier by, IClock clock)
    {
        var prior = CurrentPublished();
        Revision target = RequireRevision(number);
        target.Publish(clock.UtcNow);
        prior?.Archive(clock.UtcNow);   // atomic swap inside the same transaction (FR-003 + SC-003)
        Raise(new LayoutRevisionPublishedDomainEvent(Id, number, Name, target.Camera, clock.UtcNow, by));
        if (prior is not null)
            Raise(new LayoutRevisionArchivedDomainEvent(Id, prior.Number, clock.UtcNow, by));
    }

    public void Revert(LayoutRevisionNumber number, OperatorIdentifier by, IClock clock) { /* state guard */ }
    public void EditDraft(LayoutRevisionNumber number, CameraIdentifier camera, IClock clock) { /* state guard */ }
    public void ArchiveRevision(LayoutRevisionNumber number, OperatorIdentifier by, IClock clock) { /* state guard */ }

    private Revision? CurrentPublished() => _revisions.SingleOrDefault(r => r.State == LayoutRevisionState.Published);
    private Revision RequireRevision(LayoutRevisionNumber n) =>
        _revisions.SingleOrDefault(r => r.Number == n) ?? throw new InvalidOperationException("...");
}
```

The aggregate's `SingleOrDefault` on `CurrentPublished()` is the
ground truth for the *at-most-one-Published* invariant. A partial
unique index in Postgres backs it at the DB layer as a belt-and-
braces measure (defined in the EF configuration).

State transitions enforced inside `Revision`:

| From | To | Trigger |
|---|---|---|
| (none) | Draft | `Layout.CreateDraft` / `Layout.BranchDraft` |
| Draft | Published | `Revision.Publish` (caller: `Layout.Publish`) |
| Draft | Archived | `Revision.Archive` (= abandon a draft) |
| Published | Draft | `Revision.Revert` |
| Published | Archived | `Revision.Archive` (auto, when a new revision publishes) |
| any | any other | invalid → throws `InvalidOperationException`; handler returns `Result.Failure` |

### Value objects

- `LayoutIdentifier`: `readonly record struct LayoutIdentifier(Guid Value) : IStronglyTypedId<Guid>`. `New()` returns `Guid.CreateVersion7()`.
- `LayoutRevisionIdentifier`: same shape; unique per revision (not just per chain).
- `LayoutName`: `sealed record LayoutName : StringValueObject`. `Ensure.That(value).IsNotNullOrWhiteSpace().HasMaxLength(80).Satisfies(NoNewlines, ...).AndReturn()`.
- `LayoutRevisionNumber`: `readonly record struct LayoutRevisionNumber : IValueObject<int>`. `Ensure.That(value).IsGreaterThanOrEqual(1)`. Static factory `One` for clarity.
- `LayoutRevisionState`: enum-backed VO (`Draft|Published|Archived`); static `From(string)` for EF Core conversion.

### Application layer

Each command handler:
1. Loads the aggregate by `LayoutIdentifier` (or by `Name` for
   create-new-chain, with a uniqueness check).
2. Invokes the aggregate method that performs the transition.
3. Calls `IUnitOfWork.SaveAsync` — the EF transaction commits the
   aggregate change, the raised domain event (consumed in-process by
   the `LayoutRevisionPublishedDomainEventHandler`), and the
   Wolverine outbox row.
4. Returns `Result.Success(dto)` or surfaces an `ApiError`.

`LayoutRevisionPublishedDomainEventHandler` does two things in
sequence:
1. Publish `LayoutRevisionPublishedV1` via `IEventBus` (Wolverine
   outbox; RabbitMQ at-least-once).
2. Call `ILayoutLifecycleBroadcaster.PublishedAsync(...)` — the
   broadcast is best-effort by design (kiosks reconcile on reconnect
   if a push is missed; FR-012).

`LayoutRevisionArchivedDomainEventHandler` mirrors the same pattern.

### Wolverine subscription wiring

No inbound integration events in v1. The standard
`AddWolverineForContext<LayoutCompositionDbContext>` is wired in
infrastructure with the LayoutComposition.Application assembly
included for handler discovery (the same `configureMore` pattern
that PR #194 introduced; tracked separately by #200 for a convention-
based fix).

### SignalR design

```csharp
[Authorize(Policy = AuthenticationDefaults.AdminPolicy)]
public sealed class LayoutLifecycleHub : Hub<ILayoutLifecycleClient> { }

public interface ILayoutLifecycleClient
{
    Task LayoutRevisionPublished(LayoutRevisionPublishedNotification notification);
    Task LayoutRevisionArchived(LayoutRevisionArchivedNotification notification);
}
```

- **Authentication:** SignalR uses the same `JwtBearerHandler` as the
  HTTP API; the bearer token is supplied via the WebSocket
  query-string (`?access_token=...`) per the SignalR docs (auth
  middleware re-binds it to the HTTP `Authorization` header for the
  hub negotiation).
- **Authorization:** Hub-level `[Authorize]` requires the
  `sse.management` scope (same as the HTTP API).
- **Broadcast scope:** v1 broadcasts to all connected clients
  (`Clients.All`). Refinement to per-layout SignalR Groups so that
  only kiosks rendering layout X get pushes for layout X is deferred
  to a later spec — the broadcast cost at walking-skeleton scale
  (≤ 5 kiosks, ≤ 1 event per second peak) is trivial.
- **Reconnect handling:** SignalR's JS client handles reconnects
  natively. On `onreconnected`, the kiosk re-fetches
  `GET /layouts?state=published` and reconciles — if the rendered
  layout is no longer in the list, it force-disconnects to the
  picker (FR-012).

### Persistence schema

```sql
CREATE TABLE layouts (
  layout_id     uuid PRIMARY KEY,
  name          varchar(80) NOT NULL,
  created_at    timestamptz NOT NULL,
  created_by    uuid NOT NULL,
  version       integer NOT NULL                      -- ADR-0043 optimistic concurrency
);

CREATE TABLE layout_revisions (
  revision_id       uuid PRIMARY KEY,
  layout_id         uuid NOT NULL REFERENCES layouts(layout_id) ON DELETE CASCADE,
  revision_number   integer NOT NULL,
  state             varchar(16) NOT NULL,             -- 'Draft' | 'Published' | 'Archived'
  camera_id         uuid NOT NULL,
  created_at        timestamptz NOT NULL,
  created_by        uuid NOT NULL,
  published_at      timestamptz NULL,
  archived_at       timestamptz NULL,
  UNIQUE (layout_id, revision_number)
);

-- at-most-one Published per chain (FR-002 belt-and-braces)
CREATE UNIQUE INDEX ux_layout_revisions_one_published
  ON layout_revisions (layout_id)
  WHERE state = 'Published';

-- name uniqueness across non-archived chains (FR-006)
-- a chain is "non-archived" iff it has any revision NOT in 'Archived'
CREATE OR REPLACE FUNCTION layouts_has_active_revision(p_layout_id uuid)
RETURNS boolean LANGUAGE sql STABLE AS $$
  SELECT EXISTS (SELECT 1 FROM layout_revisions WHERE layout_id = p_layout_id AND state <> 'Archived');
$$;

CREATE UNIQUE INDEX ux_layouts_name_active
  ON layouts (lower(name))
  WHERE layouts_has_active_revision(layout_id);
```

> The function-backed partial index is the cleanest way to encode
> "name unique across non-archived chains" in Postgres. The
> alternative — a denormalized `is_archived` column on the chain
> updated by an aggregate hook — is also acceptable and the migration
> author can pick whichever is more idiomatic for our EF Core setup.

### EF Core mapping

`Layout` is mapped with `OwnsMany(l => l.Revisions, b => b.ToTable("layout_revisions"))` so the aggregate is loaded as a single graph. Camera, name, revision-state, revision-number all use the existing `HasConversion(...)` pattern from specs 001-002. The unique indexes are configured in `LayoutConfiguration.cs`.

## Frontend Design

### management-web (additive)

- New route `/layouts` registered in `apps/management-web/src/app/router.tsx`.
- `LayoutsPage.tsx` lists all chains (paged 50 per page like CamerasPage) with state filter chips. Each row shows:
  - Name + chain-state summary (e.g. "v2 Published; v1 Archived; v3 Draft")
  - Action buttons per visible revision: **Publish** (on Draft), **Revert** (on Published), **Archive** (on either), **Edit** (on Published → opens branch dialog; on Draft → opens edit dialog)
- `LayoutEditorDialog.tsx` is one component reused for the new-chain flow (no `layoutIdentifier`) and the new-revision-of-existing-chain flow (carries the chain identifier, copies values from the current revision into the form). Submission calls either `useCreateLayoutDraftMutation` or `useBranchDraftRevisionMutation`.
- Camera picker inside the dialog uses the existing `useListCamerasQuery` from spec 002.

### kiosk-web (new app)

- Vite + React + TypeScript + Tailwind, exact mirror of management-web's tooling. No new build infra.
- `vite.config.ts` proxies `/api/*` and `/hubs/*` to the same Aspire-injected URLs as management-web.
- OIDC config in `apps/kiosk-web/src/app/auth.ts`: Keycloak realm `smart-sentinel-eye`, client ID `smart-sentinel-eye-kiosk` (new Keycloak client, **public client + PKCE**, redirect URI `<kiosk-url>/oidc/callback`). Same `sse.management` scope.
- Routing:
  - `/` → `PickerPage`
  - `/layouts/:layoutIdentifier` → `CellPage`
  - `/oidc/callback` → OIDC redirect handler (provided by `react-oidc-context`)
- `PickerPage` calls `useListLayoutsQuery({ state: "published" })` and subscribes to `LayoutRevisionPublished` / `LayoutRevisionArchived` via the SignalR hook to live-update the list.
- `CellPage` reads `layoutIdentifier` from the route, calls `useGetLayoutQuery`, finds the Published revision's `cameraIdentifier`, and renders `<CameraViewer cameraIdentifier={...} />` (the spec 002 composite, used unchanged). On a SignalR `LayoutRevisionArchived` for this layout, the page navigates back to `/`.
- `useLayoutLifecycle` hook: wraps `@microsoft/signalr` client. Connects with the OIDC access token; exposes `onPublished` / `onArchived` callbacks; on `onreconnected` re-runs the published-layouts query (FR-012 reconciliation).

### Shared package additions

- `layouts.api.ts`: RTK Query slice with one endpoint per HTTP route (list, get, create, branch, edit, publish, revert, archive). Tag types: `Layout` (per-layout) + `LayoutList`.
- `realtime/layoutHub.ts`: SignalR client wrapper. Exposes:
  - `connect(accessTokenFactory)` — returns a `HubConnection` already started.
  - `subscribePublished(handler)` / `subscribeArchived(handler)` — typed callbacks.
  - `onReconnected(handler)` — for the reconciliation path.

## Cross-cutting

### NetArchTest rules added

| Rule | Why |
|---|---|
| `LayoutComposition.Domain` MUST NOT reference `Microsoft.AspNetCore.SignalR.*` | Domain stays framework-free; broadcaster is abstracted behind `ILayoutLifecycleBroadcaster` |
| `LayoutComposition.Domain` MUST NOT reference `Microsoft.EntityFrameworkCore.*` | Same NRT-free / no-infra rule as other domains |
| `LayoutComposition.Domain` MUST NOT reference `LayoutComposition.Infrastructure.*` or `LayoutComposition.Api.*` | Layer direction |
| `LayoutComposition.*` MUST NOT reference `CameraCatalog.*` or `StreamDistribution.*` | Cross-context isolation (ADR-0027) |

These are added to `tests/Architecture.Tests/BoundaryTests.cs`; the
existing wildcards (`*.Domain*` patterns) cover most of this
automatically — only the SignalR rule needs an explicit assembly
addition.

### Coverage gates

`scripts/coverage-check.ps1` adds:

```pwsh
'SmartSentinelEye.LayoutComposition.Domain'      = 90
'SmartSentinelEye.LayoutComposition.Application' = 80
```

`Shared.Contracts` gate stays at 90 % — the two new `LayoutRevision*V1`
integration-event records get unit tests in
`tests/Shared.Contracts.Tests/` parallel to the `StreamHealthChangedV1`
tests added in PR #196.

### Test strategy

**Domain tests:** state-machine matrix per `Revision`, chain
invariants per `Layout` (at-most-one-Published, monotonic revision
numbers, valid transitions, atomic swap on publish), VO validation.

**Application tests:** each handler tested against
`InMemoryLayoutRepository` + `FakeLayoutLifecycleBroadcaster` +
`FakeClock`. Idempotency, conflict (`409`), and the cross-handler
edge case "publish revision N+1 archives revision N in the same UoW."

**Integration tests:** four scenarios listed in the project tree
above. The SignalR integration tests use the official
`HubConnectionBuilder` against the Aspire-resolved hub URL with a
real Keycloak-issued access token from the existing
`AspireFixture.GetAccessTokenAsync` helper. The reconcile-on-
reconnect test deliberately drops the connection mid-test.

## Risk register

| # | Risk | Mitigation |
|---|---|---|
| 1 | SignalR auth via WebSocket query-string is a deviation from the bearer-header pattern used by HTTP. | Standard pattern documented by Microsoft; spec 002's bearer JWT validator is reusable verbatim. Integration test asserts the 401 path. |
| 2 | The "name unique across non-archived chains" partial index is non-trivial. | Plan calls it out explicitly; migration author can choose between function-backed index or denormalized `archived` boolean. Either passes the unit-test for FR-006. |
| 3 | Atomic swap on publish (FR-003 + SC-003) — the partial unique index forces it. Without the right transaction shape EF will fail the constraint. | Aggregate orders the work: archive prior first, then publish target. EF tracks both as one `SaveChangesAsync`. Integration test verifies. |
| 4 | Best-effort SignalR push could leave a kiosk "stuck" if the reconciliation hook fails too. | Two safety nets: SignalR's native reconnect + the explicit `onreconnected` callback that re-runs the published-layouts query. Integration test exercises the dropped-connection path. |
| 5 | Kiosk-web is a brand-new app; CI doesn't yet build it. | Aspire AppHost adds the kiosk-web JS resource the same way management-web is wired; CI's `pnpm -r build` picks it up via the workspace. PR will report `pnpm -r build` output. |
| 6 | Convention-based handler discovery from #200 is not done yet — each new context still needs `configureMore`. | Plan accepts the same opt-in as specs 001-002; doesn't block #200. The infrastructure module includes the Application assembly via `configureMore` per the established pattern. |

## Phase hand-off

When the user approves this plan, `/speckit-tasks` will:

- Generate atomic tasks for each layer (Domain → Application → Infrastructure → Api → Frontend → Integration tests).
- Tag with `phase:us1` … `phase:us4` per the spec's user-story bucket and `phase:polish` for the cross-cutting items (coverage gate config, README quickstart).
- Surface the migration-author choice (function-backed partial index vs denormalized `archived` flag) as a single decision task before the migration is scaffolded.
- Bundle into the same A-B-C-D-E-F-G PR sequence as spec 002:
  - **PR A:** AppHost wiring + layout-composition project scaffolds + Keycloak kiosk client.
  - **PR B:** Domain layer + per-aggregate tests.
  - **PR C:** Application + Infrastructure layers + EF migration.
  - **PR D:** Api endpoints + SignalR hub + management-web Layouts page.
  - **PR E:** kiosk-web Vite app + picker + cell view + SignalR client.
  - **PR F:** Integration tests (Aspire fixture extension + SignalR test client + full lifecycle).
  - **PR G:** Polish — coverage gates, NetArchTest additions, README quickstart, manual verification gate.

## Open clarifications

None — all design decisions are locked. Ready for Phase 3 (Tasks).
