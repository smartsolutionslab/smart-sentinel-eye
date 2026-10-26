# Tasks 463: A colour too bright for the wall

**Spec:** [spec.md](spec.md) · **Plan:** [plan.md](plan.md) · **Issue:** #3108 (feature-level, on Project #13)
**New ADR:** none (ADR-0190 decides; D1-D7 trace to it and to ADR-0146/0148/0165).
**Slices:** S1 (US1) = backend-engineer then frontend-engineer, one PR. S2 (US2) = frontend-engineer, a second PR
cut from `develop` after S1 merges.
**Colour:** both slices **RED**. S1 also carries a declared characterisation set and a rule-bounded superseded set
(plan §"Phase 4a").

Format: `[ID] [P?] [Story] description — files`

## Foundational

None. No Shared.Kernel, Shared.Contracts, AppHost or Aspire resource is touched. Within S1, **T010 (domain
constants) blocks the frontend pin work (T020-T022)**, because the FR-020 test reads the backend source.

---

## S1 — US1 (P1): a bright colour cannot be stored; stored ones are darkened

### Phase 4a-1 — characterisation, captured green first (test-writer)

- [ ] T001 [US1] Run and quote green, unmodified: `TextLegibilityTests`, `TextLegibilityVectorTests`,
      `OverlayElementConstructionCharacterisationTests`, `EditDraftRefusalOrderCharacterisationTests`,
      `apps/shared/src/ui/composites/textLegibility.test.ts` — no file edited

### Phase 4a-2 — new behaviour, observed red (test-writer)

- [ ] T002 [P] [US1] Cap boundary, alpha-ignored, new default compliant + legible —
      `tests/OverlayDesigner.Domain.Tests/Overlay/OverlayColorTests.cs` (additions)
- [ ] T003 [P] [US1] D3 vector table + invariants C2.2/C2.3 over a fixed grid —
      `tests/OverlayDesigner.Domain.Tests/Overlay/OverlayColorNearestAllowedTests.cs` (new)
- [ ] T004 [P] [US1] Every `tokens.css` primitive accepted or in `ExcludedPrimitives` with a reason —
      `tests/Architecture.Tests/OverlayColourPaletteTests.cs` (new; reuse `OklchColorResolver`)
- [ ] T005 [P] [US1] 400 naming `color` and `0.35` on POST and draft PATCH; stale+valid colour still 409 —
      Api/endpoint test file beside the existing overlay endpoint tests (additions)
- [ ] T006 [P] [US1] Scratch-database migration test: four rows across draft/published/archived, record rows,
      idempotent re-run — `tests/Integration.Tests/OverlayDesigner/OverlayColourCapMigrationIntegrationTests.cs`
      (new) **+ an entry in `tests/Integration.Tests/ci-shards/shard-N.filter`**
- [ ] T007 [P] [US1] Zod refinement; FR-020 pin for `OVERLAY_COLOR_MAX_LUMINANCE`; default paints hex —
      `apps/shared/src/api/overlays.schema.test.ts`, `apps/shared/src/ui/composites/overlayLabelStyle.test.ts`
      (additions)
- [ ] T008 [US1] Behaviourless stub commit, then quote the full red list; map every pre-existing failure to
      superseded rule (a) or (b) and apply only those edits, diff quoted per file (after T002-T007)

T002-T007 own disjoint files: **[P]**.

### Phase 4b — backend (backend-engineer; may not edit T001-T008)

- [ ] T010 [US1] C1: `RelativeLuminance` (extracted), `MaximumRelativeLuminance`, new `DefaultValue`, refusing
      `From`, `NearestAllowed` — `src/OverlayDesigner/Domain/Overlay/{RelativeLuminance,OverlayColor,TextLegibility}.cs`
- [ ] T011 [P] [US1] C3: luminance refusal message passes through — `src/OverlayDesigner/Api/OverlayElementParsing.cs`
      (T010)
- [ ] T012 [P] [US1] C5: EF migration `AddOverlayColourMigrationRecord`; `OverlayColourCapMigrator`; `[LoggerMessage]`
      lines; registration after `OverlayDesignerMigrator` —
      `src/OverlayDesigner/Infrastructure/{Persistence/Migrations/*,Persistence/OverlayColourCapMigrator.cs,Log.cs,OverlayDesignerPersistenceModule.cs}`
      (T010); prove with `git diff --stat -- src/OverlayDesigner/Infrastructure/Persistence/Migrations/`
- [ ] T013 [P] [US1] C6: seed default — `src/ScenarioSimulator/Seeding/OverlayLabel.cs` (T010)
- [ ] T014 [US1] Counterfactuals 1-4 quoted red; Release build, `dotnet format` clean; T001 re-run unmodified green
      (T010-T013)

