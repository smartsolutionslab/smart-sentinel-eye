# Implementation Plan: 010 — Multi-tile layouts (video wall)

**Branch:** `010-multi-tile-layouts` | **Date:** 2026-06-04 | **Spec:** [spec.md](./spec.md)

**Status:** Draft (Phase 2 — Plan)

**Input:** Feature specification `specs/010-multi-tile-layouts/spec.md`
(Phase 1 closed; six clarifications resolved at the gate) and
[ADR-0112](../../docs/adr/0112-multi-tile-layouts.md) (Accepted). Both are
**finalized** — `MaxTiles = 4` (2×2), clean V2 contract cut, overlay-reuse →
highlight-all, full designer (US4 in scope), explicit `rows × cols` grid, sparse
cells allowed.

## Summary

Extend the **existing** `Layout` aggregate (spec 003) so a `Revision` stops
carrying a single `Camera` + optional `Overlay` and instead owns a non-empty,
in-bounds set of **`Tile`s** plus a **`GridDimensions`**. No new aggregate
(ADR-0112 §1; ADR-0104 rule-of-three not triggered — only the revision *payload*
changes). The lifecycle (Draft→Published→Archived, branch, revert,
at-most-one-Published, optimistic concurrency) is **byte-for-byte unchanged**.

Five concrete shifts, all in `LayoutComposition` + the two SPAs + Audit:

1. **Domain:** add `Tile`, `GridPosition`, `GridDimensions` value objects; the
   `Revision` payload + the four aggregate write methods (`CreateDraft`,
   `BranchDraft`, `EditDraft`, `Publish`) move from `(camera, overlay?)` to
   `(tiles, grid)`; the grid invariants live inside the aggregate as
   `Result`-mapped errors.
2. **Contracts (clean V2 cut):** replace `LayoutRevisionPublishedV1` with
   `LayoutRevisionPublishedV2` (tile set + grid); update the only subscriber
   (`IntegrationEventAuditHandler`) to V2; `LayoutRevisionArchivedV1` and
   `OverlayHighlightRequestedV1` unchanged.
3. **Persistence:** new EF owned table `layout_revision_tiles` + `grid_rows` /
   `grid_cols` columns on `layout_revisions`; one migration that **adds → backfills
   the legacy `camera_id`/`overlay_id` into a `(0,0)` tile on a 1×1 grid → drops the
   legacy columns**, via `MigrationRunner` (ADR-0067).
4. **management-web:** evolve the create dialog into a **grid designer** (grid
   resize, per-tile camera/overlay pickers, sparse cells, edit-after-publish via
   branch draft) — RHF + Zod (ADR-0079), Radix + Tailwind (ADR-0077/0078).
5. **kiosk-web:** evolve `CellPage` into a CSS-grid renderer (N=1 is today's
   single cell, unchanged); subscribe to the existing `OverlayHighlightChanged`
   SignalR frame (net-new on the frontend) and apply `ssE-overlay-highlight` to
   **every tile bound to the matching overlay** (ADR-0112 §5).

The highlight backend leg (`OverlayHighlightRequestedV1Handler` → SignalR
`OverlayHighlightChanged` via `SignalRLayoutLifecycleBroadcaster.OverlayHighlightedAsync`)
is unchanged; only the kiosk consumer is new.

## Ground truth verified against the code

| Claim | Evidence |
|---|---|
| `Revision` binds one `Camera` + nullable `Overlay` today | `src/LayoutComposition/Domain/Layout/Revision.cs:20,27`; mutators `EditCamera`, `AttachOverlay` |
| Lifecycle lives on the aggregate, revisions are owned sub-entities | `src/LayoutComposition/Domain/Layout/Layout.cs` (`Publish`/`BranchDraft`/`EditDraft`/`Revert`/`ArchiveRevision`) |
| `Publish` raises `LayoutRevisionPublishedDomainEvent(..., Camera, ...)` | `Layout.cs:118`; event `Events/LayoutRevisionPublishedDomainEvent.cs:11` |
| Domain → integration via `LayoutRevisionPublishedDomainEventHandler` | `Application/EventHandlers/LayoutRevisionPublishedDomainEventHandler.cs:32` publishes `LayoutRevisionPublishedV1` |
| `LayoutRevisionPublishedV1` carries a single `Camera: Guid` | `src/Shared.Contracts/LayoutComposition/LayoutRevisionPublishedV1.cs:14` |
| Audit subscribes V1 via a concrete `Handle(LayoutRevisionPublishedV1 …)` | `src/AuditObservability/Application/EventHandlers/IntegrationEventAuditHandler.cs:42` |
| EF: `Revision` owned; `camera_id` required, `overlay_id` nullable | `Infrastructure/Persistence/Configurations/LayoutConfiguration.cs:63-95` |
| Migration shape precedent (`AddColumn`/`DropColumn`) | `Infrastructure/Persistence/Migrations/20260527133857_AddLayoutOverlayBinding.cs` |
| DTOs expose scalar `cameraIdentifier`/`overlayIdentifier` | `Application/DTOs/LayoutDto.cs:26,28,40,41` |
| Highlight backend leg already broadcasts `OverlayHighlightChanged` | `Infrastructure/Broadcasting/SignalRLayoutLifecycleBroadcaster.cs:99-110` |
| Frontend does **not** subscribe to `OverlayHighlightChanged` | `apps/shared/src/realtime/layoutHub.ts` has no `OverlayHighlightChanged` handler; `CellPage.tsx` does not consume it |
| `ssE-overlay-highlight` consumer does not exist in `apps/` | Grep across `apps/` returns no match — net-new frontend work |
| `CellPage` reads `published.cameraIdentifier`/`overlayIdentifier` | `apps/kiosk-web/src/features/cell/CellPage.tsx:39-40,169` |
| `layouts.api.ts` exposes scalar camera/overlay on the FE types | `apps/shared/src/api/layouts.api.ts:13-18,32-37` |
| e2e `layouts.spec.ts` covers read + create with scalar pickers | `e2e/layouts.spec.ts:65-66` selects `#layout-camera` / `#layout-overlay` |

