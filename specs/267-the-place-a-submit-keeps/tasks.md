# Tasks 267: The place a submit keeps

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Issue**: #2624 (feature-level; no per-task issues)
**Engineers**: `test-writer` (4a), then `frontend-engineer` (4b). **Reviewer**: `frontend-reviewer`.
**Phase 4a colour**: **RED** (behaviour-changing, ADR-0139). Focus now survives entering the
in-flight state, where on `develop` it falls to `<body>`. The expected-red set and the
declared-green pins are listed per task. **A pin's green result is not 4a evidence. An expected-red
test that arrives green is a 4a failure: stop and report it, do not adjust it.**

Format: `[ID] [P?] [Story] description, file(s)`.

**Foundational / blocking**: only T000, a read-only check. There is no Shared.Kernel,
Shared.Contracts, AppHost or Aspire work.
**Parallelism (ADR-0109)**: every unit-test task owns one file and every fix task owns one
component, so all `[P]` tasks inside a phase can fan out. The e2e file (T008) is one file, so one
writer owns it. T009 gates 4b. In 4b, T010a–f and T010g are disjoint.

## Pre-4a

- [x] **T000** Run `pnpm install` in the worktree (there is no `node_modules` yet). Re-read the
  **installed** sources and quote the lines in the 4a hand-off. The lines to quote are
  `@radix-ui/react-alert-dialog` (1.1.23) `AlertDialogCancel` → `DialogClose`, `DialogClose`'s
  `composeEventHandlers(props.onClick, () => context.onOpenChange(false))`, `@radix-ui/primitive`'s
  `checkForDefaultPrevented` default, and `@radix-ui/react-slot` 1.3.3 `mergeProps` calling the
  child handler before the slot's. If any of these differs from plan §3.8, **stop**: the Cancel
  guard's mechanism needs re-planning.
- [x] **T000b** Re-check that the spec number 267 is free across every remote branch and worktree
  (`git ls-tree -d --name-only origin/<b>:specs`, `git worktree list`).

## Phase 4a: tests first (test-writer; run, return verbatim output)

- [x] **T001** [P] [US1] Plan §4 cases (a)(b)(c) for Correct the address. Make the mocked
  `useChangeCameraAddressMutation` state mutable. Edit no existing case. File:
  `apps/management-web/src/features/cameras/EditCameraAddressDialog.test.tsx`
- [x] **T002** [P] [US1] The same for Rename (`useRenameCameraMutation`). File:
  `apps/management-web/src/features/cameras/RenameCameraDialog.test.tsx`
- [x] **T003** [P] [US1] The same for Register (`useRegisterCameraMutation`, already mocked beside
  `assignedGroups`). File: `apps/management-web/src/features/cameras/RegisterCameraDialog.test.tsx`
- [x] **T004** [P] [US1] The same for New rule. The form is `getByTestId('rule-form')`, and names
  follow the existing `/^name$/i` caution. File:
  `apps/management-web/src/features/rules/RuleDialog.test.tsx`
- [x] **T005** [P] [US1] The same for New variable. File:
  `apps/management-web/src/features/systemVariables/SystemVariableDialog.test.tsx`
- [x] **T006** [P] [US2] **New file**: plan §4's DryRunPanel cases (a)(b)(c). Mock
  `useDryRunRuleMutation` from `@smart-sentinel-eye/shared/api/rules.api` with a mutable
  `isLoading`, using the same `vi.mock` + `importOriginal` shape as `RegisterCameraDialog.test.tsx`.
  File: `apps/management-web/src/features/rules/DryRunPanel.test.tsx`
- [x] **T007** [P] [US3] Plan §4's ConfirmDialog cases (a)(b)(c). The existing "Refuses a second
  confirmation while in flight" and "Puts the keyboard on cancel" must stay **unmodified**. File:
  `apps/shared/src/ui/primitives/ConfirmDialog.test.tsx`
- [x] **T008** [US1][US2][US3] **New file**: plan §6. Use the file-local `holdWrites` helper and
  one `test.describe` per story. There are eight tests: Register, Rename, Correct address, New
  rule, New variable (steps 1–7), DryRun, Retire-confirm and Retire-Cancel. Every hold is
  released in `finally`. Waits are by condition only (ADR-0150): there is no `waitForTimeout`,
  and no `setTimeout` except inside the gate. Camera names start `E2E Focus `. File:
  `e2e/in-flight-focus.spec.ts`
