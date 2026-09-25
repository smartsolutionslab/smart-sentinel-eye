# Spec 257 — The scales nobody picked

**Issue:** [#2332](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2332)
— *The design system is seven colours and nothing else*. Labels `enhancement`,
`agent:ready`. Phase 01 of the frontend redesign programme. On Project #13, status
**Todo** — verified 2026-09-25 via `gh issue view 2332 --json projectItems`. No
`item-add` needed.

**Spec number.** Every remote branch, every local branch and every worktree was
listed on 2026-09-25. Develop's highest is 252; 253 is an untracked directory in the
main checkout; 254 is claimed three times (`fix/2570`, `fix/2589`, `test/2450`); 255
by `fix/2586`. **256 was picked first and turned out to be a three-way collision** —
`fix/2366-mousedown-blur-submit-race` and `fix/2522-cameradetailpage-stale-render`
both already carried unmerged `specs/256-*` directories that the first check missed.
Found and renumbered to **257** on 2026-09-26, immediately before opening this PR,
which is exactly the re-check this note already told the next reader to do.

**ADRs referenced:**

- **ADR-0148** (two-layer tokens in OKLCH): the architecture this implements. Its
  Consequences section names this issue: *"the work is adding the ninety that were
  never there. Issue #2332 carries it."*
- **ADR-0146** (one discipline, two surfaces): the values: near-black ground kept,
  one cyan accent, 4-point rhythm, one radius scale, 120–200 ms motion, tabular
  figures, and the wall's prohibitions.
- **ADR-0147** (IBM Plex, self-hosted): the type scale is sized for it. **The font
  pipeline itself is #2333, not this spec** (§3).
- **ADR-0078** (Tailwind + CSS custom properties): the mechanism, unchanged.
- **ADR-0139 / ADR-0144** (red first; phase-4a colours). §6 picks **red**.
- **ADR-0036** (smallest change), **ADR-0037** (phases and gates), **ADR-0109**
  (`[P]` rule), **ADR-0052 / 0053** (xUnit + Shouldly; sentence-style names).

**No new ADR.** See §4. Every structural question the issue raises is answered by
ADR-0146/0147/0148. What this spec decides (ramp steps, pixel values, curve
coefficients, which Tailwind namespaces close) is implementation detail inside those
decisions, and each such choice is written down in plan.md §2 so it is not silent.

**Latency budget (§IV): no leg is spent; composite-and-render is touched and
covered.** See §5.

---

## 1. The issue's premise, re-checked on this tree (`0d349ce8`, 2026-09-25)

| Claim in #2332 | On this tree |
|---|---|
| `colors.css` is seven colour properties | **True.** Plus a `[data-theme='light']` block that redefines four of them in raw hex. |
| No type/spacing/radius/elevation/motion/z scale | **True.** Neither `tailwind.config.ts` extends anything but `colors`. |
| `rounded-md` and `rounded-lg` used interchangeably | **True.** 60 × `rounded-md`, 4 × `rounded-lg`, 2 × `rounded` across `apps/**/src`. |
| No shadow model | **True.** 2 × `shadow-xl` (Dialog, ConfirmDialog), 1 × `shadow-md` (Tooltip), all stock. |
| Only transition is stock `transition-colors`, plus one keyframe | **True.** `Button.tsx` and the kiosk's `ssE-overlay-highlight` pulse. |
| No font declared | **True.** ADR-0147's grep re-run: no `font-family`, `@font-face` or `fontFamily` under `apps/**/src`. |

Two further findings the issue does not mention, both material to scope:

1. **The programme has already split this work.** #2333 is the font pipeline
   (subset, `@font-face`, metric overrides, preload, the external-font-host
   architecture test). #2334 is the motion language (roles, springs,
   reduced-motion as a designed path). #2336 is every primitive's interaction-state
   matrix, starting with `Button.tsx`, and is gated on this issue. #2342 is the
   overlay editor's inline styles, also gated on this issue.
