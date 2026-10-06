# Tasks 305: The editor that had to fit a dialog

Spec: [spec.md](spec.md) · Plan: [plan.md](plan.md) · Issue #2350 (feature-level; must be on Project #13).
**Engineer:** frontend. **One PR**, commits in task order, each building and passing on its own.
`[P]` = disjoint files (ADR-0109). Phase 4a colour per task: **C** characterisation (green
before and after, unmodified), **R** red first.

## Phase 0: baseline (blocks everything)

- [ ] **T001 [C]** On `4c57cd73`, run and record verbatim (counts + pass): `OverlayEditorDialog*.test.tsx` (8 files), `OverlayEditorReseedRegression.test.tsx`, `OverlaysPage.test.tsx`, `apps/shared/src/ui/composites/OverlayEditor*.test.tsx`, `app/router`/`ShellLayout*.test.tsx`. This output is the characterisation baseline quoted in the PR.

## Phase 1: extraction — behaviour-preserving (blocks Phase 2)

- [ ] **T002 [C] [US1,US2]** Create `features/overlays/OverlayDraftForm.tsx` holding the dialog body verbatim (props `editTarget?`, `onDone`, `onCancel`, optional `canvasWidthPx`/`canvasHeightPx` passed through to `OverlayEditor`); reduce `OverlayEditorDialog.tsx` to `<Dialog>` + title/description + reset-on-close + `<OverlayDraftForm>`. **Gate:** T001's suites green with zero test-file changes. Commit: `refactor(2350): extract OverlayDraftForm from OverlayEditorDialog`.
- [ ] **T003 [C] [US1,US2]** Re-host the form-behaviour suites onto `OverlayDraftForm` (rename files `OverlayDraftForm*.test.tsx`; change imports/render helpers only). Do not re-host the dialog-only cases listed in plan.md §Phase 4a. **Gate:** green; `git diff -M` on these files shows no changed `expect(` / `waitFor` assertion line. Commit: `test(2350): host the editor's form suites on OverlayDraftForm`.

## Phase 2: routes and pages — new behaviour (RED first; after T003)

Test-writer writes T004–T007 first, runs them, returns verbatim red output; engineer then implements T008–T011.

- [ ] **T004 [R] [P] [US1]** `features/overlays/OverlayEditPage.test.tsx` (real `createMemoryRouter`, mocked RTK hooks as in `OverlayEditorDialog.test.tsx`): seeds from the named revision's elements + chain name; renders no `role="dialog"`; not-a-draft notice + back link, no Save; 404 notice; `revisions/abc` → notice and **no** GET; 5xx → `RetryBanner`, Retry refetches; Save success → location `/overlays`; Cancel → `/overlays`, no PATCH; a refetch after a user edit does **not** reseed the text field; canvas receives `canvasWidthPx`/`canvasHeightPx` at 16:9 from a stubbed `ResizeObserver` (e.g. 1600 → 1600×900), and 800×450 when `ResizeObserver` is absent.
- [ ] **T005 [R] [P] [US1]** `features/overlays/OverlayEditPageNavigation.test.tsx` — REAL `useGetOverlayQuery` (pattern `CameraDetailPageNavigation.test.tsx`): one mounted instance navigated `…/A/revisions/2/edit → …/B/revisions/1/edit`; B's editor never shows A's text or submits A's version before B's GET answers (re-pins `OverlayEditorDialogChainRetention`'s A→B case on the route).
- [ ] **T006 [R] [P] [US2]** `features/overlays/OverlayCreatePage.test.tsx`: Name has focus; Save as draft success → `/overlays`; `OVERLAY_NAME_TAKEN` keeps `/overlays/new` with the existing message; Cancel → `/overlays`, no POST.
- [ ] **T007 [R] [US1,US2]** `OverlaysPage.test.tsx` (behaviour-changing edits, declared): `New overlay` → `/overlays/new`; `Edit draft` → `/overlays/{id}/revisions/{draft}/edit`, no branch sent; `Edit (new draft)` success → `…/revisions/{returned}/edit`; branch refused → stays on `/overlays`, fault notice. Replace any assertion that the dialog opened. Plus `app/router.test.tsx` or `ShellLayout.test.tsx` case: both new paths render inside the shell with the Overlays nav link `aria-current="page"`.
- [ ] **T008 [US1]** `features/overlays/useCanvasFit.ts` (FR-006).
- [ ] **T009 [US1]** `features/overlays/OverlayEditPage.tsx` per plan.md §Edit page (FR-003, FR-007), using `useCanvasFit`.
- [ ] **T010 [P] [US2]** `features/overlays/OverlayCreatePage.tsx`, using `useCanvasFit`.
- [ ] **T011 [US1,US2]** `app/router.tsx`: register `overlays/new` and `overlays/:overlayIdentifier/revisions/:revisionNumber/edit` with `SurfaceCrash` (FR-001). `OverlaysPage.tsx`: replace dialog state + both mounts with `useNavigate` calls (FR-002); move `elementsOf` to the edit page if it has no remaining caller. **Gate:** T004–T007 green; T003 suites still green.
- [ ] **T012 [US1,US2]** Delete `OverlayEditorDialog.tsx` and the remaining dialog-only test cases; update the `OverlayEditor.tsx` canvas-prop doc comment (comment only) and the cross-file comments that cite `OverlayEditorDialog.tsx:<line>` where they would now dangle (grep `OverlayEditorDialog` in `apps/`). Commit separately: `refactor(2350): delete OverlayEditorDialog`.

## Phase 3: e2e (after T011)

- [ ] **T013 [R] [US1,US2]** `e2e/overlays.spec.ts`: replace `getByRole('dialog')` visible/closed waits with `toHaveURL` (create → `/overlays/new`, edit → `/revisions/\d+/edit`, save → `/overlays$`); add one test: open edit page, `page.reload()`, label text and bounding box (relative to canvas) unchanged; add one test at 1920×1080: canvas width > 800 and width/height ≈ 16/9 (±1 px). `e2e/overlay-editor-preview-scale.spec.ts` must pass unmodified (its claim is canvas-relative).

## Phase 5 (verify)

- [ ] **T014** Run the spec's independent procedure on the live stack; note the canvas size observed at the verifier's viewport and a reload of the edit URL. Latency: N/A (§IV untouched).

## Dependency summary for the orchestrator

T001 → T002 → T003 → {T004, T005, T006, T007 in parallel (test-writer)} → T008 → {T009, T010 [P]} → T011 → T012 → T013 → T014.
Foundational/blocking: T002 (the extraction everything else hosts on) and T011 (`router.tsx` + `OverlaysPage.tsx` are single-owner files). No backend, AppHost, contract or migration task exists.
