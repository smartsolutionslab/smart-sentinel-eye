# Tasks 478 — `OverlayEditor` is broken up by responsibility

Spec: [spec.md](spec.md) · Plan: [plan.md](plan.md) · Issue #3191 (feature-level; on Project #13).
Engineer: **frontend**. Phase 4a colour: **behaviour-preserving → characterisation.** The eight covering files stay
unmodified (`git diff` on them empty). Anything the probes (T002) show the family does not notice gets a test in a **new**
file, written first and **green on the unsplit file** (T003), with its failure message read (T004).

One PR. The editor and its family live in `apps/shared` (run Vitest from `apps/shared`); the caller's tests run from
`apps/management-web`. Scripts, from the repo root: `npm run -w <app> test | lint | typecheck`, `npm run format:check`.
Scratchpad files are not committed.
**Commit hygiene (lessons from specs 470–477):** anchor the working directory in every command and print `pwd`; `git add`
only the files of the step (never `git checkout --` on a dirty tree); `prettier --write` every generated file before
committing; **a counterfactual must fail for the right reason: read the failure message, not the count**; a break that leaves
everything green proves nothing, so say so and pick another; grep for other uses before removing an import; count hook calls
generic-aware (`use\w+(<[^>]*>)?\(`); rewrite every "above"/"below" pointer when the code it points at moves; **one vitest
at a time**, long runs as a direct background command (not a trailing `&`), batches of ten; **restore the file after every
break before anything else runs**; every spec rewrites `.specify/feature.json`, keep the branch's value on a rebase.

## Phase 1 — Record the "before" (T-A)

- [x] **T001 [US1]** (frontend) On the unsplit tree record into the scratchpad, for the 8 covering files
  (`OverlayEditor{Characterisation,Keyboard,Undo,Colour,Shape,Backdrop,InertElements}.test.tsx`,
  `OverlayLabelNodeCountCharacterisation.test.tsx`): (a) vitest JSON → sorted `file :: full name :: status`
  (`names471.cjs`); (b) sorted assertion lines; (c) line counts of `OverlayEditor.tsx` (911) and the component (576), code
  and comment lines apart; (d) hook calls in the file, generic-aware (expect 34); (e) `eslint-disable` (expect none); (f)
  the `shared` suite and the `management-web` caller tests (`OverlayDraftForm*`, `OverlayEditorReseedRegression`): counts
  and durations. Depends on nothing.

## Phase 2 — Probe first, then pin (US1)

### User story 1 (P1) — the editor behaves exactly as before (FR-001, FR-003, SC-005)

- [x] **T002 [US1]** (test-adversary) Write `probe478.cjs` in the scratchpad (pattern of `probe477.cjs`/`run477.cjs`): the
  named breaks of plan §R2 (A–K), each asserting its pattern matches **exactly once**, applying it to the unsplit
  `OverlayEditor.tsx`, running the family from `apps/shared`, recording which test failed and its message or "none", and
  restoring from git (`git diff` empty after each). Run in batches of ten, one vitest at a time. Record the matrix; for each
  "none" decide: new test, or **double break** when another term masks it. Depends on T001.
- [x] **T003 [US1]** (test-writer) For every concern the matrix leaves unnoticed, create a test in a **new file** beside the
  editor (expected: announcer, capture, gestures/live readout, undo strings, plus whatever T002 names; direct tests for the
  pure functions only where a term is not otherwise pinned — plan §R3). Run from `apps/shared`: **green on the unsplit
  file** (quote the output). Depends on T002.
- [x] **T004 [US1]** (test-adversary) For each new test, re-apply its break and **read and quote the failure message** (right
  reason, not just red); restore (`git diff` empty). Write the double-break table for anything defended jointly. Commit
  `test(478): pin the OverlayEditor behaviours the family missed`. Depends on T003.

## Phase 3 — One extraction per commit, least coupled first (US1, US2)

### User story 2 (P2) — the component reads as a list of what it composes (FR-002, FR-005, FR-006, FR-007)

Each task: move only what plan §R4 names, **at the position of the code it replaces**; `useOverlayEditHistory` still called
once in the editor; hooks called unconditionally in the same order; dependency arrays copied verbatim; comments move with
their code once; after the move apply the matching T002 break to the **new unit** and see the same test fail there; run the 8
covering files + the new gap files from `apps/shared`, `eslint --max-warnings 0`, `tsc --noEmit`, `prettier --check` (before
committing); `git diff --stat` shows only the editor and its new unit(s); `git add` those only; commit
`refactor(478): <unit>`. Fixed order, one branch, one index. Each depends on the previous.

