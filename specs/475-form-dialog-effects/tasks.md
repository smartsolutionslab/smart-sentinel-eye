# Tasks 475 — The form-dialog reset, clear and submit-guard logic lives in one place

Spec: [spec.md](spec.md) · Plan: [plan.md](plan.md) · Issue #3198 (feature-level; on Project #13).
Engineer: **frontend**. Phase 4a colour: **hooks → red first (T003–T005); dialogs → behaviour-preserving,
characterisation (existing tests, unmodified, `git diff` on them empty); the one missing pin (Rename `currentName`) →
written first, green on the unmigrated tree (T002)**.

One PR. Scripts, from the repo root: `npm run -w <app> test | lint | typecheck`, `npm run format:check`; apps:
`management-cameras`, `management-web`, `shared`. Scratchpad files are not committed.
**Commit hygiene (lessons from specs 470-474):** `git add` only the files of the step (never `git checkout --` to "clean
up": it restores from the index); `prettier --write` before committing (the hook rejects unformatted files); **run Vitest
from the app directory** (elsewhere there is no jsdom and everything fails with `document is not defined`); **a
counterfactual must fail for the right reason: read the failure message, not just the count**; a first counterfactual that
leaves everything green proves nothing, say so and pick a break that changes what the tests depend on.

## Phase 1 — Record the "before" (T-A)

- [x] **T001 [US1]** (frontend) On the unmigrated tree record into the scratchpad, for the six dialogs' test files
  (`RegisterCameraDialog.test.tsx`, `EditCameraAddressDialog.test.tsx`, `EditCameraAddressDialog.cache.test.tsx`,
  `RenameCameraDialog.test.tsx`, `RuleDialog.test.tsx`, `SystemVariableDialog.test.tsx`, `WallForm.test.tsx`): (a) vitest
  JSON → sorted `file :: full name :: status` per app; (b) sorted assertion lines (`expect(`, `.toHaveBeen…`, `.toBe…`,
  `.toEqual`, `.toThrow`); (c) the six dialogs' line counts; (d) a general-pattern grep over `apps/` for
  `useEffectEvent` and `function handleFormSubmit` in production files (expect the 5 and 8 from the spec); (e) the three
  apps' whole suites and durations. Depends on nothing.

## Phase 2 — Pin the one missing behaviour, then the hooks

### User story 2 (P2) — the invariants live in one place, with a test that fails when they break (FR-004)

- [x] **T002 [US1]** (test-writer) Create `apps/management-cameras/src/features/cameras/RenameCameraDialogReset.test.tsx`
  (a **new** file; the existing Rename tests are not touched). Cases, characterising today's behaviour: opening the dialog
  fills the field with `currentName`; with the dialog open, typing then **rerendering with a different `currentName`**
  resets the field to the new name (the `[open, currentName]` dependency); closing and reopening resets it. Run it:
  **green on the unmigrated tree** (quote the output). Commit `test(475): pin RenameCameraDialog's reset on currentName change`.
  Depends on T001.
- [x] **T003 [US2]** (test-writer) Create `apps/shared/src/hooks/useFormDialog.test.tsx` **before** the module exists. Cases:
  `useClearOnClose` — clears when `open` is false on mount; clears when `open` flips true→false (a parent-driven close);
  does not clear while open; **does not re-fire when only the callback identity changes** (the #2624 property, expressed at
  the hook); calls the **latest** callback. `useResetOnOpen` — resets when `open` flips to true; not on close; **re-fires
  when `resetKey` changes while open** (Rename); does not re-fire when only the callback identity changes; with no
  `resetKey` does not re-fire on a rerender. `useSubmitGuard` — while loading: `preventDefault` and `onSubmit` not
  called; otherwise `onSubmit` called with the event; follows `isLoading` across rerenders. Run
  `npm run -w shared test -- useFormDialog`; **quote the red output verbatim** (expected: cannot resolve the module).
  Test-only commit `test(475): form-dialog hooks (red)`. Depends on T002.
- [x] **T004 [US2]** (frontend) Create `apps/shared/src/hooks/useFormDialog.ts` (`useClearOnClose`, `useResetOnOpen`,
  `useSubmitGuard`, each wrapping its callback in `useEffectEvent` inside the hook) with the explanatory comments moved
  in from the dialogs (the #2624 note, "the obvious rewrite is wrong here", the parent-driven-close note), and one export
  line in `src/hooks/index.ts`. T003's tests pass **unmodified**; `eslint --max-warnings 0`, `tsc --noEmit`,
  `prettier --check` for `shared`. Commit `refactor(475): form-dialog hooks`. Depends on T003.
- [x] **T005 [US2]** (test-adversary) **Counterfactuals, one break at a time, not committed**, each run with the matching
  test and **each failure message read and quoted**: (a) add the callback to `useResetOnOpen`'s effect dependencies
  (#2624: it re-fires when only the callback identity changes); (b) make `useClearOnClose` fire from the callback instead
  of an effect on `open` (a parent-driven close is missed); (c) drop `preventDefault` from `useSubmitGuard`; (d) drop
  `resetKey` from `useResetOnOpen`'s dependencies (Rename's re-reset is lost). Restore after each (`git diff` empty).
  Depends on T004.

## Phase 3 — One commit per dialog

### User story 1 (P1) — the six dialogs behave exactly as before (FR-001, FR-003, FR-005)

Each task: replace only what plan §R3 names, **at the position of the code it replaces**; keep the dialog's own
clear/reset body and dependencies; the moved comments appear in the hook only (leave a one-line pointer where the
effect was, if the dialog would otherwise read as missing something). Run the dialog's test files **before** committing
(from the app directory), `eslint --max-warnings 0` and `tsc --noEmit`, `git diff --stat` shows only the dialog,
`git add` that file only, commit `refactor(475): <Dialog> uses the shared form-dialog hooks`. Fixed order, one branch,
one index. Each depends on the previous.

- [x] **T006 [US1]** `management-cameras/.../RegisterCameraDialog.tsx` — `useClearOnClose` + `useSubmitGuard`.
- [x] **T007 (decision gate)** (orchestrator) **Measure, then decide.** Lines of `RegisterCameraDialog.tsx` before/after;
  extrapolate to the six using plan §R3's table; add the hook (~70 lines) and its tests. Report the projection to the
  maintainer **before** T008. If the projected saving is below the point the maintainer accepted for #3190's pieces
  (consistency only), stop and ask; otherwise continue. Depends on T006.
- [x] **T008 [US1]** `RenameCameraDialog.tsx` — `useResetOnOpen(open, resetForOpen, currentName)` + `useSubmitGuard`;
  `RenameCameraDialogReset.test.tsx` (T002) must still be green.
- [x] **T009 [US1]** `EditCameraAddressDialog.tsx` — `useResetOnOpen(open, resetForOpen)` (no key) + `useSubmitGuard`;
  both `EditCameraAddressDialog*.test.tsx` files green.
- [x] **T010 [US1]** `management-web/.../rules/RuleDialog.tsx` — `useClearOnClose` + `useSubmitGuard`.
- [x] **T011 [US1]** `management-web/.../systemVariables/SystemVariableDialog.tsx` — `useClearOnClose` + `useSubmitGuard`.
- [x] **T012 [US1]** `management-web/.../walls/WallForm.tsx` — `useSubmitGuard` only.

## Phase 4 — Gates and PR

- [x] **T013** (frontend) **T-A after:** repeat T001's records. (a) names+status `diff` empty; (b) assertion lines `diff`
  empty; `git diff --stat origin/develop...HEAD` on the existing dialog test files is empty (FR-001), the only added test
  file outside `shared` being `RenameCameraDialogReset.test.tsx`; (c) the six dialogs' line counts, their sum, **and**
  the added lines of the hook file and the tests (report both); (d) the SC-003 greps with the general patterns: none of
  the six declares `useEffectEvent` or `function handleFormSubmit`, and `apps/` contains them only in the shared hook and
  in `LayoutEditorDialog` / `OverlayDraftForm`; (e) the distinctive comment phrases appear once (hook) and zero times
  (dialogs); (f) each touched app's whole suite passes, with durations before/after. Depends on T012.
- [x] **T014** (frontend) `npm run -w <app> lint|typecheck|test` for `shared`, `management-cameras`, `management-web`;
  `npm run format:check`; `dotnet test tests/Architecture.Tests -c Release` (scans `.tsx`). `git diff --stat
  origin/develop...HEAD` shows only the allowed paths (FR-007). Tick this file's tasks. Depends on T013.
- [ ] **T015** (orchestrator) PR to `develop` (`gh pr create --base develop`): body states Phase 4a (red for the hooks with
  T003's output quoted; characterisation for the dialogs; the Rename pin), T005's failures with their messages, the T-A
  diffs, the **honest line accounting** (dialogs down by N, new code up by M), the T007 decision, durations, and
  **Closes #3198**. Park; do not wait on CI. Depends on T014.
- [ ] **T016** (orchestrator) After merge: confirm #3198 closed (closing keyword, then check the state); comment that
  `LayoutEditorDialog` and `OverlayDraftForm` still carry their own `saveBlocked` guard and effects (#3193, #3192) and
  can adopt `useSubmitGuard`'s shape if they want it after their own refactors. Depends on T015 merged.

## Dependencies and parallelism

Strictly sequential: T001 → T002 → T003 → T004 → T005 → T006 → T007 → T008 … T012 → T013 → T014 → T015 → T016. The
dialogs share one branch and one index, so none can run in parallel. T003 must be seen red before T004 exists; T005 and
T007 are not committed work.

## Explicitly not tasks

`LayoutEditorDialog` and `OverlayDraftForm`. The `FormErrorSummary` / `renderedFields` wiring. Any `react-hooks`
suppression (#2118). Any edit to an existing dialog test file. Any behaviour change, including Rename's
`[open, currentName]` dependency. A move of the hooks anywhere but `apps/shared/src/hooks`. No ADR. No
`shard-N.filter` entry (Vitest).