2. **Tailwind v4's theme-variable names coincide with ADR-0148's token names.**
   Tailwind 4.3.3 emits `border-radius: var(--radius-md)` for stock `rounded-md`,
   and `--text-sm`, `--ease-out`, `--font-sans`, `--tracking-wide` are its own
   theme variables. Measured against the installed 4.3.3 compiler (plan.md §4): the
   token file is imported **unlayered** before `tailwindcss`, and Tailwind's
   defaults sit in `@layer theme`, so the token wins every collision. That is the
   intended outcome, but it means some stock utilities pick up token values *without*
   the config mapping them. Tests must therefore assert things stock Tailwind cannot
   produce, or they will arrive green.

## 2. User stories

### US1 (P1): One token file carries every category, in two layers

An engineer styling either surface finds a value for colour, type, spacing, radius,
elevation, motion and z-order in one shared file. Colour is two layers: an OKLCH
primitive scale, and semantic roles that cite it. A theme remaps roles, never
primitives. The semantic triad keeps its names and its exact rendered values. There
is no blur token, because nothing on the wall may blur.

**Why P1:** everything else in the programme (#2333, #2334, #2336, #2342) cites
these names. It is also the only story that can be observed without the others: a
`var(--space-4)` resolves in the browser whether or not Tailwind knows about it.

**Acceptance scenarios**

```gherkin
Scenario: every category the issue lists has tokens
  Given the token file both apps import
  Then it declares tokens in each of: color, space, text, font, tracking,
       radius, shadow, duration, ease, z

Scenario: semantic colours cite the scale, never a literal
  Given any --color-* token in any block of the token file
  Then its value is var(<primitive or semantic token>) or
       color-mix(in oklch, <those> N%, <those | transparent>)
  And no hex, rgb(), hsl() or oklch() literal appears in it

Scenario: primitives are fixed across themes
  Given the [data-theme='light'] and [data-theme='high-contrast'] blocks
  Then neither declares a primitive (--gray-*, --cyan-*, --green-*, --red-*,
       --amber-*, --black, --white)
  And neither declares a name the :root block does not also declare

Scenario: the triad keeps its names and values (declared GREEN in advance)
  Given --color-accent-active, --color-accent-fault, --color-accent-warning
  When each resolves through the file to an sRGB colour
  Then they equal #00c853, #ff5252 and #ffab40 exactly (8-bit)

Scenario: the dark ground does not change colour (ADR-0146) (declared GREEN in advance)
  When --color-bg-base and --color-bg-elevated resolve in the default theme
  Then they equal #0b0d10 and #14171c exactly

Scenario: the wall's prohibition is an absence (declared GREEN in advance)
  Then no token name starts with --blur
  And no token value contains backdrop-filter or blur(

Scenario: both surfaces share one file (declared GREEN in advance)
  Given apps/management-web/src/styles/index.css and apps/kiosk-web/src/styles/index.css
  Then each one's first @import is the same file under apps/shared/src/ui/tokens/

Scenario: bad request, a component cites a primitive
  Given any .ts/.tsx/.css file under apps/*/src outside apps/shared/src/ui/tokens/,
        or either tailwind.config.ts, or the shared Tailwind theme module
  When it contains var(--gray-*) or any other primitive
  Then the architecture test fails, naming the file and the primitive
```

*Auth/conflict: N/A. No endpoint, no request, no concurrency. The "bad request"
analogue for a design system is a component bypassing the semantic layer, above.*

### US2 (P1): Both apps' Tailwind consumes the tokens, and closed scales close

A `rounded-md`, `text-sm`, `p-4`, `shadow-popover`, `duration-fast` or `z-overlay`
in either app compiles to the token, through one theme object both
`tailwind.config.ts` files import, so the two configs cannot drift. A value that is
not on a closed scale (`shadow-xl`, `rounded-3xl`, `text-7xl`, `font-bold`,
`ease-in` in its stock sense) stops generating. `transition-colors` takes its
duration and curve from the motion tokens.

**Why P1:** without it, the tokens exist and nothing reaches them except
hand-written `var()`. The issue's done-criterion names both configs.

**Acceptance scenarios** (compiled by the installed `@tailwindcss/postcss`, against
each app's real `src/styles/index.css`)

```gherkin
Scenario: spacing routes through the rhythm tokens
  When p-4 is compiled
  Then its padding is var(--space-4)          # stock: calc(var(--spacing) * 4)

Scenario: type carries its own leading
  When text-sm is compiled
  Then font-size is var(--text-sm) and line-height falls back to var(--text-sm-leading)

Scenario: role-named utilities exist
  When shadow-popover, shadow-overlay, duration-fast, ease-out, z-overlay,
       bg-bg-raised, bg-scrim, bg-accent, bg-accent-hover, border-border-subtle,
       text-fg-on-accent, ring-focus-ring are compiled
  Then each rule cites its matching token

Scenario: closed scales reject stock values
  When shadow-xl, shadow-md, rounded-3xl, text-7xl, font-bold are compiled
  Then no rule is emitted for any of them

Scenario: Tailwind's transition defaults are bound to motion tokens
  When the stylesheet is compiled
  Then an unlayered declaration sets --default-transition-duration to var(--duration-fast)
  And --default-transition-timing-function to var(--ease-out)

Scenario: bad request, a config maps a token locally
  Given either tailwind.config.ts
  When it contains a var(--...) literal, or does not import the shared theme
  Then the architecture test fails
```

### US3 (P2): No shared UI component hard-codes a value that should be a token

The issue's third done-criterion, scoped by the programme's own split: the shared
primitives and composites that are not owned by a later issue cite tokens for every
colour, elevation and layer. The floating primitives (Dialog, ConfirmDialog,
Tooltip) take ADR-0146's elevation model: raised tone, subtle border, a shadow
token, a scrim token, a z-layer. `CameraViewer`'s black behind live video becomes
the video-ground token, which is black.

**Why P2:** this is where the tokens become visible. It is separable, and it is the
story most likely to be argued with in review.

**Acceptance scenarios**

```gherkin
Scenario: no stock-palette colour in shared UI
  Given every .ts/.tsx under apps/shared/src/ui outside the carve-out list
  Then none uses a Tailwind stock-palette colour utility
       (bg-black, text-white, border-gray-700, ...)

Scenario: no call-site alpha on a semantic colour
  Then none uses a semantic colour with an opacity modifier (border-fg-muted/30)

Scenario: no colour literal in shared UI
  Then none contains a hex, rgb() or rgba() literal

Scenario: the carve-outs are named, reasoned and shrink-only
  Given the carve-out list in the test
  Then each entry names the issue that owns it (#2342)
  And each listed file still exists and still violates
      (so a converted file must leave the list)
```

## 3. Scope

### In this PR

| # | What | Why here |
|---|---|---|
| 1 | `apps/shared/src/ui/tokens/tokens.css` replacing `colors.css`: primitives (gray, cyan ramps; triad stops; black/white), semantic colour roles with derived state variants, type scale, spacing, radii, elevation, motion, z-layers; `light` and `high-contrast` themes | US1. ADR-0148's whole decision. |
| 2 | One shared Tailwind theme module; both `tailwind.config.ts` import it | US2. Removes the duplicated config. |
| 3 | Both apps' `index.css` import `tokens.css` | Rename is atomic with #1. |
| 4 | `font-variant-numeric: tabular-nums` applied globally from a type token | ADR-0147: "from the type tokens, not per call site". Works with the system face today and with Plex after #2333. |
| 5 | Dialog, ConfirmDialog, Tooltip, DataTable, CameraViewer migrated to semantic tokens | US3. The issue's third criterion for every file no later issue owns. |
| 6 | Two C# architecture tests, and one Tailwind build test per app | US1–US3 guards. |

### Deferred, and to where

| What | Owner | Reason |
|---|---|---|
| IBM Plex self-hosting: subset, `@font-face`, `size-adjust`/`ascent-override`/`descent-override`, preload, **the external-font-host architecture test** | **#2333** | A separate issue, Phase 01, "not `agent:ready` yet — gated on #2330". #2330 is now closed, so #2333 needs only its label. `--font-sans`/`--font-mono` ship here holding Tailwind's current system stacks, and #2333 prepends the face. **Naming "IBM Plex Sans" here without the `@font-face` would make any machine with Plex installed locally render it and every other machine not, which is the fleet disagreement ADR-0147 exists to end.** |
| Motion roles, springs, reduced-motion as a designed path, the wall's motion rule | **#2334** | This spec ships the category ADR-0148 names (`--duration-fast`, `--ease-out`) and nothing that designs reduced motion. Setting durations to `0ms` under `prefers-reduced-motion` is the "deletion" #2334 exists to replace. |
| `Button.tsx` states (hover, pressed, focus, disabled, loading), `Input.tsx`/`DataTable.tsx` focus rings, `@media (hover: hover)` | **#2336** | Phase 02, gated on this issue, names `Button.tsx`. It consumes `--color-accent-hover`/`-pressed`/`-disabled`, `--color-fg-disabled` and `--color-focus-ring`, which ship here unused. |
| **The triad used as brand/affordance colour**: Button `primary` is `bg-accent-active`; `Input`'s and `DataTable`'s focus rings are `ring-accent-active` | **#2336** | ADR-0146 says the triad is "not available as brand colour". These are the violations on this tree. They sit inside #2336's files and state matrix, and fixing them here would half-do its work. **Recorded so the handover is explicit, not so it is forgotten.** |
| `OverlayEditor.tsx`, `BackdropControls.tsx`, `OverlayGeometryFields.tsx`, `overlayLabelStyle.ts` inline styles | **#2342** | "gated on #2332". `BackdropControls.tsx:29-31` already says a half-converted file is worse than none. `overlayLabelStyle.ts` is the label's rendered look over live video on both surfaces (spec 146), so it is on the render leg. |
| Every other hard-coded utility in `apps/management-web` and `apps/kiosk-web` (~35 files use `border-fg-muted/30`-style call-site alpha, off-rhythm `p-1.5`, `bg-black`) | Phase 02 migration (no issue yet) | The issue's criterion is scoped to `apps/shared/src/ui`. A repo-wide restyle is not an independently reviewable slice. The inventory is in plan.md §6 so the follow-up does not re-measure. |
| Theme switching UI; the light theme as a *designed* peer | ADR-0146 console work (no issue yet) | Both `index.html` hard-code `data-theme="dark"`. Nothing sets `light` or `high-contrast`. The blocks ship here as remappings so they are correct when something does. |
| `forced-colors` handling | #2336 | Nothing in the token layer needs to change: under `forced-colors` the UA owns the palette. The failure is the focus ring. Tailwind's `ring-*` is a `box-shadow`, and forced-colors mode drops box-shadows, so a focus-visible ring vanishes. That belongs to #2336's focus-ring item. |

## 4. Why no new ADR

Checked against each ADR for silence on something structural:

- **Ramp values.** ADR-0148 gives three example primitives. ADR-0146 fixes the
  accent "in the region of `oklch(78% 0.09 210)`" and keeps `#0b0d10`/`#14171c`.
  Constructing the rest of a ramp consistent with those anchors is the ordinary
  implementation detail ADR-0148 anticipates ("the exact ramp ADR-0148 defines" is
  illustrated, not enumerated). **One small tension, resolved without an ADR:**
  ADR-0148's example `--gray-950: oklch(16% 0.012 250)` renders `#090e12`, not
  `#0b0d10`. ADR-0146 says explicitly that the existing base is **kept**, "so the wall
  does not change colour". The specific statement governs the illustrative example,
  and the ramp pins `--gray-950`/`--gray-900` to the exact OKLCH of the kept hex.
- **Token names that coincide with Tailwind's.** An implementation hazard, not an
  undecided architecture. ADR-0078's mechanism (custom properties, consumed via
  `tailwind.config.ts`) holds, and the collision resolves toward the token file by
  cascade layer order. This is measured and tested (US2).
- **Keeping `bg-*`/`fg-*` beside ADR-0148's `surface`/`text` examples.** ADR-0148
  settles it: "The seven existing properties keep their names where they still make
  sense." They still make sense. They are ~170 call sites across 35 files. New roles
  in the same families follow them (`--color-bg-raised`, `--color-fg-disabled`), so
  there are not two words for one category.
- **The font-host test mechanism.** Specified enough to build (ADR-0147 §5, modelled
  on `ContainerImagePinTests`). It is #2333's, not this spec's.
- **Conflicts between the three ADRs.** None found beyond the ground-colour example
  above.

**Two things surfaced for a human, neither blocking this spec:**

1. **The triad fails contrast as text on the light theme** (`#00c853` on the light
   ground: 2.08:1; `#ffab40`: 1.75:1). This predates this spec, because the current
   light block never redefined the triad. ADR-0148 pins the triad's values.
   Whether a *theme* may remap the triad to darker stops is a question
   ADR-0148 does not answer. It is unreachable today (nothing sets `data-theme='light'`)
   and should be settled before the light theme ships.
2. **#2333 is unblocked and unlabelled.** Its gate (#2330) closed on 2026-09-13.

## 5. Latency budget (§IV)

**The event-to-overlay path is not changed. The composite-and-render leg's inputs
are touched, and the CI gate that watches it (spec 225, #2337) covers the change.**

- The kiosk consumes the same token file, so wall tiles pick up new *static*
  values: `rounded-md` 6 → 4 px, `--color-fg-muted` `#7a8294` → `#818a98`, and
  `CameraViewer`'s `bg-black` → `bg-bg-video` (both black). None adds a compositing
  layer, an animation, a filter, a `box-shadow` or a `backdrop-filter` to a tile.
- **No wall prohibition is loosened.** No `--blur-*` token exists. The two shadow
  tokens are cited only by console-only floating primitives (Dialog, ConfirmDialog,
  Tooltip, none of which `apps/kiosk-web` imports). The overlay-highlight pulse is
  untouched.
- `overlayLabelStyle.ts` (the label drawn over video) is deliberately **not**
  touched (#2342).
- Phase 5 reads the render-leg check on the PR's CI run and cites its figure.

## 6. Phase-4a colour: **red**

This is new behaviour: tokens that do not exist, utilities stock Tailwind does not
generate, stock utilities that stop generating. The following are **declared green
in advance** as pins, and their green result is not phase-4a evidence:

- the triad resolves to its legacy sRGB (US1),
- the dark ground resolves to `#0b0d10`/`#14171c` (US1),
- both apps import the same token file first (US1),
- no `--blur-*` token and no blur in any value (US1, true today by absence),
- `rounded-md` compiles to `var(--radius-md)` (US2, true today by the name
  collision in §1),
- every declared custom property name is category-prefixed or a named bridge
  (US1, true today because `colors.css`'s seven existing names are all
  `--color-*` — vacuously pure, not evidence that the naming rule holds once
  primitives exist; found and reported during T005's red run),
- `ease-out` compiles to `var(--ease-out)` (US2, true today by the same
  Tailwind 4.3.3 stock-theme-variable collision as `rounded-md` — Tailwind's
  own default theme ships `--ease-out: cubic-bezier(0, 0, 0.2, 1)`, a
  different curve than ADR-0146's planned one under the same property name;
  found and reported during T005's red run).

Every other scenario above must be observed red on unmodified `develop`.

## 7. Independent end-to-end test procedure

1. `dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~DesignToken|FullyQualifiedName~SharedUiTokenUsage"`: green.
2. `pnpm --filter @smart-sentinel-eye/management-web test -- tokens.build` and the
   same for `kiosk-web`: green.
3. `pnpm build` for both apps. Open each `dist/assets/*.css` and confirm `.p-4`
   cites `var(--space-4)` and `.shadow-xl` is absent.
4. Boot the stack and open the console and a wall in a real browser (Playwright).
   On `<html>`, `getComputedStyle` gives `--color-bg-base` rendering `rgb(11, 13, 16)`
   (compare via a probe element's `background-color`). A status badge renders
   `rgb(0, 200, 83)`. A Dialog shows the raised tone, a 1 px subtle border and the
   overlay shadow. Set `data-theme='light'`, then `data-theme='high-contrast'`, on
   `<html>`: grounds and text remap, and the triad does not change.
5. Screenshot both surfaces before and after, at the same route. The expected
   visible differences are exactly those in plan.md §7.

## 8. Success criteria

- **SC-001** All ten token categories are present, and US1's structural rules hold
  (architecture test).
- **SC-002** Both apps compile every US2 scenario (build test, per app).
- **SC-003** `apps/shared/src/ui` has no stock-palette colour, call-site alpha or
  colour literal outside the named carve-outs (architecture test).
- **SC-004** Triad and dark ground unchanged to the 8-bit value (architecture test
  plus Phase 5 browser read).
- **SC-005** Render-leg CI check green on the PR, figure cited in the verification
  note.
