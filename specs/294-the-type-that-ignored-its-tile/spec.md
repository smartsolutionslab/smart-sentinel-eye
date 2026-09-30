# Spec 294 — The type that ignored its tile

**Issue:** [#2353](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2353)
— "An overlay label's box is tile-relative while its type is viewport-relative, so text
overruns its plate as the grid densifies".
**Branch:** `bug/2353-label-container-relative` (cut from `origin/develop` @ `e56c2b8e`)
**Created:** 2026-09-30
**Lane:** autonomous (ADR-0144)

**ADRs and specs this spec is bound by (it applies them; it decides nothing they left open):**

- **Constitution §IV** / **ADR-0015** — the ≤ 50 ms composite + render leg. This change is on it.
- **ADR-0123** — *A render leg is the operator's wait*. The instrument is not modified; a
  breach is read cadence first, compositing second.
- **ADR-0156** — 3×3 walls with spanning tiles. **The wall ceiling is nine tiles**, not four
  (see §1).
- **ADR-0112 §1/§3** and **ADR-0155** — the pre-production precedent: no external consumers, so
  a stored shape or its interpretation may change without a migration. Governs §4.
- **ADR-0146** — one discipline, two surfaces: the editor and the wall render one label.
- **Spec 004** (`specs/004-overlay-designer/spec.md:250-254`) — `fontSizePx` is "the
  author-time reference at 1080p" and labels "scale proportionally". This spec restores that
  stated meaning at tile granularity; it does not invent a new one.
- **Spec 146** (#2339) — folded editor and wall into `overlayLabelSurfaceStyle()` and preserved
  the `vw` term byte-for-byte on purpose, naming this issue as its follow-up.
- **Spec 225** (#2337) — the render-leg figure every CI run now reports. Reused in §7.
- **ADR-0144** — the lane; phase 4a colours are declared per task in `tasks.md`.

**No new ADR is needed.** §4 records why the one data decision this spec takes is an
application of ADR-0112/ADR-0155 and spec 004, not a new architectural call.

---

## 1. The issue's premise, re-checked on this tree (`e56c2b8e`)

| Issue / brief says | Tree says | Consequence |
|---|---|---|
| The formula lives in `overlayLabelStyle.ts`. | True: `apps/shared/src/ui/composites/overlayLabelStyle.ts:42`, `clamp(${min(12,f/4)}px, ${f/16}vw, ${f}px)`. Its two callers are `CameraViewer.tsx:524-540` (`OverlayLabel`, the wall) and `OverlayEditor.tsx:641` (the editor's `<Rnd>`). | One line in one function, plus the container on each surface. |
| Tiles are laid out in `CellPage.tsx:340-347`. | Stale. The grid is `LayoutGrid.tsx:229-236` (`repeat(gridCols, minmax(0, 1fr))`, `h-screen gap-1 p-1`); the tile is `LayoutGrid.tsx:436-458`. **The label's containing block is not the tile**: it is `CameraViewer`'s root, `relative aspect-video w-full overflow-hidden` (`CameraViewer.tsx:393`), against which the label's `width: normalizedWidth × 100%` resolves. | The container must be `CameraViewer`'s root, not the `LayoutGrid` tile — the element whose width the box's `%` already resolves against. That makes the ratio exact by construction, and every consumer of `CameraViewer` inherits it. |
| Max grid is 2×2, four tiles (#2353's correction comment, spec 225 §0). | **Stale since ADR-0156.** `GridDimensions.MaxTiles = 9`, `MaxCells = 9` (`src/LayoutComposition/Domain/Layout/GridDimensions.cs:26,29`, 3×3, with spanning tiles). The render-leg CI fixture is already nine tiles (`54b47034`, `e2e/support/seed-live-video-wall.setup.ts:26-27`). | Changes the reproduction (§3): **at 2×2 the defect cannot overrun** — only 3×3 can. And the containment context lands on up to nine elements, not four and not 250. |
| "#2337's render-budget gate exists" (lane pickup comment). | **It does not.** #2337 is closed, but PR #2555 — the last PR against it — says in its own body "**Issue #2337 stays open**" and ships the checker unwired: there is no `render-leg-gate` job in `ci.yml`, no `specs/225-*/baseline.json`, no `figures.md`; spec 225 T015–T022 and T028 are undone. The summary step (`ci.yml:713-715`) still prints "No threshold is asserted". | What exists is the **measurement, reported on every run** (spec 225 US1, nine-tile wall) and a **tested checker script** (`scripts/render-leg-check.mjs`). That is enough to meet #2353's stated block reason — *"about evidence rather than caution"*, i.e. not shipping the change **unmeasured** — provided phase 5 does the comparison by hand (§7). It is not a gate, and this spec does not pretend otherwise. The premature closure is raised with the orchestrator, not fixed here. |
| The fix is `cqw` with `container-type: inline-size`. | Correct. Net-new to the repo (no `cqw`, `@container` or `container-type` anywhere in `apps/`). Tailwind 4.3.3 ships the `@container` utility (`container-type: inline-size`) in core; no plugin is needed. | Two class additions and one formula. |

## 2. What is broken, as arithmetic

Box width = `normalizedWidth × W` where `W` is the container (CameraViewer root / editor canvas).
Type today = `clamp(min(12, f/4)px, (f/16)vw, f px)` — bound to the **viewport** `V`, capped at
`f` for `V ≥ 1600`.

The quantity the operator judges is **type ÷ box**. At `f = 48`, `normalizedWidth = 0.5`, with
`LayoutGrid`'s 4 px padding and gap:

| Surface (viewport 1920×1080) | `W` | box | type today | type ÷ box today |
|---|---|---|---|---|
| Editor canvas (console ≥ 1600 px wide) | 800 | 400 | 48 | **0.120** |
| Wall 1×1 | 1912 | 956 | 48 | 0.050 |
| Wall 2×2 | 954 | 477 | 48 | 0.101 |
| Wall 3×3 | 634.7 | 317 | 48 | **0.151** |

So the preview is wrong in **both** directions:

- **3×3 (the only overrun):** the wall's type is 1.26× larger relative to its plate than the
  preview promised — text that fitted in the editor overruns on the wall.
- **1×1 and 2×2 (the common walls):** the wall's type is 0.42–0.84× the preview's relative size.
  It fits, but the preview shows a label that looks nothing like the wall.

Below a 1600 px viewport the `vw` term is live and the arithmetic shifts (2×2 then matches the
editor exactly, because `type ∝ V` and `W ≈ V/2`), but 3×3 still overruns by 1.5×. **The
issue's original table (1×1 / 3×3 / 5×5) is right in shape but a 5×5 wall cannot exist**, and
at 2×2 — the grid the brief proposed to reproduce on — no overrun is predicted at all.

## 3. User story

One story: the smallest vertical that can be built and observed end to end.

### User Story 1 — A label's type scales with the tile it is drawn on (Priority: P1)

An operator authoring an overlay in the editor sees the same type-to-plate proportion that every
wall bound to it will paint, at every grid density and every display width — so "does my text fit
its plate?" is answered once, in the editor, correctly.

**Why P1 / why one story:** the defect is one formula on one shared renderer; the editor and the
wall cannot be fixed separately without re-opening the divergence spec 146 closed.

**Independent Test:** boot the stack; on a 1920×1080 kiosk, open one overlay bound to a 1×1, a
2×2 and a 3×3 layout in turn, and the same overlay in the editor. Measure
`computed font-size ÷ label box width` on each surface. All four agree to ±1 %; before the change
they span 0.050–0.151 (§2).

**Acceptance scenarios** (Gherkin; "type" is the label's computed `font-size`, "container width"
is the computed width of `CameraViewer`'s root on the wall and of the editor canvas in the editor):

```gherkin
Scenario: Happy path — the wall's type is proportional to its tile
  Given an overlay with fontSizePx 48 bound to a published 3×3 layout
  And a kiosk at a 1920×1080 viewport showing that layout
  When the label renders on a tile whose container width is W
  Then its type is W × 48 / 1920 px (±0.5 px)
  And CameraViewer's root has computed container-type "inline-size"

Scenario: Happy path — the editor previews the same proportion
  Given the overlay editor with its default 800×450 canvas
  When the operator sets fontSizePx to 48
  Then the preview's type is 800 × 48 / 1920 = 20 px (±0.5 px)
  And the canvas has computed container-type "inline-size"

Scenario: The canonical 1080p full-screen wall does not move
  Given the same overlay bound to a 1×1 layout on a 1920×1080 kiosk
  When it renders
  Then its type is within 1 % of what it painted before this change (47.8 px against 48 px)

Scenario: Conflict — density no longer changes the proportion
  Given one overlay bound to a 1×1, a 2×2 and a 3×3 layout
  When each is shown on a 1920×1080 kiosk and in the editor
  Then type ÷ label box width is equal on all four surfaces (±1 %)

Scenario: Bad input at the schema bounds — the legibility floor still holds
  Given an overlay at the schema floor fontSizePx 8 or ceiling fontSizePx 256
  When overlayLabelSurfaceStyle computes its type
  Then the type is max(min(12, f/4) px, f × W / 1920)
  And a label never renders below 12 px when f ≥ 48, nor below f/4 px when f < 48

Scenario: Existing published revisions re-render under the new formula without a migration
  Given an overlay revision published before this change
  When a kiosk loads it after deployment
  Then it renders with the new formula from its unchanged stored fontSizePx and geometry
  And no stored row, contract or schema is modified

Scenario: Auth — unchanged surface
  Given the kiosk and management authentication paths
  When this change ships
  Then no endpoint, scope, token flow or authorization rule is touched (N/A by construction)
```

**Explicitly not changed:** the other seven properties of `overlayLabelSurfaceStyle()`
(`display`, `alignItems`, `justifyContent`, `background`, `color`, `fontWeight`, `padding`);
label placement; the editor's controls; the stored `Label` value object; any contract.

## 4. Existing data — the decision

**Decision: no migration and no version marker. Every published revision re-renders under the
new formula from its unchanged stored values on the next load.**

The three options, and why the other two lose:

1. **Version marker (old revisions keep the `vw` formula until re-saved).** Rejected. It keeps a
   second rendering path alive for data that has no production consumer, forever — exactly the
   speculative generality ADR-0036 bans — and it would keep the editor unfaithful for every old
   revision, which is the defect.
2. **One-time migration of stored `fontSizePx`.** Rejected. There is nothing to migrate *to*:
   no single stored value reproduces the old rendering, because the old rendering depended on the
   viewport of whichever kiosk loaded it, not on anything stored.
3. **Re-render as-is (chosen).**

Why re-rendering is acceptable, and why this is not a new architectural decision:

- **There is no production deployment.** Constitution `:316`; ADR-0118; ADR-0125:123;
  ADR-0155:94-96 ("no production deployment exists yet … pre-production schema churn, not a
  breaking change"). ADR-0112 §1/§3 already made the same call for a larger change ("a clean V2
  cut (pre-production)"). This applies that precedent.
- **Stored data does not change; its interpretation returns to the documented one.** Spec 004
  defines `fontSizePx` as "the author-time reference at 1080p" and requires labels to "scale
  proportionally". The new formula is that definition, literally: `f` px at a 1920-wide frame,
  proportional elsewhere. The `vw` term was the drift, not the contract.
- **The canonical wall does not move.** A 1×1 layout on a 1920×1080 kiosk paints 47.8 px
  instead of 48 px (the 4 px grid padding, §3 scenario 3).
- **The direction is safe for every existing revision.** Under the old formula an operator
  authored against a preview ratio of 0.120 (or 0.1025 on a 1366 px console). Under the new one
  every surface paints 0.050 for the same label (§2). **No revision that fitted its preview can
  newly overrun its plate**; revisions only get smaller relative to their plate on dense walls,
  which is the proportional behaviour spec 004 asked for.

**Assumption, marked:** that no deployment outside development exists today. If one does, this
section is wrong and the run must stop (`agent:blocked`) — a live fab's labels would change size
on deploy. Evidence for the assumption is the five citations above; no counter-evidence was found.

## 5. Formula

```
fontSize = max( min(12, f/4) px ,  calc(f cqw / 19.2) )
```

- `calc(f cqw / 19.2)` = `f × W / 1920` px: `f` px on a 1920-wide container, proportional
  elsewhere. Written as a `calc()` of an integer so no floating-point string is ever emitted
  (`8 / 19.2` is `0.41666666666666663` in JavaScript).
- The **floor is kept** (`min(12, f/4)` px): it is the existing legibility guard. It only binds
  below a 480 px container for `f ≥ 48` (a 3×3 wall on a 1366 px kiosk is ~450 px: 11.25 → 12 px,
  ratio 0.053 against 0.050). Never in the 800 px editor.
- The **`f` px cap is dropped**. It existed to stop `vw` growing on large viewports; under `cqw`
  it would re-break the ratio on every container wider than 1920 px (a 1×1 wall on a
  3840-CSS-px kiosk would paint 48 px in a 1912 px box — 0.025 against the editor's 0.050).
  Proportionality is spec 004's stated requirement.
- The container is established with Tailwind's core `@container` utility
  (`container-type: inline-size`) on **`CameraViewer`'s root** and on **the editor canvas**
  (`OverlayEditor.tsx:610-620`). Both already have a definite width (`w-full` in a definite
  parent; `width: canvasWidthPx`), so inline-size containment cannot collapse them. The two
  `CameraViewer` call sites (`LayoutGrid.tsx:452`, `CameraDetailPage.tsx:147`) both give it a
  definite-width parent; `tasks.md` T012 re-verifies this in a real browser.

## 6. Reproduction — "does this reproduce?" is part of what is verified, not assumed

Spec 146's phase-5 step 6 (the live overrun check) was **never run** — it asked for a 5×5 wall,
which the domain refuses. So the operator-visible symptom has never been observed.

`tasks.md` **T001 observes the unfixed behaviour on a live stack before any test is written**:
one overlay, geometry chosen to fit its editor preview with a small margin, bound to 1×1, 2×2
and 3×3 layouts, kiosk at 1920×1080; editor and wall screenshots; measured type ÷ box per
surface against §2's predictions.

- **Outcome A — the 3×3 overrun is visible** (text outside the plate background, wrapped or
  clipped at the tile edge): proceed as filed.
- **Outcome B — no visible overrun on 3×3, but the measured ratios match §2** (the preview
  disagrees with every wall): **proceed, re-scoped** — the PR title and body describe a
  preview-fidelity defect, not an overrun, and say the overrun did not reproduce. Argument: the
  issue's "re-scope or close" clause was written about the *overrun*. The fidelity defect is
  independently operator-visible (the editor shows a label 2.4× larger relative to its plate than
  the 1×1 wall it is bound to), is the thing spec 146 named as the correct fix's purpose ("the
  only option under which the editor's fixed 800x450 canvas becomes a *faithful* preview"), costs
  two classes and one line, and carries no data risk (§4). Closing it would leave the one
  surface operators author on knowingly unfaithful.
- **Outcome C — the measured ratios do not match §2:** the arithmetic in this spec is wrong.
  **Stop.** Comment the measurements on #2353, label `agent:blocked`. Do not fix a defect whose
  shape is not understood.

## 7. Latency budget impact

| Leg | Budget | Touched? |
|---|---|---|
| Camera → SFU | ≤ 80 ms | No |
| SFU → kiosk decode | ≤ 120 ms | No |
| Presentation buffer | ≤ 200 ms | No |
| Event → overlay state | ≤ 200 ms | No |
| **Overlay composite + render** | **≤ 50 ms** | **Yes** — adds `container-type: inline-size` to ≤ 9 `CameraViewer` roots; the label's `font-size` resolves against its container. |
| Headroom | ≤ 150 ms | No |

**The leg has no headroom today.** The nine most recent `develop` push runs on the nine-tile
CI fixture (first attempt, all `complete: true`, artifact `playwright-report-4-of-4`, read
2026-09-30):

| Run | SHA | n | p50 ms | p95 ms | max ms | T before/after ms |
|---|---|---|---|---|---|---|
| 36674174440 | `ca5d1c6c2b56a920f738684349b26424600017d2` | 108 | 59.55 | 74.70 | 85.40 | 29.41 / 32.26 |
| 36666872927 | `dcd686942e88ce8c9391bd062e38cce50477ea32` | 108 | 66.85 | 134.20 | 166.90 | 32.29 / 26.76 |
| 36661215565 | `d65418a1f0e5db1140d4e0500a1e32519eadcf1d` | 108 | 57.15 | 93.80 | 100.10 | 30.39 / 32.29 |
| 36624326723 | `f484b51b4e6a7fa0244cb001ffd886497c001b07` | 108 | 50.55 | 66.90 | 69.10 | 31.77 / 32.29 |
| 36578982802 | `7ef12d7e0bac007d4aa95cd179d10f3805d9f032` | 108 | 55.80 | 91.00 | 141.50 | 32.26 / 32.29 |
| 36564234273 | `af79b5ac5b9d6af3e376c71ee3bd9b0e50a451f3` | 108 | 57.75 | 94.00 | 124.90 | 31.77 / 32.29 |
| 36538160799 | `082778553da67a70c2f2bef28883d4773689e68f` | 108 | 50.60 | 147.40 | 158.20 | 31.77 / 30.39 |
| 36498579914 | `3cb08cb2c53163b9acc3ccab018ade85f2164f7b` | 108 | 49.80 | 110.40 | 116.60 | 32.29 / 28.57 |
| 36491446212 | `ba8c5041147a0c510be63b2537420a8d20c0e4d4` | 108 | 70.90 | 112.70 | 131.00 | 32.29 / 29.05 |

Mean p50 **57.66 ms**, sample σ **7.32 ms**, spec 225 FR-018's 3σ tolerance **21.97 ms**
(threshold 79.63 ms). These are CI-runner figures (software rasterisation, ~31 Hz cadence), not
kiosk figures, and they are a **comparison set for this spec only** — not spec 225's committed
baseline, which remains T015–T019's job.

**What this can and cannot detect, stated before measuring:** a 3σ band of ~22 ms is about
two-thirds of one frame at the observed `T ≈ 32 ms`. Spec 225 §9.2 F2 found render cost on this
runner shows up as a *cadence step* (a dropped frame, +`T`), which this band catches. A sub-frame
cost that does not drop a frame is invisible to every instrument this repo has on this runner.
That limit is recorded, not papered over.

**Expected effect:** neutral. Inline-size containment on an element whose width is already set by
its parent adds no layout pass, and `cqw` resolution is a style computation of the same cost as
`vw`. That is a prediction; §8 of `plan.md` and `tasks.md` T014 are how it is measured.

## 8. Requirements

- **FR-001** `overlayLabelSurfaceStyle()` returns `fontSize: max(${min(12, f/4)}px, calc(${f}cqw / 19.2))`.
- **FR-002** `CameraViewer`'s root establishes an inline-size container.
- **FR-003** `OverlayEditor`'s canvas establishes an inline-size container.
- **FR-004** The other seven surface properties, label placement, and every editor behaviour are
  unchanged — their existing characterisation and parity tests stay green **unmodified**.
- **FR-005** No stored data, schema, contract or endpoint changes (§4).
- **FR-006** The composite + render leg is measured on the PR against §7's comparison set and
  the result is written into `verification.md` (run ids, SHAs, figures) — not only reported to
  the orchestrator.
- **FR-007** T001's reproduction outcome (A/B/C, §6) is recorded in `verification.md` with the
  screenshots' measured ratios, and the PR title/body follow it.
- **NFR-001** No new dependency; no Tailwind plugin; no new CSS file.
- **NFR-002** Nothing in this spec repeats the 250-tile or four-tile premise (#2363). Where a
  per-tile count is argued, it is nine (ADR-0156).

## 9. Out of scope, raised separately

- **#2337's premature closure.** The gate is unwired (§1); spec 225 T015–T022/T028 remain. For the
  orchestrator to reopen #2337 or file the follow-up — this lane may not decide it on #2353's behalf.
- **The editor's "Font size: Npx" caption** (`OverlayEditor.tsx:727`) now means "px at a 1080p
  frame" and the preview shows `N × 800/1920`. A copy question for the editor shell (#2350), not
  a defect in this change; flagged in the PR.
- **`CameraViewer.tsx:388` and `overlayLabelStyle.ts:28` comments cite 250 tiles.** The second is
  rewritten by FR-001's doc-comment update anyway; the first is not in this diff (#2363 family).
