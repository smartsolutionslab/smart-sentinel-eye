# Verification 294 — The type that ignored its tile

Spec: [`spec.md`](./spec.md) · Plan: [`plan.md`](./plan.md) · Tasks: [`tasks.md`](./tasks.md)

This file is filled in phase by phase. **Only T001 (V1, the gate) is recorded
here so far** — written by the test-writer pass that produced the red tests
(T003–T006) and the characterisation baseline (T007). V2 (T013) and V3
(T014) are phase-5 work, done on the branch after the formula and
`@container` classes exist, and are not yet filled in.

## T001 — V1: the premise, observed on the unfixed build (`develop` @ `e56c2b8e`)

Live Aspire stack, booted clean (`dotnet run -c Release --no-build` from
`src/AppHost`, no prior stack running). One overlay authored through
management-web (`fontSizePx 48`, `normalizedX 0.25`, `normalizedY 0.425`,
`normalizedWidth 0.5`, `normalizedHeight 0.15`, text `PRODUCTION LINE
STATUS`), published, bound to a 1×1, a 2×2 and a 3×3 layout (one shared
camera pointed at an unserved address — video is irrelevant to this
measurement; the overlay label mounts regardless of stream status,
`CameraViewer.tsx:402`). Kiosk at 1920×1080. Editor measured separately at a
≥1600 px operator viewport (1920×1080), matching spec.md §2's stated
assumption for its 0.120 prediction — the operator app's own default
Playwright viewport is 1280 px wide, which puts the old formula's `vw` term
in its _live_ (uncapped) branch and is a different, also-valid measurement
(see the editor screenshot's caption below).

Reproduction script: an ad hoc Playwright script (not part of the committed
suite — a throwaway per plan.md §8 V1), run twice; figures below are from
the completed run. Not committed; the method is fully described here and
mirrors `e2e/kiosk-label-scales-with-its-tile.spec.ts` /
`e2e/overlay-editor-preview-scale.spec.ts`'s own measurement technique
(`getComputedStyle(label).fontSize`, the label's own
`getBoundingClientRect().width` as the box).

### Measured ratios (type ÷ box), against spec.md §2's predictions

| Surface                              | Measured `type` | Measured `box` | Measured ratio | Predicted ratio | Match       |
| ------------------------------------ | --------------- | -------------- | -------------- | --------------- | ----------- |
| Editor (operator viewport ≥ 1600 px) | 48.00 px        | 399.63 px      | **0.1201**     | 0.120           | within 0.1% |
| Wall 1×1 (kiosk 1920×1080)           | 48.00 px        | 956.00 px      | **0.0502**     | 0.050           | within 0.4% |
| Wall 2×2 (kiosk 1920×1080)           | 48.00 px        | 477.00 px      | **0.1006**     | 0.101           | within 0.4% |
| Wall 3×3 (kiosk 1920×1080)           | 48.00 px        | 317.33 px      | **0.1513**     | 0.151           | within 0.2% |

`computed font-size` was 48 px flat on every wall tile regardless of grid
density — the `clamp(min(12,f/4)px, (f/16)vw, f px)` formula's `vw` term
(`(48/16)vw = 3vw` = 57.6 px at a 1920 px kiosk viewport) exceeds the `f px`
cap at this viewport, so every wall paints the same flat 48 px and only the
**box** shrinks as the grid densifies — exactly spec.md §2's arithmetic.
`container-type` was `normal` everywhere (no `@container` anywhere in the
tree yet, spec.md §1).

**All four ratios match spec.md §2's predictions to within measurement
noise (≤ 0.4%).** The arithmetic in spec.md is correct on this tree.

### Visible overrun

Screenshots: `t001-screens/editor-preview.png`, `wall-1x1.png`,
`wall-2x2.png`, `wall-3x3.png` (this directory).

- **1×1**: text sits inside its plate with margin on all sides.
- **2×2**: text is visibly larger relative to its (now smaller) plate than
  at 1×1 — consistent with the ratio roughly doubling (0.050 → 0.101).
- **3×3**: text is packed to within ~2% of its plate's own width (309.9 px
  of text against a 317.3 px box, measured via a `Range` bounding rect over
  the label's rendered content) — the tightest of the four surfaces, exactly
  as the ratio predicts (0.151, the highest of the four).

**Caveat, stated rather than hidden:** the chosen overlay text/geometry
wraps onto two lines at **every** density, including the editor reference —
`normalizedHeight 0.15` is too short for two lines of 48 px text even at the
_correct_ proportion. That produces a vertical (height) overflow visible at
1×1 and 2×2 as well as 3×3, which is **not** the width-ratio defect spec.md
describes — it is an artifact of this script's own text/geometry choice, and
should not be read as "the defect is already visible at 1×1". The
width-only signal (the ratio table above, and the tightening horizontal
margin visible across the three wall screenshots) is what is diagnostic, and
it matches spec.md's predictions exactly. **A future V2 (T013) capture
should choose a taller box or shorter text** so the visual evidence isolates
the width dimension without this confound.

### Outcome: **A**

Ratios match spec.md §2's predictions, and text is visibly, measurably
closer to overrunning its plate at 3×3 than at 1×1/2×2 (2% margin against
much wider margins at lower densities) — the arithmetic in spec.md is right,
and the defect it describes is real and observable. Per spec.md §6:
**proceed as filed.** No re-scoping to a preview-fidelity framing is needed.

---

## T013 — V2: the fix, on a live wall

Not yet run. Phase 5, after the formula and `@container` classes land.

## T014 — V3: the composite + render leg

Not yet run. Phase 5, per plan.md §8's five steps (scratch baseline, three
downloaded PR-run records, `scripts/render-leg-check.mjs`, ADR-0123 triage).
