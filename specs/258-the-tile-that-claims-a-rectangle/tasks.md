# Tasks 258 — The tile that claims a rectangle

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Issue**: #2607
(feature-level issue, on Project #13, Todo; **no per-task issues**, no
`/speckit-taskstoissues`).
**Lane**: supervised (ADR-0037). **Phase 4a colour**: **RED** (behaviour-changing,
ADR-0139); the declared-green pins are listed in spec §8.
**Engineers**: `test-writer` (4a) → `backend-engineer` (4b, US1) → `test-writer` (4a
frontend, may run earlier, see below) → `frontend-engineer` (4b, US2 + US3).
**Reviewers**: `backend-reviewer`, `frontend-reviewer`.

Format: `[ID] [P?] [Story] description — file(s)`.

**Foundational / blocking.** T010–T013 (domain value objects and `ValidateGrid`) block
every other backend 4b task. T016 (`Shared.Contracts.LayoutTileV2`) is disjoint and can
run beside them. **T030 (the shared TS schema/type) blocks T031 and T032**; after it,
kiosk (US2) and designer (US3) own disjoint files and fan out (ADR-0109). No AppHost,
ServiceDefaults or Shared.Kernel task exists.

**Gate reminders.** D1 (spec §7) is **answered: option A** — raise the CI fixture to 9
tiles, confirmed directly by the product owner on 2026-09-26. T041 executes branch **A**
only; branch B's text stays in T041 as the rejected alternative, not a live option.
The PR must not merge before ADR-0156's PR #2606.

**Do not touch**: `OverlayHighlightRequestedV1` and its handler, `layoutHub.ts`,
`ScenarioSimulator/**`, `AuditObservability/**`, the §IV table in the constitution,
`docs/adr/**`.

---

## Phase 4a — backend tests first (test-writer; observe red, return verbatim output)

- [ ] **T001** [P] [US1] Domain tests, per plan §6.1 (rewrites) and §6.2 (new):
  new `TileSpanTests.cs`; extend `TileTests.cs` (3-arg ctor → `TileSpan.Single`;
  `Overlaps` cases: corner touch false, shared edge false, containment true, partial
  true, identical origin true, symmetric); `GridDimensionsTests.cs` (cap 9, accepted
  3×3/1×9/3×2, rejected 2×5/4×3/10×1, full-span `Contains`); `LayoutTests.cs` and
  `LayoutGridInvariantTests.cs` (hero wall and full 3×3 valid; overlap → `Overlap`;
  span off the edge → `OutOfBounds`; oversize uses 4×3; 10 tiles on 3×3 → `TooLarge`;
  order preserved when several apply); new `LayoutBranchSpanTests.cs` (BranchDraft of a
  published hero wall keeps the 2×2 span). Rewrites change only what plan §6.1 lists —
  `tests/LayoutComposition.Domain.Tests/Layout/`
- [ ] **T002** [P] [US1] Application tests: `GetLayoutQueryHandlerTests` (DTO carries
  spans); `LayoutRevisionPublishedDomainEventHandlerTests` (V2 carries spans);
  `CreateLayoutDraftCommandHandlerTests` and `EditDraftRevisionCommandHandlerTests`
  (overlap → `LAYOUT_TILE_OVERLAP`, span off the edge → `LAYOUT_TILE_OUT_OF_BOUNDS`) —
  `tests/LayoutComposition.Application.Tests/`
- [ ] **T003** [P] [US1] Contract tests: positional ctor defaults `RowSpan`/`ColSpan` to
  1; JSON without the two fields deserialises as 1×1; JSON round-trip of a 2×2 tile —
  `tests/Shared.Contracts.Tests/LayoutRevisionPublishedV2Tests.cs`
- [ ] **T004** [P] [US1] Integration tests over HTTP against the Aspire fixture, one
  `[Fact]` per US1 scenario in spec §2 (hero 201 + GET spans; full 3×3; omitted spans →
  1×1; publish → audited/observed `LayoutRevisionPublishedV2` carries spans; branch keeps
  spans; migration default via raw-SQL insert naming no span column, then GET → 1×1;
  overlap 400 `LAYOUT_TILE_OVERLAP`; duplicate origin 400 `LAYOUT_TILE_OVERLAP`; span off
  edge 400 `LAYOUT_TILE_OUT_OF_BOUNDS`; span 0 / −1 400 `LAYOUT_INVALID_INPUT`; 2×5 400;
  stale If-Match 409 keeps spans; 401; 403). Anonymous-object bodies, so it compiles
  today. Raw-SQL precedent: `CameraCatalog/StaleIdempotencyReservationIntegrationTests.cs` —
  `tests/Integration.Tests/LayoutComposition/TileSpanIntegrationTests.cs`
