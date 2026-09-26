# Plan 258 — The tile that claims a rectangle

**Spec**: [spec.md](./spec.md) · **Issue**: #2607 · **ADR**: 0156 (amends 0112)
**Engineers, in order**: `backend-engineer` (US1: domain, contract, EF, migration,
handlers, API) → `frontend-engineer` (US2 kiosk, US3 designer, shared schema, e2e).
Backend first because both frontend stories consume the new `rowSpan`/`colSpan`
shape. **Phase 4a colour: RED** (spec §8). **Reviewers**: `backend-reviewer`,
`frontend-reviewer`.

**Constitution check.** §II: the span is a value object; `Tile` keeps no public
primitive (mirrors `GridPosition`, and `PrimitiveBoundaryTests` would fail the build
otherwise). §III: no cross-context reference; the only cross-context surface is
`Shared.Contracts.LayoutTileV2`, extended additively. §IV: spec §5. §IX: no config
knob — the cap is a constant, as ADR-0112 required. **No new ADR** (spec §6).

---

## 1. Context and layers

| Layer | Project | Change |
|---|---|---|
| Domain | `LayoutComposition.Domain` | `TileSpan` (new VO), `Tile`, `GridDimensions`, `GridViolation`, `Layout.ValidateGrid`, `Revision.NewDraft` |
| Application | `LayoutComposition.Application` | `TileDto`, `GetLayoutQueryHandler.MapTiles` (shared by `ListLayoutsQueryHandler`), `LayoutRevisionPublishedDomainEventHandler`, both `*Errors.cs` |
| Infrastructure | `LayoutComposition.Infrastructure` | `LayoutConfiguration` tile mapping, new migration + snapshot |
| Api | `LayoutComposition.Api` | `TileRequest`, `LayoutEndpoints.Commands.ParseTiles` |
| Contracts | `Shared.Contracts` | `LayoutTileV2` |
| Frontend shared | `apps/shared/src/api` | `layouts.schema.ts`, `layouts.api.ts` |
| Kiosk | `apps/kiosk-web/src/features/cell` | explicit placement |
| Console | `apps/management-web/src/features/layouts` | span controls, presets to 3×3 |
| e2e | `e2e/` | new `spanning-wall.spec.ts`; D1 edit |

Not touched: AppHost, ServiceDefaults, Shared.Kernel, AuditObservability (its handler
records the event generically — `IntegrationEventAuditHandler.cs:45`),
ScenarioSimulator (defaults cover it; spec §3 out of scope), `layoutHub.ts`,
`OverlayHighlightRequestedV1` path.

## 2. Domain (US1)

### 2.1 `TileSpan` — new value object

`src/LayoutComposition/Domain/Layout/TileSpan.cs`, next to `GridPosition`, same shape:

```csharp
public sealed record TileSpan(int Rows, int Cols) : IValueObject
{
    public static readonly TileSpan Single = new(1, 1);
    public static TileSpan From(int rows, int cols) // Ensure.That(rows).AtLeast(1); same for cols
}
```

- Invariant: `Rows ≥ 1`, `Cols ≥ 1` (ADR-0156 §2 "construction error"). The upper
  bound is the grid's, so it is checked by `GridDimensions`, exactly as
  `GridPosition` leaves its upper bound to the aggregate.
- Name: `TileSpan` with components `Rows`/`Cols` (a span *is* its two extents,
  §II exemption 3). The wire keeps ADR-0156's `RowSpan`/`ColSpan`.

### 2.2 `Tile`

- Two new private fields `rowSpan`, `colSpan`, exposed only as
  `public TileSpan Span => new(rowSpan, colSpan);` — the `row`/`col` pattern.
- New public constructor `Tile(CameraIdentifier, Option<OverlayIdentifier>, GridPosition, TileSpan)`
  with `Ensure.That(span).IsNotNull()`.