## Technical Context

| Concern | Decision | Source |
|---|---|---|
| Backend | C# / .NET 10; EF Core CRUD (not Marten) | ADR-0024, ADR-0009, ADR-0071, ADR-0112 §2 |
| Aggregate | extend `Layout` — no new aggregate | ADR-0112 §1, ADR-0104 |
| Value objects | hand-written `Tile`/`GridPosition`/`GridDimensions`, `Ensure.That(...)` + `.From(...)` | ADR-0038/0046/0066/0105 |
| IDs | reuse `CameraIdentifier`/`OverlayIdentifier` (existing typed records) | ADR-0039/0090 |
| Nulls | NRT-off; `Option<OverlayIdentifier>` on `Tile`; nullable `overlay_id` column | ADR-0048 |
| Errors | `Result<T, Error>`; `ApiError(Code, Message, HttpStatusCode)` cases | ADR-0047/0089 |
| Grid cap | `MaxTiles = 4` / `MaxCells = 4`, default 2×2 — constants on `GridDimensions` | ADR-0112 §4 |
| Contracts | clean **V2 cut**; `LayoutRevisionPublishedV2` replaces V1; `V<N>` suffix | ADR-0073/0040, ADR-0112 §3 |
| Migration | EF migration via `MigrationRunner`; add → backfill → drop legacy columns, one migration | ADR-0067, ADR-0112 §3 |
| Messaging | Wolverine; per-module queue isolation; Postgres outbox | ADR-0042/0088 |
| Real-time | SignalR `OverlayHighlightChanged` (existing backend frame) | ADR-0076 v1, ADR-0112 §5 |
| Frontend | React + TS + Vite, two apps; RTK Query; RHF + Zod; Radix + Tailwind | ADR-0074/0075/0079/0077/0078 |
| Tests | xUnit + Shouldly + Moq + fakes; AspireFixture; Playwright e2e | ADR-0052/0103/0108 |
| Logging | `ILogger<T>` + `[LoggerMessage]` source-gen | ADR-0050 |
| Metrics | ≤300 LOC/file, ≤30 LOC/method, ≤4 params, complexity ≤10, depth ≤3 | ADR-0084 |

## Constitution Check

| Principle | Check | Status |
|---|---|---|
| §II DDD + value objects | `Tile`, `GridPosition`, `GridDimensions` are maximalist hand-written VOs; the grid invariants (≥1 tile, no dup position, in-bounds, ≤4) live inside the `Layout` aggregate transaction; primitives never cross the domain boundary | ✅ |
| §III Bounded-context isolation | all work in `SmartSentinelEye.LayoutComposition.*` + `Shared.Contracts/LayoutComposition/`; no project ref to CameraCatalog/OverlayDesigner/StreamDistribution; the kiosk merges camera/overlay reads in the browser; NetArchTest unchanged and still green (FR-013, SC-007) | ✅ |
| §IV Latency budget sacred | wall decodes ≤4 simultaneous WHEP peers (one `<CameraViewer>` per tile); `MaxTiles = 4` is the §IV mitigation on the SFU→decode (≤120 ms) + composite+render (≤50 ms) legs; the highlight leg (event→overlay-state ≤200 ms) is byte-identical — only which DOM node(s) the existing frame targets changes client-side. Per-tile-decode note carried; Phase-5 measures click-to-first-frame per tile on a 2×2 wall | ✅ |
| §VI Aspire is composition root | no new runtime resources — same `layout-composition` Api + `layout-composition-db`; the V2 event ships on the existing LayoutComposition queues; `MigrationRunner` already references LayoutComposition persistence | ✅ |
| §VII Observability | `[LoggerMessage]` structured logs on the new grid-invariant rejections + publish path carry `{ layoutIdentifier, revisionNumber, tileCount, gridRows, gridCols }` | ✅ |
| §VIII Safe at trust boundaries | request DTOs are primitives; validation at the VO `.From(...)` boundary (parse → `400 LAYOUT_INVALID_INPUT`) and at the aggregate (invariants → `400` with the documented codes); `sse.layouts.write`/`read` unchanged (FR-008) | ✅ |
| §IX Forward-compat | no speculative generality — `MaxTiles = 4` is a domain invariant, not a config knob; a bigger wall is a future measured-decode ADR (ADR-0112 §4) | ✅ |