- [ ] **T005** [US1] On unchanged production code run
  `dotnet test tests/LayoutComposition.Domain.Tests tests/LayoutComposition.Application.Tests tests/Shared.Contracts.Tests`
  and `dotnet test tests/Integration.Tests --filter "FullyQualifiedName~TileSpanIntegrationTests"`;
  capture **verbatim**. **Required**: T001–T003 red by **compile error** naming
  `TileSpan` / `GridViolation.Overlap` / `RowSpan` (quote the errors); T004 compiles and
  every scenario is red **on its assertion** except the declared pins (401, 403, and
  4×3/2×5 → 400), which are green and labelled as characterisation. The 409 scenario is
  **red** today, not a pin: its setup cannot create a 3×3 hero wall yet. Any other green
  → stop and report; do not adjust the test until it is red. Depends on T001–T004.

## Phase 4b — backend (backend-engineer; tests may not be edited)

- [ ] **T010** [US1] `TileSpan` value object per plan §2.1 (`Single`, `From` with
  `Ensure.That(...).AtLeast(1)`) — `src/LayoutComposition/Domain/Layout/TileSpan.cs`.
  Depends on T005.
- [ ] **T011** [US1] `Tile`: private `rowSpan`/`colSpan`, `Span` property, 4-arg public
  ctor, 3-arg ctor delegating with `TileSpan.Single`, EF ctor gains `rowSpan`/`colSpan`,
  `Overlaps(Tile)`; doc comment updated (ADR-0156) —
  `src/LayoutComposition/Domain/Layout/Tile.cs`. Depends on T010.
- [ ] **T012** [P] [US1] `GridDimensions`: `MaxTiles = MaxCells = 9`,
  `Contains(GridPosition, TileSpan)`; doc says 9 is ADR-0156's ceiling and **not
  verified on kiosk hardware** — `src/LayoutComposition/Domain/Layout/GridDimensions.cs`.
  Depends on T010.
- [ ] **T013** [US1] `GridViolation.DuplicatePosition` → `Overlap`; `ValidateGrid` step 3
  full-span, step 4 pairwise `Overlaps`; class doc updated —
  `src/LayoutComposition/Domain/Layout/GridViolation.cs`, `Layout.cs`. Depends on T011, T012.
- [ ] **T014** [US1] `Revision.NewDraft` clones with `tile.Span` —
  `src/LayoutComposition/Domain/Layout/Revision.cs`. Depends on T011.
- [ ] **T015** [US1] Errors: `TilePositionDuplicate` → `TileOverlap` /
  `LAYOUT_TILE_OVERLAP`, static factory renamed, `TileOutOfBounds` message — both
  `src/LayoutComposition/Application/Commands/CreateLayoutDraftErrors.cs` and
  `EditDraftRevisionErrors.cs`. Depends on T013.
- [ ] **T016** [P] [US1] `LayoutTileV2` gains trailing `int RowSpan = 1, int ColSpan = 1`;
  doc on both records notes the in-place extension (ADR-0156) —
  `src/Shared.Contracts/LayoutComposition/LayoutRevisionPublishedV2.cs`. Depends on T005.
- [ ] **T017** [US1] `TileDto` gains `RowSpan`/`ColSpan`; `GetLayoutQueryHandler.MapTiles`
  and `LayoutRevisionPublishedDomainEventHandler` map `tile.Span` —
  `src/LayoutComposition/Application/DTOs/LayoutDto.cs`,
  `Application/Queries/Handlers/GetLayoutQueryHandler.cs`,
  `Application/EventHandlers/LayoutRevisionPublishedDomainEventHandler.cs`. Depends on T011, T016.
- [ ] **T018** [US1] `TileRequest` gains `RowSpan = 1`/`ColSpan = 1`; `ParseTiles` builds
  `TileSpan.From(...)` inside the existing `try` —
  `src/LayoutComposition/Api/Requests/CreateLayoutRequest.cs`,
  `src/LayoutComposition/Api/LayoutEndpoints.Commands.cs`. Depends on T011.
- [ ] **T019** [US1] EF mapping (`rowSpan`/`colSpan` → `row_span`/`col_span`,
  `HasDefaultValue(1)`, `Ignore(Span)`) and `dotnet ef migrations add TileSpans`.
  **Accept only** an `Up` of exactly two `AddColumn<int>(nullable: false, defaultValue: 1)`
  and a matching `Down`; otherwise stop and report —
  `src/LayoutComposition/Infrastructure/Persistence/Configurations/LayoutConfiguration.cs`,
  `Infrastructure/Persistence/Migrations/*_TileSpans*.cs`, `LayoutCompositionDbContextModelSnapshot.cs`.
  Depends on T011.
