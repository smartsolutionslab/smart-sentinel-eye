# Tasks 477 — `OverlayDraftForm` is broken up by responsibility

Spec: [spec.md](spec.md) · Plan: [plan.md](plan.md) · Issue #3192 (feature-level; on Project #13).
Engineer: **frontend**. Phase 4a colour: **behaviour-preserving → characterisation.** The eleven family files stay
unmodified (`git diff` on them empty). Anything the probes (T002) show the family does not notice gets a test in a **new**
file, written first and **green on the unsplit form** (T003), with its failure message read (T004).

One PR. Scripts, from the repo root: `npm run -w management-web test | lint | typecheck`, `npm run format:check`.
Scratchpad files are not committed.
**Commit hygiene (lessons from specs 470–476):** anchor the working directory in every command and print `pwd`; `git add`
only the files of the step (never `git checkout --` on a dirty tree); `prettier --write` every generated file before
committing; run Vitest from `apps/management-web`; **a counterfactual must fail for the right reason: read the failure
message, not the count**; a break that leaves everything green proves nothing, so say so and pick another; grep for other uses
before removing an import; count hook calls generic-aware (`use\w+(<[^>]*>)?\(`); rewrite every "above"/"below" pointer
when the code it points at moves.

## Phase 1 — Record the "before" (T-A)

- [x] **T001 [US1]** (frontend) On the unsplit tree record into the scratchpad, for the 11 family files: (a) vitest JSON →
  sorted `file :: full name :: status` (`names471.cjs`); (b) sorted assertion lines; (c) line counts of
  `OverlayDraftForm.tsx` (627) and the component (476), code and comment lines separately; (d) hook calls in the
  component, generic-aware (expect 21); (e) `eslint-disable` across the form (expect 3, with rule + justification text);
  (f) the whole `management-web` suite count and duration (697, ~102 s). Depends on nothing.

## Phase 2 — Probe first, then pin (US1)

### User story 1 (P1) — the form behaves exactly as before (FR-001, FR-003, SC-005)

- [x] **T002 [US1]** (test-adversary) Write `probe477.cjs` in the scratchpad (pattern of `probe476.cjs`): the named breaks of
  plan §R2 (A–K, ~30), each asserting its pattern matches **exactly once**, applying it to the unsplit
  `OverlayDraftForm.tsx`, running the family, recording which test failed and its message or "none", and restoring from git
  (`git diff` empty after each). Run all. Record the matrix; for each "none" decide: new test, or **double break** when
  another term masks it (as `currentData` was). Depends on T001.
- [x] **T003 [US1]** (test-writer) For every concern the matrix leaves unnoticed, create a test in a **new file** beside the
  form (expected: `OverlayDraftFormReadOnly.test.tsx` for the `readOnly` gating — `onChange` no-op, element operations'
  early returns, `aria-busy`; `OverlayDraftFormUnmount.test.tsx` for the mount-once reset of both states;
  `OverlayDraftFormHost.test.tsx` for the stale `onElementsChange` callback; plus whatever else T002 names). Run: **green on
  the unsplit form** (quote the output). Depends on T002.
- [x] **T004 [US1]** (test-adversary) For each new test, re-apply its break and **read and quote the failure message**
  (right reason, not just red); restore (`git diff` empty). Write the double-break table for anything defended jointly.
  Commit `test(477): pin the OverlayDraftForm behaviours the family missed`. Depends on T003.

## Phase 3 — One extraction per commit, least coupled first (US1, US2)

### User story 2 (P2) — the component reads as a list of what it composes (FR-002, FR-005, FR-006, FR-007)

Each task: move only what plan §R4 names, **at the position of the code it replaces**; hooks called unconditionally in the
same order; dependency arrays copied verbatim; comments move with their code once; after the move apply the matching T002
break to the **new unit** and see the same test fail there; run the 11 family files + the new gap files, `eslint
--max-warnings 0`, `tsc --noEmit`, `prettier --check` (before committing); `git diff --stat` shows only the form and its
new unit; `git add` those only; commit `refactor(477): <unit>`. Fixed order, one branch, one index. Each depends on the
previous.

- [x] **T005 [US2]** `overlayDraftForm/overlayEditTarget.ts` (neutral home of `OverlayEditTarget`; the form re-exports it, so
  no caller changes and no type-only cycle) + `useLatestAccessToken.ts` (concern B, **suppression 1: `react-hooks/refs`**).
- [x] **T006 [US2]** `useUnmountMutationReset(createState, editState)` (concern C, **suppression 2: `exhaustive-deps`**, the
  empty array and the "BOTH, mount-once" comment unchanged).