**Result:** No constitutional violations. No Complexity Tracking entries.

## Backend Design

### Domain layer — new value objects + reshaped `Revision`

New files under `src/LayoutComposition/Domain/Layout/` (one type per file, ADR-0092):

- **`GridDimensions.cs`** — `readonly record struct GridDimensions(int Rows, int Cols)`.
  Single source of truth for the cap (ADR-0112 §4 / Implementation Notes):
  ```
  public const int MaxTiles = 4;
  public const int MaxCells = 4;
  public static readonly GridDimensions Default = new(2, 2);   // designer default
  public static readonly GridDimensions Single = new(1, 1);    // N=1 / migrated layouts
  ```
  `From(rows, cols)` guards `rows ≥ 1`, `cols ≥ 1`, `rows * cols ≤ MaxCells` via
  `Ensure.That(...)`. A `Contains(GridPosition)` helper backs the in-bounds check.
- **`GridPosition.cs`** — `readonly record struct GridPosition(int Row, int Col)`;
  `From(row, col)` guards `row ≥ 0`, `col ≥ 0` (upper bound is checked against the
  owning revision's `GridDimensions`, not here — a position is meaningless without
  its grid).
- **`Tile.cs`** — `sealed record Tile(CameraIdentifier Camera, Option<OverlayIdentifier> Overlay, GridPosition Position)`.
  `Camera` required; `Overlay` is `Option<T>` (ADR-0048). No bounds logic here —
  the aggregate validates the whole tile set against the grid (a tile can't see its
  grid).

`Revision.cs` changes (lifecycle untouched, payload only):

- Replace `CameraIdentifier Camera` + `OverlayIdentifier? Overlay` with
  `GridDimensions Grid` + an owned `IReadOnlyList<Tile> Tiles` (backed by a private
  `List<Tile>`).