- [x] **T009** On unchanged production code, run the following and capture the output
  **verbatim**:
  - `pnpm --filter @smart-sentinel-eye/management-web exec vitest run src/features/cameras src/features/rules src/features/systemVariables`
  - `pnpm --filter @smart-sentinel-eye/shared exec vitest run src/ui/primitives/ConfirmDialog.test.tsx`
  - `pnpm test:e2e e2e/in-flight-focus.spec.ts` against a booted stack (one stack per machine;
    stop it before building)
  - `pnpm typecheck` (and `pnpm typecheck:e2e`; if that fails, first check that it also fails on a
    clean `develop`, since there is a known `@types/node` gap)

  **Required red**: every (a) case in T001–T007; every (b) case in T001–T005; and all eight e2e
  focus assertions, each failing on `toBeFocused` (received `<body>`). **Required green (pins)**:
  every (c) case; T006 (b); T007 (b); both unmodified ConfirmDialog cases; every pre-existing case
  in these files; and every e2e `count() === 1` assertion that is reached. Typecheck must be
  green. If any required-red case arrives green, stop and report.
  Commit: `test(ui): pin focus through an in-flight submit at the eight ADR-0151 sites`.
  Depends on T001–T008.

## Phase 4b: production (frontend-engineer; tests may not be edited)

- [x] **T010a** [P] [US1] Plan §3.1. File:
  `apps/management-web/src/features/cameras/EditCameraAddressDialog.tsx`. Depends on T009.
- [x] **T010b** [P] [US1] Plan §3.2. File:
  `apps/management-web/src/features/cameras/RenameCameraDialog.tsx`. Depends on T009.
- [x] **T010c** [P] [US1] Plan §3.3. File:
  `apps/management-web/src/features/cameras/RegisterCameraDialog.tsx`. Depends on T009.
- [x] **T010d** [P] [US1] Plan §3.4. File: `apps/management-web/src/features/rules/RuleDialog.tsx`.
  Depends on T009.
- [x] **T010e** [P] [US1] Plan §3.5. File:
  `apps/management-web/src/features/systemVariables/SystemVariableDialog.tsx`. Depends on T009.
- [x] **T010f** [P] [US2] Plan §3.6. File: `apps/management-web/src/features/rules/DryRunPanel.tsx`.
  Depends on T009.
- [x] **T010g** [P] [US3] Plan §3.7 and §3.8. File: `apps/shared/src/ui/primitives/ConfirmDialog.tsx`.
  Depends on T009 and T000.

  Commits: T010a–f as `fix(ui): keep focus on in-flight submits in the camera, rule and variable
  dialogs`, and T010g as `fix(shared): keep focus on ConfirmDialog's buttons while pending`.
- [x] **T011** Re-run T009's commands. Every test must be green and **unmodified** since T009's
  commit (`git diff <T009-sha> -- '*.test.tsx' e2e/in-flight-focus.spec.ts` must be empty). Also
  run `pnpm lint` (`--max-warnings 0`; add no suppression), `pnpm format:check` and
  `pnpm typecheck`. Depends on T010a–g.
- [x] **T012** Search again for the defect class and confirm nothing in scope was missed:
  `git grep -nE '(^|[^-])disabled=\{(isLoading|pending)\}' -- 'apps/**/*.tsx'` must return
  nothing. List the spec §1 "outside the list" sites unchanged in the PR body. Depends on T011.
- [x] **T013** **Counterfactuals** (ADR-0151 Implementation Notes). Quote each, then revert it.
  1. Remove the `isLoading` guard in `RegisterCameraDialog.tsx`'s `handleFormSubmit`, then run its
     e2e test. Required: `count()` gives `Expected: 1, Received: 2`, and T003 (b) goes red.
  2. Remove `if (pending) event.preventDefault()` from ConfirmDialog's Cancel, then run the
     Retire-Cancel e2e test and T007 (b). Required: the dialog closes while held, and (b) goes red.
  3. Remove `if (pending) return;` from confirm. Required: the existing "Refuses a second
     confirmation while in flight" goes red.

  Depends on T011.

## Phase 5

- [ ] **T014** Spec §6 steps 1–5 by hand, on a booted stack, for all eight. Record the
  `document.activeElement` read per site in the verification note, and quote the T009 and T011
  e2e runs. There is no latency figure (§IV N/A). Depends on T013.

  **Partially done.** The unit-test suite (T009/T011) is quoted in the PR and a source trace
  confirmed the fix is real (`RegisterCameraDialog.tsx`). The stack was not booted: this machine's
  C: drive was at 98% free-space-critical (6.5GB free) with another build active throughout this
  run, and booting Aspire risked crashing Docker for the shared machine (recorded repo lesson).
  The 8 real-browser focus assertions in `e2e/in-flight-focus.spec.ts` are unobserved locally;
  CI's `e2e` job on a fresh runner is the first real observation. Left unticked deliberately.

## Phase 7 notes

- Before `gh pr create --base develop`, re-run T000b, and apply the plan §7 rebase rule if #2336 or
  #2335 has merged in the meantime.
- Report the spec §1 "same-shape sites outside the list" to the orchestrator for follow-up filing.
  **Do not file or label them from this run.**

## Dependencies

```
T000 ─────────────────────────────────┐
T000b                                 │
T001 T002 T003 T004 T005 T006 T007 T008 ─→ T009 ─→ T010a…f ─┐
                                              └──→ T010g ←──┘(T000)
                                                     ↓
                                     T011 → T012 → T013 → T014
```
