# Tasks 234 — A capture that fails quietly

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Issue**: #2356 (feature-level issue; no per-task issues)
**Engineer**: `frontend-engineer` · **Phase 4a colour**: per task below — RED (items 1, 3),
characterisation observed green (items 2, 4), guards green before and after (FR-003, FR-007).

Format: `[ID] [P?] [Story] description — file(s)`.
`[P]` = owns files no other open task touches (ADR-0109). No foundational task: nothing in
Shared.Kernel/Contracts, AppHost or Aspire resources changes, so nothing blocks the fan-out.
Item 5 has **no task** (spec §3 — not measured, no 4K camera).

## Phase 4a — tests first (test-writer; return verbatim output)

- [ ] **T001** [P] [US1+US2] New `describe` block **inserted after the FR-012 cancel test (`:306`)**, not appended: `console.info` spy + `resilienceLines` filter, then tests a–h of plan §4 — `apps/shared/src/ui/composites/FrameCapture.test.tsx`
- [ ] **T002** [P] [US3] New test after the create test (`:19-35`): White field paints `overlay-editor-canvas` `rgb(255, 255, 255)`, asserted not-white before the click; no save, no camera-catalogue assertion — `e2e/overlays.spec.ts`
- [ ] **T003** [US1+US2] Run `pnpm --filter @smart-sentinel-eye/shared test -- FrameCapture` on unchanged code. Expected, verbatim: **b, c, e, f, g red** (failing on the missing log line / live region / focus, not on a compile or import error); **a, d, h green**; the nine existing tests green. Depends on T001.
- [ ] **T004** [US1] Counterfactual for test a: temporarily make the `FrameGrabber.tsx:84` `catch` rethrow, run test a, quote its red output, revert, confirm green again. Depends on T003.
- [ ] **T005** [US3] Boot the stack; run `pnpm exec playwright test e2e/overlays.spec.ts -g "White field"` — **green** (characterisation). If the stack cannot boot, report that verbatim; do not report green. Depends on T002.

## Phase 4b — production (engineer; T001/T002 may not be edited)

- [ ] **T006** [P] [US1] Import `logResilienceEvent`; log `frame-capture-failed` with `{ cameraIdentifier, error }` on the three draw-path failures (plan §2); add `cameraIdentifier` to the effect deps; leave `:74-83` and `:90-103` untouched — `apps/shared/src/ui/composites/FrameGrabber.tsx`
- [ ] **T007** [P] [US2] In `CameraCaptureSection`: persistent `aria-live="polite"` region (`data-testid="frame-capture-live-region"`) above the alert; `captureButtonRef` + `cancelWasFocusedRef` + effect returning focus only when focus fell to `body` (plan §3) — `apps/shared/src/ui/composites/BackdropControls.tsx`
- [ ] **T008** [US1+US2+US3] `pnpm --filter @smart-sentinel-eye/shared test` (full suite, incl. `OverlayEditorBackdrop.test.tsx`, `OverlayEditorKeyboard.test.tsx`), `pnpm -r lint`, `pnpm -r typecheck` green; T001's tests green **unmodified**; T005's e2e still green. Depends on T004–T007.

## Phase 5

- [ ] **T009** Spec §10 step 3 if a camera is available; otherwise record US1/US2 as component-test-verified and US3 as e2e-verified. Record item 5 as not measured. Depends on T008.

## Dependencies

```
T001 ─→ T003 ─→ T004 ─┐
T002 ─→ T005 ─────────┼─→ T008 ─→ T009
T006, T007 (after T004) ┘
```

T001/T002 are disjoint files and run in parallel; T006/T007 likewise. Test/production pairs
never share a file. In practice one `frontend-engineer` does T006–T007 in minutes; `[P]` records
that nothing forces an order between them.
