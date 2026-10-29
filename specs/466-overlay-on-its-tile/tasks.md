# Tasks 466 — An overlay seen on its tile

Spec: [spec.md](spec.md) · Plan: [plan.md](plan.md) · Research: [research.md](research.md) · Issue #3184 (feature-level, on Project #13, status Todo).
Engineer: **frontend**. No backend, `Shared.Contracts`, migration, AppHost or ADR change.

Phase 4a colour, per task group (architect's declaration, ambiguity resolves to red):
- **T001–T002 behaviour-preserving → characterisation, observed green first**, unchanged after. A refactor with no covering test is a rewrite.
- **Everything else is new behaviour → red first**, failure quoted in the PR (ADR-0139/0144).

Every `[P]` task touches files no other open task touches. The test files below are new, so no shard-filter edit is needed (those exist for `Integration.Tests` only).

## Phase 1 — Setup

None. The folders are created by the first task that needs them.

## Phase 2 — Foundational (blocks every story)

- [x] **T001** (test-writer) Characterise `OverlayDraftForm` before it is touched: run `apps/management-web/src/features/overlays/OverlayDraftForm*.test.tsx`, `OverlayEditPage*.test.tsx`, `OverlayCreatePage.test.tsx` and `apps/shared` composite tests for `OverlayEditor`; **quote the green output as the baseline**. Add a characterisation test only where the draft logic the hook will own (create vs edit-after-publish, save, stale-conflict message, legibility and placeholder advisories) has no covering test. Counterfactual: temporarily break one branch and show the test fails (memory: prove a guard by counterfactual).
- [x] **T002** ~~Extract `useOverlayDraft`~~ **Dropped (owner decision 2026-10-29).** `OverlayDraftForm` already takes `canvasWidthPx/HeightPx`, `readOnly`, `editTarget`, `onDone`, `onCancel`; the view embeds it instead. The two optional props it still needs (backdrop camera, layout mode) are added in T013 under red tests, and the T001 baseline must pass unmodified there. The ID stays so references do not shift.
- [ ] **T003** [P] (test-writer) `apps/management-web/src/features/layouts/tileView/displayShapes.test.ts`: the five shapes (16:9, 16:10, 21:9, 4:3, 9:16); cell pixel size for a given shape, grid and span; a span never leaves the grid; the wall's own `p-1`/`gap-1` accounted for; scaling to a preview area. Run; **quote red**.
- [ ] **T004** [P] (frontend) `.../tileView/displayShapes.ts`: pure functions for the above — the **cell** size only. The 16:9 viewer box inside the cell depends on what T005 observes and is implemented in T007. Depends on T003 (red observed); T003 passes unmodified.
- [x] **T005** (frontend) **Observation, not code.** On the seeded nine-tile wall, record how a cell flatter than 16:9 treats the viewer box — clipped or shrunk (research R1 open item) — with the measured pixel sizes at two viewport sizes. Write it into `research.md` R1 and into `displayShapes.test.ts` as the pinned expectation before any frame-size assertion exists. Depends on T004. Needs the Aspire stack: stop any other AppHost first (one machine, one stack); check free RAM first.

## Phase 3 — User story 1 (P1): see an overlay on the tile it will occupy (FR-001–004, 008, 009, 012 read-only, 014, 015)

**Independent test:** open a designer cell that has a camera and a published overlay with a placeholder; the frame shows the picture, the overlay and the resolved value.

- [ ] **T006** [US1] (test-writer) `.../tileView/TileView.test.tsx` and `TileFrame.test.tsx` (Testing Library, RTK Query mocked at the API boundary like the neighbouring tests): frame has the chosen shape; each element at its normalised position and size; a placeholder shows its resolved value, not `{{…}}`; no overlay → picture only + offer to assign; capture failure → neutral backdrop + notice, overlay still shown; overlay with a draft but no Published revision → draft + "the wall shows nothing" notice; archived-after-assignment → no overlay + notice; the view shows the designer's **unsaved** cell state; the text readout lists every element with kind, resolved text and position in percent; opening and reading work with the keyboard. Run; **quote red**.
- [ ] **T007** [US1] (frontend) `.../tileView/TileFrame.tsx`: one tile at a pixel size — backdrop + overlay layer using `CameraViewer`'s overlay rendering and `overlayLabelSurfaceStyle`/`overlayShapeStyle` (no second renderer, research R1), plus the viewer box inside the cell exactly as T005 recorded it (clipped or shrunk), with its test. Depends on T004, T005, T006.
- [ ] **T008** [US1] (frontend) `.../tileView/TileView.tsx` and `TileViewReadout.tsx`: display-shape chooser (default 16:9, optional guarded `localStorage`), frame, notices, readout, backdrop via `useFrameCapture`/`FrameGrabber`. Depends on T007.
- [ ] **T009** [US1] (test-writer) Extend `GridDesigner`/`LayoutEditorDialog` tests: an "Open tile view" control exists only on populated cells; opening passes the form's current `cells`/`grid` (an unsaved camera change is visible); assigning an overlay in the view sets that cell's `overlayIdentifier` in the dialog form and saves nothing; closing writes nothing. Run; **quote red**.
- [ ] **T010** [US1] (frontend) Wire it: `GridDesigner.tsx` control, `LayoutEditorDialog.tsx` hosts `TileView` and `onAssignOverlay`. Depends on T008, T009; existing layout tests pass unmodified.

## Phase 4 — User story 2 (P2): edit the overlay in place (FR-005–007, 009, 010, 012 edit, 013, 015)

**Independent test:** in the view, drag an element and change its text; the frame follows, and the saved draft reopens identically.

- [ ] **T010a** [US2] (frontend + test-writer) Establish how `management-web` knows an operator may write overlays. The navigation declares read scopes only (`shellEntries.ts`: `sse.overlays.read`); find whether feature code can read a write scope. If it can, expose it as `canEditOverlays`; if it cannot, the editor is offered and a 403 from the existing mutation shows the existing refusal message, with the view read-only afterwards. Record the finding in `research.md` R8 (replacing "the way `OverlaysPage` does", which is unverified) and cover the chosen behaviour with a test, red first. Blocks T011 and T013.
- [ ] **T011** [US2] (test-writer) `.../tileView/TileViewEditing.test.tsx` and `useOverlayUsage.test.ts`: moving/resizing/colour/text/add/remove update the frame as they happen; editing a Published revision goes to a draft and the Published one is untouched; the same legibility and placeholder advisories as the editor; stale conflict shows the existing message; without the overlay-write right the editor is `readOnly` with a notice, and an operator who has the layout right but not the overlay right can still open the view (FR-012); "at least N tiles" counts Draft and Published tiles across `listLayouts` with the open layout's unsaved cells replacing its saved ones; an "Open in the overlay editor" link is present and keyboard-reachable; the legacy editor route still works. Run; **quote red**.
- [ ] **T012** [P] [US2] (frontend) `.../tileView/useOverlayUsage.ts`. Depends on T011 (red observed). Different file from T013.
- [ ] **T013** [US2] (frontend) Embed `OverlayDraftForm` in `TileView` with the canvas size from `displayShapes` and `readOnly` from `canEditOverlays`, adding only the two optional props (backdrop camera, layout mode); the shared-overlay notice from `useOverlayUsage`; the open-in-editor link. Depends on T001, T010, T010a, T011; T001 tests pass unmodified.

## Phase 5 — User story 3 (P3): judge the overlay against its neighbours (FR-011, 014)

**Independent test:** open a tile in a multi-tile layout; the others appear around it in their cells, each with its still and its overlay; the open tile is marked.

- [ ] **T014** [US3] (test-writer) `.../tileView/useNeighbourStills.test.ts` and a `TileView` neighbour test: the open tile and the neighbours share **one** capture queue (the open tile first, then neighbours nearest first), so two captures are never in flight even though each `useFrameCapture` instance only guards itself; each bounded at 10 s; a failed or timed-out neighbour keeps a neutral backdrop with its overlay; stills cached per camera while open and **released on close**; a spanning tile occupies its cells; a covered cell has no frame of its own; neighbours are read-only; the open tile is marked. Run; **quote red**.
- [ ] **T015** [US3] (frontend) `.../tileView/useNeighbourStills.ts` and the neighbour layout in `TileView.tsx`. Depends on T013, T014.

## Phase 6 — Cross-cutting

- [ ] **T016** (test-writer) `e2e/overlay-tile-view-matches-the-wall.spec.ts` (SC-002), modelled on `kiosk-label-scales-with-its-tile.spec.ts` and `overlay-editor-preview-scale.spec.ts`, reading expected values from `e2e/support/live-video-wall.ts` rather than restating them (memory: an assertion must not check its own input). On the seeded nine-tile wall: element boxes within 1 % of tile width/height, text line count and per-line text equal, computed colour and opacity equal. Run; **quote red** (no view exists on `develop`). Prove it can fail: perturb one element's geometry in the view and show the spec catches it.
- [ ] **T017** (frontend) Make T016 pass without editing it; any gap it finds in the shared rendering is fixed in the shared code, not by relaxing the tolerance. Depends on T015, T016.
- [ ] **T018** (frontend) Gates: `prettier --check` explicitly (eslint clean ≠ prettier clean), lint, `tsc --noEmit`, Vitest for the touched packages, `typecheck:e2e` (fails on a clean `develop`; stash and compare before blaming this branch). Depends on T017.
- [ ] **T019** `/verify` (Phase 5): run the quickstart end to end; quote SC-004 (frame updates ≤ 1 s after an edit settles — measured in the browser from the edit event to the frame's changed element via a performance mark, twice), SC-006 (event→overlay on the wall not worse — **run twice**, spec 108's e2e, before and after), the N−1 neighbour-capture count on the nine-tile wall, and the honest limit on SC-002 (computed style, not blended pixels). Depends on T018.

## Dependencies and order

- T001 → T013 (T002 dropped). T003 → T004 → T005 → T007.
- T006 → T007 → T008; T009 + T008 → T010; T010 → T013; T010a → T011 → T012, T013; T013 + T014 → T015; T015 + T016 → T017 → T018 → T019.
- **Parallel:** T003/T004 with T001/T002 (disjoint files); T012 with T013's component work once T011 is red; T014 can be written while T013 is in progress.
- **Phase 4 gate for each group:** red observed and quoted before its implementation task starts; commits follow ADR-0030 (`test(466): …` then `feat(466): …`, no `Co-Authored-By`, ADR-0086).

## Implementation strategy

- **MVP = Phase 2 + User story 1** (view only, no editing, no neighbours). It already ends the guessing about legibility and placement and could ship alone.
- Then US2, then US3. Each is independently testable and each leaves the existing editor unchanged (FR-013).
- Not in this feature: the chat editor, an exact cross-fab usage count, any change to the wall. If T005 shows the wall clips a flat cell, record it as an observation in the verification note; changing the wall is a separate issue.
