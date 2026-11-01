# Tasks 476 — `LayoutEditorDialog` is broken up by responsibility

Spec: [spec.md](spec.md) · Plan: [plan.md](plan.md) · Issue #3193 (feature-level; on Project #13).
Engineer: **frontend**. Phase 4a colour: **behaviour-preserving → characterisation**. The covering tests are the seven
`LayoutEditorDialog*.test.tsx` files (unmodified, `git diff` on them empty) plus **new gap tests written first and seen
green on the unmigrated tree**. No red-first test: no new behaviour.

One PR, one commit per step; every commit leaves the family green. Scripts, from the repo root:
`npm run -w management-web test | lint | typecheck`, `npm run format:check`. Scratchpad files are not committed.
**Commit hygiene (lessons from specs 470-475):** `git add` only the files of the step (never `git checkout --` to "clean up":
it restores from the index); `prettier --write` before committing (the hook rejects unformatted files); **run Vitest from
`apps/management-web`, and anchor the working directory in the same command** (`cd "$(git rev-parse --show-toplevel)/apps/…"`,
print `pwd`): relative paths from the wrong directory have silently done nothing three times; **a counterfactual must fail for
the right reason (read the failure message, not just the count)** and a first break that leaves everything green proves
nothing: pick one that changes what the test depends on; **grep for other uses before removing an import**
(`useEffect` was still used elsewhere twice in #3198).

The family: `LayoutEditorDialog.test.tsx`, `…Retention`, `…SaveGate`, `…ChainRecovery`, `…ChainRetention`,
`…ChainAnnouncement`, `…TileView` (all `.test.tsx`, in `apps/management-web/src/features/layouts/`).

## Phase 1 — Record the "before" (T-A)

- [ ] **T001 [US1]** (frontend) On the unmigrated tree record into the scratchpad: (a) `vitest run` of the seven files →
  sorted `file :: full name :: status` (count it); (b) their sorted assertion lines (`expect(`, `.toHaveBeen…`, `.toBe…`,
  `.toEqual`, `.toThrow`); (c) lengths: the file (501), the component (444), hook calls in it (21), and the `eslint-disable`
  count in the file (expect 1, `react-hooks/refs`); (d) the whole management-web suite (684 tests) and its duration; (e) grep
  counts of the distinctive comment phrases ("`currentData`, never `data`", "contents, not on the array", "BOTH mutation
  states", "The obvious rewrite"-style ones that apply here) in the file. Depends on nothing.

## Phase 2 — Find and pin what the family does not (R2, R3)

### User story 1 (P1) — the dialog behaves exactly as before (FR-001, FR-003)

- [ ] **T002 [US1]** (test-adversary) **Probe each concern** (plan R2) on the unmigrated dialog, scratch breaks, one at a
  time, running the whole family each time and recording, per break, **which test failed and why** (or "none"):
  B reset only `editState`; `saveBlocked` dropping `isLoading`, then `knownCameras.size === 0`, then `chainFetching`, then
  `chainFailed`; C dropping the `wasOpen` filter clear, then the merge keyed on array identity, then swapping the order of
  "searching" and "nothing matched" in the status ladder; D dropping the re-seed effect; G a stale token (assigning the ref
  once). Restore after each (`git diff` empty). The table of results goes into `verification.md`. A probe nobody catches
  becomes a T003 case. Depends on T001.
- [ ] **T003 [US1]** (test-writer) Create `LayoutEditorDialogCameraFilter.test.tsx` (new): typing a fragment shows
  "Searching…" during the debounce, then each final sentence — "N of M cameras match.", "No camera matches “x”.", "K matches so
  far — the list is incomplete.", "The camera list could not be loaded." — the `aria-live="polite"` region, `aria-describedby`
  on the field, and the filter clearing when the dialog closes. Plus a case for every T002 probe nobody caught that concerns
  the picker. **Green on the unmigrated tree** (quote). Prove each case able to fail by a break in the dialog (message read).
  Commit `test(476): pin LayoutEditorDialog's camera filter status`. Depends on T002.
- [ ] **T004 [US1]** (test-writer) Create `LayoutEditorDialogToken.test.tsx` (new): with `TileView` stubbed to expose the
  `getToken` it receives, `getToken()` resolves the **latest** token after a rerender with a new one, and `null` with no auth
  provider; the stub also exposes `canEditOverlays` and the case pins what it receives. Plus a case for every T002 probe
  nobody caught that concerns Save, the form or the close. **Green on the unmigrated tree**; each proved able to fail (message
  read). Commit `test(476): pin LayoutEditorDialog's token callback and any other unpinned behaviour`. Depends on T003.

## Phase 3 — Extract, least coupled first (one commit each)

### User story 2 (P2) — the component is short enough to read (FR-002, FR-005, FR-006, FR-007)

Each task: move the code **verbatim** (comments, dependency arrays) into the unit named; the dialog calls it at the position
of the code it replaced; run the family and the two gap files before committing; `eslint --max-warnings 0` and
`tsc --noEmit`; `git diff --stat` shows only the dialog and the new unit; `git add` those files only; commit
`refactor(476): <Dialog> takes <thing> from <unit>`. After each, grep the dialog for imports the move made unused **and** for
other uses of them before removing any.

- [ ] **T005 [US2]** `layoutEditor/useLatestAccessToken.ts` — `useAuth`, the token ref, **the one `react-hooks/refs`
  suppression with its justification**, `getToken`. Dialog: `const getToken = useLatestAccessToken();`.
- [ ] **T006 [US2]** `layoutEditor/useResetMutationsOnClose.ts` — the close effect, deps `[open, createState, editState]` kept.
- [ ] **T007 [US2]** `layoutEditor/cameraFilterStatus.ts` — the pure status ladder with its ordering comments.
- [ ] **T008 [US2]** `layoutEditor/useKnownCameras.ts` — the accumulator and the **during-render** contents-based merge.
- [ ] **T009 (decision gate)** (orchestrator) **Measure, then decide.** Component length, file total (dialog + units),
  hook calls left in the component, and the number of inputs each unit takes. Report to the maintainer **before** T010; the
  line total will not have fallen, so the question is whether the readability gain justifies the wider extractions
  (`useCameraPicker`, the form, the Save gate). Stop and ask; continue only on a yes. Depends on T008.
- [ ] **T010 [US2]** `layoutEditor/useCameraPicker.ts` — filter text, debounce, the page-walk query, `useKnownCameras`, the
  `wasOpen` filter-clear adjustment (during render), derived flags.
- [ ] **T011 [US2]** `layoutEditor/CameraFilterField.tsx` — the truncation notice, the filter input and the status `<p>`.
- [ ] **T012 [US2]** `layoutEditor/useLayoutChain.ts` — `useGetLayoutQuery` with `currentData`, `refetchOnMountOrArgChange`,
  and its long comment.
- [ ] **T013 [US2]** `layoutEditor/useLayoutEditorForm.ts` — `defaultValues`, `useForm`, the re-seed effect, `onSubmit` for
  create and edit. **If it needs more than ~8 inputs, stop and ask (plan risk).**
- [ ] **T014 [US2]** `layoutEditor/useSaveGate.ts` — `saveRef`, `saveBlocked` (all six terms, the long comment),
  `handleFormSubmit`. Same stop rule.
- [ ] **T015 [US2]** `layoutEditor/useTileView.ts` — the open index, the watched grid/cells, `assignOverlay`.

## Phase 4 — Gates and PR

- [ ] **T016** (frontend) **T-A after:** repeat T001's records. (a) family names+status `diff` empty; (b) assertion lines
  `diff` empty; `git diff origin/develop...HEAD` on the seven files empty (FR-001); (c) lengths: the component, every unit,
  and the **file total** (SC-002), reported together; (d) exactly one `eslint-disable` for `react-hooks/refs` across the dialog
  and `layoutEditor/` (SC-003); (e) the distinctive comment phrases once each; (f) the gap files pass; (g) the whole suite
  (684 + new) with durations before/after. Depends on T015.
- [ ] **T017** (frontend) `npm run -w management-web lint|typecheck|test`, `npm run format:check`, and
  `dotnet test tests/Architecture.Tests -c Release` (scans `.tsx`). `git diff --stat origin/develop...HEAD` shows only the
  allowed paths (FR-008). Tick this file's tasks. Depends on T016.
- [ ] **T018** (orchestrator) PR to `develop` (`gh pr create --base develop`): body states Phase 4a characterisation, the R2
  probe table, the gap tests with their counterfactual messages, the T-A diffs, the **honest length accounting** (component
  down by N, file total up by M), the T009 decision, durations, the corrected hook count (21, not the issue's 11), and
  **Closes #3193**. Park; do not wait on CI. Depends on T017.
- [ ] **T019** (orchestrator) After merge: confirm #3193 closed (closing keyword, then check the state); comment that
  `saveBlocked` can now adopt `useSubmitGuard(saveBlocked, onSubmit)` (#3198 merged) as a separate trivial change, and that
  `OverlayDraftForm` (#3192) has the same shape. Depends on T018 merged.

## Dependencies and parallelism

Strictly sequential: T001 → T002 → T003 → T004 → T005 … T008 → T009 → T010 … T015 → T016 → T017 → T018 → T019. The extractions
share one dialog file and one index, so none can run in parallel. T009 is a stop-and-ask, not work.

## Explicitly not tasks

`useSubmitGuard` for `saveBlocked`. `OverlayDraftForm`. `GridDesigner`, `TileView`, `ChainRecoveryNotice`. Any caller. Any move
to `apps/shared`. Any edit to an existing test file. Any behaviour change or "obvious fix" found on the way. A new
`eslint-disable`. No ADR. No `shard-N.filter` entry (Vitest).
