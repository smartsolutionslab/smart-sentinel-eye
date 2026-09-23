# Tasks 228 — What the frontend review left

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Issue**: #2306 (feature-level; already on Project #13)
**Engineer**: `frontend-engineer` · **4a author**: `test-writer`
**Phase 4a colour**: two colours, sequenced — CHARACTERISATION first (items 5, 6), then RED (items 2, 3, 4). Spec §6.

Format: `[ID] [P?] [Story] description — files`. `[P]` = owns disjoint files (ADR-0109).
There is **no foundational task**: nothing here touches `Shared.Kernel`, `Shared.Contracts`
or AppHost, so nothing blocks the rest. The items are mutually independent; within one
branch they run sequentially (one index), but any order is valid.

## Phase 4a-green — characterisation (test-writer)

- [ ] **T001** [P] [US5] Run `apps/shared` `wallAlignment.test.ts`; capture the green output verbatim. Then set `trial = null` at `wallAlignment.ts:425`, run again, capture the red output naming the marked-tile recovery tests, **restore** the line (`git diff` empty). — read-only on `wallAlignment.test.ts`
- [ ] **T002** [P] [US4] In `apps/kiosk-web/src/features/cell/useWallAlignment.test.ts`, add "Reports no frame age for a tile that reported and then aged out" (plan §3 item 5: precondition `toBe(150)`, then `toBeNull()` after > 15 s of survivor-only cycles). Run; capture green. Counterfactual: delete `useWallAlignment.ts:145` (`lagsRef.current.delete(tileKey)`), run, capture red, **restore**. — `useWallAlignment.test.ts`

## Phase 4a-red — new behaviour (test-writer)

- [ ] **T003** [P] [US3] Add a case to `apps/shared/src/observability/kioskLatency.test.ts`: with `vi.stubEnv('DEV', false)` (unstub in `afterEach`), a valid sample writes no `[latency]` console line **and** still calls `fetch` once. Run; capture red. — `kioskLatency.test.ts`
- [ ] **T004** [P] [US1] Create `apps/management-web/src/features/layouts/GridDesignerKeyboard.test.tsx` covering spec US1's four scenarios (one tab stop in and out, ArrowRight moves selection and grid, click still works, radios named `1×1`…`2×2` in a group named "Grid size"). Harness: mirror `GridDesignerRetainedOption.test.tsx`. Run; capture red. If user-event cannot drive radio arrows in jsdom (spec A4), say so verbatim and move the keyboard scenarios to `e2e/layouts.spec.ts` instead. — new file
- [ ] **T005** [P] [US2] Create `apps/shared/src/ui/composites/CameraViewerAnnouncement.test.tsx` covering spec US2 (region present and empty while live; "Reconnecting…", "Stream is offline", "Viewer error"; cleared on recovery; overlay `aria-hidden`; video named with and without `cameraName`). Harness: mirror `CameraViewer.test.tsx` (`@vitest-environment jsdom`, `FakePeerConnection`, mocked `useGetStreamQuery`). Run; capture red. — new file
- [ ] **T006** [US2] Add one test to `apps/management-web/src/features/cameras/CameraDetailPage.test.tsx`: the mocked `CameraViewer` receives `cameraName === record.name`. Extend the existing mock's props type (`:64-65`); do not change any existing assertion. Run; capture red. — `CameraDetailPage.test.tsx` (depends on nothing; separate file from T005)

**Gate to 4b**: every T003-T006 output is red for the stated reason; any that arrives green stops the lane (ADR-0144).

## Phase 4b — implementation (frontend-engineer)

- [ ] **T007** [P] [US5] Hoist the trial target above the marked-tile loop in `apps/shared/src/observability/wallAlignment.ts` (plan §3 item 6). `wallAlignment.test.ts` green and **byte-unmodified**. — `wallAlignment.ts`
- [ ] **T008** [P] [US4] Delete the entailed `.not.toBe(0)` at `useWallAlignment.test.ts:380` and `apps/shared/src/observability/labelDelay.test.ts:32`. — `useWallAlignment.test.ts` (after T002), `labelDelay.test.ts`
- [ ] **T009** [P] [US3] Gate the `console.info` in `apps/shared/src/observability/kioskLatency.ts:87` on `import.meta.env.DEV`; rewrite `:81-86`'s comment (plan §3 item 4). T003 green; existing `:97` case unmodified and green; `CameraViewerSamplerWindow.test.tsx` green. — `kioskLatency.ts` (after T003)
- [ ] **T010** [P] [US1] Convert `apps/management-web/src/features/layouts/GridDesigner.tsx` to native radios (plan §3 item 2; FR-001-FR-003). T004 green; `LayoutEditorDialog.test.tsx` unmodified and green. — `GridDesigner.tsx` (after T004)
- [ ] **T011** [US2] In `apps/shared/src/ui/composites/CameraViewer.tsx`: `announcementFor`, the always-mounted `sr-only` status region, `aria-hidden` on `ViewerOverlay`, `cameraName` prop and `aria-label` on `<video>` (plan §3 item 3; FR-004-FR-007). T005 green; all existing `CameraViewer*.test.tsx` unmodified and green. — `CameraViewer.tsx` (after T005)
- [ ] **T012** [US2] Pass `cameraName={record.name}` at `apps/management-web/src/features/cameras/CameraDetailPage.tsx:147`. T006 green. — `CameraDetailPage.tsx` (after T006, T011: the prop must exist)

## Phase 4 close — whole-suite checks (frontend-engineer)

- [ ] **T013** `npm run lint`, `npm run typecheck`, `npm test` in all three workspaces; `npm run typecheck:e2e` (a failure on clean `develop` is pre-existing — stash and compare before touching config). — no files

## Phase 5 — verify

- [ ] **T014** Spec §4 steps 2-4 against the running stack (keyboard on the grid picker; screen reader on a camera detail page with a provoked outage; `[latency]` present under `vite dev`, absent under `vite preview` while the `POST` continues). Record observations on the PR.
- [ ] **T015** Run `e2e/layouts.spec.ts`, `e2e/camera-detail.spec.ts`, `e2e/kiosk-shows-a-label-over-video.spec.ts`; confirm `[legs] N [latency] line(s) captured` with N > 0 (spec A2) and no strict-mode `getByRole('status')` violation (spec A3).

## Phase 7 — orchestrator

- [ ] **T016** Comment on #2342: #2306 item 1 is folded into it; the literals now live at `overlayLabelStyle.ts:38-39` and `OverlayEditor.tsx:191`. PR body uses a closing keyword for #2306 and states item 1 is deferred to #2342, not done.

## Dependencies

```
T001 ─────────────► T007
T002 ─────────────► T008
T003 ─────────────► T009
T004 ─────────────► T010
T005 ─────────────► T011 ─┐
T006 ─────────────────────┴► T012
T007..T012 ───────► T013 ─► T014, T015 ─► T016
```

Items are independent of each other; no cross-item edge exists. Commit order per plan §6.