- **The existing 3-argument constructor stays** and delegates with `TileSpan.Single`.
  It is the honest meaning of a 1×1 tile, and it keeps the ~15 existing call sites
  (tests, builders) compiling unchanged — a behaviour-preserving convenience, not
  speculative generality.
- The EF materialisation constructor gains `int rowSpan, int colSpan` (parameter
  names must equal the field-backed property names, as `row`/`col` do).
- `public bool Overlaps(Tile other)` — rectangle intersection:
  `row < other.row + other.rowSpan && other.row < row + rowSpan` and the same on
  columns. Equivalent to ADR-0156's "checked cell-by-cell"; the tests enumerate
  cell cases (corner touch, edge touch, containment, identical origin).
- Record equality now includes the span fields (records compare all instance
  fields). Intended: a 2×2 tile and a 1×1 tile at the same origin are different.

### 2.3 `GridDimensions`

- `MaxTiles = 9`, `MaxCells = 9`. Doc comment: cite ADR-0156, and say plainly that 9
  is the ADR's ceiling, **not verified safe on kiosk hardware** (ADR-0156
  §Consequences) — the comment is where the next reader looks.
- New `public bool Contains(GridPosition position, TileSpan span)` —
  `Row ≥ 0 && Row + span.Rows ≤ Rows && Col ≥ 0 && Col + span.Cols ≤ Cols`.
  The existing single-argument `Contains` stays (equivalent to `span = Single`).
- `Default` stays 2×2, `Cell` stays 1×1. ADR-0156 does not change the default.

### 2.4 `GridViolation` and `Layout.ValidateGrid`

- `DuplicatePosition` → **`Overlap`** (doc: "Two tiles' spans share a cell").
- `ValidateGrid`, same order, same first-violation contract:
  1. `Empty` — unchanged.
  2. `TooLarge` — unchanged expression, new constants. (With non-overlap, the
     `tiles.Count > MaxTiles` disjunct can only fire when an overlap also exists;
     it stays, because it runs first and keeps the 10-tile case a `TooLarge`.)
  3. `OutOfBounds` — `!grid.Contains(tile.Position, tile.Span)`.
  4. `Overlap` — any pair `i < j` with `tiles[i].Overlaps(tiles[j])`. ≤ 36 pair
     checks at the cap.
- `RequireValidGrid` and both write methods unchanged. Class doc "(≥1 tile, no
  duplicate position, in-bounds, ≤4)" updated.

### 2.5 `Revision.NewDraft` — the silent-drop fix

`revision.tiles.Add(new Tile(tile.Camera, tile.Overlay, tile.Position, tile.Span));`
Without this `BranchDraft` flattens every span (spec §1 finding 1).

### 2.6 Domain event

`LayoutRevisionPublishedDomainEvent` carries `IReadOnlyList<Tile>` already; spans ride
along. No change to its shape.

## 3. Application, API, contract, persistence (US1)

### 3.1 Contract (`Shared.Contracts`)

```csharp
public sealed record LayoutTileV2(
    Guid Camera, Guid? Overlay, int Row, int Col, int RowSpan = 1, int ColSpan = 1);
```

Trailing, defaulted: every existing positional construction compiles
(`V1ResourceMapTests.cs:337`, `LayoutRevisionPublishedV2Tests.cs:17`), and a message
serialised without the fields deserialises as 1×1 (System.Text.Json honours
constructor-parameter defaults). **A test proves the deserialisation default** rather
than trusting this sentence. Doc comment on `LayoutRevisionPublishedV2` notes the
in-place extension and cites ADR-0156.

### 3.2 HTTP shapes

- `TileRequest(..., int Row, int Col, int RowSpan = 1, int ColSpan = 1)`.
- `TileDto(..., int Row, int Col, int RowSpan = 1, int ColSpan = 1)` — the default is
  for construction symmetry; the read side always sets both explicitly.
- `GridRequest`, `CreateLayoutRequest`, `EditDraftRequest` unchanged.

### 3.3 Mapping sites (exhaustive, by grep)