- `NewDraft` / `Branch` take `(grid, tiles)` instead of `(camera, overlay)`.
- Replace `EditCamera` + `AttachOverlay` with one package-internal
  `ReplaceTiles(GridDimensions grid, IReadOnlyList<Tile> tiles)` (state-guarded to
  Draft, same `InvalidOperationException` pattern as today). The whole tile set is
  replaced atomically — there is no per-tile mutator (a draft edit is "here is the
  new grid + tiles").

`Layout.cs` changes (method names + lifecycle preserved):

- `CreateDraft(name, grid, tiles, createdBy, clock)` — drops the `camera`/`overlay`
  params. Validates the tile set against the grid before constructing the first
  revision (returns the validation failure to the handler — see *Invariant
  placement* below).
- `BranchDraft` — copies `baseRevision.Grid` + `baseRevision.Tiles` into the new
  draft (was `baseRevision.Camera`/`Overlay`).
- `EditDraft(number, grid, tiles, clock)` — re-validates and calls
  `target.ReplaceTiles(grid, tiles)`.
- `Publish` — raises `LayoutRevisionPublishedDomainEvent(Id, number, Name,
  target.Grid, target.Tiles, now, by)` (event reshaped, below). The archive-prior +
  atomic-swap logic is unchanged.

`Events/LayoutRevisionPublishedDomainEvent.cs` — replace `CameraIdentifier Camera`
with `GridDimensions Grid` + `IReadOnlyList<Tile> Tiles`. `LayoutRevisionArchivedDomainEvent`
is unchanged.

**Invariant placement.** The grid invariants are validated as a
`Result<Unit, GridError>`-style check the aggregate exposes (e.g. a private static
`ValidateGrid(grid, tiles)` returning the first violation), surfaced to the command
handler which maps to the `400` codes. The aggregate must not throw for an operator
input error (those are `Result` failures, ADR-0047); it keeps throwing
`InvalidOperationException` only for *programmer* errors (illegal state transitions),
exactly as today. The four invariants (ADR-0112 §2):

| Invariant | Error code | HTTP |
|---|---|---|
| ≥ 1 tile | `LAYOUT_GRID_EMPTY` | 400 |
| no two tiles at the same `(row,col)` | `LAYOUT_TILE_POSITION_DUPLICATE` | 400 |
| every tile in-bounds (`0 ≤ row < rows`, `0 ≤ col < cols`) | `LAYOUT_TILE_OUT_OF_BOUNDS` | 400 |
| `rows*cols ≤ 4` and populated tiles ≤ 4 | `LAYOUT_GRID_TOO_LARGE` | 400 |

### Application layer

- **`CreateLayoutDraftCommand`** — `Name`, `Grid: GridDimensions`,
  `Tiles: IReadOnlyList<Tile>`, `CreatedBy` (drops `Camera`/`Overlay`).
  `CreateLayoutDraftError` gains the four grid cases above (alongside the existing
  `LayoutNameTaken`).
- **`EditDraftRevisionCommand`** — `Layout`, `RevisionNumber`, `Grid`, `Tiles`
  (drops `Camera` + the tri-state `OverlayChange` — a multi-tile edit replaces the
  whole set, so the tri-state is obsolete; **`OverlayChange` is deleted**).
  `EditDraftRevisionError` gains the four grid cases.
- **`BranchDraftRevisionCommand`** — unchanged signature (branch copies the prior
  revision's grid+tiles inside the aggregate; no payload on the command).
- **DTOs (`LayoutDto.cs`)** — replace scalar `CameraIdentifier`/`OverlayIdentifier`
  on `LayoutRevisionDto` and `PublishedLayoutDto` with `GridRows`, `GridCols`, and
  `IReadOnlyList<TileDto>` where `TileDto(Guid CameraIdentifier, Guid? OverlayIdentifier,
  int Row, int Col)`. The legacy scalar fields are **removed** (FR-006).
- **Query handlers** — `GetLayoutQueryHandler.MapRevision` and
  `ListLayoutsQueryHandler` map `revision.Tiles`/`revision.Grid` into the new DTO
  shape (Published-picker projection now carries the tile set + grid).
- **`LayoutRevisionPublishedDomainEventHandler`** — publish `LayoutRevisionPublishedV2`
  (map `Tiles`/`Grid`); the best-effort SignalR `PublishedAsync` broadcast is kept
  (the `LayoutRevisionPublishedHubMessage` may keep a single representative
  camera/grid for the picker live-update, or carry the tile set — decision below).

> **Designer-task decision (surface in Tasks):** `LayoutRevisionPublishedHubMessage`
> /`LayoutRevisionPublishedNotification` currently carry a scalar `Camera`. The
> picker live-update only needs name + layout identifier to invalidate the list, so
> the simplest change keeps the hub message lean and lets the picker re-fetch via
> the existing `LayoutList` tag invalidation. Carrying the full tile set on the hub
> frame is unnecessary for v1 — call this out so the Tasks author picks the lean
> path (no tile payload on the lifecycle hub frame; the picker re-queries).

### API layer

- **`Requests/CreateLayoutRequest.cs`** — replace with
  `CreateLayoutRequest(string Name, GridRequest Grid, IReadOnlyList<TileRequest> Tiles)`
  where `GridRequest(int Rows, int Cols)` and `TileRequest(Guid CameraIdentifier,
  Guid? OverlayIdentifier, int Row, int Col)`. Primitives only at the boundary.
- **`Requests/EditDraftRequest.cs`** — replace with `EditDraftRequest(GridRequest Grid,
  IReadOnlyList<TileRequest> Tiles)`; delete `OverlayBindingUpdate`.
- **`LayoutEndpoints.Commands.cs`** — `CreateDraft` / `EditDraft` parse each
  `TileRequest` into a `Tile` (via `CameraIdentifier.From` / `OverlayIdentifier.From`
  / `GridPosition.From`) and `GridDimensions.From` inside the existing
  `try/catch (ArgumentException)` → `400 LAYOUT_INVALID_INPUT` block. Delete
  `TranslateOverlayChange`. The aggregate-level grid invariants surface through the
  command `Result` as the four `LAYOUT_*` codes via `error.ToProblem()`. Routes,
  scopes, and all other handlers (`Publish`/`Archive`/`Branch`/`Revert`) unchanged.

### Contracts (clean V2 cut)

- **Add** `src/Shared.Contracts/LayoutComposition/LayoutRevisionPublishedV2.cs`:
  ```
  LayoutRevisionPublishedV2(
      Guid Layout, int RevisionNumber, string Name,
      IReadOnlyList<LayoutTileV2> Tiles, int GridRows, int GridCols,
      DateTimeOffset PublishedAt, Guid PublishedBy, EventMetadata Metadata) : IIntegrationEvent
  ```
  with `LayoutTileV2(Guid Camera, Guid? Overlay, int Row, int Col)` (primitives at
  the wire, ADR-0040). **Delete** `LayoutRevisionPublishedV1.cs`.
- **Audit:** in `IntegrationEventAuditHandler.cs` replace the
  `Handle(LayoutRevisionPublishedV1 …)` entry with `Handle(LayoutRevisionPublishedV2 …)`
  (one line; the generic `AuditAsync` body is shape-agnostic). The architecture test
  `Every_integration_event_has_an_audit_handler` keeps this honest.
- `LayoutRevisionArchivedV1` and `OverlayHighlightRequestedV1` unchanged.

### Persistence + migration

`LayoutConfiguration.cs` — replace the scalar `Camera`/`Overlay` revision properties
with:
- `grid_rows` / `grid_cols` `int` columns on `layout_revisions` (mapped from
  `revision.Grid`), and
- a nested `OwnsMany(revision => revision.Tiles, …)` mapped to **`layout_revision_tiles`**
  `{ tile_id (PK, or composite key on revision_id+row+col), revision_id (FK), row,
  col, camera_id (required), overlay_id (nullable) }`, with `WithOwner().HasForeignKey("revision_id")`
  and a unique index on `(revision_id, row, col)`. (EF supports nested owned
  collections; the tile is a VO with no identity of its own, so a composite key on
  `(revision_id, row, col)` is the natural PK.)

The at-most-one-Published partial index and the `(layout_id, revision_number)`
unique index are unchanged.

**Migration (one file, via `MigrationRunner`, ADR-0067 / ADR-0112 §3):**
`<timestamp>_MultiTileLayouts.Up`:
1. `CreateTable("layout_revision_tiles", …)` with FK to `layout_revisions`.
2. `AddColumn grid_rows int NOT NULL default 1` and `grid_cols int NOT NULL default 1`
   on `layout_revisions` (default 1 so existing rows become a 1×1 grid).
3. **Backfill** (raw `migrationBuilder.Sql`): `INSERT INTO layout_revision_tiles
   (revision_id, row, col, camera_id, overlay_id) SELECT revision_id, 0, 0, camera_id,
   overlay_id FROM layout_revisions;` — every existing revision becomes one tile at
   `(0,0)` carrying its old camera + (nullable) overlay. Zero loss.
4. Drop the temporary defaults on `grid_rows`/`grid_cols` (they were only to satisfy
   the NOT NULL backfill).
5. **`DropColumn camera_id`** and **`DropColumn overlay_id`** from `layout_revisions`
   (clean cut — no read window).
`Down` reverses it (re-add columns, copy the `(0,0)` tile back, drop the tile table).
Regenerate `LayoutCompositionDbContextModelSnapshot`.

> The defaults-then-drop-defaults dance keeps the migration self-contained: AppHost
> dev DBs already hold spec-003/004 layouts, so the backfill must run before the
> columns are dropped, in the same migration (no two-deploy window — this is the
> clean-cut requirement, ADR-0112 §3).

## Frontend Design

### Shared seam (`apps/shared`)

- **`api/layouts.api.ts`** — the FE seam (see Risks). Replace scalar
  `cameraIdentifier`/`overlayIdentifier` on `LayoutRevision` and `PublishedLayout`
  with `gridRows`, `gridCols`, and `tiles: LayoutTile[]` where
  `LayoutTile { cameraIdentifier; overlayIdentifier: string | null; row; col }`.
  `createLayoutDraft` / `editDraftRevision` mutation bodies send `{ name?, grid:
  { rows, cols }, tiles: [...] }`.
- **`api/layouts.schema.ts`** — replace `createLayoutDraftSchema` with a multi-tile
  Zod schema: `grid: { rows: 1..2, cols: 1..2 }`, `tiles: array(tileSchema).min(1)`,
  with `superRefine` for the same four invariants the backend enforces (≥1 tile, no
  dup `(row,col)`, in-bounds, `rows*cols ≤ 4` and ≤4 populated). Add a shared
  `MAX_TILES = 4` const mirroring `GridDimensions.MaxTiles` (the one cross-tier
  duplication, justified — the browser validates before POST for inline feedback).
- **`realtime/layoutHub.ts`** — **add** `OverlayHighlightChangedMessage { overlay:
  string; durationMs: number }`, an `onOverlayHighlightChanged?` callback in
  `LayoutHubCallbacks`, and `connection.on('OverlayHighlightChanged', …)` wiring
  (mirrors the existing `OverlayRevisionPublished` block). Net-new, additive.

### management-web — grid designer (FR-010, US1 + US4)

`apps/management-web/src/features/layouts/`:
- Evolve **`LayoutEditorDialog.tsx`** into the grid designer (or add a
  `GridDesigner.tsx` it composes): a grid-size control (1×1 / 1×2 / 2×1 / 2×2 preset
  buttons, all derived from `MAX_TILES`), and a cell grid where each cell has a
  camera `<select>` (required) + overlay `<select>` (optional, "(none)"), reusing
  `useListCamerasQuery` + `useListOverlaysQuery('Published')` (unchanged). Empty
  cells allowed (sparse). RHF `useFieldArray` over the tile list; `zodResolver` on
  the new schema gives inline per-cell + grid-level errors. Submit calls
  `useCreateLayoutDraftMutation`.
- **US4 edit-after-publish:** an **Edit** affordance on a Published chain calls
  `useBranchDraftRevisionMutation` (creates Draft N+1 pre-filled server-side), then
  opens the designer pre-loaded with the branched draft's grid+tiles and submits via
  `useEditDraftRevisionMutation`. Reuses the spec 003 lifecycle wholesale (no new
  lifecycle UI).
- `LayoutsPage.tsx` row summary shows "N tiles, R×C" instead of a single camera.

### kiosk-web — grid renderer + per-tile highlight (FR-011/FR-012, US2 + US3)

`apps/kiosk-web/src/features/cell/CellPage.tsx` → grid renderer:
- Read `published.tiles` + `published.gridRows`/`gridCols`. Render a CSS grid
  (`grid-template-columns/rows` from the dimensions). For each populated cell render
  one `<CameraViewer>` (the spec 002 composite, **unchanged**, one per tile) with its
  per-tile overlay binding (the existing overlay/snapshot fetch logic becomes
  per-tile). Empty cells render a placeholder div. N=1 (incl. migrated 1-tile
  layouts) renders identically to today (SC-004, FR-011).
- **Per-tile highlight (US3, net-new):** subscribe via `useLayoutLifecycle`'s new
  `onOverlayHighlightChanged` to the `OverlayHighlightChanged` frame. On a frame,
  find **every** tile whose `overlayIdentifier === message.overlay` and apply the
  `ssE-overlay-highlight` class for `message.durationMs`, then auto-revert.
  Overlapping highlights on the same overlay are OR'd (track per-overlay expiry,
  matching the existing `OverlayHighlightRequestedV1` contract semantic). A highlight
  for an overlay bound to no tile is a no-op (no flash, no error).
- **`ssE-overlay-highlight` CSS** — net-new (Grep confirms it exists nowhere in
  `apps/`). Add the class to the kiosk Tailwind/global stylesheet (a brief
  outline/glow keyframe), referenced by the tile renderer.
- `apps/kiosk-web/src/features/revocation/useLayoutLifecycle.ts` — add the
  `onOverlayHighlightChanged` option + forward it to the hub client (mirrors the
  existing `onOverlayPublished` plumbing; uses the same `optionsRef` latest-value
  pattern). `PickerPage.tsx` unchanged except the published-list type now carries
  tiles (it only displays name).

Per-tile decode note (§IV): rendering ≤4 `<CameraViewer>` opens ≤4 WHEP peers;
`MaxTiles = 4` keeps this within kiosk-GPU HW-decode capacity — verified at Phase 5.

## Slice plan (parallel worktree dispatch, ADR-0109)

**S0 is the gate — it must land and merge before S1–S3 fan out.** S0 owns every
contention file (the Domain shapes, the V2 contract, the migration, the FE seam
types). S1/S2/S3 then run in parallel on disjoint files; S4 closes after S1+S3.

### S0 — Domain + Contracts + Migration + seam types (single-owner, foundational)

Lands the full backend payload reshape, the V2 contract, the EF migration, the
Audit update, **and** publishes the FE seam types in `layouts.api.ts` /
`layouts.schema.ts` / `layoutHub.ts` so S2/S3 compile against a stable seam.

**Owns (exclusive):**
- `src/LayoutComposition/Domain/Layout/Tile.cs`, `GridPosition.cs`,
  `GridDimensions.cs` (new)
- `src/LayoutComposition/Domain/Layout/Revision.cs`, `Layout.cs`,
  `Events/LayoutRevisionPublishedDomainEvent.cs`
- `src/LayoutComposition/Application/Commands/{CreateLayoutDraftCommand,
  CreateLayoutDraftErrors,EditDraftRevisionCommand,EditDraftRevisionErrors}.cs`
  (+ their `Handlers/*`); delete `OverlayChange` (in `EditDraftRevisionCommand.cs`)
- `src/LayoutComposition/Application/DTOs/LayoutDto.cs` (+ new `TileDto`)
- `src/LayoutComposition/Application/Queries/Handlers/{GetLayoutQueryHandler,
  ListLayoutsQueryHandler}.cs`
- `src/LayoutComposition/Application/EventHandlers/LayoutRevisionPublishedDomainEventHandler.cs`
- `src/Shared.Contracts/LayoutComposition/LayoutRevisionPublishedV2.cs` (new),
  **delete** `LayoutRevisionPublishedV1.cs`
- `src/AuditObservability/Application/EventHandlers/IntegrationEventAuditHandler.cs`
  (V1→V2 line)
- `src/LayoutComposition/Infrastructure/Persistence/Configurations/LayoutConfiguration.cs`
- `src/LayoutComposition/Infrastructure/Persistence/Migrations/*` (new migration +
  snapshot)
- `apps/shared/src/api/layouts.api.ts`, `apps/shared/src/api/layouts.schema.ts`,
  `apps/shared/src/realtime/layoutHub.ts` (publish the seam types only)
- Domain/Application/Contracts unit tests for the above

### S1 — Api (∥ S2, ∥ S3 after S0)

**Owns (exclusive):** `src/LayoutComposition/Api/Requests/CreateLayoutRequest.cs`,
`EditDraftRequest.cs` (delete `OverlayBindingUpdate`),
`src/LayoutComposition/Api/LayoutEndpoints.Commands.cs`,
`LayoutEndpoints.Queries.cs`; Api-level tests. Depends on S0's command/DTO shapes.

### S2 — management-web designer (∥ S1, ∥ S3 after S0)

**Owns (exclusive):** `apps/management-web/src/features/layouts/*`
(`LayoutEditorDialog.tsx` + new `GridDesigner.tsx`, `LayoutsPage.tsx`, their
`*.test.tsx`). Consumes the S0 seam types in `layouts.api.ts`/`layouts.schema.ts`
(read-only — does **not** edit them).

### S3 — kiosk-web grid + highlight (∥ S1, ∥ S2 after S0)

**Owns (exclusive):** `apps/kiosk-web/src/features/cell/CellPage.tsx` (+ tests),
`apps/kiosk-web/src/features/revocation/useLayoutLifecycle.ts` (+ test), the kiosk
`ssE-overlay-highlight` stylesheet, `PickerPage.tsx` (type-only touch). Consumes the
S0 seam types in `layoutHub.ts`/`layouts.api.ts` (read-only).

### S4 — e2e (after S1 + S3 merge)

**Owns (exclusive):** `e2e/layouts.spec.ts`, `e2e/support/*` (only if a new helper
is needed). Extends the author→publish→render→(highlight)→archive flow to 2×2.

### Contention-file ownership map

| Contention file | Owner | Notes |
|---|---|---|
| `src/Shared.Contracts/LayoutComposition/*` (V2 add, V1 delete) | **S0** | the contract cut |
| `Application/DTOs/LayoutDto.cs` (+`TileDto`) | **S0** | read-side shape |
| `AuditObservability/.../IntegrationEventAuditHandler.cs` | **S0** | V1→V2 line |
| `Infrastructure/Persistence/.../LayoutConfiguration.cs` + `Migrations/*` | **S0** | EF + migration |
| `apps/shared/src/api/layouts.api.ts` | **S0** writes; S1/S2/S3 read | **the FE seam** |
| `apps/shared/src/api/layouts.schema.ts` | **S0** writes; S2 reads | Zod seam |
| `apps/shared/src/realtime/layoutHub.ts` | **S0** writes; S3 reads | highlight frame type |
| `e2e/support/*` | **S4** | only if a helper is added |

No two parallel slices touch the same file. S2 and S3 both *read* `layouts.api.ts`
but neither edits it (S0 froze it). This map is directly dispatchable to
`isolation:worktree` agents; the orchestrator commits/pushes/PRs (subagent push is
sandbox-unreliable).

## Migration & data-safety plan

1. **One EF migration**, run by `MigrationRunner` (ADR-0067), executed before any
   service that uses the DB starts.
2. **Order:** create `layout_revision_tiles` → add `grid_rows`/`grid_cols` (NOT NULL
   default 1) → `INSERT … SELECT revision_id, 0, 0, camera_id, overlay_id FROM
   layout_revisions` → drop the temporary defaults → drop `camera_id` + `overlay_id`.
3. **Zero loss:** every existing revision (Draft/Published/Archived) becomes a 1×1,
   1-tile wall preserving its camera + nullable overlay (SC-004). Published layouts
   keep working; kiosks render them identically (FR-011).
4. **Clean cut:** legacy columns are dropped in the *same* migration — no transitional
   read window (pre-production, every reader migrates together; ADR-0112 §3).
5. **Dev-volume gotcha:** a stale `layout-composition-db` volume could skip the
   migration; the standard "drop the volume" recovery applies (MEMORY: timescale /
   keycloak volume gotchas pattern). Call out in the Tasks verification step.
6. **`Down`** is provided (reverse the backfill) so a local rollback is possible.

## Test strategy

| Layer | Coverage of | Gate |
|---|---|---|
| **Domain** (`LayoutComposition.Domain.Tests`) | `GridDimensions`/`GridPosition`/`Tile` VO validation; the four grid invariants (SC-005); `CreateDraft`/`EditDraft`/`BranchDraft` tile-set handling; lifecycle regression (publish/branch/revert/archive unchanged, SC-009); `Publish` raises the reshaped domain event | ≥ 90% (ADR-0065) |
| **Application** (`LayoutComposition.Application.Tests`) | create/edit handlers return the right `LAYOUT_GRID_*` `Result` failures; query handlers map tiles+grid; the published-event handler emits `LayoutRevisionPublishedV2` with the full tile set | ≥ 80% |
| **Contracts** (`Shared.Contracts.Tests`) | `LayoutRevisionPublishedV2` positional ctor + `IIntegrationEvent` + JSON round-trip of the tile list; delete the V1 test | ≥ 90% |
| **Architecture** | NetArchTest still green — no new cross-context ref (SC-007); `Every_integration_event_has_an_audit_handler` passes with V2 | green |
| **Integration** (`Integration.Tests`, AspireFixture) | author 2×2 → publish → `GET ?state=published` returns grid+tiles → `LayoutRevisionPublishedV2` on the bus; migrated 1-tile layout reads back as 1×1 (SC-004); `GET ?state=published` ≤ 100 ms p95 (SC-006) | green |
| **Component (kiosk)** | per-tile highlight routing: a synthetic `OverlayHighlightChanged` lights exactly the matching tile(s) and OR's overlapping durations (US3 scenarios 1–4) | — |
| **e2e (Playwright, ADR-0108)** | steps 1–4 + 6: author→publish→2×2 grid renders→archive→force-disconnect | CI gate |

**e2e vs. component split (spec §Independent E2E):** the e2e gate covers
author→publish→render→archive (steps 1–4, 6) — deterministic via the UI. The
**per-tile highlight** (step 5) needs a highlight trigger; if the harness can seed an
Automation rule or a test-only highlight publish, it joins the e2e gate, otherwise
it stays a **kiosk component test** (grid + synthetic `OverlayHighlightChanged`) and
step 5 is the manual dev-run check. Plan for the component test as the reliable
floor; promote to e2e if the trigger is cheap.

## Risks / sequencing

| # | Risk | Mitigation |
|---|---|---|
| 1 | **S0 seam must land first.** S1/S2/S3 all compile against the reshaped command/DTO/contract + the FE `layouts.api.ts`/`layoutHub.ts` types. | S0 is a hard predecessor; it freezes every contention file (map above). Orchestrator merges S0 before dispatching S1–S3. |
| 2 | The FE seam is `apps/shared/src/api/layouts.api.ts` — both SPAs import its `LayoutTile`/`gridRows` types. A late shape change ripples into S2 + S3. | S0 publishes the final TS types (even ahead of backend impl if needed); S2/S3 treat it read-only. |
| 3 | EF **nested** owned collection (`Tiles` inside the owned `Revisions`) is heavier to map than two scalar columns; composite-key + FK wiring is fiddly. | Mirror the existing `OwnsMany(Revisions)` pattern; integration test asserts a round-trip load of a 4-tile revision. Accepted cost (ADR-0112 Consequences). |
| 4 | The backfill-then-drop migration is irreversible-in-practice on prod data; a bug drops `camera_id` after a bad backfill. | Pre-production; `Down` provided; the backfill is a single deterministic `INSERT … SELECT`; integration test runs the migration against a seeded 1-tile row and asserts it reads back as a `(0,0)` tile before the columns are dropped. |
| 5 | "Highlight all matching tiles" could surprise an operator who reused an overlay. | Locked product semantic (ADR-0112 §5); covered by US3 scenario 2. |
| 6 | Per-tile decode at 4 tiles could exceed the ≤120 ms leg on weak kiosk GPUs. | `MaxTiles = 4` is the §IV ceiling; Phase-5 measures click-to-first-frame per tile on a 2×2 wall and confirms no software-decode fallback. |
| 7 | Deleting `OverlayChange` + the scalar request shape touches every in-repo caller. | Clean cut is the locked decision; the compiler + the e2e gate catch any missed caller (all callers are in-repo). |

## Constitution / ADR conformance summary

- **Value objects + `Ensure.That`** (ADR-0038/0046/0066/0105): `Tile`,
  `GridPosition`, `GridDimensions` hand-written, validated via `Ensure.That(...)` in
  `.From(...)`; no primitives cross the domain boundary.
- **`Result<T, Error>`** (ADR-0047/0089): grid-invariant violations are `Result`
  failures mapped to `400` with `ApiError` codes — not exceptions. Illegal *state
  transitions* keep throwing `InvalidOperationException` (programmer error) as today.
- **`Option<T>` / NRT-off** (ADR-0048): `Tile.Overlay` is `Option<OverlayIdentifier>`;
  nullable `overlay_id` column.
- **No cross-context refs** (ADR-0027): all backend work inside `LayoutComposition.*`
  + `Shared.Contracts/LayoutComposition/`; the kiosk merges reads in the browser;
  NetArchTest unchanged and green (SC-007).
- **Versioned events** (ADR-0073/0040): `LayoutRevisionPublishedV2`, `V<N>` suffix,
  primitives at the wire; V1 removed (clean cut, ADR-0112 §3).
- **MigrationRunner** (ADR-0067): single EF migration, no upcaster (CRUD, not Marten).
- **Wolverine** (ADR-0088): V2 ships on the existing per-module queues + outbox; no
  new wiring.
- **`[LoggerMessage]`** (ADR-0050): structured logs on the publish + grid-rejection
  paths.
- **Code metrics** (ADR-0084): one type per file; the reshaped `Layout`/`Revision`
  stay within ≤300 LOC/file, ≤30 LOC/method — `ValidateGrid` factored to keep
  complexity ≤10.
- **§IV latency:** SFU→decode + composite legs affected, mitigated by `MaxTiles = 4`;
  highlight leg byte-identical; per-tile-decode note carried to Phase 5.

## Items for the product owner to confirm before Phase 3 (Tasks)

1. **`LayoutRevisionPublishedHubMessage` payload.** The plan keeps the lifecycle
   SignalR frame **lean** (no tile set — the picker re-queries via `LayoutList` tag
   invalidation on publish). Confirm we do **not** need the full tile set pushed on
   the publish frame (the integration `V2` event carries it; the picker only shows
   the name). Recommended: lean frame.
2. **Highlight e2e vs. component.** Confirm whether the e2e harness should seed an
   Automation rule (or a test-only highlight publish) so US3 step 5 is in the e2e
   gate, or whether per-tile highlight stays a kiosk **component test** with step 5
   as the manual dev-run check. Recommended: component test as the floor, promote to
   e2e only if the trigger is cheap.
3. **Tile PK choice.** `layout_revision_tiles` keyed on composite `(revision_id, row,
   col)` (tiles have no identity) vs. a synthetic `tile_id`. Recommended: composite
   key (matches the VO semantics; no spurious identity). Flagging because it shapes
   the migration + EF config.

No `[NEEDS CLARIFICATION]` markers remain in the spec; all architectural decisions
are locked in ADR-0112. The three items above are confirmations, not open design
questions — none block starting Tasks. **Phase-2 gate: awaiting review.**
