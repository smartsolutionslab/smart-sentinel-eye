# Tasks 231 — The stale sentence and the sticky fab error

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Issue**: #2433 (feature-level issue; no per-task issues)
**Engineer**: `frontend-engineer` · **Phase 4a colour**: **RED** for every test task below.

Format: `[ID] [P?] [Story] description — file(s)`.
`[P]` = owns files no other open task touches (ADR-0109). No foundational task: nothing in
`apps/shared`, AppHost or Contracts changes, so nothing blocks the fan-out.

## Phase 4a — tests first (test-writer; observe red, return verbatim output)

- [ ] **T001** [P] [US1] Make one layout-mutation state injectable in the harness (mutable `publishState`, reset in `beforeEach`), then add: 409 `LAYOUT_REVISION_STALE` without detail → alert shows the conflict sentence, not "Could not apply that change.", Reload offered; 409 `LAYOUT_NAME_TAKEN` without detail → generic, no "someone else"; 409 stale with detail → detail shown — `apps/management-web/src/features/layouts/LayoutsPage.test.tsx`
- [ ] **T002** [P] [US1] Add: 409 `VARIABLE_STALE` without detail → conflict sentence + Reload, generic absent; 400 without detail → generic, no Reload — `apps/management-web/src/features/systemVariables/SystemVariablesPage.test.tsx`
- [ ] **T003** [P] [US2] Multi-fab: submit with no fab → message; select `dresden` → message gone, `registerMock` not called — `apps/management-web/src/features/cameras/RegisterCameraDialog.test.tsx`
- [ ] **T004** [P] [US2] Same for rules (`fillValidRule`, `createMock`) — `apps/management-web/src/features/rules/RuleDialog.test.tsx`
- [ ] **T005** [P] [US2] First multi-fab test in the file: set two fab groups, submit valid variable with no fab → message; select `dresden` → message gone, `defineMock` not called; ensure `beforeEach` restores single-fab — `apps/management-web/src/features/systemVariables/SystemVariableDialog.test.tsx`
- [ ] **T006** [US1+US2] Run the five test files on the unchanged code; capture verbatim output showing T001–T005's new tests **red** (and only those). Depends on T001–T005.

## Phase 4b — production (engineer; tests may not be edited)

- [ ] **T007** [P] [US1] Import `CONFLICT_FALLBACK`, `isStaleConflict`; fallback `isStaleConflict(mutationError) ? CONFLICT_FALLBACK : 'Could not apply that change.'` — `apps/management-web/src/features/layouts/LayoutsPage.tsx`
- [ ] **T008** [P] [US1] Same — `apps/management-web/src/features/systemVariables/SystemVariablesPage.tsx`
- [ ] **T009** [P] [US2] Fab select `onChange` also calls `setFabError(null)` — `apps/management-web/src/features/cameras/RegisterCameraDialog.tsx`
- [ ] **T010** [P] [US2] Same — `apps/management-web/src/features/rules/RuleDialog.tsx`
- [ ] **T011** [P] [US2] Same — `apps/management-web/src/features/systemVariables/SystemVariableDialog.tsx`
- [ ] **T012** [US1+US2] `pnpm --filter management-web test`, `lint`, `typecheck` green; T001–T005 green unmodified. Depends on T006–T011.

## Phase 5

- [ ] **T013** [US2] Live check with a two-fab user in the three dialogs (spec §7); US1 recorded as component-test-verified only. Depends on T012.

## Dependencies

```
T001..T005 (parallel) → T006 → T007..T011 (parallel) → T012 → T013
```

Test/production pairs share a directory but not a file; `SystemVariablesPage.*` (T002/T008) and
`SystemVariableDialog.*` (T005/T011) are distinct files. In practice one `frontend-engineer` does
T007–T011 in minutes; `[P]` records that nothing forces an order.