- [x] **T005 [US2]** `overlayEditor/overlayGeometry.ts` (concern A): the six functions, the step/size constants and their long
  comments. Exports only what the editor and the later units use.
- [x] **T006 [US2]** `overlayEditor/overlayAnnouncement.ts` (concern B): `AnnounceAxis`, labels, `edgeName`,
  `buildAnnouncement`, `arrowRunKey`.
- [x] **T007 [US2]** `overlayEditor/canvasStyle.ts` + `overlayEditor/InertElement.tsx` (concern C): the focus ring, the
  checkerboard, `canvasBackgroundStyle`, `InertElement`.
- [x] **T008 [US2]** `overlayEditor/useGeometryAnnouncer.ts` (concern G): `liveMessage`, `queueAnnouncement`, the timer, the
  repeat toggle, the unmount cleanup.
- [x] **T009 (decision gate T-D1)** (orchestrator) **Measure, then ask.** Component lines, `OverlayEditor.tsx` lines, hook
  calls left, the five units' sizes and the **file total** against T001; extrapolate to the remaining units using plan §R4.
  Report to the maintainer **before** T010 and ask whether the wider extractions are worth it. Depends on T008.
- [x] **T010 [US2]** `overlayEditor/useUndoRedoControls.ts` (concern I): the token-keyed announcement, the two click handlers,
  `handleRootKeyDown`; the key/`token` fix is **not** unified with T008's.
- [x] **T011 [US2]** `overlayEditor/useEmitNormalized.ts` (concern D): `emitNormalized` over the single `commit`.
- [x] **T012 [US2]** `overlayEditor/useCanvasGestures.ts` (concern E): the live `preview`, `geometryFromPixels`,
  `emitGeometry`, the four `Rnd` handlers. **Stop and ask** past ~8 inputs.
- [x] **T013 [US2]** `overlayEditor/useLabelKeyboard.ts` (concern H): `handleLabelKeyDown`, `handleLabelKeyUp`. **Stop and ask**
  past ~8 inputs.
- [x] **T014 [US2]** `overlayEditor/useBackdropCapture.ts` (concern J): `backdrop`, `capturedFrame`, `selectedCamera`,
  `useFrameCapture`, the four handlers.
- [x] **T015 [US2]** `overlayEditor/LabelFields.tsx` (concern K): the label-text input and the font-size range, in place so
  DOM order is unchanged; the `as T` cast stays in the editor's call (`onChange={(next, boundary) => commit(next as T, boundary)}`).

## Phase 4 — Gates and PR

- [x] **T016** (frontend) **T-A after:** repeat T001's records. (a) names+status `diff` empty for the 8 covering files (new
  gap files listed separately); (b) assertion lines `diff` empty; `git diff --stat origin/develop...HEAD` on the family is
  empty (FR-001); (c) `OverlayEditor.tsx`, the component, every unit (code and comment lines apart) and the **file total**
  before/after (SC-002); (d) no `eslint-disable` in the editor or `overlayEditor/` (SC-003); (e) distinctive comment phrases
  once, no stale "above"/"below" pointers; (f) the `shared` and `management-web` suites, counts and durations before/after
  (SC-004); (g) the T002 per-unit counterfactuals recorded. Depends on T015.
- [x] **T017** (frontend) `eslint --max-warnings 0`, `tsc --noEmit`, `prettier --check` for `shared` and `management-web`;
  `dotnet test tests/Architecture.Tests -c Release` (scans `.tsx`). Depends on T016.
- [x] **T018** (frontend) `specs/478-overlay-editor-split/verification.md` (probe matrix, gap tests with quoted messages,
  double-break table, sizes, what was not extracted and why); tick T001–T019. Depends on T017.
- [ ] **T019** (orchestrator) Phase 6 `/code-review medium`, findings fixed or accepted in writing; Phase 7 PR to `develop`
  (`--base develop`, `Closes #3191`, Phase 4a characterisation stated, sizes stated as measured, gaps and slips kept
  visible); park; merge when CI concludes success on the PR tip; close-out comment on #3191. Depends on T018.

## Dependencies

T001 → T002 → T003 → T004 → T005 → … → T009 (**gate**) → T010 → … → T015 → T016 → T017 → T018 → T019. Strictly linear: one
branch, one index, one concern per commit. No parallel tasks.

## Implementation strategy

MVP is T005–T008 plus the gate: four low-coupling units (two pure modules, the canvas style, the announcer). If T009 shows the
decomposition is not earning its keep, stop there and ask. The units that share `commit` and the history (T011–T013) come
last, after the method has been proved on the simple ones.