| Site | Change |
|---|---|
| `Api/LayoutEndpoints.Commands.cs:439` `ParseTiles` | `TileSpan.From(request.RowSpan, request.ColSpan)` into the 4-arg ctor. A span < 1 throws `ArgumentException` inside the existing `try` → `400 LAYOUT_INVALID_INPUT`, the same path a negative row takes today. |
| `Application/Queries/Handlers/GetLayoutQueryHandler.cs:61` `MapTiles` | `RowSpan: tile.Span.Rows, ColSpan: tile.Span.Cols`. `ListLayoutsQueryHandler` reuses it. |
| `Application/EventHandlers/LayoutRevisionPublishedDomainEventHandler.cs:36` | same two fields on `LayoutTileV2`. |

### 3.4 Errors

In **both** `CreateLayoutDraftErrors.cs` and `EditDraftRevisionErrors.cs`:
`TilePositionDuplicate` → `TileOverlap`, code `LAYOUT_TILE_OVERLAP`, message "Two
tiles overlap.", mapped from `GridViolation.Overlap`; the static factory renamed to
match. `TileOutOfBounds` message → "A tile's span extends outside the grid bounds."
(code unchanged). `GridTooLarge` message reads the new constants unchanged.

Rename evidence: `grep -rn LAYOUT_TILE_POSITION_DUPLICATE` on this tree hits only the
two error files — no frontend, e2e, simulator or test reads the code.

### 3.5 Persistence

`LayoutConfiguration` tile block, after `col`:

```csharp
tiles.Property<int>("rowSpan").HasColumnName("row_span").HasDefaultValue(1).IsRequired();
tiles.Property<int>("colSpan").HasColumnName("col_span").HasDefaultValue(1).IsRequired();
tiles.Ignore(tile => tile.Span);
```

Key stays `(revision_id, row, col)` — valid because non-overlapping rectangles have
distinct origins, which `ValidateGrid` guarantees before any write.