### Phase 4b — frontend mirror (frontend-engineer; may not edit T001-T008)

- [ ] T020 [P] [US1] C7: `DEFAULT_OVERLAY_COLOR`, `OVERLAY_COLOR_MAX_LUMINANCE`, refinement; export
      `relativeLuminance` — `apps/shared/src/api/overlays.schema.ts`, `apps/shared/src/ui/composites/textLegibility.ts`
      (T010)
- [ ] T021 [P] [US1] C8: remove the default token branch — `apps/shared/src/ui/composites/overlayLabelStyle.ts`
- [ ] T022 [P] [US1] C9: literal → constant — `apps/management-web/src/features/overlays/OverlayEditPage.tsx`
- [ ] T023 [US1] Counterfactual 5 quoted red; `prettier --check`, eslint, `tsc --noEmit` (shared, management-web,
      kiosk-web, e2e); vitest (T020-T022)

T011-T013 and T020-T022 own disjoint files: **[P]** once T010 lands.

### Phase 5 — verify (S1)

- [ ] T030 [US1] Spec §"Independent end-to-end test (US1)" 1-3 on the run-mode stack; render-leg records from ≥ 2
      PR CI runs beside 2 recent `develop` push runs (spec §"Latency"), truncated, whatever they show;
      `git diff --stat origin/develop` shows no `apps/kiosk-web` file —
      `specs/463-a-colour-too-bright-for-the-wall/verification.md`

### Phase 6 — review (S1)

- [ ] T031 backend-reviewer: invariant on every write path, migrator transaction + idempotency + raw-SQL read,
      registration order, migration file present, superseded edits limited to rules (a)/(b)
- [ ] T032 frontend-reviewer: pin parity, refinement message, default branch removed with no kiosk diff

---

## S2 — US2 (P2): an operator colours the element they are editing (after S1 merges)

### Phase 4a — new behaviour, observed red (test-writer)

- [ ] T040 [P] [US2] 13 swatches, each schema-valid and legible at `FF`, no triad hue —
      `apps/shared/src/ui/composites/overlayPalette.test.ts` (new)
- [ ] T041 [P] [US2] Swatch selection, hex canonicalisation, too-bright / illegible / malformed refusals, opacity
      minimum per kind, read-only — `apps/shared/src/ui/composites/OverlayColorFields.test.tsx` (new)
- [ ] T042 [P] [US2] Selected element's colour edits preview + is one undo step (Text and Box) —
      `apps/shared/src/ui/composites/OverlayEditorColour.test.tsx` (new)
- [ ] T043 [P] [US2] Swatch → one PATCH with the hex; refused hex sends nothing; 409 stale offers Reload —
      `apps/management-web/src/features/overlays/OverlayDraftFormColour.test.tsx` (new)
- [ ] T044 [P] [US2] Swatch values equal accepted non-status primitives (source scan, slash-normalised) —
      `tests/Architecture.Tests/OverlayColourPaletteTests.cs` (addition)
- [ ] T045 [P] [US2] Spec §"Independent end-to-end test (US2)" — `e2e/overlay-colour.spec.ts` (new; reuse
      `e2e/support/management-overlays.ts`)

### Phase 4b — implementation (frontend-engineer; may not edit T040-T045)

- [ ] T050 [P] [US2] C10 — `apps/shared/src/ui/composites/overlayPalette.ts` (new)
- [ ] T051 [US2] C11 — `apps/shared/src/ui/composites/OverlayColorFields.tsx` (new) (T050)
- [ ] T052 [US2] C12 — `apps/shared/src/ui/composites/OverlayEditor.tsx`; `OverlayDraftForm.tsx` only if the
      round-trip check fails (T051)
- [ ] T053 [US2] Counterfactual 6 quoted red; prettier/eslint/tsc/vitest; diff shows no backend, contract, kiosk,
      `overlays.schema.ts` or `overlayLabelStyle.ts` file (T052)

### Phase 5 / 6 (S2)

- [ ] T060 [US2] US2 e2e procedure on the run-mode stack; screenshots of the swatches, the too-bright refusal and the
      wall tile — `specs/463-a-colour-too-bright-for-the-wall/verification.md` (S2 section)
- [ ] T061 frontend-reviewer: swatch a11y (radio semantics, focus), no new dependency, ADR-0165 §3 (hex stored, not a
      token name), triad absent

## After merge

- [ ] T070 Owner follow-ups, not lane work: ADR-0165's text still names `#FFFFFFD9`; A1 (forced wall repaint) and
      A2 (record surface) as candidate issues
