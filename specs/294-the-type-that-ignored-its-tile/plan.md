# Plan 294 — The type that ignored its tile

Spec: [`spec.md`](./spec.md). Issue #2353. Lane: autonomous (ADR-0144).

## 1. Where this lives

Frontend only. No bounded context, no backend project, no `Shared.Contracts` change, no
migration, no AppHost resource.

| File | Change |
|---|---|
| `apps/shared/src/ui/composites/overlayLabelStyle.ts` | `fontSize` formula (spec §5) and its doc comment (drop the `vw` rationale and the "250 times per wall render" line; state the 1080p reference from spec 004). |
| `apps/shared/src/ui/composites/CameraViewer.tsx` | Add `@container` to the root `clsx(...)` at `:393`. Nothing else. |
| `apps/shared/src/ui/composites/OverlayEditor.tsx` | Add `@container` to the canvas `className` at `:612`. Nothing else. |
| `apps/shared/src/ui/composites/overlayLabelStyle.test.ts` | Three cases **superseded by name** (spec §8 FR-001). |
| `apps/shared/src/ui/composites/OverlayLabelCharacterisation.test.tsx` | One case superseded by name: *"Sizes the type with the vw-derived clamp formula"*. The other five stay **unmodified**. |
| `e2e/kiosk-label-scales-with-its-tile.spec.ts` (new) | Real-Chromium proof on the wall (kiosk project). |
| `e2e/overlay-editor-preview-scale.spec.ts` (new) | Real-Chromium proof in the editor (chromium project). |
| `specs/294-*/verification.md` (new, phase 5) | Reproduction outcome, screenshots' ratios, latency comparison. |

**Reuse, not invention:** Tailwind core `@container`; the nine-tile fixture wall from
`e2e/support/seed-live-video-wall.setup.ts` / `live-video-wall.ts`; the existing management
sign-in + overlay helpers in `e2e/support/management-overlays.ts`; `scripts/render-leg-check.mjs`
for the latency verdict.

## 2. Domain

Untouched. The `Label` value object's `FontSizePx` (8–256), normalized geometry and their
invariants are unchanged; so is `overlays.schema.ts`. Only the renderer's interpretation of
`fontSizePx` changes, back to spec 004's definition (spec §4).

## 3. Messaging

None. No domain or integration event changes. The kiosk still receives the same overlay revision
over SignalR (ADR-0152) and renders it.

## 4. Boundary rules

- The formula stays in **one** function in `apps/shared`; both surfaces keep calling it
  (spec 146's fold). `OverlayLabelParity.test.tsx` must stay green **unmodified** — it is the
  guard that the editor and wall still share the string.
- No `apps/shared` import of either app; no new dependency (NFR-001).

## 5. Why `CameraViewer`'s root and not the `LayoutGrid` tile

The label is `position: absolute` inside `CameraViewer`'s `relative` root, so its `width: N%` is
N% of that root. Putting the container anywhere else makes `cqw` resolve against a *different*
width than the box does — equal today by coincidence (`w-full` inside the tile), unequal the
moment the tile gains padding, a badge column, or a non-`w-full` viewer. Same element, exact
ratio, by construction. It also covers `CameraDetailPage`'s viewer for free (no overlay there
today).

Containment risk: `container-type: inline-size` makes an element's inline size ignore its
content. Both containers have a definite width from their parent (`w-full` in a block/flex
container of definite width; the editor canvas sets `width: canvasWidthPx`), so neither can
collapse. T012 confirms by reading computed widths in a real browser at both call sites.

## 6. Tests — what is red, what is characterisation

This is a **behaviour-changing** fix: the rendered size is the point. Red-first.

**Red (must be observed failing before 4b, output quoted verbatim in the PR):**

1. `overlayLabelStyle.test.ts` — `fontSize` at `f = 8, 48, 256` is exactly
   `max(2px, calc(8cqw / 19.2))`, `max(12px, calc(48cqw / 19.2))`,
   `max(12px, calc(256cqw / 19.2))`. Pure object, no DOM. Red today (`clamp(... vw ...)`).
2. `OverlayLabelCharacterisation.test.tsx` — the superseded case asserts the wall label's
   `style.fontSize` equals the new string. **Unknown to verify in 4a:** whether jsdom 30's
   `cssstyle` preserves `max(… calc(… cqw …))`. If it drops the value (reads `''`), the DOM case
   cannot pin the string; the test-writer then deletes that one case by name, records why in the
   PR, and the formula is pinned by (1) and the wall's behaviour by (3). It must **not** be
   weakened to a substring or `toBeTruthy`.
3. `e2e/kiosk-label-scales-with-its-tile.spec.ts` (kiosk project, `test.use({ viewport: { width:
   1920, height: 1080 } })`), on the seeded nine-tile live-video wall: for **every** tile, read
   the `camera-viewer-overlay-label`'s computed `font-size`, its parent `CameraViewer` root's
   computed `width` and `container-type`; assert `font-size = width × f / 1920 ± 0.5 px` (subject
   to the floor — with the fixture's default `f = 32` and ~635 px tiles the floor of 8 px does not
   bind) and `container-type = inline-size`. Red today: 32 px against ~10.6 px, `normal`.
   The expected `f` is read from the seeded overlay, not restated as a literal next to the
   assertion (memory: *an assertion must not check its own input*).
