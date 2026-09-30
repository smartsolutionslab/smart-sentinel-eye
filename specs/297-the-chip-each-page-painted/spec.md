# Spec 297 — The chip each page painted

**Issue:** [#2635](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2635)
— *Build a Badge / status pill primitive*. Labels `tech-debt`, `agent:ready`. Project #13,
status **In Progress** (verified 2026-09-30, `gh issue view 2635 --json projectItems`).
**Branch:** `feat/2635-badge-status-pill-primitive` (cut from `origin/develop` @ `ab030e5a`)
**Created:** 2026-09-30
**Lane:** autonomous (ADR-0144)

**Spec number.** Every remote branch, local branch and worktree was listed on 2026-09-30.
`origin/develop` tops out at **295**; `origin/feat/2618-automation-switches-a-wall` claims
**296** (`specs/296-the-rule-that-turns-the-wall`). **297** is free. Re-check before opening
the PR — this session creates specs quickly (memory: *spec number: origin/develop isn't
enough*).

**ADRs this spec is bound by (it applies them; it decides nothing they left open):**

- **ADR-0146** — one discipline, two surfaces. Governs everything on the wall: *"`backdrop-filter`
  and any translucency over live video"*, *"static bright regions larger than a status chip"*,
  no per-tile `box-shadow`, no motion that is not a signal. Item 4: the triad is status
  vocabulary.
- **ADR-0148** — two-layer OKLCH tokens: a primitive scale, a semantic layer citing it,
  components cite semantic roles only, a theme remaps semantic roles only.
- **ADR-0077** / **ADR-0078** — the design system on Radix + Tailwind tokens; `asChild` via
  `@radix-ui/react-slot` (already a dependency, `Button.tsx`).
- **Constitution §IV**, **ADR-0015**, **ADR-0123** — the ≤ 50 ms *overlay composite + render*
  leg, read cadence first, compositing second. This change is on that leg (§5).
- **ADR-0156** — walls are up to 3×3 = **nine tiles**, not 250 and not four.
- **ADR-0139 / ADR-0144** — red first; phase-4a colour declared in §6.
- **ADR-0150** — every new test waits on a condition.
- **ADR-0036** — smallest change; the basis for every "not here" in §3.
- **ADR-0162** — e2e helpers extracted only where duplicated (§7 keeps the one new e2e check
  local to its spec).
- **ADR-0037**, **ADR-0109** (`[P]`), **ADR-0053** (sentence-style test names).

**No new ADR is needed**, and none is written. §4 records the two places a reader might think
one is, and why each is an application of an existing ADR rather than a new decision.

---

## 1. The issue's premise, re-checked on this tree (`ab030e5a`)

| Issue says | Tree says | Consequence |
|---|---|---|
| The pattern is duplicated across five sites. | **Three of the five are pills; two are not.** `StreamHealthBadge.tsx:9-17` (tint `/20` + border `/40`, `px-2 py-0.5 text-xs`), `LiveUpdatesBadge.tsx:15` (tint `/15` + border `/40`, `px-3 py-1 text-xs`), `TileAlignmentBadge.tsx:20` (tint `/30`, no border, `px-3 py-1 text-xs`). **`RulesPage`'s `StateBadge`** (`RulesPage.tsx:224-228`) is coloured **text only** — no fill, no padding. **`LayoutsPage`'s `badge()`** (`LayoutsPage.tsx:366-377`) is a **string formatter** (`"v3 · Published · draft v4"`) rendered as muted metadata text; it has a byte-identical twin in `OverlaysPage.tsx:391-402` the issue does not name. | `badge()` is not a status pill and is **not** consolidated (§3.2). `StateBadge` *becomes* a pill — a visible change, not a consolidation. |
| (not named) | **Two more pills exist**: the wall's *"Overlay unavailable"* chip (`LayoutGrid.tsx:443-450`, tint `/30`, `px-4 py-1 text-sm`) and the console's *"Retired"* chip (`CameraDetailPage.tsx:98`, `bg-fg-muted/15 px-2 py-1 text-xs font-medium`). | Both are in scope (§3.1): leaving them would leave one translucent chip over live video and one console chip on call-site alpha. |
| The three wall-relevant sites are merely inconsistent. | **Three of them violate ADR-0146 today.** `TileAlignmentBadge` and the *Overlay unavailable* chip are `absolute` inside the tile, over `CameraViewer`'s video, filled with call-site alpha (`bg-accent-warning/30`). `LiveUpdatesBadge` is `fixed bottom-3 right-3` over the bottom-right tile, `bg-accent-warning/15`. ADR-0146: *"disqualified there … any translucency over live video"*. | Consolidation is also a **defect fix on the wall**: the Badge's fill is opaque (§2 US2, FR-004). |
| Needs **three new** semantic roles, a subtle tint per triad hue. | **One exists already**: `--color-accent-fault-subtle` (`tokens.css:136`, issue #2523, used by `RetryBanner.tsx:13`). | Two roles are new (`-active-subtle`, `-warning-subtle`); the third is **re-pointed** (next row). |
| The tints should *mirror `--color-accent-subtle`*, i.e. `color-mix(in oklch, <hue> N%, var(--color-bg-base))`. | **That formula does not produce a tint of the hue. Observed in Chromium 1243 (Playwright 1.63.0), 2026-09-30** — `getComputedStyle` + a 1×1 canvas read-back: `color-mix(in oklch, var(--red-500) 10%, var(--gray-950))` renders **`rgb(20,24,37)` — slate blue**, `oklch(0.2102 0.0274 271.0)`; the active and warning mixes render `rgb(15,27,37)` and `rgb(24,27,38)`, both blue. `color-mix(in oklch, …)` interpolates **hue** linearly by the mix percentage, and every ground in the scale carries a real hue (`--gray-950` is `oklch(15.82% 0.0072 258.4)`), so a 10 % mix takes ~90 % of its hue from the ground. Mixing toward `var(--black)`/`var(--white)` is worse: those are specified in OKLCH with hue `0`, which is not treated as powerless, so the green tint renders **brown-red** (`rgb(48,14,0)`). **The existing `--color-accent-fault-subtle` is affected: `RetryBanner`'s "fault" box is slate blue in the dark theme.** | The three tints are **primitive stops** at each triad's own hue, not mixes (§4.1, plan §2). `--color-accent-fault-subtle` is re-pointed at its stop, which changes `RetryBanner`'s fill from slate to a red tint — declared (§6). |
| — | **The architecture tests would not have caught that.** `InteractionStateTests.ResolveOklch` (`:507-511`) resolves a `color-mix` by taking the **hue of the higher-chroma operand** — a documented approximation for mixes toward black/white — so it computes fault-subtle as red. Only the browser shows the drift. | FR-003 pins each tint to an OKLCH **literal** at its triad's hue, so no resolver approximation is involved; FR-011 checks the rendered hue in real Chromium. |
| — | **No test covers `TileAlignmentBadge`** (no `*.test.*` references `tile-out-of-alignment` or `TileAlignmentBadge`), and **none covers the *Retired* chip** or `RulesPage`'s state cell text. | Each gets a characterisation test **observed green before** the change (§6): a conversion with no covering test is a rewrite. |
| Needs a live check on the wall "since ADR-0146's prohibition is only checkable by looking". | **Two of its clauses are mechanically checkable for these chips**, and this spec checks them: *translucency* (the rendered fill's alpha, FR-011) and *larger than a status chip* (the rendered box, FR-012). What stays visual-only is burn-in over months — no test reaches that. Looking is still required (§7), as corroboration. | §7 V2. |

## 2. User stories

Each story is independently shippable once the foundation (§3.1 F) is in: its own call sites,
its own tests, observable in a running stack without the others.

### US1 (P1): The console reads stream health from one Badge

`StreamHealthBadge` renders through a shared `Badge`, carrying the triad as an **opaque tint
of the status hue** with the status-coloured label, and keeps everything spec 266 gave it:
it is still a focusable button opening the detail popover.

**Why P1:** it is the one existing site that is already a full pill, has the richest state
set (five), and has tests — the smallest adoption that proves the primitive end to end,
exactly as spec 266 chose `RuleDialog`'s Action for `Select`.

```gherkin
Scenario: each stream state carries its triad tone (happy path)
  Given the cameras list shows streams in states Healthy, Degraded, Offline and Provisioning
  Then the Healthy pill uses the active tone, Degraded the warning tone, Offline the fault tone
  And Provisioning uses the neutral tone
  And no pill's classes contain a call-site alpha modifier ("/NN") or a border

Scenario: unknown is neutral and inert (bad request — nothing to disclose)
  Given no stream record exists for the camera
  Then the pill reads "Unknown", uses the neutral tone and is not a button

Scenario: the detail stays reachable from the keyboard (unchanged — characterisation)
  Given a Degraded stream with error "source unreachable"
  When the operator tabs to its "Degraded" pill and presses Enter
  Then a dialog-role popover opens containing "Error: source unreachable"
  And Escape closes it and returns focus to the pill

Scenario: an unrecognised state falls back to neutral (conflict — server ahead of client)
  Given the API returns a state the client's StreamState union does not name
  Then the pill shows that state's text in the neutral tone rather than throwing
```

*Auth: N/A — no request semantics change; the page's existing polling and scopes are untouched.*

### US2 (P1): The wall's status chips are opaque, one size, and the same chip

`LiveUpdatesBadge`, `TileAlignmentBadge` and the *Overlay unavailable* chip render through
the same `Badge`, in the warning tone at the wall size. Their fill is **opaque**: live video
no longer shows through them. Their semantics — `role="status"`, test ids, `data-camera`,
text, placement — are unchanged.

**Why P1:** it removes three ADR-0146 violations from the surface ADR-0146 exists to protect,
and it is the latency-relevant slice (§5).

```gherkin
Scenario: the degraded chip is an opaque warning chip (happy path)
  Given a kiosk showing a wall
  When every /hubs/** request is aborted and the page is reloaded
  Then the "Live updates degraded" status is shown
  And its rendered background has alpha 255 (no translucency over live video)
  And its rendered box is a single line no taller than 24 CSS px
  And its classes are the Badge's warning tone at the wall size

Scenario: a tile the wall cannot hold says so on an opaque chip
  Given a tile the alignment controller has released
  Then that tile shows "Not in sync with the wall" with role status, test id "tile-out-of-alignment" and data-camera naming its camera
  And the chip uses the warning tone at the wall size, with no alpha modifier

Scenario: an archived overlay's tile says so on the same chip
  Given a tile bound to an overlay a pushed OverlayRevisionArchived names
  Then that tile shows "Overlay unavailable" with role status
  And the chip uses the warning tone at the wall size, with no alpha modifier
  And a tile bound to a different overlay shows no such chip

Scenario: recovery clears the chip (unchanged — characterisation)
  Given the degraded chip is shown
  When the hub becomes reachable again
  Then the chip is removed

Scenario: bad request — a translucent triad fill reaches the wall
  Given a kiosk-web source file containing "bg-accent-warning/30" in a class string
  Then the architecture guard fails naming that file and match
```

*Auth / conflict: N/A — presentation only; no request, hub message or scope changes.*

### US3 (P2): The console's other status labels are the same chip

`RulesPage`'s state cell and `CameraDetailPage`'s *Retired* marker render through `Badge`:
Active → active, Draft → warning, Archived → neutral; Retired → neutral.

**Why P2:** consistency, not a defect. `StateBadge` gains a fill it never had — a visible
change a reviewer should see on its own.

```gherkin
Scenario: a rule's state is a pill in its tone (happy path)
  Given the rules list shows an Active, a Draft and an Archived rule
  Then each State cell shows its state text in a pill of tone active, warning and neutral respectively

Scenario: a retired camera is marked with a neutral pill
  Given a retired camera's detail page
  Then "Retired" is shown in a neutral pill beside the name
  And a camera that is not retired shows no such pill

Scenario: the state text is unchanged (characterisation)
  Then the State cell's text is exactly the rule's state name
```

*Auth / conflict: N/A.*

### US0 (P1, foundational — blocks US1–US3): the tints and the Badge exist

Not user-visible on its own; listed so its acceptance is explicit.

```gherkin
Scenario: each triad tint is its own hue, in every theme
  Given tokens.css
  Then --color-accent-active-subtle, --color-accent-warning-subtle and --color-accent-fault-subtle
    each resolve, through var() only, to an oklch() literal whose hue equals its triad's hue
  And the light theme redeclares each to a light stop of the same hue
  And no theme block redeclares a triad role itself

Scenario: the label is legible on its tint
  Then each triad colour on its subtle tint is at least 4.5:1 in the dark and high-contrast themes
  And --color-fg-muted on --color-bg-raised (the neutral tone) is at least 4.5:1 in all three themes

Scenario: the tint renders as its hue in a real browser
  Given the kiosk page in Chromium
  Then each subtle tint's rendered colour is opaque and its OKLab hue angle is within 20 degrees of its triad's

Scenario: bad request — a tint is re-derived with color-mix
  Given --color-accent-warning-subtle: color-mix(in oklch, var(--color-accent-warning) 10%, var(--color-bg-base))
  Then the token fact fails naming --color-accent-warning-subtle (a mix, not a literal at the triad's hue)
```

## 3. Scope

### 3.1 In this spec

- **F (foundation).** Six OKLCH primitive stops (a dark and a light tint per triad hue); the
  three `-subtle` roles in `:root` citing the dark stops and redeclared in `light` citing the
  light stops; their `tailwindTheme.ts` entries; `apps/shared/src/ui/composites/Badge.tsx`
  and its `package.json` `exports` entry. Plan §2–§3.
- **US1.** `StreamHealthBadge.tsx`.
- **US2.** `LiveUpdatesBadge.tsx`, `TileAlignmentBadge.tsx`, `LayoutGrid.tsx`'s *Overlay
  unavailable* chip; a kiosk-web architecture guard (FR-010).
- **US3.** `RulesPage.tsx`'s `StateBadge`, `CameraDetailPage.tsx`'s *Retired* chip.
- **Collateral, declared:** `RetryBanner` changes fill (slate → red tint) with no code change,
  because the token it cites is re-pointed (§1, §6).

### 3.2 Not in this spec

| Item | Why not | Where it goes |
|---|---|---|
| `LayoutsPage` / `OverlaysPage` `badge()` | Not a status pill: a revision-summary string in muted metadata text, read verbatim by two e2e assertions (`LayoutsPage.tsx:364-365`). Giving "Published" a triad tone would make it status vocabulary it is not (ADR-0146 item 4). The real duplication is the **two identical formatter functions** — a text-helper refactor, behaviour-preserving, unrelated to Badge. | #2692. |
| The five console error banners on `border-accent-fault/40 bg-accent-fault/10` (`WallDetailPage.tsx:143`, `RulesPage.tsx:152`, `OverlaysPage.tsx:116`, `SystemVariablesPage.tsx:109`, `LayoutsPage.tsx:124`) and `RulesPage.tsx:169` | `RetryBanner` look-alikes, not badges. | #2693. |
| The wall's four full-screen action buttons on `bg-accent-active/20` (`WallPage.tsx:166`, `ReconnectingScreen.tsx:35`, `PickerPage.tsx:74`, `LayoutGrid.tsx:210`) | Buttons, not status; the triad used as affordance (spec 287's concern, on the wall). Not over video. | #2694; a named, shrink-only allowlist entry in FR-010's guard until then. |
| Light-theme contrast of triad text | Pre-existing and wider than badges: a triad hue as text on any near-white surface is 1.6–2.9:1 (plan §2.2). No app sets `data-theme="light"` at runtime today (`apps/*/index.html` pin `dark`). Fixing it needs per-theme triad *text* roles — a decision about ADR-0146 item 4 ("the triad survives unchanged"), not a Badge detail. | #2695; FR-008 names `light` as a shrink-only exclusion. |
| `--color-accent-subtle` and `--color-accent-hover` hue drift | Same mechanism as §1 (cyan drifts ~37° toward blue at 16 %); small, and not a triad. | Recorded in #2695 as a related observation, not filed separately. |
| A `Badge` `size`/`tone` beyond what consumers use; icons; dismiss buttons; dot variants | No consumer (ADR-0036). | — |

## 4. Why no new ADR

### 4.1 Tints as primitive stops, not derived mixes

ADR-0148 decision 2 says *state variants* are derived with `color-mix(in oklch, …)`. A status
tint is not a state variant of an interactive role, and §1 shows the derivation does not work
for it anyway. ADR-0148's primitive layer is "the raw scale"; adding stops to it is what the
layer is for. The comment at `tokens.css:70` (*"Triad stops, one each, no ramp"*) records
spec 257's scope, not an ADR constraint, and is updated to say why the tint stops exist.
`DesignTokenLayerTests` already accepts `--<hue>-<step>` OKLCH literals in `:root` and
`var()` citations in the semantic layer. **No guard is widened.**

*Would need an ADR (not taken):* switching the derivation to `color-mix(in oklab, …)` — the
one formula that renders the right hue (§1 probe: `rgb(44,26,27)` for fault) — amends
ADR-0148's stated mechanism and widens `DesignTokenLayerTests.ColorMixValue` and
`InteractionStateTests.ResolveOklch`. The lane may do neither. **Finding for a human:** ADR-0148
decision 2's premise ("the same mix percentage produces the same perceived shift on any
hue") holds only for mixes whose other operand is hue-less; against this scale's grounds and
its `--black`/`--white` primitives it drifts. Raised in #2695.

### 4.2 ADR-0146 needs no interpretation for this design

- *"Any translucency over live video"* — the Badge's fill is an opaque literal; FR-011 checks
  the rendered alpha. No reading needed.
- *"Static bright regions larger than a status chip"* — the Badge **is** the status chip, at
  `text-xs` on one line; its fill is dark (`L` = 25 % in the dark theme). FR-012 bounds the
  rendered box. A bright *region* would need a bright fill; the only bright pixels are glyphs.
- *"`box-shadow` on a per-tile element"*, *"any animation that is not itself a signal"* —
  the Badge has neither; `MotionLanguageTests` fact 1 already scans the kiosk reach closure,
  which will include `Badge.tsx` through the `exports` map.

### 4.3 Where the Badge lives, and why that is not a way round a guard

`InteractionStateTests.Primitives_use_the_accent_not_the_triad_for_affordance` scans
`apps/shared/src/ui/primitives/` for `\bbg-accent-(?:active|warning)\b`, which also matches
`bg-accent-active-subtle`. A Badge is **status, not affordance** — exactly what that fact
reserves the triad for — so it lives in `apps/shared/src/ui/composites/`, beside
`RetryBanner`, the other status surface on a triad tint. `SharedUiTokenUsageTests` scans all of
`apps/shared/src/ui`, so the Badge is still held to "no call-site alpha, no stock palette, no
colour literal". The fact is not edited. *If a reviewer rules the placement an evasion, the
lane blocks* — moving it to `primitives/` needs that regex narrowed, which the lane may not do.

## 5. Latency budget (§IV): **overlay composite + render** leg

**What changes on the leg.** Up to nine tiles (ADR-0156) each render, *only while their
condition holds*, one `TileAlignmentBadge` and one *Overlay unavailable* chip as siblings of
`CameraViewer`, inside the tile's paint. `LiveUpdatesBadge` is one fixed element outside the
tiles. The change swaps a **translucent** fill (blended over decoding video on every frame the
region repaints) for an **opaque** one, drops one 1 px border, and shrinks the *Overlay
unavailable* chip (`text-sm px-4` → `text-xs px-3`). No layer is added (no new
`transform`/`will-change`/`filter`/`shadow`/`position` context — both tile chips already carry
`absolute … -translate-x-1/2`), no element count changes, no animation.

**Expected effect: none measurable.** Opaque over video is no more work than blended, and
usually less. That is an argument, and §IV requires a demonstration.

**What is measured, and its honest limit.** The only instrument on this leg is `overlay_draw`
(ADR-0123), read by spec 225's span test on a healthy wall. **On a healthy wall none of the
three chips renders** — unless the alignment controller has released a tile during the run,
which the phase-5 record must state either way (plan §8 V3 step 5). So the CI figure proves
*the healthy path did not regress*; it does not time a badged tile. The badged path is
covered by the argument above and by looking (V2). No figure will be reported as covering it.

**The leg is already over budget** (ADR-0123: p50 54.2 ms via #1891; spec 225 §0 — never a
recorded distribution before spec 225; spec 294's scratch baseline p50 **57.66 ms**,
tolerance 3σ 21.97 ms). The comparison is against that baseline, not against 50 ms.
§IV's leg-state table is **not** changed by this spec.

## 6. Phase-4a colour: **red** (behaviour-changing), with a characterisation set

Every call site's rendered output changes (tint hue and opacity at minimum), so the work is
red. What each site must **keep** — role, test id, `data-camera`, text, focusability,
popover behaviour — is held by tests that must pass **unmodified**, observed green on the
untouched tree first.

| Site | Colour | New red tests | Characterisation (green before, unmodified after) | Tests edited, and why |
|---|---|---|---|---|
| Tokens (F) | red | token facts FR-003/FR-008 (red: roles absent / fault-subtle is a mix) | `DesignTokenLayerTests`, `InteractionStateTests`, `SharedUiTokenUsageTests` — all existing facts | none |
| `Badge.tsx` (F) | red | `Badge.test.tsx` against a signature-only stub | — | — |
| `StreamHealthBadge` (US1) | red | tone per state via Badge classes; no alpha, no border; unknown neutral; unrecognised state neutral | the three spec-266 popover cases (`Is a focusable button…`, `Enter opens a popover…`, Healthy/no-error, Unknown not a button) | the two `it.each`/Unknown **tone-class** cases (`StreamHealthBadge.test.tsx:20-40`) assert `bg-accent-*/20` — the exact behaviour this spec changes. Rewritten to the new classes; quoted in the PR. |
| `LiveUpdatesBadge` (US2) | red | e2e opacity + box (FR-011/012) in `kiosk-live-updates.spec.ts`; unit class case | `CellPage.test.tsx:522-532,1220`, `PickerPage.test.tsx:153-180`, `e2e/kiosk-live-updates.spec.ts` existing lines, `kiosk-reconciliation.spec.ts`, `wall-changes-its-scene.spec.ts` | none |
| `TileAlignmentBadge` (US2) | red | class case | **new** `TileAlignmentBadge.test.tsx` characterisation (role, test id, `data-camera`, text) — written and observed green on the untouched tree first | none |
| *Overlay unavailable* (US2) | red | class case in `CellPage.test.tsx` | `CellPage.test.tsx:2270-2320` (`unavailableBadgeIn`), `kiosk-reconciliation.spec.ts:103` | none |
| kiosk guard (US2) | red | FR-010, naming exactly `LiveUpdatesBadge.tsx`, `TileAlignmentBadge.tsx`, `LayoutGrid.tsx` (warning matches) | — | — |
| `StateBadge` (US3) | red | tone per state | **new** characterisation case: the State cell's text equals the state | none |
| *Retired* (US3) | red | neutral Badge | **new** characterisation case: "Retired" shown iff retired | none |
| `RetryBanner` (collateral) | no code change | — | its existing tests and every page test rendering it | none |

**Declared green in advance (pins, not 4a evidence):** FR-008's neutral-tone contrast half
(`--color-fg-muted` on `--color-bg-raised` is 4.64:1 dark today); FR-003's "no theme redeclares
a triad role" half. Any case expected red that arrives green is a stop-and-report.

## 7. Independent end-to-end test procedure

Against the running Aspire stack (`dotnet run --project src/AppHost`), Chromium, a 2×2 wall
with fixture video (`e2e/support/seed-live-video-wall.setup.ts`'s shape):

1. **US2, degraded chip.** Sign in to the kiosk. Abort `**/hubs/**` and reload → *Live
   updates degraded* appears bottom-right over the tile. Video is **not** visible through it.
   Read its computed background (canvas read-back: alpha 255) and bounding box (one line,
   ≤ 24 px tall). Unroute → it clears. `e2e/kiosk-live-updates.spec.ts` covers the same path.
2. **US2, overlay unavailable.** Archive the overlay bound to a tile (management-web) → that
   tile shows *Overlay unavailable* on an opaque chip; other tiles do not.
3. **US2, out of alignment.** Induce skew as spec 045's verification did (§1 there: a harness
   delaying one tile's media). If it cannot be induced in the session, record *not observed
   live* — covered by unit tests only — and do not claim otherwise.
4. **US1.** Cameras list: provoke an outage on one camera (repo memory: patch its MediaMTX
   path, keep a control tile) → *Offline* pill in the red tint; Tab + Enter opens the popover.
5. **US3.** Rules list shows Active/Draft/Archived pills; a retired camera's detail page shows
   the neutral *Retired* pill.
6. **Collateral.** Provoke a list-load failure → `RetryBanner` shows a red-tinted box (it was
   slate).
7. Screenshots of 1–6 in the dark theme; of 4–6 also with `data-theme` set to `light` and
   `high-contrast` on `<html>` via DevTools.

## 8. Functional requirements

- **FR-001** `Badge` renders its children in an element whose classes are exactly the tone
  set and size set of plan §3 — tone ∈ {`active`, `warning`, `fault`, `neutral`}, size ∈
  {`sm` (default), `md`}. No other visual prop.
- **FR-002** `Badge` with `asChild` renders **no element of its own**: it merges its classes
  onto its single child, which keeps its own element, role and attributes.
- **FR-003** `--color-accent-{active,warning,fault}-subtle` are declared in `:root`, each as a
  `var()` chain ending in an `oklch()` literal whose hue equals its triad role's hue exactly;
  `[data-theme='light']` redeclares each to a light stop of the same hue; no theme block
  redeclares `--color-accent-{active,warning,fault}`.
- **FR-004** No Badge class string contains a call-site alpha modifier, `border`, `shadow`,
  `backdrop-`, `transition`, `animate-`, `opacity-` or `ring`.
- **FR-005** Every site in §3.1 renders through `Badge`; each keeps its role, test id,
  `data-*` attributes, text and placement classes.
- **FR-006** `StreamHealthBadge` maps Healthy→active, Degraded→warning, Offline→fault,
  Provisioning→neutral, missing record or unrecognised state→neutral.
- **FR-007** `StateBadge` maps Active→active, Draft→warning, Archived→neutral; *Retired* is
  neutral. The wall's three chips are warning at size `md`.
- **FR-008** Each triad colour on its `-subtle` tint is ≥ 4.5:1 (WCAG 1.4.3) in `dark` and
  `high-contrast`; `--color-fg-muted` on `--color-bg-raised` is ≥ 4.5:1 in all three themes.
  `light` is excluded from the triad half by a **named, issue-tagged, shrink-only** entry,
  with an honesty fact that fails once light meets the threshold (so the exclusion is removed,
  not forgotten).
- **FR-009** `tailwindTheme.ts` maps `accent.active-subtle` and `accent.warning-subtle`
  (fault's entry exists) — covered by the existing
  `Every_token_the_theme_cites_is_declared_and_semantic`.
- **FR-010** A guard fails when any non-test `.ts`/`.tsx` file under `apps/kiosk-web/src`
  contains `(bg|border|text|ring|outline|fill|stroke)-accent-(active|warning|fault)/<n>` in a
  string literal, outside a shrink-only `(file, match, reason #issue)` allowlist whose entries
  must each still match (honesty fact).
- **FR-011** In Chromium, the *Live updates degraded* chip's rendered background has alpha 255,
  and each `-subtle` tint's rendered colour has an OKLab hue angle within 20° of its triad's.
- **FR-012** In Chromium, the *Live updates degraded* chip's bounding box is ≤ 24 CSS px tall
  (one line of `text-xs` plus `py-1`).
- **FR-013** The overlay composite + render leg's CI figure on the PR is **within tolerance**
  of the develop baseline on three complete records (plan §8 V3), or the PR blocks.

## 9. Success criteria

- **SC-1** Every FR has a test; every red one observed red first and quoted verbatim in the PR.
- **SC-2** The characterisation set in §6 is green before any edit and green, unmodified, after.
- **SC-3** `grep` finds no `accent-(active|warning|fault)/\d` in any file of §3.1.
- **SC-4** `pnpm lint`, `pnpm typecheck`, `pnpm format:check`, `pnpm test`; `dotnet test
  tests/Architecture.Tests`; e2e green in CI.
- **SC-5** V3 (plan §8) passes on three records with figures quoted in `verification.md`.
- **SC-6** V2 screenshots show no video through any wall chip.
