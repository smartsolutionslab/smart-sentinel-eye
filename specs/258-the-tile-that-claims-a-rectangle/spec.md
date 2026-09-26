# Spec 258 — The tile that claims a rectangle

**Issue:** [#2607](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2607)
— *Deliver ADR-0156: 3x3 walls with spanning tiles*. On Project #13, status **Todo**
(verified 2026-09-26 via `gh issue view 2607 --json projectItems`). No labels, so
**supervised lane** (ADR-0037) — which the issue and ADR-0156 §Implementation Notes
both require. No `item-add` needed.

**Spec number.** On 2026-09-26 every local directory, every remote branch's `specs/`
tree and the main checkout were listed. Highest present is 257 (on develop);
253 is an untracked directory in the main checkout; nothing claims 258. Re-check
before the PR (the standing lesson from spec 257's renumbering).

**Branch:** `feat/2607-3x3-walls-spanning-tiles`, cut from `origin/develop`.
`docs/adr/0156-3x3-walls-with-spanning-tiles.md` is present as an untracked copy
ahead of its own PR #2606. **This spec's PR must not merge before #2606**, or
`develop` carries a spec citing an ADR it does not contain.

**ADRs referenced:**

- **ADR-0156** (3×3 walls with spanning tiles): the decision this spec delivers,
  in full and no further. Its decision 1 (cap 4 → 9, gated on a real-hardware
  decode measurement) and decision 2 (general row/col span, updated invariants,
  V2 extended in place, additive migration) are the scope.
- **ADR-0112** (multi-tile layouts): the aggregate, grid model, V2 contract and
  single-source-of-truth posture ADR-0156 amends (decisions 2 and 4).
- **ADR-0123** (a render leg is the operator's wait): how the composite-and-render
  figure is read — cadence first.
- **ADR-0073 / ADR-0040** (versioned integration events): why a V2 extended with
  defaulted fields is not a V3.
- **ADR-0067** (MigrationRunner), **ADR-0043 / ADR-0113** (two-layer optimistic
  concurrency, unchanged), **ADR-0046 / ADR-0066 / ADR-0105** (hand-written value
  objects, `Ensure.That`), **ADR-0141** (`Option<T>`), **ADR-0047 / ADR-0089**
  (`Result` + `ApiError`).
- **ADR-0079** (React Hook Form + Zod), **ADR-0108** (Playwright e2e gate),
  **ADR-0109** (`[P]` = disjoint files), **ADR-0139 / ADR-0144** (red first;
  phase-4a colour — §8 picks **red**).
- **ADR-0138** (honesty rules): no leg-state change a person did not earn; a
  measurement not taken is recorded as not taken.

**No new ADR.** See §6. One cross-spec decision is surfaced for the Phase 1 gate
(**D1**, §7): it is not an architecture question, but it is not this spec's to
take silently either.

**Latency budget (§IV):** composite-and-render and SFU→kiosk decode are **loaded**
(more tiles, spanning placement). Event→overlay state is untouched. See §5.

---

## 1. The issue's premise, re-checked on this tree (`b2f09bf7`, 2026-09-26)

| Claim | On this tree |
|---|---|
| `GridDimensions.MaxCells = 4`, `MaxTiles = 4` | **True.** `src/LayoutComposition/Domain/Layout/GridDimensions.cs:18,21`. |
| A tile names exactly one cell | **True.** `Tile` holds private `row`/`col` fields exposed as `GridPosition` (`Tile.cs:29-30,68`). |
| `Layout.ValidateGrid` is the single source of the grid invariants | **True.** `Layout.cs:70-92`: Empty → TooLarge → OutOfBounds → DuplicatePosition, first violation wins. |
| One EF migration for the context | **False, harmlessly.** There are seven (`Migrations/`), latest `20260907012846_LayoutNameUniquePerLiveChain`. A new additive migration follows the latest. |
| Tiles keyed `(revision_id, row, col)` | **True.** `LayoutConfiguration.cs:163`. Still a valid key after this change: non-overlapping rectangles have distinct origins. |
| The kiosk places tiles one cell each | **True, and by auto-placement.** `CellPage.tsx:433-446` emits every coordinate row-major and lets CSS grid flow them; there is no explicit `grid-row`/`grid-column`. A span therefore cannot be added per tile without switching to explicit placement (plan §4.2). |
| The designer is a grid of equal cells | **True.** `gridDesignerModel.ts` holds a dense `rows×cols` cell array; presets are `rows, cols ∈ {1,2}` (`:44-54`); the TS schema caps each dimension at 2 (`layouts.schema.ts:25-26`). |

Three findings the issue does not mention, each material:

1. **`Revision.NewDraft` re-constructs every tile** (`Revision.cs:101-104`) as
   `new Tile(tile.Camera, tile.Overlay, tile.Position)` so EF sees fresh owned
   instances. Left as is, **`BranchDraft` would silently drop every span** — a
   published hero wall branches into a draft of 1×1 tiles. US1 carries an explicit
   scenario for it.
2. **Several tests pin the old cap as behaviour** — `GridDimensionsTests`
   (`MaxTiles_and_MaxCells_are_four`, `From_rejects_an_invalid_grid` with `2,3`/`3,2`),
   `LayoutTests.ValidateGrid_rejects_an_oversized_grid_as_TooLarge` (3×3),
   `LayoutGridInvariantTests` (3×3 oversize, 5-tiles-on-2×2), the kiosk and designer
   `2×2` tests, and `e2e/kiosk-shows-a-label-over-video.spec.ts:124`
   (`toBe(4)`, "the fixture wall must be at the domain ceiling"). These change
   **because the decision changed the behaviour**, not to reach green; plan §6
   lists each with its new expectation so the change is reviewable as such.
3. **The render-leg CI fixture is sized to "the domain's real ceiling"** (spec 225
   US2, `e2e/support/live-video-wall.ts:21-27`). At `MaxTiles = 9` that sentence
   becomes false while its guard stays green. See **D1** (§7).

## 2. User stories

### US1 (P1): A wall may be 3×3, and a tile may claim a rectangle

An API caller (the designer, the scenario simulator, an e2e seed) creates or edits
a draft whose grid is up to 9 cells and whose tiles each carry `rowSpan`/`colSpan`.
The aggregate refuses overlap, out-of-bounds spans and zero spans; the read side,
the published integration event and the persisted row all carry the spans; a
request that omits them means exactly what it meant yesterday.

**Why P1:** everything else consumes this contract. It is independently observable
over HTTP and on the bus, with no frontend.

**Independent test:** against the Aspire stack, `POST /layouts` a 3×3 wall whose
tile at (0,0) spans 2×2 plus five 1×1 tiles; `GET` it back; publish it and read the
audited `LayoutRevisionPublishedV2`.

```gherkin
Scenario: a hero-and-thumbnails wall is accepted (happy)
  Given an operator with sse.layouts.write in fab "munich" and six registered cameras
  When they POST /layouts with grid 3x3 and tiles
       (0,0) span 2x2, (0,2) 1x1, (1,2) 1x1, (2,0) 1x1, (2,1) 1x1, (2,2) 1x1
  Then the response is 201
  And GET /layouts/{id} returns revision 1 with gridRows 3, gridCols 3
  And the tile at (0,0) has rowSpan 2 and colSpan 2, every other tile rowSpan 1 and colSpan 1

Scenario: a full 3x3 of single tiles is accepted at the new cap
  When nine 1x1 tiles fill a 3x3 grid
  Then the response is 201

Scenario: omitted spans mean 1x1 (additive contract)
  When a POST or PATCH body's tiles carry no rowSpan / colSpan fields
  Then the request is accepted exactly as before this feature
  And every tile reads back with rowSpan 1 and colSpan 1

Scenario: publishing carries the spans on the integration event
  Given the hero wall above as a Draft
  When it is published
  Then LayoutRevisionPublishedV2.Tiles carries RowSpan 2 / ColSpan 2 for the (0,0) tile
  And RowSpan 1 / ColSpan 1 for every other tile
  And the event type is still LayoutRevisionPublishedV2 (no V3)

Scenario: branching a published wall keeps its spans
  Given the hero wall is Published
  When POST /layouts/{id}/draft branches revision 2
  Then revision 2's (0,0) tile still has rowSpan 2 and colSpan 2

Scenario: rows written before the migration read as 1x1
  Given a layout_revision_tiles row that existed before the migration
  When the migration has run and the layout is read
  Then its tiles have rowSpan 1 and colSpan 1 and nothing else about them changed

Scenario: two spans that intersect are refused (conflict)
  When a 3x3 wall has (0,0) span 2x2 and (1,1) span 1x1
  Then the response is 400 with code LAYOUT_TILE_OVERLAP
  And nothing is persisted

Scenario: the ADR-0112 duplicate-position case is the 1x1 instance of overlap
  When two 1x1 tiles share (0,0)
  Then the response is 400 with code LAYOUT_TILE_OVERLAP

Scenario: a span that runs off the grid is refused (bad request)
  When a 3x3 wall has (1,1) span 1x3
  Then the response is 400 with code LAYOUT_TILE_OUT_OF_BOUNDS
  # origin (1,1) is in-bounds; the span's last column (3) is not

Scenario: a zero or negative span is refused at the boundary (bad request)
  When a tile carries rowSpan 0, or colSpan -1
  Then the response is 400 with title LAYOUT_INVALID_INPUT
  # construction error (ADR-0156 §2), same path as a negative row today

Scenario: a grid over nine cells is refused (bad request)
  When the grid is 2x5 or 4x3
  Then the response is 400 with title LAYOUT_INVALID_INPUT
  # GridDimensions.From, as a 3x3 was refused before this feature

Scenario: a stale edit is still refused (conflict, unchanged)
  Given the hero wall Draft at version v
  When PATCH /layouts/{id}/revisions/1 carries If-Match of an older version
  Then the response is 409 and the draft keeps its spans
  # LayoutComposition's EditDraftRevisionErrors.LayoutRevisionStale maps to
  # Conflict (409) today, not the 412/PreconditionFailed convention
  # CameraCatalog's stale-version errors use (ADR-0119) — a pre-existing,
  # unrelated divergence. Out of scope here (plan.md never lists this
  # mapping as touched); tracked by a follow-up issue instead of "fixed"
  # silently by writing 412 into this test.

Scenario: authorization is unchanged (auth)
  When the POST carries no token
  Then the response is 401
  When the caller's token lacks sse.layouts.write
  Then the response is 403
```

### US2 (P1): The kiosk draws a spanning tile across its rectangle

A kiosk opening a wall places each tile at its origin and stretches it over its
span; cells covered by no tile show the existing empty placeholder; nothing else
about a tile (video, overlay label, highlight, alignment) changes.

**Why P1:** without it, a wall authored under US1 renders wrong — auto-placement
would flow a 2×2 hero into one cell and push the rest out of place.

**Independent test:** seed the US1 hero wall through the API, open it in the kiosk,
and read tile geometry with Playwright.

```gherkin
Scenario: a 2x2 hero occupies four cells' area (happy)
  Given the published hero wall
  When the kiosk opens it
  Then there are 6 layout-tile elements and 0 layout-empty-cell elements
  And the (0,0) tile's bounding box is about twice the width and height of the (2,2) tile
  And the (0,0) tile's style places it at grid-row 1 / span 2 and grid-column 1 / span 2

Scenario: uncovered cells still show the placeholder (sparse)
  Given a published 3x3 wall with only (0,0) span 2x2
  Then there is 1 layout-tile and 5 layout-empty-cell elements
  And no placeholder is drawn under the hero

Scenario: a 1x1 wall renders exactly as today
  Given any wall whose tiles are all 1x1
  Then every tile and placeholder occupies one cell in row-major position

Scenario: highlight still reaches a spanning tile
  Given the hero tile is bound to overlay O
  When OverlayHighlightChanged for O arrives
  Then the hero tile is highlighted

Scenario: an invalid layout from the wire is still drawn totally (defensive)
  Given a tiles payload whose span would exceed the grid
  Then the renderer clamps it to the grid rather than throwing
```

*Auth / conflict / bad request: N/A at this layer — the kiosk reads a layout the
backend already validated and authorised; its auth is unchanged (spec 041).*

### US3 (P2): An operator authors spans in the wall designer

In the management console, an operator picks a grid up to 3×3 and gives any
populated tile a row span and a column span. Cells a span covers leave the grid;
shrinking the span brings them back empty. The designer offers only spans that stay
in-bounds and do not cover another populated tile, and the shared Zod schema
enforces the same rules for anything that bypasses the UI.

**Why P2:** US1 + US2 already let a hero wall be authored (via API) and shown. The
designer is the operator's way in, and it is the largest frontend surface.

**Interaction (resolved, smallest surface):** two native `<select>`s per populated
tile, **Row span** and **Column span**, whose options are the in-bounds,
non-overlapping values. Keyboard-operable for free, consistent with spec 228's
move to native controls. ADR-0156 names "drag-to-resize or an equivalent
interaction"; this is the equivalent. Drag handles are not in scope (plan §4.3).

```gherkin
Scenario: author a hero wall (happy)
  Given the New layout dialog with the 3x3 preset selected
  When the operator assigns cameras to (0,0), (0,2), (1,2), (2,0), (2,1), (2,2)
  And sets (0,0) Row span 2 and Column span 2
  Then cells (0,1), (1,0), (1,1) are no longer shown
  And saving POSTs tiles with (0,0) rowSpan 2 colSpan 2 and the rest 1x1
  And the new layout is listed as "6 tiles, 3×3"

Scenario: nine presets up to 3x3
  Then the Grid size group offers 1×1 1×2 1×3 2×1 2×2 2×3 3×1 3×2 3×3

Scenario: only spans that fit are offered
  Given a 3x3 grid with a camera on (0,2)
  Then (0,0)'s Column span offers 1 and 2, not 3

Scenario: shrinking a span returns the cells empty
  Given (0,0) spans 2x2
  When its Row span is set to 1
  Then (1,0) and (1,1) reappear as empty cells

Scenario: a smaller preset clamps spans rather than invalidating the wall
  Given (0,0) spans 3x3 on a 3x3 grid
  When the operator picks 2x2
  Then (0,0) spans 2x2

Scenario: edit round-trips spans
  Given a published hero wall
  When the operator opens it for edit
  Then (0,0) shows Row span 2 and Column span 2 and covered cells are hidden

Scenario: the schema refuses what the UI cannot produce (bad request)
  Given a createLayoutDraftSchema input with overlapping spans, or a span off the grid, or a span of 0
  Then parsing fails with an issue on the offending tile
```

*Auth: unchanged — the dialog is behind `sse.layouts.write` as today. Conflict:
the dialog's existing 412 / chain-recovery handling (`LayoutEditorDialogChainRecovery.test.tsx`)
is untouched.*

## 3. Requirements

- **FR-001** `GridDimensions.MaxCells = 9`, `MaxTiles = 9`. `GridDimensions.From`
  accepts any `rows × cols ≤ 9` (so 1×9 and 4×2 are domain-legal — ADR-0156 §2
  keeps the rule's shape and changes the number). `Default` stays 2×2.
- **FR-002** A tile carries a span of `RowSpan ≥ 1`, `ColSpan ≥ 1`, default 1×1,
  as a value object (constitution §II — not two `int`s on `Tile`).
- **FR-003** `ValidateGrid` order and first-violation semantics are kept:
  Empty → TooLarge → OutOfBounds (**full span**) → Overlap (**cell by cell**).
  The in-bounds and overlap predicates live on the value objects
  (`GridDimensions`, `Tile`), and `ValidateGrid` calls them (ADR-0156
  §Implementation Notes).
- **FR-004** `GridViolation.DuplicatePosition` becomes `GridViolation.Overlap`;
  the error code becomes `LAYOUT_TILE_OVERLAP` in both command-error hierarchies.
  No consumer reads the old code (grep, plan §3.4).
- **FR-005** `TileRequest`, `TileDto`, `LayoutTileV2` gain `RowSpan`, `ColSpan`
  as trailing positional parameters defaulting to `1`. No field is removed or
  renamed; the event stays `LayoutRevisionPublishedV2`.
- **FR-006** EF migration adds `row_span`, `col_span` `integer NOT NULL DEFAULT 1`
  to `layout_revision_tiles`. No backfill statement; the model snapshot agrees.
- **FR-007** `Revision.NewDraft` / `Branch` preserve each tile's span.
- **FR-008** Kiosk places every tile and every uncovered placeholder explicitly
  (`grid-row: r+1 / span rs`, `grid-column: c+1 / span cs`); covered cells render
  nothing. Tile keys stay the origin `row:col`, so alignment keys do not move.
- **FR-009** TS: `MAX_TILES = MAX_CELLS = 9`; grid dimensions 1..3 each in the
  schema (the designer's surface, as 1..2 was); `LayoutTile` and `tileSchema` carry
  `rowSpan`/`colSpan`; `refineGrid` checks full-span in-bounds and cell overlap.
- **FR-010** Designer per §US3.
- **FR-011** Phase 5 records, in `verification.md` and the PR body, the 9-tile
  figures this environment can produce **and a row stating that the
  real-kiosk-hardware decode measurement was not performed**, with the follow-up
  issue that carries it. §4.

### Out of scope, stated as decisions

| What | Why |
|---|---|
| Drag-to-resize handles | ADR-0156 allows "an equivalent". Selects are the smallest, accessible surface. A later issue can add handles over the same model. |
| Designer presets beyond 3×3-bounded dimensions (1×4…1×9, 2×4, 4×2) | Same precedent as today: the designer offered `{1,2}²` while the domain admitted 1×4. The API still accepts them. |
| ScenarioSimulator span support | It seeds 1×1 walls; the defaulted fields keep it correct. Its `Matches` ignores spans, so an operator-spanned seeded wall is not re-tiled on re-seed — noted, not changed. |
| Per-tile highlight, overlay uniqueness, camera reuse | ADR-0156 §2: unchanged. |
| Changing the §IV leg-state table | ADR-0138: nothing here earns a state change. |
| Lowering the cap below 9 | Only a real-hardware measurement can justify a different number (§4). |

## 4. The NFR gate — what this spec can and cannot discharge

ADR-0156 §1 and the issue: *a real-kiosk-hardware decode measurement at 9 tiles is
required before the cap ships to production; if it fails either budget, the shipped
cap is what the measurement supports, not 9.*

**This environment has no kiosk hardware.** It has a Windows dev machine, a
Chromium under Playwright, the Aspire stack and the `fixture-video` container. So:

| Measurement | Where | Discharges the ADR-0156 gate? |
|---|---|---|
| M1 composite-and-render (`overlay_draw`) on a 9-tile vs a 4-tile wall, same machine, same run conditions, cadence read first (ADR-0123), two runs each | dev machine | **No.** Evidence that spanning placement and 9 `<video>` elements add no compositing pathology. |
| M2 SFU→kiosk decode, **in part** (`receive_to_decoded`, §IV) per tile at 9 vs 4, with each tile's `decoderImplementation` / `powerEfficientDecoder` from `RTCInboundRtpStreamStats` | dev machine | **No.** Dev GPU is not kiosk hardware; the stats say whether decode was hardware at all. |
| **M3 the same on real kiosk hardware** | **not available** | **Yes — and it is NOT PERFORMED by this spec.** |

**M3 not being performed is recorded, not deferred silently:** a named task (T040)
writes it into `verification.md` as a "NOT PERFORMED — precondition for production"
row, opens a follow-up issue on Project #13 carrying the measurement and the
"ship what it supports" rule, and puts both in the PR body. Merging to `develop` is
not shipping to production (there is no production deployment — ADR-0118), which is
why the ADR's gate and this merge are compatible; the gate travels with the
follow-up issue.

## 5. Latency budget (§IV)

- **SFU→kiosk decode (≤ 120 ms):** loaded. A wall may now open up to 9
  `RTCPeerConnection`s instead of 4. Unmeasured on kiosk hardware (§4 M3).
- **Composite + render (≤ 50 ms):** touched. Explicit grid placement replaces
  auto-placement; no new layer, animation, filter, shadow or blur. Up to 9 `<video>`
  elements instead of 4. Dev-machine figure in §4 M1.
- **Event → overlay state:** untouched. The highlight path is overlay-keyed and
  unchanged (ADR-0112 §5).
- **Camera→SFU, presentation buffer:** untouched (per-tile alignment keys unchanged).

## 6. Why no new ADR

Checked each open point against ADR-0156 / ADR-0112:

- **Span as a value object rather than `int RowSpan`/`ColSpan` on `Tile`.**
  Constitution §II decides it (`Tile.Row`/`Col` were the named precedent); ADR-0156
  names the concept, not the C# shape. Implementation detail.
- **Where the predicates live.** ADR-0156 §Implementation Notes: on the value
  objects. Decided.
- **The error-code rename.** ADR-0156 says the invariant *generalises*; a code named
  "position duplicate" for an overlap would misdescribe it. Wire-visible but no
  consumer; a detail, recorded in plan §3.4.
- **Designer interaction.** ADR-0156 explicitly leaves it ("or an equivalent").
- **1×9 / 4×2 grids.** ADR-0156 §2: "unchanged shape of the rule, new number".
- **Merge vs ship.** §4.

## 7. Decision for the Phase 1 gate

**D1 — What does the render-leg CI fixture measure after the cap rises?**
Spec 225 US2 sized `LIVE_VIDEO_WALL_TILE_COUNT = 4` as "the domain's real ceiling",
pinned by `kiosk-shows-a-label-over-video.spec.ts:124` (`toBe(4)`, message "must be
at the domain ceiling"). ADR-0156 does not mention the CI fixture. Either:

- **A — raise the fixture to 9** (a 3×3 of `fixture-video`). Keeps spec 225's
  premise true. Cost: nine concurrent decodes on a shared runner against the e2e
  shard's 45-minute timeout (unmeasured); and spec 225 US3's baseline (five
  `develop` runs on the four-tile fixture, not yet committed) must be taken on the
  nine-tile fixture instead, or retaken.
- **B — keep the fixture at 4** and correct the wording: the pin asserts 4 as "the
  measured four-tile fixture", not the ceiling. Cost: the CI gate under-reads a
  per-tile regression on a full wall by up to 9/4, which is the exact weakness
  spec 225 US2 was written to remove.

**Decided (2026-09-26, product owner): option A** — raise the fixture to 9. Spec 225
US3's baseline must be taken on the nine-tile fixture; the unmeasured 45-minute shard
timeout risk is accepted and should be watched on the first PR run, not re-litigated
here. Task T041 executes branch A.

## 8. Phase-4a colour: **red**

New behaviour: new invariants (full-span bounds, overlap), a new value object, new
contract fields, new rendering, new designer controls. Every US1–US3 scenario must
be observed red on unmodified code, **except these, declared green in advance** as
pins whose green result is not 4a evidence:

- "authorization is unchanged" (existing behaviour, characterisation).
- "a 1x1 wall renders exactly as today" (kiosk characterisation).
- "a grid over nine cells is refused" for 4×3 (refused today too; 2×5 likewise).

Two that look like pins and are **not**, so a green one is a defect in the test:

- "omitted spans mean 1x1" must be red today, because it asserts `rowSpan`/`colSpan`
  on the read-back, which the DTO does not carry yet. Green means it is not reading
  the field.
- "the ADR-0112 duplicate-position case" must be red today, because the code string
  changes to `LAYOUT_TILE_OVERLAP`.
- "a stale edit is still refused" must be red today: the 409 path is unchanged, but
  the scenario's setup (a 3×3 hero draft) cannot be created yet.

## 9. Independent end-to-end test procedure

1. `dotnet test tests/LayoutComposition.Domain.Tests tests/LayoutComposition.Application.Tests tests/Shared.Contracts.Tests tests/Architecture.Tests` — green (including `PrimitiveBoundaryTests`).
2. `dotnet test tests/Integration.Tests --filter "FullyQualifiedName~LayoutComposition"` against the Aspire fixture — green, including the new span and migration tests.
3. `pnpm --filter @smart-sentinel-eye/shared test`, `--filter management-web test`, `--filter kiosk-web test`, `lint`, `typecheck` — green.
4. `npx playwright test e2e/spanning-wall.spec.ts` — the designer authors the hero wall, publishes it, and the kiosk renders the hero at twice a thumbnail's size.
5. Live (Phase 5): boot the stack, author the hero wall by hand in the console, open it on a kiosk tab, screenshot; then run M1 and M2 (§4) and record them with cadence and decoder implementation.

## 10. Success criteria

- **SC-001** Every US1 scenario passes against the Aspire stack.
- **SC-002** A published hero wall's `LayoutRevisionPublishedV2` carries the spans (integration test reading the event).
- **SC-003** The kiosk renders the hero wall with the hero's box ≈ 2× a thumbnail's in each axis (e2e).
- **SC-004** The designer authors and edits the hero wall with keyboard only (component test + e2e).
- **SC-005** `verification.md` holds M1 and M2 figures with machine, cadence and decoder implementation, **and an explicit NOT PERFORMED row for M3** linked to its follow-up issue.