4. `e2e/overlay-editor-preview-scale.spec.ts` (chromium project): open the overlay editor, set
   font size 48 through the slider, assert the preview label's computed `font-size` is
   `canvasWidth × 48 / 1920 = 20 ± 0.5 px` and the canvas's `container-type` is `inline-size`.
   Red today: 48 px (Desktop Chrome viewport 1280 → `3vw` = 38.4 px; either way not 20).

(3) and (4) together are the ratio-invariance proof: both surfaces equal the same `f/1920`
constant × their own container width, and the label box is `normalizedWidth ×` that same width.

**Characterisation (observed green before, must pass unmodified after):**
`OverlayLabelCharacterisation.test.tsx` (the five other cases), `OverlayLabelParity.test.tsx`,
`OverlayEditorCharacterisation.test.tsx`, `OverlayEditorKeyboard.test.tsx`,
`OverlayEditorUndo.test.tsx`, `OverlayEditorBackdrop.test.tsx`, `OverlayGeometryFields.test.tsx`,
`CellPage.test.tsx`, `LayoutGrid`'s tests, and `e2e/kiosk-shows-a-label-over-video.spec.ts` (the
render-leg span test). An assertion in any of these that has to be edited is a blocked outcome.

**Superseded by name, and only these** (each is an assertion *of the defect*):
`overlayLabelStyle.test.ts` › the three "Reproduces the wall clamp formula …" cases;
`OverlayLabelCharacterisation.test.tsx` › "Sizes the type with the vw-derived clamp formula".
Spec 146 wrote them knowing this issue would replace them (`overlayLabelStyle.ts:18-21`).

## 7. Commit shape (ADR-0030, rebase-merge per ADR-0087)

Every commit green on its own. The red tests and the change that turns them green land in the
**same** commit (the red run is evidenced by 4a's verbatim output in the PR body, not by a red
commit that would break `git bisect`):

1. `fix(overlay): size a label's type against its tile, not the viewport` — formula, both
   `@container` classes, the superseded unit/DOM cases, both e2e specs.
2. `docs(294): …` — spec/plan/tasks (phase 1–3) and, later, `verification.md`.

## 8. Verification (phase 5) — three observations, in this order

**V1 — premise, before any code (T001).** Live stack at `develop`'s tip, unfixed. Spec §6
outcomes A/B/C. Run by the orchestrator/verifier at the start of phase 4 so the outcome is known
before tests are written; outcome C stops the run.

**V2 — the fix, on a live wall (T013).** Repeat V1's exact procedure on the branch. Expected: type
÷ box equal on editor, 1×1, 2×2, 3×3 (±1 %); the 1×1 type within 1 % of V1's; no text outside
its plate where it fits in the editor. Screenshots and measured ratios into `verification.md`.

**V3 — the latency leg (T014).** #2337's gate is not wired (spec §1), so the comparison is done by
hand with the gate's own checker:

1. Build a **scratch** `baseline.json` (scratchpad, **not committed** — committing one is spec 225
   T019's job) from spec §7's nine runs plus any newer `develop` push runs up to this branch's
   base, in `render-leg-check.mjs`'s schema; `baselineP50Milliseconds` = mean, 
   `toleranceMilliseconds` = 3 × sample σ (the script self-verifies both).
2. Download `playwright-report-*-of-4` from the PR's CI run **before** any re-run (a re-run
   erases the prior attempt from history). Obtain **three** complete PR-run records (the PR run
   plus two re-runs, downloading each first).
3. Run `node scripts/render-leg-check.mjs <shards-dir> <scratch-baseline.json>` against each.
   **Pass:** all three `within tolerance`. Record every p50, p95, max, `T` and the mean shift
   against 57.66 ms in `verification.md`, with run ids and full SHAs.
4. **On `regressed`:** ADR-0123 triage — compare `T`. If `T` stepped (a dropped frame) on the PR
   and not on `develop` runs of the same day, the containment is costing a frame: **block**
   (`agent:blocked`, figures quoted). If `T` did not move, re-measure (memory: the first run after
   machine churn looks like a regression) — two more runs; a second `regressed` blocks.
5. **On `unmeasured`** (the span test produced no complete attempt): not a pass. Re-run once
   after downloading; a second `unmeasured` blocks, because shipping this unmeasured is the one
   thing the issue forbade.

Optional, labelled `local` if taken: the same span test on one developer machine with and without
the two `@container` classes, three runs each. A local figure is supporting evidence, never a
substitute for V3.

## 9. Risks

| # | Risk | Mitigation |
|---|---|---|
| R1 | The premise does not reproduce. | V1 first; spec §6 decides A/B/C before any code. |
| R2 | jsdom drops the `cqw` string. | Plan §6 (2): delete the one DOM case by name; unit + e2e carry the proof. Never weaken. |
| R3 | `inline-size` containment collapses a viewer with no definite width. | Plan §5; T012 reads computed widths at both call sites. |
| R4 | The leg regresses and CI cannot tell (no wired gate). | V3 by hand with the gate's own script; honest detection floor stated in spec §7. |
| R5 | Labels on dense walls become small (e.g. `f = 32` → ~10.6 px on 3×3). | That is the proportional behaviour spec 004 specifies, and the editor now shows it. Flagged in the PR; the caption question goes to #2350. |
| R6 | A deployment outside dev exists, so §4's premise is wrong. | Spec §4 marks the assumption; if contradicted, block. |
