# Tasks 329 — The latch three files still shared

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Issue**: #2670 · **Phase**: 3 (Tasks)

**Colour**: **behaviour-preserving** (characterisation, test-harness only) — evidence is the
counterfactual pair (plan §3) plus unmodified-green runs, not a new red test.
**Engineer**: `test-writer` (4a) then `frontend-engineer` (4b). **Reviewer**: `frontend-reviewer`.
**Tracking**: feature-level issue #2670 (already on Project #13, In Progress). No per-task issues.
**New ADR**: none (spec 290's merged pattern, `1d536690`).

Format: `[ID] [P?] [Story] Description`. No foundational work (no Shared.Kernel / Contracts /
AppHost). The three target files are disjoint, so their 4a and 4b tasks are `[P]`; one engineer in
one worktree is still the expected execution (memory: *one branch, one index*). Precondition for
every task: `pnpm install --frozen-lockfile` in the worktree.

## Phase 4a — characterise and reproduce (`test-writer`, before any edit; return verbatim output)

- [ ] **T001 [P] [US1]** Run `apps/management-web/src/features/overlays/OverlayDraftForm.test.tsx`
  unmodified (`cd apps/management-web && npx vitest run src/features/overlays/OverlayDraftForm.test.tsx`).
  Expected `Tests 21 passed (21)`. Then build the uncommitted latch probe from it per plan §3
  (anchor: the `frame-capture-alert` expectation), run it — expected exactly
  `× PROBE: a later test still receives store notifications`, `Tests 1 failed | 21 passed (22)`.
  Delete the probe. If the probe does not go red, stop and report.
- [ ] **T002 [P] [US1]** Same for
  `apps/management-cameras/src/features/cameras/CameraViewerLifecycle.test.tsx` (baseline 3/3;
  probe anchor `expect(construct).toHaveBeenCalledTimes(2);`; expected `1 failed | 3 passed (4)`),
  plus the control (probe without the injected dispatch — expected `4 passed (4)`).
- [ ] **T003 [P] [US1]** Same for
  `apps/management-cameras/src/features/cameras/CameraViewerAlignment.test.tsx` (baseline 2/2;
  anchor `expect(setPlayoutTarget).not.toHaveBeenCalled();`; expected `1 failed | 2 passed (3)`).

## Phase 4b — fix (`frontend-engineer`, receives T001–T003 output as its brief; no `expect(` edits)

- [ ] **T004 [P] [US1]** `OverlayDraftForm.test.tsx` per plan §2.1 and §2.4: import `apiSlices` +
  `createApiStore`, local `createStore()`, `let store`, `store = createStore();` first in the
  file-level `beforeEach` (line 96). Commit 1 (plan §4). Depends on T001.
- [ ] **T005 [P] [US1]** `CameraViewerLifecycle.test.tsx` per plan §2.2 and §2.4. Depends on T002.
- [ ] **T006 [P] [US1]** `CameraViewerAlignment.test.tsx` per plan §2.3 and §2.4 —
  `store = createStore();` **before** `vi.useFakeTimers();` in `beforeEach`. Commit 2 with T005.
  Depends on T003.

## Phase 4b/5 — prove it

- [ ] **T007 [US1]** Rebuild the latch probe from each **fixed** file (same anchors); 10 runs per
  file, every run all green (22/22, 4/4, 3/3). Record verbatim output. Delete the probes.
  Depends on T004, T005, T006.
- [ ] **T008 [US1]** 20 consecutive runs of each fixed file (21/21, 3/3, 2/2); full
  `management-web` and `management-cameras` Vitest suites green; `prettier --check`, ESLint,
  `tsc --noEmit` clean for both packages. Confirm
  `git diff origin/develop -U0 -- apps | grep -E '^[+-].*expect\('` prints nothing and
  `git status` shows no `ZzLatchProbe` file. Depends on T007.

## Dependencies

T001 → T004; T002 → T005; T003 → T006; {T004, T005, T006} → T007 → T008.
T001–T003 may run in parallel; T004–T006 may run in parallel (disjoint files).

## Follow-up (not tasks here — for the orchestrator)

- File an issue for `apps/kiosk-web/src/features/cell/CellPage.test.tsx` and
  `apps/kiosk-web/src/features/cell/LayoutGridLabelPairing.test.tsx` (kiosk app singleton +
  `vi.useFakeTimers()`), citing plan §3's probe recipe. Not probed; out of this issue's scope.