- [ ] **T020** [US1] Stop any running AppHost first. Run the T005 commands plus
  `dotnet test tests/Architecture.Tests` (incl. `PrimitiveBoundaryTests`,
  `HandlerDeconstructionTests`) and the full `LayoutComposition` integration filter;
  `dotnet format --verify-no-changes`; Release build clean. T001–T004 green and
  **unmodified** (`git diff` on the test files since T005 is empty). Commit per plan §7,
  each commit building alone. Depends on T010–T019.

## Phase 4a — frontend tests first (test-writer)

These own files disjoint from every backend task and assert against today's frontend
code, so they **may be written in parallel with Phase 4b backend**. T024 needs the
backend from T020 running.

- [ ] **T021** [P] [US3] Schema tests: 3×3 accepted, 4-row grid refused, full-span OOB
  issue on the tile's index, overlap issue on the *later* tile's index, span 0 refused,
  10 tiles refused — new `apps/shared/src/api/layouts.schema.test.ts`
- [ ] **T022** [P] [US2] Kiosk: new `wallGrid.test.ts` (hero → 6 items, 0 placeholders,
  origin keys, row-major order; sparse 3×3 hero → 1 tile + 5 placeholders; all-1×1 wall
  identical to today's row-major list — characterisation; span past edge clamped;
  overlapping second tile dropped); `CellPage.test.tsx`: add a hero-wall case asserting
  the hero element's `grid-row`/`grid-column` style, and add `rowSpan: 1, colSpan: 1` to
  existing `LayoutTile` fixtures (type change only) —
  `apps/kiosk-web/src/features/cell/wallGrid.test.ts`, `CellPage.test.tsx`
- [ ] **T023** [P] [US3] Designer: new `gridDesignerModel.test.ts` (nine presets;
  `buildCells` clamps spans on shrink; `spanOptions` stops at the grid edge and before a
  populated cell; `coveredBy`; `cellsFromTiles`/`tilesFromCells` round-trip spans;
  clearing a camera resets its span); new `GridDesignerSpans.test.tsx` (set (0,0) to 2×2
  → three cells hidden; shrink → they reappear **empty**; Column span options with a
  camera on (0,2) are exactly 1,2; keyboard-only operation; edit flow shows stored
  spans); `GridDesignerKeyboard.test.tsx` preset count 4 → 9 —
  `apps/management-web/src/features/layouts/`
- [ ] **T024** [P] [US2][US3] e2e: register six cameras via the existing support helpers,
  author the hero wall in the console (3×3, spans), publish, open **by name** in the kiosk,
  assert 6 `layout-tile`, 0 `layout-empty-cell`, hero box ≈ 2× the (2,2) tile in each
  axis — new `e2e/spanning-wall.spec.ts`
- [ ] **T025** [US2][US3] On unchanged frontend code run
  `pnpm --filter @smart-sentinel-eye/shared test`, `--filter kiosk-web test`,
  `--filter management-web test`, and (against the T020 backend) `npx playwright test e2e/spanning-wall.spec.ts`;
  capture **verbatim**. Required: every new case red (missing module / missing preset /
  missing control / assertion), the all-1×1 characterisation green. Type errors from the
  new `rowSpan` fixtures are expected red and quoted. Depends on T021–T024 (T024 also on T020).

## Phase 4b — frontend (frontend-engineer; tests may not be edited)

- [ ] **T030** [US2][US3] **Foundational for the frontend.** `MAX_TILES = MAX_CELLS = 9`,
  grid dims `.max(3)`, `tileSchema` spans, `refineGrid` full-span + occupancy overlap;
  `LayoutTile` gains `rowSpan`/`colSpan` —
  `apps/shared/src/api/layouts.schema.ts`, `apps/shared/src/api/layouts.api.ts`.
  Depends on T025.
- [ ] **T031** [P] [US2] `buildGridItems` per plan §4.2; `CellPage` renders items with
  explicit `grid-row`/`grid-column` span placement and `gridTemplateRows`; old
  `buildGridCells`/`positionKey` removed; no new class, animation, filter or shadow —
  new `apps/kiosk-web/src/features/cell/wallGrid.ts`, `CellPage.tsx`. Depends on T030.
- [ ] **T032** [P] [US3] Model per plan §4.3 (presets, spans on `DesignerCell`,
  clamp, `coveredBy`, `spanOptions`, reset-on-clear) —
  `apps/management-web/src/features/layouts/gridDesignerModel.ts`. Depends on T030.
- [ ] **T033** [US3] New `TileSpanFields.tsx` (two native selects, `valueAsNumber`);
  `GridDesigner.tsx` renders uncovered cells with explicit placement and the span fields
  on populated cells — `apps/management-web/src/features/layouts/TileSpanFields.tsx`,
  `GridDesigner.tsx`. Depends on T032.
- [ ] **T034** [US2][US3] Run all three workspaces' `test`, `lint` (`--max-warnings 0`),
  `typecheck`, then `e2e/spanning-wall.spec.ts` plus `e2e/layouts.spec.ts` and
  `e2e/kiosk-shows-a-wall.spec.ts` (regression). T021–T024 green and **unmodified**.
  Depends on T031, T033.

