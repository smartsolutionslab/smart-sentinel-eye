# Tasks 294: The type that ignored its tile

Spec: [`spec.md`](./spec.md) · Plan: [`plan.md`](./plan.md) · Issue #2353

## Phase-3 declarations (for `/next-issue`)

| Declaration | Value |
|---|---|
| Engineer | **`frontend-engineer`** only. No CI wiring is needed: the render-leg figure is already emitted and summarised on every run (spec 225 US1), and the verdict script already exists; V3 runs it by hand. No `infra-engineer`. |
| Reviewer (phase 6) | `frontend-reviewer`. `/security-review` not required (no auth, input, or trust-boundary change). |
| New ADR? | **No.** The data decision (spec §4) applies ADR-0112 §1/§3 and ADR-0155's pre-production precedent and restores spec 004's documented meaning of `fontSizePx`. No contract, schema or stored value changes. If spec §4's marked assumption (no non-dev deployment) is contradicted, the run **blocks** rather than writing one. |
| Phase 4a colour | **Behaviour-changing → RED** for T003–T006. **Characterisation → observed green, unmodified** for T007. No task is behaviour-preserving-only; the latency comparison set is pre-recorded (spec §7) from existing `develop` CI artifacts, so no separate characterisation commit is needed for it. |
| Board | Feature-level issue #2353 is already on Project #13; no per-task issues (CLAUDE.md, Phase 3 row). |

**Blocking order:** T001 gates everything. T002 is done. T003–T006 are `[P]` (disjoint files)
but all precede T008–T010. T008–T010 are `[P]` (disjoint files). T011–T015 are sequential.

## Phase 3 (architect — done here)

- [x] **T002** [US1] Spec, plan, tasks at `specs/294-the-type-that-ignored-its-tile/`.

## Gate: premise (before 4a)

- [ ] **T001** [US1] ⟨GATE⟩ **Observe the unfixed behaviour live (plan §8 V1).** On this branch
  before any code change (`develop` @ `e56c2b8e`): boot the stack; in management-web author one
  overlay (text chosen to fill its plate in the editor with a small margin, e.g.
  `fontSizePx 48`, `normalizedWidth 0.5`, `normalizedHeight 0.15`), publish, bind to a 1×1, a
  2×2 and a 3×3 layout. Kiosk at **1920×1080**. For editor + each wall: screenshot; read computed
  `font-size` and label box width; compute type ÷ box; compare with spec §2 (0.120 / 0.050 /
  0.101 / 0.151). Record whether any text lies outside its plate background and whether it is
  clipped at the tile edge. Write the outcome **A / B / C** (spec §6) into `verification.md`.
  **C stops the run** (`agent:blocked`, measurements commented on #2353). **B** proceeds
  re-scoped: PR title/body describe preview fidelity, not overrun.

## Phase 4a: red, then characterise (test-writer) — output returned verbatim

- [ ] **T003** [P] [US1] **RED** — `apps/shared/src/ui/composites/overlayLabelStyle.test.ts`:
  replace the three "Reproduces the wall clamp formula …" cases, by name, with exact-string cases
  for `f = 8, 48, 256` (plan §6 (1)). Run; quote the failures.
- [ ] **T004** [P] [US1] **RED** — `OverlayLabelCharacterisation.test.tsx`: replace *only* "Sizes
  the type with the vw-derived clamp formula" with the new string. First confirm whether jsdom
  preserves it; if it reads `''`, delete that one case by name and say why (plan §6 (2), R2) —
  never weaken to a substring. Leave the other five cases byte-identical.
- [ ] **T005** [P] [US1] **RED** — new `e2e/kiosk-label-scales-with-its-tile.spec.ts` (kiosk
  project; viewport 1920×1080; the seeded nine-tile live-video wall): per tile,
  `font-size = containerWidth × f / 1920 ± 0.5 px` and `container-type = inline-size`, with `f`
  read from the seeded overlay (plan §6 (3)). Run against the unfixed stack; quote the failure.
- [ ] **T006** [P] [US1] **RED** — new `e2e/overlay-editor-preview-scale.spec.ts` (chromium
  project): editor preview `font-size = canvasWidth × 48 / 1920 ± 0.5 px`, canvas
  `container-type = inline-size` (plan §6 (4)). Reuse `e2e/support/management-overlays.ts`.
  Run; quote the failure.
- [ ] **T007** [US1] **CHARACTERISATION** — run, unmodified, and quote green:
  the five other `OverlayLabelCharacterisation` cases, `OverlayLabelParity.test.tsx`,
  `OverlayEditorCharacterisation/Keyboard/Undo/Backdrop.test.tsx`,
  `OverlayGeometryFields.test.tsx`, `CellPage.test.tsx`, the `LayoutGrid` tests. Depends on
  T003–T006 (so the run is of the final test set).

## Phase 4b: implement (frontend-engineer) — receives 4a's verbatim output; may not edit tests

- [ ] **T008** [P] [US1] `overlayLabelStyle.ts`: `fontSize` →
  `` `max(${Math.min(12, fontSizePx / 4)}px, calc(${fontSizePx}cqw / 19.2))` ``; rewrite the doc
  comment (1080p reference per spec 004; container is the label's positioning parent; no `vw`; no
  250-tile count — nine, ADR-0156). Depends on T003–T007.
- [ ] **T009** [P] [US1] `CameraViewer.tsx:393`: add `@container` to the root classes. Nothing
  else in the file. Depends on T003–T007.
- [ ] **T010** [P] [US1] `OverlayEditor.tsx:612`: add `@container` to the canvas classes. Nothing
  else in the file. Depends on T003–T007.
- [ ] **T011** [US1] Green run: `apps/shared`, `kiosk-web`, `management-web` vitest; lint;
  typecheck; `pnpm test:guards`; T005/T006 e2e locally against the stack. T003–T006 now green,
  T007's set still green **unmodified**. Depends on T008–T010.

## Phase 5: verify

- [x] **T012** [US1] Containment sanity (plan §5, R3): in a real browser read computed widths of
  `CameraViewer`'s root at both call sites (`LayoutGrid` tile, `CameraDetailPage`) and of the
  editor canvas; none collapsed, none changed from V1.
- [x] **T013** [US1] **V2** — repeat T001's procedure on the branch (plan §8). Expected: equal
  type ÷ box on all four surfaces (±1 %); 1×1 type within 1 % of T001's. Into `verification.md`.
- [ ] **T014** [US1] **V3 — the composite + render leg** (plan §8, all five steps): scratch
  baseline from spec §7's runs (+ newer `develop` push runs), three complete PR-run records
  downloaded before each re-run, `scripts/render-leg-check.mjs` verdict per record, ADR-0123
  triage on `regressed`, block on a repeated `regressed` or `unmeasured`. Every figure, run id and
  full SHA into `verification.md`.
- [ ] **T015** [US1] PR body: leg cited (composite + render, §IV), V3 figures, T001 outcome and
  the title that follows from it, 4a's verbatim red output, the four superseded test names, and
  spec §9's three raised items (#2337 closure, the editor caption → #2350, the remaining
  250-tile comment). Base `develop`; closing keyword `Fixes #2353` (and check state after merge).

## Dependencies

```
T001 ⟨GATE: A/B proceed, C blocks⟩
 └─ T003 ┐
    T004 ├─[P]─ T007 ─┬─ T008 ┐
    T005 │            ├─ T009 ├─[P]─ T011 ─ T012 ─ T013 ─ T014 ─ T015
    T006 ┘            └─ T010 ┘
```

No foundational (Shared.Kernel / Contracts / AppHost) task exists; nothing blocks fan-out beyond
T001.
