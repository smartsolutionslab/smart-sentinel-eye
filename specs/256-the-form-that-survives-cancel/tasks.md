# Tasks 256 — The form that survives Cancel

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Issue**: #2579 (feature-level issue; no per-task issues)
**Engineer**: `frontend-engineer` · **Phase 4a colour**: **RED** (behaviour-changing, ADR-0139).

**Confirmed affected, all three dialogs.** Cancel bypasses the form reset in `SystemVariableDialog`
and `RegisterCameraDialog`. In `RuleDialog`, Cancel, Esc and overlay click all bypass it, because
it has no close-path form reset at all.

**Fix mechanism, same for all three.** Call `reset(defaults)` from the existing `if (!open)` close
effect. Do not re-route the Cancel button: the `toggleOpen` test never calls Cancel's `onClick`, so
a Cancel-only fix stays red (spec §1). Drop the Dialog wrapper's reset in the two dialogs that have
one, and add no wrapper to `RuleDialog`.

Format: `[ID] [P?] [Story] description — file(s)`.
There is no foundational task, because nothing in `apps/shared`, AppHost or Contracts changes. The
three stories own disjoint files (one component and one test file each), so they are `[P]` across
stories (ADR-0109). Within a story the order is strict: red before fix.

## Phase 4a — tests first (test-writer; observe red, return verbatim output)

- [ ] **T001** [P] [US1] Add a module-level `toggleOpen(rerender, open)` that re-renders `<Provider store={store}><SystemVariableDialog open={open} onOpenChange={() => {}} /></Provider>`, and leave the nested multi-fab one untouched. In `describe('SystemVariableDialog')`, add: type `lineStatus` into Name → `toggleOpen(false)` → `toggleOpen(true)` → re-queried Name `toHaveValue('')`. Add: select Type `Number` → close/reopen → Type `toHaveValue('String')`. Do **not** unmount, and do not click Cancel. Edit no existing test — `apps/management-web/src/features/systemVariables/SystemVariableDialog.test.tsx`
- [ ] **T002** [P] [US2] Add a module-level `toggleOpen`. Add: `fillValidCamera(user)` → close/reopen → Name `''` and RTSP URL `''`. Edit no existing test — `apps/management-web/src/features/cameras/RegisterCameraDialog.test.tsx`
- [ ] **T003** [P] [US3] Add a module-level `toggleOpen`. Add: fill `/^name$/i` with `high-oee` and `/predicate/i` with any text, then clear and fill `/trigger source/i` with `mqtt` → close/reopen → Name `''`, Predicate `''`, Trigger source `'plc'`. Use `/^name$/i`, because `/name/i` also matches "Variable name". Edit no existing test — `apps/management-web/src/features/rules/RuleDialog.test.tsx`
- [ ] **T004** [US1-3] On unchanged code, run `pnpm --filter management-web test -- SystemVariableDialog RegisterCameraDialog RuleDialog` and capture the verbatim output. It must show every T001-T003 case **red** (field still populated) and every other case green. Depends on T001, T002, T003.

## Phase 4b — production (engineer; tests may not be edited)

- [ ] **T005** [P] [US1] Close effect: add `reset(DEFAULT_INPUT)`, move the effect below `useForm`, deps `[open, reset, resetMutationState]`. Dialog `onOpenChange={onOpenChange}` (remove the wrapper). Add one clause to the effect comment — `apps/management-web/src/features/systemVariables/SystemVariableDialog.tsx`. Depends on T004.
- [ ] **T006** [P] [US2] The same change with `reset()`: effect below `useForm`, wrapper removed — `apps/management-web/src/features/cameras/RegisterCameraDialog.tsx`. Depends on T004.
- [ ] **T007** [P] [US3] Close effect: add `reset(DEFAULT_INPUT)`, move it below `useForm`, deps `[open, reset, resetMutationState]`, and add a one-line comment. **Add no wrapper.** `onOpenChange={onOpenChange}` stays — `apps/management-web/src/features/rules/RuleDialog.tsx`. Depends on T004.
- [ ] **T008** [US1-3] Run `pnpm --filter management-web test`, `lint` (`--max-warnings 0`; suppression only if demanded) and `typecheck`, all green, with T001-T003 green and **unmodified**. Depends on T005, T006, T007.

## Phase 5

- [ ] **T009** [US1-3] Live check per spec §6 step 2: all three dialogs, Cancel **and** Esc, fields at defaults after reopen. Esc on New rule is the path that is broken today beyond Cancel. Depends on T008.

## Dependencies

```
T001 ─┐
T002 ─┼→ T004 → T005 ─┐
T003 ─┘         T006 ─┼→ T008 → T009
                T007 ─┘
```

The fan-out is safe because T001/T005, T002/T006 and T003/T007 each own one file pair. A single
test-writer doing T001-T003 in sequence is equally fine: they are small.
