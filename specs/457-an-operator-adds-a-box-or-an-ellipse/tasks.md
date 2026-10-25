# Tasks 457: An operator adds a box or an ellipse

**Spec:** [spec.md](spec.md) · **Plan:** [plan.md](plan.md) · **Issue:** #3107 (feature-level, on Project #13)
**Engineer:** frontend-engineer only. **Colour:** RED (behaviour-changing), with a declared characterisation set
and a declared superseded set (plan §"Phase 4a").
**Reviewers:** frontend-reviewer. No security-reviewer (no endpoint, scope, token or validation change).
**New ADR:** none (decisions trace to ADR-0077/0151/0165 and the owner's 2026-10-23 ruling on #3107).

Format: `[ID] [P?] [Story] description — files`

## Foundational

None. No Shared.Kernel/Contracts, AppHost or Aspire resource is touched; nothing blocks the test fan-out.

## Phase 4a-1 — characterisation, captured green first (test-writer)

- [ ] T001 [US1] Run and quote green, unmodified, every suite in plan §"Characterisation" — no file edited

## Phase 4a-2 — new behaviour, observed red (test-writer)

- [ ] T002 [P] [US1] `newElement(kind)` defaults per kind, `addedAnnouncement(kind, n)` —
      `apps/shared/src/ui/composites/overlayLayers.test.ts` (additions only)
- [ ] T003 [P] [US1] Add menu, per-kind `onAdd`, unavailable trigger opens nothing (cap + read-only), focus on new
      chip, per-kind announcement, `aria-hidden` kind icon with unchanged chip names —
      `apps/shared/src/ui/composites/OverlayLayerListAdd.test.tsx` (new)
- [ ] T004 [P] [US1] Shape value: `overlayShapeStyle`, name/roledescription, no text/font/preview, geometry fields,
      drag + nudge + undo keep kind and colour — `apps/shared/src/ui/composites/OverlayEditorShape.test.tsx` (new)
- [ ] T005 [P] [US1] Shape echo vs re-seed, shape geometry step — `apps/shared/src/ui/composites/useOverlayEditHistory.test.ts`
      (additions only; existing tests untouched)
- [ ] T006 [US1] Form: add Box → POST body; Ellipse drag → PATCH keeps colour; shapes-only opens editable with
      camera picker; Add unavailable at 8; refused width never sent; 409 stale keeps the shape and offers Reload;
      403 keeps it — `apps/management-web/src/features/overlays/OverlayDraftFormShapes.test.tsx` (new)
- [ ] T007 [US1] Superseded set (plan table): rewrite the four named tests to the new behaviour, and make the
      activation-path-only edits; quote each file's diff in the hand-back —
      `OverlayDraftForm.test.tsx`, `OverlayDraftFormLayers.test.tsx`, `OverlayDraftFormReview.test.tsx`,
      `apps/shared/src/ui/composites/OverlayLayerList.test.tsx`, `OverlayLayerListReview.test.tsx`,
      `e2e/overlay-layer-list.spec.ts`
- [ ] T008 [P] [US1] e2e: spec §"Independent end-to-end test procedure" steps 2-4 and 6, GET via the API —
      `e2e/overlay-shapes.spec.ts` (new; reuse `e2e/support/management-overlays.ts`)

T002-T005 and T008 own disjoint files: **[P]**. T006 and T007 share the overlays feature folder and the
management-web test setup; run them in one test-writer pass after T002-T005. A behaviourless stub commit (as
spec 441's `a57f4c4d`) is allowed so reds fail on assertion, not compilation.

## Phase 4b — implementation (frontend-engineer; may not edit T001-T008)

- [ ] T010 [P] [US1] C1 `newElement`, `addedAnnouncement` — `apps/shared/src/ui/composites/overlayLayers.ts` (T002)
- [ ] T011 [P] [US1] C2 kind icon — `apps/shared/src/ui/composites/OverlayKindIcon.tsx` (new)
- [ ] T012 [P] [US1] C5 union-aware history — `apps/shared/src/ui/composites/useOverlayEditHistory.ts` (T005)
- [ ] T013 [US1] C3 Add menu + icon in chip — `apps/shared/src/ui/composites/OverlayLayerList.tsx`
      (T003, T010, T011; `DropdownMenu.tsx` only under plan C3's stated condition)
- [ ] T014 [US1] C4 kind branch — `apps/shared/src/ui/composites/OverlayEditor.tsx` (T004, T012)
- [ ] T015 [US1] C6 form wiring, notices removed, camera skip removed —
      `apps/management-web/src/features/overlays/OverlayDraftForm.tsx`; header copy in `OverlayCreatePage.tsx` /
      `OverlayEditPage.tsx` if needed (T006, T007, T010, T013, T014)
- [ ] T016 [US1] Plan §"Counterfactuals" 1-7, each quoted red; `prettier --check`, eslint, `tsc --noEmit` for
      shared + management-web + e2e; vitest; T001 suites re-run unmodified and green (T015)

T010-T012 own disjoint files: **[P]**. T013 → T014 → T015 are sequential (each consumes the previous API).

## Phase 5 — verify

- [ ] T020 [US1] Spec §"Independent end-to-end test procedure" steps 1-6 on the run-mode stack; screenshots of the
      Add menu, a selected Box's inspector and the wall tile; `git diff --stat origin/develop` showing no
      kiosk/backend/contract/schema file — `specs/457-an-operator-adds-a-box-or-an-ellipse/verification.md`

## Phase 6 — review

- [ ] T030 frontend-reviewer: Text path byte-identical (characterisation untouched), superseded-set diffs limited to
      what plan §"Phase 4a" declares, menu a11y (focus, unavailable), ADR-0151, no new dependency

## After merge

- [ ] T040 File slice 3 (colour authoring, spec 300 US4 / spec 441 plan) as a feature issue on Project #13, linked
      from #3072 and #3107
