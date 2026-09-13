# Tasks — Spec 146, one label renderer

**Spec:** `specs/146-one-label-renderer/spec.md` · **Plan:**
`specs/146-one-label-renderer/plan.md`
**Issue:** #2339 · **Branch:** `fix/2339-one-label-renderer` · **Engineer:**
`frontend-engineer`
**Phase 4a colour:** **two** — CHARACTERISATION (green) on the wall via **T002**, RED on the
parity guard via **T003**. Both observed against `origin/develop`, before T004 exists.

`[P]` marks disjoint files (ADR-0109). Every task names the file it owns.

---

## Foundational — blocks everything

- [ ] **T001** Baseline. On this branch, with no edits: run
      `pnpm --filter @smart-sentinel-eye/shared test`,
      `pnpm --filter @smart-sentinel-eye/management-web test` and `pnpm --filter @smart-sentinel-eye/kiosk-web test`. Record the
      pass counts verbatim. A suite that is already red is a stop-and-report, not something
      to work around. **Blocks: all.**

---

## User Story 1 — an operator's preview matches the wall (P1)

### 4a, the two colours. Both run before any source change.

- [ ] **T002 [P] [US1]** **CHARACTERISATION, observed GREEN on today's tree.** New file
      `apps/shared/src/ui/composites/OverlayLabelCharacterisation.test.tsx`. Line 1 is
      `// @vitest-environment jsdom`. Mirror the mock setup in
      `apps/shared/src/ui/composites/CameraViewer.test.tsx` — do not invent one. Render
      `CameraViewer` with an `overlay` prop (`fontSizePx: 48`, `normalizedX: 0.25`,
      `normalizedY: 0.05`, `normalizedWidth: 0.5`, `normalizedHeight: 0.1`) and assert, on
      `camera-viewer-overlay-label`, **each property separately**:
      `position`, `left`, `top`, `width`, `height`, `display`, `alignItems`,
      `justifyContent`, `background`, `color`, `fontSize`, `fontWeight`, `pointerEvents`,
      `padding`.
      **`fontSize` is `'clamp(12px, 3vw, 48px)'` and `padding` is `'0px 4px'`** — jsdom
      normalises the shorthand, and it preserves `clamp()` verbatim (verified, jsdom 30.0.1).
      **Do NOT assert `label.getAttribute('style')` as one string**: React serialises in key
      order and T005 changes that order, which would be a false red on a characterisation
      test. `apps/shared` has **no jest-dom** — use `expect(el.style.x).toBe(...)`, not
      `toHaveStyle`. Run it. **It must pass. Record the output.** It must pass *unmodified*
      at T008. **Depends: T001.**

- [ ] **T003 [P] [US1]** **RED, observed FAILING on today's tree.** New file
      `apps/shared/src/ui/composites/OverlayLabelParity.test.tsx`. Line 1 is
      `// @vitest-environment jsdom`. Render **both** components with the *same*
      `OverlayLabel` and assert the two nodes agree on the eight surface properties —
      `display`, `alignItems`, `justifyContent`, `background`, `color`, `fontSize`,
      `fontWeight`, `padding` — and that the editor's `border` is empty.
      **This file must NOT import `overlayLabelStyle`.** It compares the two components to
      each other, so it compiles and runs on `origin/develop`; a test that imported the
      not-yet-existing module would fail on module resolution, which is evidence of nothing.
      The editor's style lives on the `<Rnd>` box, not on the `overlay-editor-preview`
      `<span>` — reach it via `.parentElement`, or add
      `data-testid="overlay-editor-label"` to the `<Rnd>` in T006 and say in the PR which was
      used. Run it. **It must fail**, with `background` mismatching `0.92` against `0.85`.
      **Quote the verbatim failure — it goes in the PR body (ADR-0139).** **Depends: T001.**

### The fold

- [ ] **T004 [US1]** New file `apps/shared/src/ui/composites/overlayLabelStyle.ts`. Export
      `interface OverlayLabelAppearance { fontSizePx: number }` and
      `overlayLabelSurfaceStyle(label: OverlayLabelAppearance): CSSProperties` returning the
      wall's eight properties **verbatim**, `plan.md` §"What it returns". Do **not** name any
      export `OverlayLabel` — the identifier is taken at `CameraViewer.tsx:392` and
      `overlays.api.ts:9`. **Pure, allocation-only, DOM-free:** no `getComputedStyle`, no
      observer, no effect, no memo cache, no `container-type`. It runs up to 250 times per
      wall render on the §IV composite + render leg. **Depends: T002, T003.**

- [ ] **T005 [P] [US1]** `apps/shared/src/ui/composites/CameraViewer.tsx` — `OverlayLabel`
      (`:392-416`) spreads `overlayLabelSurfaceStyle(overlay)` into its style object and
      keeps `position`, `left`, `top`, `width`, `height` and `pointerEvents` inline. **No
      value changes.** The `vw` term is preserved exactly; the spec's decision section says
      why it is wrong and why it stays. **Depends: T004.**

