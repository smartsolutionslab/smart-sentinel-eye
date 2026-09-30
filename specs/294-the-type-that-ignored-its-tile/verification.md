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

## T012 — Containment sanity (plan §5, R3)

Live Aspire stack, booted clean on this branch (`bug/2353-label-container-relative`
@ `b0b65615`, `dotnet run -c Release --no-build` from `src/AppHost`, no prior
stack running). Read in a real browser (Chromium, Playwright) at both
`CameraViewer` call sites:

| Call site | Computed width | `container-type` | Collapsed? |
| --- | --- | --- | --- |
| `LayoutGrid` tile, 1×1 wall (`T001 Repro 1x1`) | 1912.00 px | `inline-size` | No |
| `LayoutGrid` tile, 2×2 wall (`T001 Repro 2x2`) | 954.00 px | `inline-size` | No |
| `LayoutGrid` tile, 3×3 wall (`T001 Repro 3x3`) | 634.66 px | `inline-size` | No |
| `CameraDetailPage` viewer | 768.00 px × 432.00 px (16:9, `aspect-video`) | `inline-size` | No |

Both call sites resolve to a definite, non-zero width and establish
`container-type: inline-size`, matching plan §5's prediction — neither the
`LayoutGrid` tile (`w-full` inside a definite-width flex/grid parent) nor the
editor canvas / `CameraDetailPage` viewer collapses. R3 is closed.

## T013 — V2: the fix, on a live wall

Same live stack as T012. Two independent measurements, both against the
FIXED formula (`f × cqw / 19.2`, `@container` on `CameraViewer`'s root and
the editor canvas):

**(a) The committed CI proof specs, run live.** `e2e/kiosk-label-scales-with-its-tile.spec.ts`
(project `kiosk`, the real seeded nine-tile 3×3 wall, all nine tiles
independently) and `e2e/overlay-editor-preview-scale.spec.ts` (project
`chromium`, the editor canvas) — the exact specs CI will run — both **passed**
against this branch's live stack:

```
[kiosk] a label is proportional to its own tile container on every tile of the nine-tile wall — PASS
[chromium] the editor preview is proportional to its own canvas, not the viewport — PASS
```

Every one of the nine tiles independently satisfies
`font-size = containerWidth × f / 1920 (±0.5px)` with `container-type: inline-size`,
and the editor canvas satisfies the same formula with its own container-type.

**(b) A density comparison, repeating T001's exact procedure** (throwaway
Playwright scripts per plan §8 V2, not committed) — reusing the **same,
unmodified** T001 repro fixtures (leftover published overlay + 1×1/2×2/3×3
layouts left behind by the test-writer's V1 pass, `fontSizePx 48`,
`normalizedWidth 0.5`; per spec §4 an existing published revision re-renders
under the new formula from its unchanged stored values, so reusing V1's own
data is a direct before/after comparison) plus an unsaved editor draft with
the same `fontSizePx`/`Width`:

| Surface | Measured `type` | Measured `box` | Measured ratio | T001 (V1) ratio | `container-type` |
| --- | --- | --- | --- | --- | --- |
| Editor (canvas 800px @ ≥1600px viewport) | 20.00 px | 400.00 px | **0.0500** | 0.1201 | `inline-size` |
| Wall 1×1 | 47.80 px | 956.00 px | **0.0500** | 0.0502 | `inline-size` |
| Wall 2×2 | 23.85 px | 477.00 px | **0.0500** | 0.1006 | `inline-size` |
| Wall 3×3 | 15.87 px | 317.33 px | **0.0500** | 0.1513 | `inline-size` |

**All four surfaces now agree exactly** (0.0500 on every surface — tighter
than spec §3 scenario 4's ±1% requirement). Before the fix (T001/V1) they
spanned 0.0502–0.1513. The 1×1 wall's type (47.80 px) is within 1% of what it
painted before this change (48.00 px in V1, spec §3 scenario 3's own
prediction of "47.8 px against 48 px") — matched to the decimal.

**Outcome: V2 confirms the fix as specified.** The type-to-plate proportion
is now identical across the editor and every wall density; density no longer
changes it (spec §3 scenario 4); the canonical 1×1 wall does not move beyond
the predicted 4 px grid-padding effect (scenario 3); no regression found.

*Note on method:* two attempts to author fresh 1×1/2×2/3×3 layouts from
scratch inside a single throwaway script hit intermittent kiosk sign-in
timeouts unrelated to this change (cross-context session flakiness under
back-to-back Keycloak sign-ins) — abandoned in favour of (a) the committed,
CI-equivalent proof specs (the strongest possible evidence, since they are
literally what will gate the PR) and (b) reusing T001's own already-published
fixtures for the density table, which avoided the flakiness entirely and
gives a direct, apples-to-apples before/after comparison on identical data.

## T014 — V3: the composite + render leg

**Not run for real — the PR is not open yet, so no PR CI artifacts exist.**
Per the brief, this section instead records the checker invocation confirmed
working end to end, and a dry run against an already-existing `develop` CI
run's artifacts (in place of #2684/#2687, which turned out to have no
render-leg figures of their own beyond what every push run already carries).

