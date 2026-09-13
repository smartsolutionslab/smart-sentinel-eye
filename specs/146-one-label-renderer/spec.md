# Spec 146 — One label renderer, and a `vw` term that answers the wrong question

**Issue:** #2339 — *The overlay editor's preview and the wall's render are two hand-written
copies that disagree*
**Branch:** `fix/2339-one-label-renderer`
**Status:** Phase 4 complete — awaiting PR (T011)
**Lane:** autonomous (ADR-0144)
**Engineer:** `frontend-engineer`
**Phase 4a colour:** **two colours** — CHARACTERISATION (green) on the wall, RED on the
editor. Reasoned out in `plan.md` §"Phase 4a".
**ADRs:** 0037 (phases and gates), 0144 (the lane; the two colours of phase 4a), 0036
(smallest change; no speculative generality; read before write), 0074 (two React apps
over one `apps/shared`), 0077 (`ui/primitives` wraps Radix — the reason the new module is
not put there), 0078 + **0148** (tokens; the two-layer architecture the surface treatment
will later cite), **0146** (one discipline, two surfaces; the wall's prohibitions), 0129
(the label is aged, not frame-synced), 0109 (`[P]` disjoint-file parallelism), 0117 (§VII
binds implemented legs), 0028 (GitFlow), 0086 (no `Co-Authored-By`), constitution §IV
(composite + render ≤ 50 ms) and §Testing (two obligations, not one rule).

**Spec number 146.** Derived from refs, not the working tree, per spec 137's method.
`git ls-tree --name-only origin/develop specs/` and this branch's tree both top out at
`144-a-wait-that-actually-waits`; `git log --all --name-only --pretty=format: -- specs/`
shows `145-a-retried-pass-says-so` on an unmerged ref (PR #2352). 145 is taken. **146.**

---

## Phase 1 — every claim in the issue, checked against the tree

### The duplication is real, and there are exactly two copies. Confirmed.

A repo-wide sweep for `fontSizePx`, `normalizedX`, `normalizedWidth`, `rgba(255, 255, 255`
and the label testids finds **two** places where the label's appearance is written, and no
third. `CellPage.tsx:493-503` builds the *data* and hands it to `CameraViewer`; the Zod
schema, the SignalR wire shape and the C# `Label` value object carry the same six fields
and render nothing. There is no Storybook in this repo.

| # | Property | `OverlayEditor.tsx:82-94` (the preview) | `CameraViewer.tsx:396-411` (`OverlayLabel`, the wall) |
|---|---|---|---|
| 1 | `background` | `rgba(255, 255, 255, 0.92)` | `rgba(255, 255, 255, 0.85)` |
| 2 | `padding` | `0 8px` | `0 4px` |
| 3 | `border` | `1px solid rgba(17, 24, 39, 0.4)` | *(absent)* |
| 4 | `fontSize` | `value.fontSizePx` (React appends `px`) | `clamp(min(12, f/4)px, (f/16)vw, f px)` |
| — | `color` | `#111827` | `#111827` — agree |
| — | `fontWeight` | `600` | `600` — agree |
| — | flex centring | `display` / `alignItems` / `justifyContent` | identical — agree |

Rows 1-3 are unconditional and exactly as filed. Row 4 is **not**, and the difference
matters enough to be the rest of this section.

### The issue's arithmetic on row 4 is wrong, and the real defect is the opposite shape

The issue says a label authored at `fontSizePx: 48` "on one cell of a 5x5 wall renders at
the `vw` term, a fraction of what the editor showed."

**`vw` is viewport width. It is not tile width, and the tile count does not enter the
formula at all.** `CellPage.tsx:340-347` lays the wall out as
`repeat(gridCols, minmax(0, 1fr))` over `h-screen` — so a 5x5 tile is one twenty-fifth of
the screen's area, while `3vw` is 3% of the *whole* screen either way. Resolving the
clamp across viewports (`clamp(MIN, VAL, MAX)` = `max(MIN, min(VAL, MAX))`):

| `fontSizePx` | 3840px | 1920px | 1600px | 1366px | 1024px | 800px |
|---|---|---|---|---|---|---|
| 16 | 16.0 | 16.0 | 16.0 | 13.7 | 10.2 | 8.0 |
| 24 | 24.0 | 24.0 | 24.0 | 20.5 | 15.4 | 12.0 |
| 48 | 48.0 | 48.0 | **48.0** | 41.0 | 30.7 | 24.0 |
| 96 | 96.0 | 96.0 | 96.0 | 82.0 | 61.4 | 48.0 |

The `vw` term meets the `MAX` term when `viewport = 1600px`, **for every `fontSizePx`** —
the `/16` divisor and the `px` cap are the same number, so the crossover is
size-independent. Above 1600px the clamp saturates at the authored literal. So:

- **On a real wall (1920px or 3840px) the wall paints the authored `48px`, and so does the
  editor. Row 4's two formulas agree.** The issue's headline case is the one case where
  they match.
- **Below 1600px they diverge**, wall smaller, linearly: a 1366px console window paints a
  48px label at 41px. That is where a developer and the `management-web` camera-detail page
  live, and it is a genuine divergence — just not the one filed.

### What is actually broken on a 5x5 wall, and it is worse than what was filed

The label's **box** is relative to the tile and its **type** is absolute.

`CameraViewer.tsx:397-401` sizes the label as a percentage of the viewer box, and the viewer
is `relative aspect-video w-full` inside a `1fr` tile. On a 1920px screen a 5x5 tile is
~384px wide, so a label at `normalizedWidth: 0.5` is a **192px-wide box**. The type inside
it is still `48px`, because the clamp saturated. In the editor the same label is a
**400px-wide box** (0.5 x the fixed 800px canvas) holding the same 48px type.

So the operator judges "does my text fit its background plate?" against a box more than
twice the size of the real one, and the wall clips the answer. The tile's outer
`overflow-hidden` (`CameraViewer.tsx:353`) means the spill is cut at the tile edge rather
than at the plate, which reads as a rendering fault rather than as a sizing choice.

**That is the WYSIWYG failure.** The issue's four-row table is right about rows 1-3 and
right that row 4 is "a different formula"; its explanation of *why* row 4 hurts is not.
This spec fixes the four rows it names and files the fifth separately, below.

### The `vw` was a decision, not an accident, and it was made against the wrong variable

`specs/004-overlay-designer/spec.md:250-254`:

> **Kiosk renders an overlay whose `fontSizePx` would push the label off-screen at the
> kiosk's resolution:** Kiosk-side rendering uses CSS `vw`/`vh`-derived font sizing (input
> fontSizePx is the author-time reference at 1080p). Labels stay on-screen at lower
> resolutions because they scale proportionally.

and its plan's risk 6 (`specs/004-overlay-designer/plan.md:528`):

> Overlay font-size scaling via `vh` looks fine on the 1080p reference but breaks at 4K
> kiosk resolutions. | CSS uses `clamp(min, vh-derived, max)` with sensible caps.

The intent was **display-resolution independence** — one kiosk at 1080p, another at 4K. It
was written before the wall was a grid of `1fr` tiles, and it binds to the one quantity a
grid layout makes irrelevant. `git log -L 392,415` confirms the line has not been touched
since `dc0dafe5` (spec 004, T073). It is the only `clamp()` and the only `vw` in the entire
frontend.

### What else was checked

- **Two structurally identical type declarations.** `CameraViewerOverlay`
  (`CameraViewer.tsx:35-42`) and `OverlayLabel` (`overlays.api.ts:9-16`) have a
  byte-identical field list and are not aliased. Structural typing is why `CellPage` can
  feed either. Observed, **not folded here** — it is a type duplication, not a presentation
  one, and folding it changes `CameraViewer`'s exported surface for no behavioural gain
  (ADR-0036, smallest change).
- **A fifth, smaller divergence, and it should stay.** `OverlayEditor.tsx:45-46` floors the
  preview at `Math.max(..., 24)` px wide and `16` px tall; the wall has no floor. So any
  label narrower than 3% of the canvas previews too wide. The floor exists to keep the
  react-rnd drag target grabbable — an interactivity concern, and interactivity is the one
  thing the editor is *supposed* to add. Kept, and named so a reviewer does not read it as
  drift.
- **Existing coverage of the label's style is two lines.**
  `apps/management-web/src/features/cameras/CameraViewer.test.tsx:51-52` asserts
  `label.style.left === '50%'` and `label.style.top === '5%'`. **`fontSize`, `background`,
  `padding` and `border` have zero coverage at unit and e2e level.** The e2e specs
  (`e2e/kiosk-shows-a-label-over-video.spec.ts:180-188`) assert visibility and text only.
  `OverlayEditorDialog.test.tsx` never asserts anything about `overlay-editor-preview`.

---

## The one real decision: what happens to the `vw` term

**Chosen: (0) preserve it byte-for-byte in this spec, and file option (3) as its own
issue.**

The issue offered three. Taking them in turn:

**Option 1 — preview against a chosen target tile size.** Rejected as the primary answer.
It makes the preview correct for one layout and wrong for every other, and the same overlay
is bound to many layouts (spec 004's always-latest binding, plan risk 1). It also adds a
control to the editor, which is new behaviour in a fold.

**Option 2 — show a size range.** Rejected. "This renders between 12px and 48px" is a true
statement that no operator can act on, and it concedes that the editor is not a WYSIWYG
editor rather than making it one.

**Option 3 — make the type tile-relative (`cqw` with `container-type: inline-size` on the
tile).** **Correct, and it is the answer.** The tile is the natural container; it is already
the positioning context for the label (`CameraViewer.tsx:353`); and it is the only option
under which the editor's fixed 800x450 canvas becomes a *faithful* preview at any tile size,
because box and type would then scale together and their ratio — the thing the operator is
actually judging — becomes invariant.

**It is not done here, for three reasons, and the third is decisive.**

1. It changes how **already-published overlays** render on the wall. Every existing revision
   would repaint at a different size on the next reload. That is a behaviour change over
   live data and it needs its own red test and its own verification on a running wall.
2. It is on the composite + render leg (§IV, ≤ 50 ms across 250 tiles) and introduces
   `container-type: inline-size`, which is net-new to this repo — no `@container`, `cqw` or
   `container-type` appears anywhere today, and Tailwind's container-query plugin is not
   installed. Establishing a containment context on 250 elements has a layout cost that must
   be measured, and **#2337 files the CI render-budget gate that would measure it, scheduled
   before the redesign rather than after** (ADR-0146 §Consequences). Doing it now spends the
   leg before the instrument that watches it exists.
3. **CLAUDE.md, §the autonomous lane:** *"A refactor that is also a bug fix is two issues,
   because characterisation would otherwise encode the bug as the safety net."* Folding two
   copies into one is a refactor whose whole proof is that the wall does not move.
   Simultaneously moving the wall destroys that proof.

**And the fold must come first regardless.** After it, option 3 is one line in one function
with one test; before it, it is two edits in two files that can disagree again.

> **Recommendation to the orchestrator:** file an issue — *"Overlay type is absolute while
> its box is tile-relative, so a label authored at 48px overflows its own plate on a 5x5
> wall"* — carrying §"What is actually broken on a 5x5 wall" and §"The `vw` was a decision"
> above, blocked on this spec and on #2337. It belongs to the #2339–#2350 programme. **It is
> a latent defect, not a design choice**: spec 004 stated the goal (resolution independence)
> and the implementation binds to a variable that a grid layout makes meaningless.

---

## Scope of the extraction, and why it is a function rather than a component

The issue asks for "the geometry-to-CSS mapping plus the surface and type treatment". The
tree says those are two different things and only one of them has diverged.

- **Surface and type** — `display`, `alignItems`, `justifyContent`, `background`, `color`,
  `fontSize`, `fontWeight`, `padding`. **All four divergences live here. Extracted.**
- **Placement** — the wall maps normalized → `%` CSS; the editor maps normalized → px
  numbers handed to `<Rnd size>` / `<Rnd position>` (`OverlayEditor.tsx:43-46, 75-76`). Two
  different coordinate targets, one caller each, and **they have not diverged**. Extracting
  them would invent an abstraction with one consumer per branch (ADR-0036, no speculative
  generality). **Not extracted**, and this is a deliberate scoping decision rather than an
  oversight.
- **Affordance** — the editor's `cursor: 'move'` and `userSelect: 'none'`; the wall's
  `pointerEvents: 'none'`. Each surface's own, and the only difference that should exist.

**A function, not a component.** The editor's style must be handed to `<Rnd style={...}>`,
which takes a `CSSProperties` object and cannot host a wrapper component. A shared component
would therefore be usable by exactly one of the two call sites, which is the thing being
fixed. A function returning `CSSProperties` composes with both.

**Where it will make the token conversion easy (#2342, ADR-0148).** Every literal the
overlay label uses — `rgba(255, 255, 255, 0.85)`, `#111827`, `600`, `0 4px` — is hard-coded
in both copies today and none of them has an entry in `ui/tokens/colors.css` (which is
seven properties, all colour). After this spec they are **eight properties in one function
body**, so #2342 becomes a rewrite of one file. This spec introduces **no** tokens, per the
brief: the token system does not exist yet and #2342 is gated on it.

---

## User Scenarios & Testing

### User Story 1 — an operator's preview matches the wall (Priority: **P1**)

An operator opens the overlay editor, types a label and sets a font size. What the preview
box shows — its plate, its ink, its padding, its edge and its type size — is what the wall
paints for the same label, so the operator's judgement about the label transfers.

**Why this priority:** it is the entire premise of the feature ("WYSIWYG label editor", spec
004 T059) and it is the foundation the rest of the #2339–#2350 programme sits on. Every
later overlay-editor issue improves a preview; this one makes the preview mean something.

**Independent Test:** open `OverlayEditorDialog` in `management-web`, author a label, and
compare the preview box's inline style against the label `CameraViewer` paints for the same
`OverlayLabel`. Fully testable in `apps/shared` with jsdom — no stack, no Docker, no wall.

**Acceptance Scenarios:**

1. **(happy)** **Given** an `OverlayLabel` with `fontSizePx: 48`, **When** it is rendered by
   `OverlayEditor` and by `CameraViewer`, **Then** both nodes carry the **same** value for
   each of `display`, `alignItems`, `justifyContent`, `background`, `color`, `fontSize`,
   `fontWeight` and `padding`.
2. **(the four named rows)** **Given** the same label, **When** the editor's preview node is
   read, **Then** `background` is `rgba(255, 255, 255, 0.85)`, `padding` is `0px 4px`,
   `border` is empty, and `fontSize` is `clamp(12px, 3vw, 48px)` — i.e. the editor has
   adopted the wall's values, not the reverse.
3. **(the wall does not move — the conflict case)** **Given** the same label, **When**
   `CameraViewer` renders it, **Then** every property of the painted label node is
   **byte-identical** to what `origin/develop` paints, including `position`, `left`, `top`,
   `width`, `height` and `pointerEvents`.
4. **(the divergence guard)** **Given** a future edit that changes one surface property in
   only one of the two components, **When** the suite runs, **Then** it fails, naming the
   property and both values.
5. **(bad input — the schema's two bounds)** **Given** `fontSizePx: 8`
   (`overlays.schema.ts:6-13`'s floor), **When** the shared function is called, **Then**
   `fontSize` is `clamp(2px, 0.5vw, 8px)` — the `Math.min(12, f/4)` floor takes the `f/4`
   branch — and **Given** `fontSizePx: 256` (the ceiling), **Then** `clamp(12px, 16vw,
   256px)`. Both assert the formula is reproduced, not approximated.
6. **(affordance survives)** **Given** the editor's preview, **When** it is read, **Then**
   `cursor` is `move` and `userSelect` is `none`; **and Given** the wall's label, **Then**
   `pointerEvents` is `none`. The shared function contributes none of these.

**Auth:** N/A — this is presentational code in `apps/shared`, reached only after
`OverlaysPage`'s existing route guard. No endpoint, no scope, no token, no trust boundary
is touched. Recorded explicitly rather than omitted.

---

### User Story 2 — the extraction is where the tokens will land (Priority: **P2**)

The shared function is shaped so that #2342's conversion to ADR-0148 semantic tokens is a
rewrite of one file rather than a hunt through two components.

**Why this priority:** it costs nothing beyond doing US-1 in the right place, and it is the
stated reason the brief wants the fold before the token work. It is P2 because US-1 ships
value without it.

**Independent Test:** the eight literals appear in exactly one file, provable by grep.

**Acceptance Scenarios:**

1. **Given** the merged branch, **When** `rgba(255, 255, 255,` and `#111827` are grepped
   across `apps/`, **Then** each appears in exactly one non-test source file.
2. **Given** the shared module, **When** it is read, **Then** it introduces no CSS custom
   property and no entry in `ui/tokens/colors.css` — this spec converts nothing.

---

## Independent end-to-end test procedure

Not automated; run once by the verifier in phase 5. No Aspire stack required for steps 1-2.

1. `pnpm --filter management-web dev`, sign in, navigate to **Overlays → New overlay**.
2. Author a label: text `PRODUCTION LINE 1`, font size **48**. Screenshot the preview.
3. Publish it and bind it to a layout with a **5x5** grid
   (`e2e/support/seed-bound-overlay-wall.setup.ts` is the existing path).
4. Open the kiosk wall at a **1920px** viewport. Screenshot one tile.
5. **Expected:** the label's plate, ink, padding and edge are indistinguishable between the
   two screenshots, and the type is 48px in both.
6. **Expected, and it must be recorded as a residual rather than as a pass:** the label's
   text **overflows its plate on the tile and not in the editor**, because the plate is
   192px on the tile and 400px in the editor. That is the defect this spec deliberately does
   not fix. If it does *not* reproduce, the follow-up issue's premise is wrong and it should
   not be filed.
7. Narrow the browser to **1366px** and reload. **Expected:** the wall's type drops to ~41px
   and so does the editor's preview, together — they now share a formula.

---

## Latency budget impact

| Leg | Budget | Touched? |
|---|---|---|
| Camera → SFU | ≤ 80 ms | No |
| SFU → kiosk decode | ≤ 120 ms | No |
| Presentation buffer | ≤ 200 ms | No |
| Event → overlay state | ≤ 200 ms | No |
| **Overlay composite + render** | **≤ 50 ms** | **Yes — code path changed, budget unchanged.** |

**The leg does not move, by construction.** Today `OverlayLabel` allocates one object
literal per label per tile inside `render`. After this change it allocates one object
literal inside a called function and spreads it into one object literal — the same two
shapes React already diffs, plus one non-inlined call per label. There is at most one label
per tile (`CameraViewer.tsx:355`, a single optional prop), so the ceiling is **250 calls per
wall render**. The function must be **pure, allocation-only and DOM-free**: no
`getComputedStyle`, no `useMemo` cache keyed on a label object, no `ResizeObserver`, no new
DOM node, no new CSS feature. `plan.md` states this as a constraint on the engineer, and the
reviewer is asked to check it.

This spec adds **no** translucency, blur, shadow or animation, and therefore trips none of
ADR-0146's wall prohibitions. The existing `rgba(255, 255, 255, 0.85)` is an alpha fill, not
a `backdrop-filter`, and it is preserved rather than introduced.

**§VII, honestly.** ADR-0117 rule 1 says a leg whose code path exists must have a dashboard
before further work ships on it. §IV's table records **`Dashboard: no` for all five
implemented legs** — a repo-wide, pre-existing condition that long predates this spec and
that this spec cannot discharge. Noted rather than passed over in silence; no cell of §IV's
table changes.

---

## Out of scope, stated so it is not rediscovered as an omission

- **Tokens** (#2342) — gated on the token system, which does not exist. No token is added.
- **`cqw` / container queries** — the follow-up issue recommended above.
- **The canvas checkerboard** (#2340) — untouched.
- **Folding `CameraViewerOverlay` into `OverlayLabel`** — a type duplication, not a
  presentation one. Observed, recorded, not changed.
- **The editor's 24x16px drag-target floor** — kept, deliberately, and explained above.
- **`CellPage`, the API, the Zod schema, the SignalR wire shape, and every C# file.** The
  reviewer's boundary check for this PR is the diff's file list: **anything under `src/` is
  a defect.**
