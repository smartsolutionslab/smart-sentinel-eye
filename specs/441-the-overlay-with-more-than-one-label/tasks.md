# Tasks 441: The overlay with more than one label

**Spec:** [spec.md](spec.md) · **Plan:** [plan.md](plan.md) · **Issue:** #3072 (feature-level, on Project #13)
**Slice:** 1 of 3 (plan §"Slices"). **Engineer:** frontend-engineer only. **Colour:** two (plan §"Phase 4a").
**Reviewers:** frontend-reviewer. No security-reviewer (no endpoint, scope, token or validation change).

Format: `[ID] [P?] [Story] description — files`

## Phase 4a-1 — characterisation, captured green first (test-writer)

- [ ] T001 [US1] Run and quote green, unmodified: the `OverlayEditor*`, `OverlayLabel*`, `OverlayGeometryFields`,
      `OverlayDraftForm*`, `OverlayEditPage*`, `OverlayCreatePage`, `overlays.schema` suites and
      `e2e/overlays.spec.ts` — no file edited

## Phase 4a-2 — new behaviour, observed red (test-writer)

- [ ] T002 [P] [US1] `layerName`, list/ordinal mapping, `textOverlaps` (strict, Text-only, i<j),
      `selectionAfterRemove` — `apps/shared/src/ui/composites/overlayLayers.test.ts`
- [ ] T003 [P] [US1] Layer list: front-most-first order, ToggleChip `aria-pressed`, accessible names,
      `unavailable` at ends / at 8 / at 1, focus kept on the moved control, announcement repeats —
      `apps/shared/src/ui/composites/OverlayLayerList.test.tsx`
- [ ] T004 [P] [US1] `inertElements`: painted aria-hidden in ordinal order around the label, shape and label styles
      from the wall functions, drag of the selected label still works beneath an `above` node —
      `apps/shared/src/ui/composites/OverlayEditorInertElements.test.tsx`
- [ ] T005 [US1] Form integration: add/select/edit/move/remove, selection follows a move, shape-selected notice,
      overlap warning non-blocking, invalid element selected on submit, POST/PATCH body order and no client id,
      stale 409 after a reorder offers Reload — `apps/management-web/src/features/overlays/OverlayDraftFormLayers.test.tsx`
- [ ] T006 [P] [US1] e2e: create with two labels, reorder on the edit page, GET order via API, overlap status shown,
      Add disabled at 8 (reuse `e2e/support/management-overlays.ts`; Playwright `--shard` picks it up) —
      `e2e/overlay-layer-list.spec.ts`

T002–T004 and T006 own disjoint files: **[P]**. T005 depends on nothing but shares the overlays feature folder.

## Phase 4b — implementation (frontend-engineer; may not edit T001–T006)

- [ ] T010 [P] [US1] C1 pure functions — `apps/shared/src/ui/composites/overlayLayers.ts` (T002)
- [ ] T011 [US1] C2 layer list — `apps/shared/src/ui/composites/OverlayLayerList.tsx` (T003, T010)
- [ ] T012 [P] [US1] C3 optional `inertElements` prop — `apps/shared/src/ui/composites/OverlayEditor.tsx` (T004)
- [ ] T013 [US1] C4 `useFieldArray`, selection, notices, overlap, invalid-selection —
      `apps/management-web/src/features/overlays/OverlayDraftForm.tsx`; header copy in `OverlayCreatePage.tsx`,
      `OverlayEditPage.tsx` (T005, T010–T012)
- [ ] T014 [US1] Run plan §"Counterfactuals" 1–6, quote each red; `prettier --check`, eslint, `tsc --noEmit` for
      shared + management-web + e2e, vitest; T001 suites re-run unmodified and green (T013)

## Phase 5 — verify

- [ ] T020 [US1] Spec §"Independent end-to-end test procedure" steps 1–6 on the run-mode stack, screenshots of the
      list, the overlap line and the wall; `git diff --stat origin/develop` showing no kiosk/backend/contract file —
      `specs/441-the-overlay-with-more-than-one-label/verification.md`

## Phase 6 — review

- [ ] T030 frontend-reviewer (a11y of the list, ADR-0151, RHF field-array correctness, disjoint-file rule)

## After merge

- [ ] T040 File slice 2 (Box/Ellipse authoring) and slice 3 (colour authoring, spec 300 US4) as feature issues on
      Project #13, linked from #3072