- [x] **T007 [US2]** `useElementsChangeNotifier(watchedElements, onElementsChange)` (concern F, **suppression 3:
  `react-hooks/refs`**).
- [x] **T008 [US2]** `describeSaveRefusal(error)` — pure function (concern I): `staleConflict`, `notDraft`, `nameTaken`,
  `backendError`, `offerReload`, the ADR-0119 comments with them.
- [x] **T009 (decision gate T-D1)** (orchestrator) **Measure, then ask.** Component lines, `OverlayDraftForm.tsx` lines, hook
  calls left, the four units' sizes and the **file total** against T001; extrapolate to the remaining units using plan §R4.
  Report to the maintainer **before** T010 and ask whether the wider extractions are worth it. Depends on T008.
- [x] **T010 [US2]** `useDraftChain(overlayIdentifier)` (concern A): `currentData`, `refetchOnMountOrArgChange` and the long
  comment.
- [x] **T011 [US2]** `useResolvedPreview(selectedElement)` (concern G).
- [x] **T012 [US2]** `useCameraChoices()` (concern H).
- [x] **T013 [US2]** `useElementSelection(…)` (concern E, **the delicate one**): the `addedAtCount` adjustment stays in the
  render body; `editorKey` and `key={editorKey}` unchanged; a stale `chosenId` still falls back to the first Text element.
  If it needs more than ~8 inputs: **stop and ask**.
- [x] **T014 [US2]** `useOverlayDraftFormState({…})` (concern D): `useForm` → `useFieldArray` → `useWatch` contiguous and in
  order; the re-seed effect's `[defaultValues, reset]` verbatim; `onSubmit` for create and edit. **Stop and ask** past ~8
  inputs.
- [x] **T015 [US2]** `useSaveGate({…})` (concern J): `saveRef`, `colourRefused`, `onColourValidityChange`, `saveBlocked`,
  `handleFormSubmit`; all five terms of the gate probed on the new unit. **Stop and ask** past ~8 inputs.
- [x] **T016 [US2]** `ElementEditorPane`, `OverlapStatus`, `TextErrorAlert` (concern K) — one commit each, `readOnly`/`aria-busy`
  and the text-error alert re-probed on the new unit.

## Phase 4 — Gates and PR

- [x] **T017** (frontend) **T-A after:** repeat T001's records. (a) names+status `diff` empty for the 11 family files (new gap
  files listed separately); (b) assertion lines `diff` empty; `git diff --stat origin/develop...HEAD` on the family is
  empty (FR-001); (c) `OverlayDraftForm.tsx`, the component, every unit (code and comment lines apart) and the **file
  total** before/after (SC-002); (d) exactly three `eslint-disable` across the form and `overlayDraftForm/`, each in one
  unit, rule and justification unchanged (SC-003); (e) distinctive comment phrases once, no stale "above"/"below"
  pointers; (f) the whole suite 697 + new tests, durations before/after (SC-004); (g) the T002 matrix re-run on the final
  tree, every break noticed by a named test (SC-005). Depends on T016.
- [x] **T018** (frontend) `eslint --max-warnings 0`, `tsc --noEmit`, `prettier --check` for `management-web`;
  `dotnet test tests/Architecture.Tests -c Release` (scans `.tsx`). Depends on T017.
- [ ] **T019** (frontend) `specs/477-overlay-draft-form-split/verification.md` (probe matrix, gap tests with quoted
  messages, double-break table, sizes, the corrected "no second caller" statement from plan §R6 and the follow-ups:
  share `useLatestAccessToken`, `useSubmitGuard(saveBlocked, …)`); tick T001–T020. Depends on T018.
- [ ] **T020** (orchestrator) Phase 6 `/code-review medium`, findings fixed or accepted in writing; Phase 7 PR to `develop`
  (`--base develop`, `Closes #3192`, Phase 4a characterisation stated, sizes stated as measured, gaps and slips kept
  visible); park; merge when CI concludes success on the PR tip; close-out comment on #3192. Depends on T019.

## Dependencies

T001 → T002 → T003 → T004 → T005 → … → T009 (**gate**) → T010 → … → T016 → T017 → T018 → T019 → T020. Strictly linear: one
branch, one index, one concern per commit. No parallel tasks.

## Implementation strategy

MVP is T005–T008 plus the gate: four low-coupling units that carry all three suppressions and the refusal text. If T009
shows the decomposition is not earning its keep, stop there and ask. The risky extractions (T013–T015) come last, after the
method has been proved on the simple ones.
