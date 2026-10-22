# Tasks 432: The filters name their group

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Issue**: #3083 (feature-level, on
Project #13, autonomous lane; no per-task issues, per CLAUDE.md Phase 3)

**One PR, `fix/3083-filter-group-labels`, `--base develop`. Phase 4a colour: red** (R1-R3 + guard),
with P1 declared green in advance. **Not security-sensitive.** **§IV leg: N/A.**

**Roles.** Tests: `test-writer` (T010). Implementation: `frontend-engineer` (T020). Review:
`frontend-reviewer` (T030). The engineer receives the verbatim red output and may not edit the tests.
The guard file is C# but test-only and owned by `test-writer`.

**No `[P]` markers.** One branch, one index (ADR-0109); three tasks in sequence. Nothing foundational.

Format: `[ID] [P?] [Story] description, files`.

## Phase 0: Setup

- [ ] **T001** Re-verify spec §1 on the branch tip: `grep -rn "<ToggleChip" apps --include=*.tsx | grep -v
  "\.test\."` → exactly the four pages; `grep -rn 'role="group"' apps/management-web/src --include=*.tsx
  | grep -v "\.test\."` → only `RulesPage.tsx`. Confirm `432` still free on origin/develop, branches and
  worktrees. No file change.

## Phase 1: US1 + US2 red (P1)

- [ ] **T010 [US1][US2]** (`test-writer`) One red commit, `test(3083): ... (red)`:
  - R1 `apps/management-web/src/features/layouts/LayoutsPage.test.tsx`
  - R2 `apps/management-web/src/features/overlays/OverlaysPage.test.tsx`
  - R3 `apps/management-web/src/features/systemVariables/SystemVariablesPage.test.tsx`
  - P1 `apps/management-web/src/features/rules/RulesPage.test.tsx` (green in advance)
  - G `tests/Architecture.Tests/TextActionAndChipPrimitiveTests.cs`
    `A_ToggleChip_sits_inside_a_labelled_group` (plan §4); add the new test to the matching
    `shard-N.filter` if the class-level entry does not already cover it.
  Run vitest for the four suites + `tsc --noEmit` + `dotnet test tests/Architecture.Tests --filter
  FullyQualifiedName~TextActionAndChipPrimitive`. Expected: R1-R3 red with the "Unable to find an
  accessible element with the role "group" and name "Filter by state"" message, P1 green, G red listing
  exactly three `path:line` violations. Return verbatim output. Any other red/green split blocks.

## Phase 2: Implement

- [ ] **T020 [US1]** (`frontend-engineer`) `fix(3083): ...` — add `role="group" aria-label="Filter by
  state"` to the filter container in `LayoutsPage.tsx`, `OverlaysPage.tsx`, `SystemVariablesPage.tsx`
  (plan §2). Then all of T010 green; eslint, `prettier --check`, `tsc --noEmit` clean. Depends: T010.

## Phase 3: Verify + review

- [ ] **T030** (`frontend-reviewer` + phase 5) Run counterfactuals C1-C4 (plan §5) on a scratch copy and
  record observed results in the PR body; quote T010's red output. Depends: T020.