Migration: `dotnet ef migrations add TileSpans` in the Infrastructure project (the
repo's usual invocation). **Accept it only if `Up` is exactly two `AddColumn<int>`
calls with `nullable: false, defaultValue: 1`** on `layout_revision_tiles`, and `Down`
the two `DropColumn`s. Anything else (a rebuilt key, a touched index) means the model
drifted and is a stop. `HasDefaultValue(1)` in the model is what keeps the snapshot and
the migration agreeing; the CLR sentinel is `0`, which the value object makes
unreachable, so EF always writes the real value.

## 4. Frontend

### 4.1 Shared (`apps/shared/src/api`)

- `layouts.schema.ts`: `MAX_TILES = MAX_CELLS = 9`; `gridSchema` rows/cols
  `.max(3)`; `tileSchema` gains `rowSpan`/`colSpan` `z.number().int().min(1)`;
  `refineGrid` replaces the `row,col` duplicate set with (a) full-span in-bounds
  (`row + rowSpan > rows` → "Tile span is out of grid bounds") and (b) a cell
  occupancy map — the first tile to claim a cell owns it, a later claimant gets
  "Two tiles overlap" on its own index, so the resolver lands it on that cell.
- `layouts.api.ts`: `LayoutTile` gains `rowSpan: number; colSpan: number` (required —
  the backend always sends them).

### 4.2 Kiosk (US2)

Extract the placement from `CellPage.tsx:422-450` into a new pure module
`apps/kiosk-web/src/features/cell/wallGrid.ts` (CellPage is already 459 lines):

```ts
export interface GridItem { key: string; tile: LayoutTile | null; row: number; col: number; rowSpan: number; colSpan: number }
export function buildGridItems(rows: number, cols: number, tiles: LayoutTile[]): GridItem[]
```

- Each tile → an item at its origin, span clamped to the grid (renderer stays total).
- Occupancy map marks every covered cell; each **uncovered** cell → a `tile: null`
  item with span 1×1. Covered cells emit nothing. Later overlapping tiles
  (impossible from a valid layout) are dropped, first wins — defensive only.
- Order: row-major by origin, so DOM order matches today for 1×1 walls.
- `key` stays `row:col` of the origin, so `useWallAlignment`'s keys are unchanged.

`CellPage` renders items with `style={{ gridRow: \`${row + 1} / span ${rowSpan}\`, gridColumn: \`${col + 1} / span ${colSpan}\` }}`
on a wrapper (or pass a `placement` prop to `Tile` and `EmptyCell`; engineer's choice,
whichever keeps `data-testid="layout-tile"` on the placed element so e2e geometry
reads the right box). No class, animation, filter or shadow is added (§IV composite
leg).

### 4.3 Designer (US3)

**Assumption, explicit:** the span interaction is two native selects per populated
tile — `Row span`, `Column span` — not drag handles. Reason: smallest surface, keyboard
and screen-reader operable without extra work, consistent with spec 228's native
radios in the same component. ADR-0156 permits "an equivalent". If the Phase 1 gate
wants drag-to-resize, it is additive over this model (a handle writes the same two
fields) and should be its own issue.

`gridDesignerModel.ts`:

- `DesignerCell` gains `rowSpan`, `colSpan` (default 1).
- `GRID_PRESETS`: `rows, cols ∈ {1,2,3}`, `rows*cols ≤ MAX_CELLS` → nine presets.
- `buildCells(rows, cols, existing)`: carries `rowSpan`/`colSpan`, **clamped** to the
  new grid from the cell's origin. Shrinking cannot create an overlap, so a valid
  wall stays valid.
- `cellsFromTiles` / `tilesFromCells`: carry the spans.
- New pure `coveredBy(cells): Map<index, index>` — cells hidden under another
  populated cell's span.
- New pure `spanOptions(cells, index, grid): { rows: number[]; cols: number[] }` —
  for the cell at `index`, the row-span values `1..k` whose rectangle (with the
  current col span) stays in-bounds and covers no *other populated* cell; same for
  columns. Empty covered cells do not block (they disappear).
- Clearing a cell's camera resets its span to 1×1 (a span on an empty cell hides
  cells for nothing, and the empty cell is dropped on submit anyway).
- The resolver's `tiles[i]` → cell mapping is unchanged (tiles still come from
  populated cells in order).

`GridDesigner.tsx` (320 lines — put the two selects in a new
`TileSpanFields.tsx` so the file does not grow further past the advisory 300):

- The cell grid gets `gridTemplateRows` as well as columns and places each
  **uncovered** cell explicitly with the same `grid-row`/`grid-column` span syntax
  as the kiosk.
- Each populated cell shows `Row span` / `Column span` selects
  (`id="tile-{index}-row-span"` / `-col-span`) with `spanOptions` as options,
  registered as `cells.{index}.rowSpan` / `colSpan` with `valueAsNumber`.
- "Tile r,c" label unchanged; a spanning cell's label may read "Tile 1,1 (2×2)".

### 4.4 e2e

New `e2e/spanning-wall.spec.ts` (disjoint from every existing spec): register six
cameras through the existing support helpers → author the hero wall in the console
with 3×3 + spans → publish → open by name in the kiosk → assert 6 tiles, 0 empty
cells, hero box ≈ 2× thumbnail box in each axis (tolerance for the 1-unit gap).
Tiles use unserved camera URLs like the other seeds; geometry does not need video.

## 5. Messaging and boundaries

Domain → integration unchanged in shape: `Layout.Publish` raises
`LayoutRevisionPublishedDomainEvent(…, Tiles)`; its handler maps to
`LayoutRevisionPublishedV2` (spans now included) and the SignalR broadcast (which
carries no tiles — the kiosk refetches). Only `Shared.Contracts` crosses a context.
No NetArchTest rule changes.

## 6. Tests

### 6.1 Existing tests whose expectation changes because the decision changed it

