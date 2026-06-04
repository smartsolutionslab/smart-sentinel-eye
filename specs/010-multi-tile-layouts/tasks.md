# Tasks: 010 — Multi-tile layouts (video wall)

**Plan:** [plan.md](./plan.md) · **Spec:** [spec.md](./spec.md) · **ADR:** [ADR-0112](../../docs/adr/0112-multi-tile-layouts.md)

Atomic tasks grouped by the plan's parallel slices. `[P]` = parallelizable
with its siblings (disjoint files). **Slice order: S0 → (S1 ∥ S2 ∥ S3) → S4.**
S0 is the single-owner foundation — it freezes every contention file (the
domain shapes, the V2 contract, the migration, and the FE seam types); S1/S2/S3
only land after S0 merges. Each task names its primary file(s) and is sized to be
independently verifiable.

Labels: `task`, `feature:010-multi-tile-layouts`, `slice:S{n}`.

---

## S0 — Domain + Contracts + Migration + FE seam (single-owner, foundational)

- **T001** `GridDimensions` value object — `src/LayoutComposition/Domain/Layout/GridDimensions.cs`: `readonly record struct (int Rows, int Cols)`; `const MaxTiles = MaxCells = 4`; `Default = 2×2`, `Single = 1×1`; `From` guards `Rows≥1, Cols≥1, Rows*Cols ≤ MaxCells` via `Ensure.That`; `Contains(GridPosition)` helper. + VO unit tests.
- **T002** `GridPosition` value object — `GridPosition.cs`: `readonly record struct (int Row, int Col)`; `From` guards `Row≥0, Col≥0` (upper bound checked by the owning revision's grid). + tests.
- **T003** `Tile` value object — `Tile.cs`: `sealed record (CameraIdentifier Camera, Option<OverlayIdentifier> Overlay, GridPosition Position)`. + tests.
- **T004** Reshape `Revision` — `Revision.cs`: replace `Camera`+nullable `Overlay` with `GridDimensions Grid` + owned `IReadOnlyList<Tile> Tiles`; `NewDraft`/`Branch` take `(grid, tiles)`; replace `EditCamera`/`AttachOverlay` with state-guarded `ReplaceTiles(grid, tiles)`. Lifecycle untouched.
- **T005** Reshape `Layout` aggregate — `Layout.cs`: `CreateDraft`/`BranchDraft`/`EditDraft`/`Publish` move to `(grid, tiles)`; add private `ValidateGrid(grid, tiles)` returning the first invariant violation as a `Result` failure (≥1 tile / no dup position / in-bounds / ≤4). Illegal **state transitions** keep throwing. + aggregate tests incl. lifecycle regression (SC-009) + invariants (SC-005).
- **T006** Reshape `LayoutRevisionPublishedDomainEvent` — `Events/LayoutRevisionPublishedDomainEvent.cs`: carry `GridDimensions Grid` + `IReadOnlyList<Tile> Tiles` (was `Camera`). `LayoutRevisionArchivedDomainEvent` unchanged.
- **T007** `CreateLayoutDraftCommand` + handler — `Application/Commands/CreateLayoutDraftCommand.cs` (+ `CreateLayoutDraftErrors.cs`, `Handlers/`): `Name, Grid, Tiles, CreatedBy`; errors gain the four `LAYOUT_GRID_*` cases. + handler tests.
- **T008** `EditDraftRevisionCommand` + handler — `EditDraftRevisionCommand.cs` (+ Errors, Handler): `Layout, RevisionNumber, Grid, Tiles`; **delete `OverlayChange`** tri-state. + handler tests.
- **T009** DTOs + query handlers — `Application/DTOs/LayoutDto.cs` (+ new `TileDto(Guid Camera, Guid? Overlay, int Row, int Col)`); remove scalar `cameraIdentifier`/`overlayIdentifier`; map `Grid`+`Tiles` in `GetLayoutQueryHandler` + `ListLayoutsQueryHandler`.
- **T010** Published-event handler → V2 — `Application/EventHandlers/LayoutRevisionPublishedDomainEventHandler.cs`: publish `LayoutRevisionPublishedV2` (map tiles+grid); keep the lifecycle hub frame **lean** (no tile set; picker re-queries via `LayoutList` invalidation).
- **T011** `LayoutRevisionPublishedV2` contract — `src/Shared.Contracts/LayoutComposition/LayoutRevisionPublishedV2.cs` (+ `LayoutTileV2(Guid Camera, Guid? Overlay, int Row, int Col)`); **delete `LayoutRevisionPublishedV1.cs`**. + contract test (ctor/marker/JSON round-trip); delete the V1 test.
- **T012** Audit subscriber → V2 — `src/AuditObservability/Application/EventHandlers/IntegrationEventAuditHandler.cs`: `Handle(LayoutRevisionPublishedV1)` → `Handle(LayoutRevisionPublishedV2)` (one line; body shape-agnostic). Keeps `Every_integration_event_has_an_audit_handler` green.
- **T013** EF mapping — `Infrastructure/Persistence/Configurations/LayoutConfiguration.cs`: `grid_rows`/`grid_cols` columns + nested `OwnsMany(r => r.Tiles)` → `layout_revision_tiles` with composite PK `(revision_id, row, col)`, FK to `layout_revisions`, `camera_id` required / `overlay_id` nullable.
- **T014** Migration + round-trip test — `Infrastructure/Persistence/Migrations/*_MultiTileLayouts.cs` (via MigrationRunner): create tiles table → add grid cols (NOT NULL default 1) → backfill `INSERT…SELECT revision_id,0,0,camera_id,overlay_id` → drop defaults → **drop `camera_id`/`overlay_id`**; `Down` reverses; regenerate snapshot. + integration test: 4-tile round-trip and a migrated 1-tile reads back as `(0,0)` (SC-004).
- **T015** `[P]` FE seam — types — `apps/shared/src/api/layouts.api.ts`: replace scalar camera/overlay on `LayoutRevision`/`PublishedLayout` with `gridRows`,`gridCols`,`tiles: LayoutTile[]`; mutation bodies send `{ name?, grid, tiles }`.
- **T016** `[P]` FE seam — Zod — `apps/shared/src/api/layouts.schema.ts`: multi-tile schema (`grid.rows/cols 1..2`, `tiles.min(1)`, `superRefine` for the 4 invariants); shared `MAX_TILES = 4` const mirroring the domain.
- **T017** `[P]` FE seam — hub — `apps/shared/src/realtime/layoutHub.ts`: add `OverlayHighlightChangedMessage { overlay, durationMs }`, an `onOverlayHighlightChanged` callback, and the `connection.on('OverlayHighlightChanged', …)` wiring (additive).

## S1 — Api (∥ S2, ∥ S3, after S0)

- **T018** Request DTOs — `Api/Requests/CreateLayoutRequest.cs` + `EditDraftRequest.cs`: `GridRequest(Rows,Cols)` + `TileRequest(CameraIdentifier, OverlayIdentifier?, Row, Col)`; **delete `OverlayBindingUpdate`**. Primitives only at the boundary.
- **T019** Endpoints — `Api/LayoutEndpoints.Commands.cs`: parse each `TileRequest`→`Tile` + `GridDimensions.From` inside the existing `try/catch(ArgumentException)`→`400 LAYOUT_INVALID_INPUT`; delete `TranslateOverlayChange`; map the four grid `Result` errors via `error.ToProblem()`; `LayoutEndpoints.Queries.cs` returns grid+tiles. Routes/scopes unchanged. + Api tests.

## S2 — management-web grid designer (∥ S1, ∥ S3, after S0)

- **T020** `[P]` Grid designer component — `apps/management-web/src/features/layouts/GridDesigner.tsx`: grid-size presets (1×1/1×2/2×1/2×2 from `MAX_TILES`), per-cell camera `<select>` (required) + overlay `<select>` (optional), sparse cells; RHF `useFieldArray` + `zodResolver` inline errors.
- **T021** `[P]` Create flow — `features/layouts/LayoutEditorDialog.tsx`: compose `GridDesigner`; submit via `useCreateLayoutDraftMutation` with the `{ name, grid, tiles }` body.
- **T022** `[P]` Edit-after-publish (US4) — Edit affordance → `useBranchDraftRevisionMutation` → open designer pre-loaded with the branched draft's grid+tiles → `useEditDraftRevisionMutation`.
- **T023** `[P]` List summary — `features/layouts/LayoutsPage.tsx`: row shows "N tiles, R×C". + `*.test.tsx` for designer + page.

## S3 — kiosk-web grid + per-tile highlight (∥ S1, ∥ S2, after S0)

- **T024** `[P]` Grid renderer — `apps/kiosk-web/src/features/cell/CellPage.tsx`: render a CSS grid from `tiles`+`gridRows/Cols`; one `<CameraViewer>` per populated cell with per-tile overlay binding; empty cells → placeholder; N=1 (incl. migrated) renders identically to today (FR-011, SC-004). + tests.
- **T025** `[P]` Per-tile highlight — `features/revocation/useLayoutLifecycle.ts` add `onOverlayHighlightChanged`; on a frame, apply `ssE-overlay-highlight` to **every** tile whose overlay matches, for `durationMs`, OR'ing overlapping durations, auto-revert; no-op if unbound. Add the `ssE-overlay-highlight` CSS keyframe (net-new). + component test (synthetic frame, US3 scenarios 1–4).

## S4 — e2e (after S1 + S3 merge)

- **T026** e2e — `e2e/layouts.spec.ts`: author a 2×2 wall → publish → kiosk renders a 2×2 grid of 4 tiles → archive → force-disconnect (spec §Independent E2E steps 1–4, 6). Per-tile highlight (step 5) covered by T025's component test unless a cheap highlight trigger is wired.

---

**Done = each task's file(s) exist + its tests green + the slice's PR rebase-merges to `develop` with CI (incl. the e2e gate) green.** Coverage gates (ADR-0065): Domain ≥90 / Application ≥80 / Shared.Contracts ≥90.
