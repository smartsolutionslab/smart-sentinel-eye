# Tasks 482 — The two Save gates adopt the shared `useSubmitGuard`

Spec: [spec.md](spec.md) · Plan: [plan.md](plan.md) · Issue #3223 (feature-level; on Project #13).
Engineer: **frontend**. Phase 4a colour: **behaviour-preserving → characterisation.** The existing tests stay unmodified (`git diff` on
them empty): `OverlayDraftFormSaveGate`, `OverlayDraftForm*` (overlay), `LayoutEditorDialogSaveGate`, `LayoutEditorDialogChainRecovery`,
`LayoutEditorDialog*` (layouts) in `management-web`, and `useFormDialog.test` in `shared`. Anything the probes (T002) show they do not
notice gets a test in a **new** file, written first and **green on today's code** (T003), with its failure message read (T004).
**Latency (§IV): not on the path.** Operator-console form submit; no kiosk, SFU, overlay or render leg. Every commit message and the PR
say "§IV legs touched: none" (FR-009).

One PR. Run Vitest from the app that owns the test; scripts, from the repo root: `npm run -w <app> test | lint | typecheck`,
`npm run format:check`. Scratchpad files are not committed.
**Commit hygiene (lessons from specs 470–481):** anchor the working directory in every command; `git add` only the files of the step
(the two foreign C# edits are never staged); `prettier --write` every generated file; **a counterfactual must fail for the right
reason: read the message, not the count**; stage a new untracked file before its counterfactuals; scripts are files in the scratchpad
(never a bash heredoc with quotes); grep for other uses before removing an import; **one vitest at a time**, batches of ten; **restore
the file after every break before anything else runs**; every spec rewrites `.specify/feature.json`, keep the branch's value on a
rebase; workspace names are scoped (`@smart-sentinel-eye/...`), so run eslint and tsc directly in each app (`eslint src --max-warnings
0`); `apps/shared` tests default to node (`// @vitest-environment jsdom` for DOM ones).

## Phase 1 — Record the "before" (T-A)

- [x] **T001 [US1]** (frontend) On the current tree record into the scratchpad: (a) the names and status of every test in the covering
      files above (`file :: full name :: status`); (b) line counts of the two `useSaveGate.ts` files and `useFormDialog.ts`, and the total;
      (c) occurrences of the hand-written guard body (`event.preventDefault(); return;` after `if (saveBlocked)`) in non-test code (expect 2) and of `useSubmitGuard` call sites (expect 6); (d) suite counts and durations for `shared` and `management-web`. Depends on
      nothing.

## Phase 2 — Probe first, then pin (US1)

### User story 1 (P1) — both Save buttons guard exactly as before (FR-001, FR-003, SC-003)

- [x] **T002 [US1]** (test-adversary) Write `probe482.cjs` and `run482.cjs` in the scratchpad (pattern of `probe481.cjs`): named,
      exactly-once breaks of each concern in **each of the two gates**: (A) blocked: drop `event.preventDefault()`; (B) blocked: drop the
      `return` so `onSubmit` is also called; (C) open: never call `onSubmit` (`void onSubmit(event)` removed); (D) open: call `onSubmit()`
      without the event; (E) open: also `preventDefault()`; (F) guard inverted (`if (!saveBlocked)`); (G) guard reads a frozen flag (the
      first render's `saveBlocked`) so a blocked→open rerender stays blocked; (H) each `saveBlocked` term dropped once (overlay: `isLoading`,
      `colourRefused`, `isEdit && currentChain === undefined`, `chainFetching`, `chainFailed`; layout: `isLoading`, `knownCameras.size ===
0`, the same three chain terms), run against the same suites to record which test notices (context for the matrix, not new scope:
      no term changes). Each asserts its pattern matches once, applies it, runs the gate's tests, records which test failed and its message
      or "none", and restores from git (`git diff` empty). Batches of ten. For each "none" on A–G: new test, **double break** when another
      term masks it, or **equivalent** with the reason. Depends on T001.
- [x] **T003 [US1]** (test-writer) Write the gap tests for every "none" on A–G, in new files beside each gate's existing test (e.g.
      `OverlayDraftFormSubmitGuard.test.tsx`, `LayoutEditorDialogSubmitGuard.test.tsx`; names decided by the matrix): drive the real
      form's submit event on **both routes** (click Save, Enter in a text field), assert `defaultPrevented` and the `onSubmit` spy for
      blocked and open. Run from `apps/management-web`: **green on today's code** (quote the output). Depends on T002.