## Gate-dependent

- [ ] **T041** [US2] **Apply D1 as answered at the Phase 1 gate** (spec §7). Either:
  **A** — `LIVE_VIDEO_WALL_TILE_COUNT = 9`, the seed builds a 3×3, the pin becomes
  `toBe(9)`, spec 225's baseline note is updated to say which fixture it must be taken
  on, and the e2e shard's wall clock on the PR run is recorded; or **B** — count stays 4,
  the pin's comment and message say "the measured four-tile fixture", not "the domain
  ceiling", and `live-video-wall.ts:21-27` likewise. Either way the words "domain
  ceiling" stop describing 4 —
  `e2e/support/live-video-wall.ts`, `e2e/support/seed-live-video-wall.setup.ts`,
  `e2e/kiosk-shows-a-label-over-video.spec.ts`. Depends on T034 and the D1 answer.

## Phase 5 — verify (`/verify`)

- [ ] **T038** [US1-3] Observe end to end on the live stack (check the AppHost PID's
  start time is after the last commit): author the hero wall in the console, publish,
  open it on a kiosk tab, screenshot; highlight the hero's overlay and see it light.
  Record in `specs/258-the-tile-that-claims-a-rectangle/verification.md`. Depends on T034, T041.
- [ ] **T039** [US2] **NFR, measurable here — M1 and M2** (spec §4, plan §8 steps 2–3):
  9-tile 3×3 vs 4-tile 2×2 on `fixture-video`, dev machine, two runs each. Record
  cadence first, then `overlay_draw` p50/p95/max, per-tile `receive_to_decoded`, and each
  tile's `decoderImplementation` / `powerEfficientDecoder` / `framesDropped`. Name the
  machine, GPU, browser build. Every figure written into `verification.md` as observed. Depends on T038.
- [ ] **T040** [US1-3] **NFR gate, NOT measurable here — M3, the real-kiosk-hardware
  decode measurement at 9 tiles, recorded as NOT PERFORMED.** In `verification.md` add a
  row: *"M3 — 9-tile decode and composite-and-render on real kiosk hardware: **NOT
  PERFORMED** (no kiosk hardware in this environment). Precondition for shipping
  `MaxTiles = 9` to production (ADR-0156 §1). T039's figures are dev-machine evidence
  and do not discharge it."* Open a follow-up issue carrying: the measurement procedure
  (plan §8), the budgets (≤ 50 ms composite-and-render, ≤ 120 ms SFU→kiosk decode,
  cadence ≥ 40 Hz per ADR-0123), and the rule *"if either budget fails, ship the cap the
  measurement supports, not 9"*; add it to Project #13
  (`gh project item-add 13 --owner smartsolutionslab --url <url>`, verify with
  `--limit 2000`); link it from `verification.md`, the PR body, and a comment on #2607.
  **This task may not be skipped or folded into T039.** Depends on T039.

## Phase 6–7

- [ ] **T050** `/code-review` with `backend-reviewer` and `frontend-reviewer`. Findings
  resolved or accepted in writing. Depends on T040.
- [ ] **T051** Re-check spec number 258 against all branches; confirm #2606 is merged
  (else rebase onto it, never stack silently); `gh pr create --base develop`. PR body:
  both 4a outputs verbatim (T005, T025), the plan §6.1 list of rewritten tests with the
  reason, §IV legs touched (decode loaded, composite-and-render touched, event→overlay
  untouched), T039's figures, **and the T040 NOT PERFORMED statement with the follow-up
  issue link**. Closing keyword for #2607; check its state after merge. Depends on T050.

## Dependencies

```
T001 ─┐
T002 ─┼→ T005 → T010 → T011 ─┬→ T013 → T015 ─┐
T003 ─┤         └──→ T012 ───┘               │
T004 ─┘         T016 (∥ T010..T015) ─→ T017 ─┤
                T011 → T014 ─────────────────┤
                T011 → T018 ─────────────────┤
                T011 → T019 ─────────────────┴→ T020
T021 ─┐
T022 ─┼→ T025 → T030 ─┬→ T031 ───────────┐
T023 ─┤               └→ T032 → T033 ────┴→ T034 → T041 → T038 → T039 → T040 → T050 → T051
T024 ─┘ (T024 run needs T020)
```

**Fan-out**: T001 ∥ T002 ∥ T003 ∥ T004; T012 ∥ T016 alongside T011; T021 ∥ T022 ∥ T023 ∥ T024
(and concurrently with backend 4b); T031 ∥ T032. One test-writer doing T001–T004 serially is
equally fine. Backend 4b is one engineer: T013–T019 touch closely related files and are
small.
