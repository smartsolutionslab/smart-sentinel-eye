# Tasks 242 — The fab error that survives Cancel

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Issue**: #2561 (feature-level issue; no per-task issues)
**Engineer**: `frontend-engineer` · **Phase 4a colour**: **RED**.

Format: `[ID] [P?] [Story] description — file(s)`.
No foundational task: nothing in `apps/shared`, AppHost or Contracts changes. The work is one test
file and one component file, strictly sequential (red before fix), so nothing is `[P]`.

## Phase 4a — tests first (test-writer; observe red, return verbatim output)

- [ ] **T001** [US1] In the multi-fab `describe` block, add: type name, press Define → "Choose which fab" shown; rerender `open={false}` then `open={true}` in the same Provider (do **not** unmount) → message absent. Add a second case: select `dresden`, close, reopen → Fab select value `''`. Use a local rerender helper; edit no existing test — `apps/management-web/src/features/systemVariables/SystemVariableDialog.test.tsx`
- [ ] **T002** [US1] Run `pnpm --filter management-web test -- SystemVariableDialog` on unchanged code; capture verbatim output showing T001's cases **red** and every other case green. Depends on T001.

## Phase 4b — production (engineer; tests may not be edited)

- [ ] **T003** [US1] Close effect also calls `setFabId('')` and `setFabError(null)` (mirror `RegisterCameraDialog.tsx:33-46`); update the effect's comment; lint suppression only if lint demands it — `apps/management-web/src/features/systemVariables/SystemVariableDialog.tsx`. Depends on T002.
- [ ] **T004** [US1] `pnpm --filter management-web test`, `lint`, `typecheck` green; T001 green unmodified. Depends on T003.

## Phase 5

- [ ] **T005** [US1] Live check per spec §6 step 2 (Cancel and Esc paths, two-fab user). Depends on T004.

## Dependencies

```
T001 → T002 → T003 → T004 → T005
```
