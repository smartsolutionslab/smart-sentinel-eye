# Tasks — Spec 229, the hub handlers CellPage carries

**Spec:** `specs/229-the-hub-handlers-cellpage-carries/spec.md`
**Plan:** `specs/229-the-hub-handlers-cellpage-carries/plan.md`
**Issue:** [#2321](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2321)
(on Project #13 — the phase-3 gate is already satisfied)
**Lane:** autonomous (ADR-0144) · **Engineer:** frontend-engineer (US1 by test-writer)

Format: `[TNNN] [P?] [Story] description`. `[P]` marks tasks owning disjoint
files that may run concurrently (ADR-0109).

## Colour (ADR-0144 phase 4a)

| Group | Colour | Why |
|---|---|---|
| US1 | **characterisation, observed green** | Pins existing behaviour of two uncovered handlers. A US1 test arriving **red** on unrefactored code means the test is wrong or the premise is — stop and report. |
| US2 | **characterisation, must stay green unmodified** | Pure move. Any covering test needing an edit is a STOP, not an adjustment. |

## Parallelism

None worth taking. US1 and US2 both touch the cell directory, US2 depends on
US1's commit as its baseline, and the whole change is one engineer-hour.
Strictly sequential: T001 → T002–T004 → T005–T009 → T010.

---

## US1 — Safety net for the two uncovered handlers (P1)

- **[T001] [US1] Baseline.** On the untouched branch run
  `cd apps/kiosk-web && npx vitest run src/features/cell/CellPage.test.tsx` and keep
  the `Tests 53 passed (53)` line verbatim for the PR. If the count is not 53,
  stop — the premise has moved again.
- **[T002] [US1] Characterisation tests for pushed archive/publish** — append one
  new `describe` at the end of `CellPage.test.tsx` (no existing line edited),
  covering spec US1's four scenarios: pushed archive flags only the bound tile;
  later publish clears it; publish dispatches `invalidateTags` for
  `{type:'Overlay', id}` and `{type:'OverlaySnapshot', id}` (spy on `store.dispatch`
  before render, match with `overlaysApi.util.invalidateTags.match` /
  `systemVariablesApi.util.invalidateTags.match`); archive + publish for an unbound
  overlay flag nothing and dispatch no invalidation. Fire frames via
  `capturedCallbacks?.onOverlayArchived` / `onOverlayPublished` inside `act`, as the
  existing highlight tests do. Reuse the suite's existing layout/overlay builders.
  Sentence-style names (ADR-0053).
- **[T003] [US1] Observe green and prove each can fail.** Run the file: all green
  on the unrefactored `CellPage.tsx`. Then, one at a time, temporarily delete
  (a) `setUnavailableOverlays(... withAdded ...)` in `onOverlayArchived`,
  (b) `setUnavailableOverlays(... withRemoved ...)` in `onOverlayPublished`,
  (c) the two `dispatch(...invalidateTags...)` lines,
  (d) the `boundOverlays.has` guard in each of the two handlers —
  observe the matching test go red, quote the failure line, restore. Leave
  `CellPage.tsx` byte-identical afterwards (`git diff --quiet -- CellPage.tsx`).
- **[T004] [US1] Commit** `test(kiosk): pin the pushed overlay archive and publish handlers`
  — test file only. Record its SHA; it is US2's baseline.

## US2 — Extract `useOverlayHubHandlers` (P1) — depends on T004

- **[T005] [US2] `wallBindings.ts`** — move `boundOverlayIn` and `namedFab`
  verbatim with their doc comments, exported; `CellPage.tsx` imports them. Run the
  suite: green. (Shape-only step; keeps each commit buildable.)
- **[T006] [US2] `useOverlayHubHandlers.ts`** — create per plan §"New": the
  `OverlayHubHandlers` interface and the hook, with the moved state, refs,
  cleanup effect, `startHighlight`, four handler bodies and the four private
  helpers, verbatim. Handlers **not** memoised; `onLabelVerdict` stays
  `useCallback([])` (plan invariants 2-3).
- **[T007] [US2] Rewire `CellPage.tsx`** — call the hook at the old state-block
  position (plan invariant 1); spread `overlayHub.handlers` into
  `useLayoutLifecycle` beside `onArchived`/`onReconnected`; read the two sets and
  `onLabelVerdict` from the hook in render; delete the moved code and now-unused
  imports; re-point the comments listed in plan R3. `reportedLayoutFaultsRef` and
  the layout-fault effect stay (spec D1).
- **[T008] [US2] Verify unmodified.** Run the file — the identical
  `Tests N passed (N)` as T003. `git diff <T004 SHA> -- apps/kiosk-web/src/features/cell/CellPage.test.tsx`
  must print nothing; quote that. Any red: revert the offending piece and report
  verbatim — never edit a test (spec US2 conflict scenario).
- **[T009] [US2] Gates.** In `apps/kiosk-web`: `npm run lint` (`--max-warnings 0`),
  `npm run typecheck`, `npm test`. No new `eslint-disable`. Record `wc -l` for
  `CellPage.tsx` (before 707) and the two new files. Commit T005-T007 as
  `refactor(kiosk): …` commits that each build and pass on their own (CLAUDE.md
  stacked/rebase rule: verify per commit).

## Phase 5

- **[T010] [US2] Verification note** — the T001/T003/T008 `passed` lines, the empty
  test-file diff, the line counts, and "Event → overlay state leg: no impact, not
  re-measured" (spec §4). No stack boot required (spec §2).