### Checker confirmed, input format confirmed

`scripts/render-leg-check.mjs <shards-directory> <baseline.json path>` —
read in full. It:

- Self-verifies `baseline.json`: requires `runs[]` (≥5, each `{ sha: <40-hex>,
  p50Milliseconds: <number>, ... }`), `baselineP50Milliseconds` (must equal
  the mean of `runs[].p50Milliseconds` to within 0.01 ms —
  `AGREEMENT_EPSILON`), `toleranceMilliseconds` (must equal 3× the sample
  stddev, n−1 denominator, to the same epsilon).
- Discovers shards as `<shards-directory>/playwright-report-<n>-of-4/test-results/`
  (mirrors `actions/download-artifact`'s own layout).
- Reads every `render-leg-attempt-*.json` in the chosen shard, validates each
  record, picks the **first complete attempt** (a retry cannot re-roll the
  verdict), and compares its `p50` against `baselineP50Milliseconds + toleranceMilliseconds`.
- Exits 0 and prints `within tolerance` (with margin) when the figure is at
  or under threshold; exits 1 and prints `regressed` (with excess, and an
  ADR-0123 cadence-first reminder) or `unmeasured` (missing/malformed/multi-shard
  records, or every attempt incomplete) otherwise.

### Dry run against an existing CI run (tooling proof, not this PR's figure)

Built a **scratch** baseline from spec §7's exact nine runs (mean
`57.6611...` ms, 3σ tolerance `21.9746...` ms, threshold `79.6357...` ms —
matches spec §7's rounded 57.66 / 21.97 / 79.63) and downloaded all four
`playwright-report-*-of-4` artifacts from `develop`'s own most recent push CI
run (`36715830923`, SHA `e56c2b8eff33e11df8e14ea79cb301e5d7efea6a` — this
branch's base commit; #2684/#2687 merged before it and carry no render-leg
artifacts beyond this). Ran the real command:

```
node scripts/render-leg-check.mjs <downloaded-shards-dir> <scratch-baseline.json>
```

Output:

```
render-leg-check: within tolerance
  attempt 0: p50 53.60 ms (complete) — evaluated (first complete attempt)

  baseline p50: 57.66 ms
  tolerance: 21.97 ms
  baseline + tolerance: 79.64 ms
  frame interval (before → after): 31.31 → 26.76 ms/frame
  margin: 26.04 ms under baseline + tolerance
exit 0
```

**The tooling works end to end**: shard discovery, per-attempt validation,
baseline self-verification, and the pass/regressed/unmeasured decision all
function exactly as `plan.md` §8.3/§8.4 describe. This is **not** this PR's
V3 figure — it is `develop`'s own tip, unrelated to this change — recorded
only to prove the pipeline before real data exists.

### What still needs to run, once the PR is open (exact commands)

1. Build the **real** scratch baseline from spec §7's nine runs plus any
   newer `develop` push runs up to this branch's base. Two candidates beyond
   spec §7's table were found and are not yet in it: run `36715830923`
   (SHA `e56c2b8e`, this branch's base, p50 53.60 ms, `playwright-report-4-of-4`)
   and CI run `36697596703` (style/292, SHA `34d46a23...`; overall CI
   conclusion `failure` but carries a full `playwright-report-4-of-4` — check
   whether its job-level e2e result was actually green before including it).
2. Once the PR's CI run exists:
   ```sh
   gh api repos/smartsolutionslab/smart-sentinel-eye/actions/runs/<PR_RUN_ID>/artifacts -q '.artifacts[].name'
   gh run download <PR_RUN_ID> -n playwright-report-1-of-4 -D <scratch>/shards/playwright-report-1-of-4
   gh run download <PR_RUN_ID> -n playwright-report-2-of-4 -D <scratch>/shards/playwright-report-2-of-4
   gh run download <PR_RUN_ID> -n playwright-report-3-of-4 -D <scratch>/shards/playwright-report-3-of-4
   gh run download <PR_RUN_ID> -n playwright-report-4-of-4 -D <scratch>/shards/playwright-report-4-of-4
   node scripts/render-leg-check.mjs <scratch>/shards <scratch>/baseline.json
   ```
   **Download before each re-run** — a re-run erases the prior attempt from
   CI history (memory: *a re-run erases the failure from CI history*).
   Repeat for **three complete PR-run records** (the PR's own run plus two
   re-runs, downloading each first).
3. Pass = all three `within tolerance`. Record every p50/p95/max/`T` and the
   mean shift against 57.66 ms into this section, with run ids and full
   SHAs.
4. On `regressed`: ADR-0123 triage (compare `T` — a cadence step vs `develop`
   runs of the same day blocks; no `T` move re-measures, two more runs, a
   second `regressed` blocks). On `unmeasured`: re-run once after
   downloading; a second `unmeasured` blocks.

**This section is deliberately incomplete** pending real PR CI data — not
silently left blank. The commands above are what the orchestrator runs once
the PR is open.