Each is rewritten to the new boundary, not deleted or loosened. The PR lists them.

| Test | Old expectation | New expectation |
|---|---|---|
| `GridDimensionsTests.MaxTiles_and_MaxCells_are_four` | 4 / 4 | renamed `..._are_nine`, 9 / 9 |
| `GridDimensionsTests.From_accepts_grids_within_the_cell_cap` | 1×1…2×2 | add 3×3, 1×9, 3×2 |
| `GridDimensionsTests.From_rejects_an_invalid_grid` | `2,3` `3,2` `5,1` rejected | `2,5` `4,3` `10,1` rejected (0/negative rows unchanged) |
| `LayoutTests.ValidateGrid_rejects_an_oversized_grid_as_TooLarge` | `new GridDimensions(3,3)` | `new GridDimensions(4,3)` (12 cells) |
| `LayoutTests.ValidateGrid_rejects_two_tiles_at_the_same_position_as_DuplicatePosition` | `DuplicatePosition` | renamed, `Overlap` |
| `LayoutGridInvariantTests` oversize (create + edit) | 3×3 | 4×3 |
| `LayoutGridInvariantTests.CreateDraft_refuses_more_tiles_than_the_grid_allows` | 5 tiles on 2×2 | 10 tiles on 3×3 (nine distinct + one repeat); comment rewritten |
| `LayoutGridInvariantTests` duplicate-position (create + edit) | `nameof(DuplicatePosition)` | `nameof(Overlap)` |
| `CellPage.test.tsx` "2x2 grid" | unchanged fixture | `LayoutTile` fixtures gain `rowSpan: 1, colSpan: 1` (type now requires them) |
| `GridDesignerKeyboard.test.tsx` presets | 4 presets | 9 presets |
| `e2e/kiosk-shows-a-label-over-video.spec.ts:118-124` | `toBe(4)`, "domain ceiling" | per **D1** (T041) |

### 6.2 New tests (4a, red first)

- **Domain** (`tests/LayoutComposition.Domain.Tests/Layout/`): `TileSpanTests`
  (From accepts ≥1, rejects 0/−1); `TileTests` (Span defaults to Single via 3-arg
  ctor; Overlaps: corner-touch false, edge-share false, containment true, partial
  true, identical origin true, symmetric); `GridDimensionsTests` (full-span
  Contains); `LayoutTests`/`LayoutGridInvariantTests` (hero wall valid, full 3×3
  valid, overlap → Overlap, span off edge → OutOfBounds, violation order preserved
  when several apply); `LayoutRevisionStateMachineTests` or a new
  `LayoutBranchSpanTests` (BranchDraft preserves spans).
- **Application**: `GetLayoutQueryHandlerTests` (spans on the DTO);
  `LayoutRevisionPublishedDomainEventHandlerTests` (spans on V2); both command
  handler tests (overlap → `LAYOUT_TILE_OVERLAP`, span OOB → `LAYOUT_TILE_OUT_OF_BOUNDS`).
- **Contracts**: `LayoutRevisionPublishedV2Tests` — positional ctor defaults to 1;
  JSON without the fields deserialises as 1×1; round-trip with 2×2.
- **Integration** (`tests/Integration.Tests/LayoutComposition/`, new
  `TileSpanIntegrationTests.cs`): the US1 Gherkin over HTTP against the Aspire
  stack, including omitted spans, 401/403, 412, branch, and the migration default —
  insert one tile row with raw SQL naming no span column, then GET → 1×1.
- **Architecture**: none new; `PrimitiveBoundaryTests` must stay green (proves §II).
- **Frontend unit**: `layouts.schema` span refinements; `wallGrid.test.ts`
  (hero, sparse, 1×1 characterisation, clamp, drop-overlap); `gridDesignerModel`
  (presets, clamp, spanOptions, coveredBy, round-trip); `GridDesigner` span controls
  (hide/reappear, options offered, keyboard); `CellPage` hero placement style.