- [ ] **T006 [P] [US1]** `apps/shared/src/ui/composites/OverlayEditor.tsx` — the `<Rnd>`
      style object (`:82-94`) spreads `overlayLabelSurfaceStyle(value)` and keeps only
      `cursor: 'move'` and `userSelect: 'none'`. **Delete `border`** — divergence 3; the wall
      has none. Leave the `Math.max(..., 24)` / `Math.max(..., 16)` px floors at `:45-46`
      untouched: they keep the drag target grabbable and are the one difference that should
      remain. Add `data-testid="overlay-editor-label"` to the `<Rnd>` only if T003 needed it.
      **Depends: T004.**

- [ ] **T007 [US1]** New file `apps/shared/src/ui/composites/overlayLabelStyle.test.ts`. No
      `@vitest-environment` pragma — it asserts a returned object, not a DOM. Cover the
      schema's two bounds (`overlays.schema.ts:6-13`): `fontSizePx: 8` →
      `'clamp(2px, 0.5vw, 8px)'` (the `Math.min(12, f/4)` floor takes the `f/4` branch) and
      `fontSizePx: 256` → `'clamp(12px, 16vw, 256px)'`, plus `48` →
      `'clamp(12px, 3vw, 48px)'`. Assert the other seven properties once. **Depends: T004.**

### The gate

- [ ] **T008 [US1]** Re-run both colours. **T002 must pass with no assertion edited** — an
      assertion that has to change is evidence the wall moved, and the correct response is to
      block, not to adjust (constitution §Testing). **T003 must now pass.** Also re-run
      `apps/management-web/src/features/cameras/CameraViewer.test.tsx` **unmodified** — its
      `left`/`top` assertions at `:51-52` are part of the characterisation evidence and that
      file is not to be edited. **Depends: T005, T006, T007.**

---

## User Story 2 — the extraction is where the tokens will land (P2)

- [ ] **T009 [P] [US2]** Prove single-source. `grep -rn 'rgba(255, 255, 255,' apps/ --include=*.tsx --include=*.ts`
      and the same for `#111827` — each must hit exactly one non-test source file,
      `overlayLabelStyle.ts`. Then confirm the module introduces **no** CSS custom property
      and that `apps/shared/src/ui/tokens/colors.css` is **unchanged** — this spec converts
      nothing to tokens (#2342 is gated on a token system that does not exist). Record both
      results in the PR body. **Depends: T005, T006.**

---

## Polish

- [ ] **T010 [P]** `pnpm lint` and `pnpm typecheck` across the three apps, plus the three
      vitest suites from T001, all green. Compare the pass counts against T001's baseline:
      three new test files, so the shared count rises and the other two are unchanged.
      **Depends: T008.**

- [ ] **T011** PR body (ADR-0030 commits, ADR-0028 `--base develop`). It must carry: T003's
      **verbatim red output**; a statement that T002 passed unmodified; the §IV leg citation
      (**composite + render, code path changed, budget unchanged** — one call plus one object
      literal per label, ≤ 250 per wall render); T009's grep results; and the note that the
      issue's font-size arithmetic was wrong, with the resolved-size table from the spec.
      No `Co-Authored-By` (ADR-0086). **Depends: T009, T010.**

---

## Dependency summary

```
T001 ─┬─ T002 [P] ─┐
      └─ T003 [P] ─┴─ T004 ─┬─ T005 [P] ─┬─ T008 ─┬─ T010 [P] ─┬─ T011
                            ├─ T006 [P] ─┤        │            │
                            └─ T007 ─────┘        └─ T009 [P] ─┘
```

**The one ordering that cannot be rearranged:** T002 and T003 run against `origin/develop`,
before T004 exists. T002's green and T003's red are the only evidence this PR's phase-4 gate
can be satisfied with, and neither can be reconstructed after the fold lands.

---

## Not tasks — owed by the orchestrator

- **Phase 3 gate — already satisfied, verified rather than assumed.** #2339 is on
  Project #13 at status **In Progress**, confirmed with
  `gh project item-list 13 --owner smartsolutionslab --limit 2000` filtered on
  `content.url` (the number filter returns zero — this board is queried by URL, and
  `item-list` defaults to 30 items, so a filled board reads empty without `--limit`).
  Feature granularity, not per-task issues (the practice since spec 028), so
  `/speckit-taskstoissues` is deliberately not run. **Nothing to add.**
- **File the follow-up issue** the spec recommends — *overlay type is absolute while its box
  is tile-relative* — blocked on this PR and on #2337.
- **Comment on #2339** correcting its font-size arithmetic, with the spec's resolved-size
  table. Eleven issues in this programme sit behind it.