- [x] **T004 [US1]** (test-adversary) For each new test, re-apply its break and **read and quote the failure message**; restore (`git diff`
      empty). Write the double-break and equivalence tables. Commit `test(482): pin the Save-gate submit-guard behaviours the suites
missed`. Depends on T003.

**Gate:** tests green, new tests observed red under their breaks. Stop and ask.

## Phase 3 — Switch (US2) (FR-002, FR-006)

- [x] **T005 [US2]** (frontend) `apps/shared/src/hooks/useFormDialog.ts`: rename `useSubmitGuard`'s first parameter `isLoading` → `blocked` and
      widen the doc line (plan D1: ADR-0151's in-flight reason stays; the Save gates pass their whole gate). Callers are positional;
      `useFormDialog.test.tsx` and the six callers' suites re-run **unmodified**; `tsc` for `shared` and the three apps that use the hook.
      `prettier --write`. Commit `refactor(shared): name useSubmitGuard's flag for what it guards` with `§IV legs touched: none`. Depends
      on T004.
- [x] **T006 [US2]** (frontend) `overlayDraftForm/useSaveGate.ts`: replace the hand-written `handleFormSubmit` with `const handleFormSubmit =
useSubmitGuard(saveBlocked, onSubmit)` (import from `@smart-sentinel-eye/shared/hooks`); drop the `FormEvent` import only if
      grep shows no other use; keep the FR-003 comment where it still says something true (it explains why the guard exists; the
      shared hook's doc carries the mechanism: shorten, do not duplicate). `OverlayDraftFormSaveGate` and the overlay family unchanged;
      `tsc` confirms D3. Apply the matching break (A–G) to the **shared** hook: the overlay gate's tests fail. Commit `§IV legs touched:
none`. Depends on T005.
- [x] **T007 [US2]** (frontend) `layoutEditor/useSaveGate.ts` switches the same way. `LayoutEditorDialogSaveGate`,
      `LayoutEditorDialogChainRecovery` and the dialog family unchanged; matching break on the shared hook fails the layout gate's tests.
      Commit as T006. Depends on T006.

**Gate (T-D1, brief):** report the measured deltas (lines, hand-written guard bodies 2 → 0, `useSubmitGuard` call sites 6 → 8). A gate
that could not switch without an edited assertion is a **block**, reported, not adjusted. Stop and ask.

## Phase 4 — Verify (US1, US2)

- [x] **T008** (frontend) Write `verification.md`: matrix of probes and dispositions, per-commit §IV statements ("none", with the reason),
      size deltas, existing-test `git diff` empty, suite counts and durations before/after for `shared` and `management-web` (plus
      `management-cameras` for the rename), `eslint src --max-warnings 0`, `tsc --noEmit`, `prettier --check`, `Architecture.Tests` (they
      scan `.tsx`). Tick T001–T008. Depends on T007.
- [x] **T009** (frontend) Final grep: the guard body `event.preventDefault();` after `if (saveBlocked)` finds no hit in `useSaveGate` files;
      `useSubmitGuard` has eight call sites; no stale `isLoading` in the shared hook's doc. Depends on T008.

## Phase 5 — QA and PR

- [ ] **T010** `/code-review medium`; dispositions recorded in `verification.md`. **T011** open the PR to `develop` (`--base develop`) with
      the phase 4a evidence, the per-commit "§IV legs touched: none" statements and the measured figures; park it (do not wait on CI);
      merge on green with the run's head SHA equal to the PR tip; close-out comment on #3223 and check the issue closed (`Closes #3223`).

## Dependencies

T001 → T002 → T003 → T004 → T005 → T006 → T007 → T008 → T009 → T010 → T011. No parallel tasks (one branch, one index).