- **e2e**: `spanning-wall.spec.ts`.

### 6.3 Commits must build on their own

A C# test that names `TileSpan` or `GridViolation.Overlap` does not compile before
the production change, and rebase-merge lands every commit on `develop`. So the 4a
**evidence** is the test-writer's verbatim run output (quoted in the PR body), and
each test commit lands together with, or after, the production code it compiles
against. The same holds for TypeScript tests that fail `typecheck`. Runtime-red tests
(assertion failures on existing types) may land first.

## 7. Delivery order

1. **Backend (US1)**, one engineer: domain → contract → application/API → EF +
   migration → integration tests. Suggested commits:
   `feat(layout): let a tile claim a rectangle, and raise the wall cap to 3x3`
   (domain + application + api + tests), `feat(contracts): carry tile spans on
   LayoutRevisionPublishedV2`, `feat(layout): persist tile spans` (config +
   migration + integration tests). Each builds alone.
2. **Frontend**, after (1) is on the branch: shared schema/type first (it blocks
   both apps' type-check), then kiosk (US2) ∥ designer (US3) on disjoint files, then
   the e2e spec.
3. **Phase 5** (§8), then 6, then one PR to `develop` — **after #2606 merges**.

## 8. Phase 5 — measurement plan

Per `/verify`, plus the NFR gate. Written into
`specs/258-the-tile-that-claims-a-rectangle/verification.md`.

1. **Observe the feature** — hero wall authored in the console, shown in the kiosk,
   screenshot; highlight a spanning tile's overlay and watch it light.
2. **M1 composite + render** — on the dev machine, open a 9-tile 3×3 wall and a
   4-tile 2×2 wall, all tiles on `rtsp://fixture-video:8554/loop`, one at a time, same
   browser build, same window size. Change the bound overlay value N times and harvest
   `overlay_draw` samples (the spec 225 `render-leg` harness in
   `e2e/support/render-leg.ts` or the Aspire dashboard's kiosk-latency metric). Record
   p50/p95/max **and the frame cadence first** (ADR-0123). Two runs each (the first
   run after churn looks like a regression).
3. **M2 decode, in part** — same walls; per tile read `receive_to_decoded` and, from
   `RTCPeerConnection.getStats()` inbound-rtp, `decoderImplementation`,
   `powerEfficientDecoder`, `framesDecoded`, `framesDropped`. Record whether all 9
   decoded in hardware or some fell back to software.
4. **M3 real kiosk hardware — NOT PERFORMED.** Write the row, state what would
   discharge it (M1+M2 on the fab's kiosk model, figures against ≤ 50 ms and ≤ 120 ms,
   cadence ≥ 40 Hz per ADR-0123), and the rule "if either budget fails, ship the cap
   the measurement supports, not 9". Open the follow-up issue carrying exactly that,
   add it to Project #13, and link it from `verification.md`, the PR body, and a
   comment on #2607.
5. **§IV table**: no edit (ADR-0138). The dev figures are evidence about the code,
   not about the fleet.

## 9. Risks

- **A span silently lost on branch.** Covered by 2.5 and its test; if that test
  arrives green on unchanged code, it is not exercising `BranchDraft`.
- **Migration drift.** Guarded by the "exactly two AddColumns" acceptance rule (3.5).
- **Kiosk DOM order change** breaking existing kiosk tests that index tiles by
  position: row-major-by-origin keeps 1×1 walls identical (characterisation test).
- **CI cost of nine decodes** — only if D1 = A. Measure the shard's wall clock on the
  PR run and record it.
- **Designer form arrays and hidden cells.** Covered cells stay in the dense
  `useFieldArray` (so indices and resolver mapping are stable) but are not rendered;
  an unregistered field keeps its value, so a covered cell's stale camera could
  resurface when the span shrinks. `spanOptions` never covers a populated cell, so a
  covered cell is always empty; the designer test asserts the reappearing cells are
  empty.
