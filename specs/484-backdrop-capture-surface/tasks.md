# Tasks 484 — `useBackdropCapture` returns three groups, not eleven values

Spec: [spec.md](spec.md) · Plan: [plan.md](plan.md) · Issue #3225 (feature-level; on Project #13).
Engineer: **frontend**. Phase 4a colour: **behaviour-preserving → characterisation.** The existing tests stay unmodified (`git diff`
on them empty): `OverlayEditorCapture.test.tsx` and the other `OverlayEditor*.test.tsx` files in `apps/shared/src/ui/composites/`
(twelve), plus `BackdropControls*.test.tsx` and the `management-web` overlay tests. Anything the probes (T002) show they do not notice
gets a test in a **new** file, written first and **green on today's code** (T003), with its failure message read (T004).
**Latency (§IV): not on the path.** `OverlayEditor` is the operator's authoring canvas; only `management-web` imports it. Every
commit message and the PR say "§IV legs touched: none" (FR-009).

One PR. Run Vitest from `apps/shared` (`management-web` at verify); scripts, from the repo root: `npm run format:check`; eslint and tsc
directly in each app (`eslint src --max-warnings 0`, `tsc --noEmit`; workspace names are scoped). Scratchpad files are not committed.
**Commit hygiene (lessons from specs 470–483):** anchor the working directory in every command; `git add` only the files of the step
(the two foreign C# edits and `apphost.pid` are never staged); `prettier --write` every generated file; **a counterfactual must fail
for the right reason: read the message, not the count**; stage a new untracked file before its counterfactuals; scripts are files in
the scratchpad written with the Write tool (a bash heredoc turns `\n` into a real newline); **a probe runner restores only the file(s)
it breaks, never the working tree**, and after a run **check the results file is fresh and the tree clean**; **one vitest at a time**,
batches of five in the foreground (a background job may be reaped for low memory: report it, do not restart it unasked, and check for a
break left applied); every spec rewrites `.specify/feature.json`, keep the branch's value on a rebase; `apps/shared` tests default to
node (`// @vitest-environment jsdom` for DOM ones); never `git stash` the working tree.

## Phase 1 — Record the "before" (T-A)

- [x] **T001 [US1]** (frontend) On the current tree record into the scratchpad: (a) the names and status of every test in the covering files above
      (`file :: full name :: status`); (b) line counts of `useBackdropCapture.ts`, `OverlayEditor.tsx` and their total; (c) the number of
      names `OverlayEditor.tsx` destructures from the hook (expect 11); (d) suite counts and durations for `shared` and `management-web`.
      Depends on nothing.

## Phase 2 — Probe first, then pin (US1)

### User story 1 (P1) — the editor captures and paints exactly as before (FR-001, FR-004, SC-003)

- [x] **T002 [US1]** (test-adversary) Write `probe484.cjs` and `run484.cjs` in the scratchpad (pattern of `probe483.cjs`; restore only the two
      files it breaks): named, exactly-once breaks, in `useBackdropCapture.ts`: (A) the initial camera ignored (`useState('')`); (B)
      `handleCapture` without its empty-camera guard; (C) `handleCameraChange` without `cancel()`; (D) `handleCameraChange` without
      `setSelectedCamera`; (E) `handleCaptured` without `setBackdrop('captured')`; (F) without `setCapturedFrame`; (G) without `cancel()`;
      (H) initial backdrop `'white'`; (I) `handleCapture` captures `initialCamera`, not `selectedCamera`; (J) `handleCapture` with `[]`
      dependencies (stale camera); (K1–K3) each of `handleCameraChange`, `handleCapture`, `handleCaptured` loses its `useCallback`; and in
      `OverlayEditor.tsx`: (L) `canvasBackgroundStyle(backdrop, null)`; (M) `hasCapturedFrame` constant `true`; (N) the grabber
      `Fragment` without its `key`; (O) `onCancelCapture` a no-op; (P) `onFailed` a no-op; (Q) `onCaptured` and `onFailed` swapped.
      Each asserts its pattern matches once, applies it, runs `OverlayEditorCapture` and the `OverlayEditor*` family, records which test
      failed and its message or "none", and restores from git (`git diff` empty). Batches of five. For each "none": new test, **double
      break** when another term masks it, or **equivalent** with the reason. Depends on T001.
- [x] **T003 [US1]** (test-writer) Write the gap tests for every "none", in new files beside `OverlayEditorCapture.test.tsx` (names decided by the
      matrix, e.g. `OverlayEditorCaptureBackdrop.test.tsx`), reusing its harness: drive the real `OverlayEditor` with a `renderFrameGrabber`
      double, assert the canvas background after a delivered frame, the backdrop choice reaching the controls, `Capture` refusing with no
      camera, `cancel` versus `fail` (a `failed` alert versus `idle`), the grabber released and keyed on the active camera. Run from
      `apps/shared`: **green on today's code** (quote the output). Depends on T002.
- [x] **T004 [US1]** (test-adversary) For each new test, re-apply its break and **read and quote the failure message**; restore (`git diff`
      empty). Write the double-break and equivalence tables. Commit `test(484): pin the backdrop-capture behaviours the suites missed`.
      Depends on T003.

**Gate:** tests green, new tests observed red under their breaks. Stop and ask.

## Phase 3 — Switch (US2) (FR-002, FR-003, FR-006)

- [x] **T005 [US2]** (frontend) `useBackdropCapture.ts` returns `{ canvas, controls, grabber }` (plan D1: `controls` in `BackdropControls`' prop
      names); `OverlayEditor.tsx` destructures the three, calls `canvasBackgroundStyle(canvas.backdrop, canvas.capturedFrame)`, spreads
      `{...controls}` into `<BackdropControls>` beside `getToken` and `cameras`, and reads `grabber.activeCamera`, `grabber.onCaptured`,
      `grabber.onFailed` for the grabber. The `getToken` gating comment keeps its place (D5). One commit (D4). `prettier --write`; `tsc`,
      `eslint`. `OverlayEditorCapture` and the family re-run **unmodified**. Re-apply, to the new code, the breaks that still have a place
      (A–K3 in the hook; L–Q reworded onto the group reads): the same tests fail. Commit `refactor(484): useBackdropCapture returns three
groups` with `§IV legs touched: none`. Depends on T004.

**Gate (T-D1, brief):** report the measured deltas (lines, names the editor destructures 11 → 3). A test that cannot pass without an
edited assertion is a **block**, reported, not adjusted. Stop and ask.

## Phase 4 — Verify (US1, US2)

- [ ] **T006** (frontend) Write `verification.md`: matrix of probes and dispositions, the "§IV legs touched: none" statement with the reason,
      size deltas, existing-test `git diff` empty, suite counts and durations before/after for `shared` and `management-web`,
      `eslint src --max-warnings 0`, `tsc --noEmit`, `prettier --check`, `Architecture.Tests` (they scan `.tsx`; one foreground attempt;
      if it is reaped for memory, say so and name the CI `backend` job as the evidence). Tick T001–T006. Depends on T005.
- [ ] **T007** (frontend) Final grep: `OverlayEditor.tsx` destructures at most three names from the hook; no loose `setBackdrop`,
      `handleCameraChange`, `handleCapture`, `handleCaptured`, `fail` or `cancel` read remains in the editor. Depends on T006.

## Phase 5 — QA and PR

- [ ] **T008** `/code-review medium`; dispositions recorded in `verification.md`. **T009** open the PR to `develop` (`--base develop`) with the
      phase 4a evidence, "§IV legs touched: none" and the measured figures; park it (do not wait on CI); merge on green with the run's head
      SHA equal to the PR tip; close-out comment on #3225 and check the issue closed (`Closes #3225`).

## Dependencies

T001 → T002 → T003 → T004 → T005 → T006 → T007 → T008 → T009. No parallel tasks (one branch, one index).
