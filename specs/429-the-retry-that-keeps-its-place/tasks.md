# Tasks 429: The retry that keeps its place

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Issue**: #3073 (feature-level, on
Project #13, autonomous lane; no per-task issues, per CLAUDE.md Phase 3)

**One PR, `fix/3073-retrybanner-focus`, `--base develop`. Phase 4a colour: red** (behaviour-changing:
new announcements, an in-flight guard, a focus move), with characterisation pins declared green in
advance (plan §5b). **Not security-sensitive.** **§IV leg: none.** Decision D1 and assumptions A1-A3
(spec §6) are surfaced in the PR body.

**Roles.** Tests: `test-writer` (T010, T020, T040). Implementation: `frontend-engineer` (T030, T050).
Review: `frontend-reviewer` (T060). The engineer receives the verbatim red output as its brief and may not
edit the red tests.

**Where tests run.** Local: vitest per workspace + `tsc --noEmit` every time, eslint, `pnpm format:check`
(prettier — eslint clean is not prettier clean). No Docker, no Playwright change.

**No `[P]` markers.** One branch, one index (ADR-0109). Nothing foundational in
Shared.Kernel/Contracts/AppHost. US2's sites depend on US1's component (T030).

Format: `[ID] [P?] [Story] description, files`.

## Phase 0: Setup

- [ ] **T001** Re-verify spec §1 on the branch tip: `grep -rn "<RetryBanner" apps --include=*.tsx | grep
  -v "\.test\."` → exactly the 10 sites S1-S10; `grep -rn "tabIndex={-1}" apps --include=*.tsx` → empty;
  `RulesPage.tsx` still gates on `isError`. Confirm `429` is still free on `origin/develop`, every remote
  branch, every `D:\Github\sse-*` worktree and open PRs. No file change.

## Phase 1: US1 — the shared composite (P1)

- [ ] **T010 [US1]** (`test-writer`) Characterisation C1, commit 1 — `apps/management-web/src/features/walls/WallsPage.test.tsx`:
  failed GET, second GET held pending, Retry is the same node after the click. **Observed green.**
  Also run the existing `RetryBanner.test.tsx` and record its four tests green.
- [ ] **T020 [US1]** (`test-writer`) Red R1-R7, commit 2 — `apps/shared/src/ui/composites/RetryBanner.test.tsx`
  (new `describe` block; the four existing tests untouched). Run, quote the verbatim failure. Run the
  three counterfactuals of plan §5a after T030 and record them in the PR body.
- [ ] **T030 [US1]** (`frontend-engineer`) Commit 3 — `apps/shared/src/ui/composites/RetryBanner.tsx`
  per plan §3. Done when R1-R7 and the four pre-existing tests are green unmodified, every existing
  suite in `apps/` is green, and `tsc --noEmit` passes in all workspaces (S10 compiles on defaults).

## Phase 2: US1 + US2 — the nine sites

- [ ] **T040 [US1][US2]** (`test-writer`) Red R8-R17, commit 4 — new files beside each page:
  `SystemVariablesPageRetryRecovery.test.tsx`, `LayoutsPageRetryRecovery.test.tsx`,
  `OverlaysPageRetryRecovery.test.tsx`, `WallsPageRetryRecovery.test.tsx`, `AuditPageRetryRecovery.test.tsx`
  (management-web); `CamerasPageRetryRecovery.test.tsx`, `CameraDetailPageRetryRecovery.test.tsx`
  (management-cameras); `RulesPageRetryRecovery.test.tsx` (R15 + R17); `HeldEventTypesPageRetryRecovery.test.tsx`
  (management-event-ingestion). Reuse each page's existing harness. Quote the verbatim failures (focus on
  `body`; R17: Retry not in the document).
- [ ] **T050 [US1][US2]** (`frontend-engineer`) Commit 5 — wire S1-S9 per plan §3: `<h1 ref tabIndex={-1}>`,
  unconditional `<RetryBanner failed retrying onRecovered>`; S4/S8/S9 destructure `isFetching`; S8
  switches `isError` → `error` and reshapes its ternary; S9 reshapes its ternary. `OverlayEditPage.tsx`
  untouched. Done when R8-R17 green, every pre-existing suite green unmodified, tsc/eslint/prettier clean.

## Phase 3: Review and hand-off

- [ ] **T060** (`frontend-reviewer`) Review the diff against spec FR-001…FR-010 and plan §5b (no
  pre-existing test edited; OverlayEditPage untouched).
- [ ] **T070** (orchestrator) File the S10 follow-up issue from plan §8 (not `agent:ready`); link it in
  the PR body and on #2378.

## Dependencies

T001 → T010 → T020 → T030 → T040 → T050 → T060 → T070. Strictly sequential (shared index; US2 sites
consume the US1 component's new props).
