# Plan 256: The scales nobody picked

**Spec**: [spec.md](spec.md) · **Issue**: #2332 · **Phase**: 2 (Plan)

## Constitution / ADR check

| Rule | How this plan meets it |
|---|---|
| §I, no outbound dependency | Nothing is fetched. The font stacks are local system faces until #2333 self-hosts Plex. |
| §II, value objects | N/A. No C# domain code. The two C# files are tests. Frontend props are not changed. |
| §III, context isolation | N/A. No bounded context is touched. `apps/shared` is already the one place both apps import from. |
| §IV, latency | Composite-and-render inputs change as static values only. See spec §5. The spec-225 render-leg check is the gate. |
| §Testing / ADR-0139 | Red first, with pins declared green in advance (spec §6). |
| ADR-0078 | Mechanism unchanged: custom properties, consumed by `tailwind.config.ts`. |
| ADR-0146 | Values from §2 below. Wall prohibitions: no blur token; shadows cited only by console-only primitives; pulse untouched. |
| ADR-0147 | Type scale sized for Plex. Tabular figures from a type token, applied once. The face itself is #2333. |
| ADR-0148 | Two layers, OKLCH, components cite semantic only, themes remap semantic only, derived states via `color-mix(in oklch, …)`, one file, no `--blur-*`, triad names and values kept, no `@theme`, no component layer. |

## Bounded context and layers

None. This is frontend design-system infrastructure:

```
apps/shared/src/ui/tokens/tokens.css          NEW (replaces colors.css, deleted)
apps/shared/src/ui/tokens/tailwindTheme.ts    NEW: the one theme object
apps/management-web/tailwind.config.ts        theme: tailwindTheme
apps/kiosk-web/tailwind.config.ts             theme: tailwindTheme
apps/management-web/src/styles/index.css      @import …/tokens.css
apps/kiosk-web/src/styles/index.css           @import …/tokens.css  (pulse block untouched)
apps/shared/src/ui/primitives/{Dialog,ConfirmDialog,Tooltip}.tsx      US3
apps/shared/src/ui/composites/{DataTable,CameraViewer}.tsx            US3
tests/Architecture.Tests/DesignTokenLayerTests.cs                     US1 + US2 static
tests/Architecture.Tests/SharedUiTokenUsageTests.cs                   US3
apps/management-web/src/styles/tokens.build.test.ts                   US2 compiled
apps/kiosk-web/src/styles/tokens.build.test.ts                        US2 compiled
```

No messaging, entities or contracts.

---

## 1. Naming, and the rules the tests enforce

- **Categories** (ADR-0148 `--<category>-<role>-<variant>`): `color`, `space`,
  `text`, `font`, `tracking`, `radius`, `shadow`, `duration`, `ease`, `z`.
- **Primitives**: `--<hue>-<step>` or `--black` / `--white`, and nothing else. In
  regex form: `^--(?!(color|space|text|font|tracking|radius|shadow|duration|ease|z)-)[a-z]+(-\d+)?$`.
  They are declared **only** in `:root`, and only as `oklch(…)` literals.
- **Semantic colour** (`--color-*`): the value is `var(--x)` or
  `color-mix(in oklch, var(--x) N%, var(--y) | transparent)`, where `--x`/`--y` are
  primitives or other `--color-*`. No literal.
- **Non-colour categories are single-layer.** `--space-4: 1rem` is itself the
  semantic token, because spacing does not vary by theme. ADR-0148's own naming
  example lists `--space-4` and `--radius-md` beside the semantic colours.
  `--shadow-*` values contain lengths but take their colour from primitives via
  `color-mix`.
- **Bridges**: exactly `--default-transition-duration` and
  `--default-transition-timing-function`. These are Tailwind's names, bound to
  motion tokens (§4.3). They are neither tokens nor primitives, and the test lists
  them by name.
- **Kept names**: `--color-bg-base`, `--color-bg-elevated`, `--color-fg-primary`,
  `--color-fg-muted` and the triad. New roles in those families follow them
  (`bg-raised`, `bg-video`, `fg-disabled`, `fg-on-accent`). Wholly new families use
  ADR-0148's words (`border`, `accent`, `focus`, `scrim`).
- **`-pressed`, never `-active`, for the press state.** `--color-accent-active` is
  the triad's green ("go"). An interaction variant named `-active` would read as a
  status.
- **Derived tokens cite the role, not the primitive**
  (`color-mix(in oklch, var(--color-accent) 88%, var(--white))`), so a theme that
  remaps `--color-accent` gets its hover for free. This relies on `data-theme` sitting
  on `<html>`, the same element as `:root`, which is where both `index.html` put it
  today. A theme applied to a subtree would inherit already-computed derived values.
  The file's header comment says so.

## 2. Values (implementation detail inside ADR-0146/0148, written down)

### 2.1 Primitives

Gray: hue ≈ 260, low chroma. **950 and 900 are pinned** to the exact OKLCH of the
kept `#0b0d10`/`#14171c` (ADR-0146). 50 lands on the old dark `fg-primary`
`#f5f7fa` by construction. Lightness is finer at the dark end, where the tonal
elevation steps live.

| Token | Value | sRGB |
|---|---|---|
| `--gray-50` | `oklch(97.5% 0.005 260)` | `#f5f7fa` |
| `--gray-100` | `oklch(94% 0.008 260)` | `#e8ebf1` |
| `--gray-200` | `oklch(88% 0.012 260)` | `#d3d8e0` |
| `--gray-300` | `oklch(81% 0.016 260)` | `#bbc1cc` |
| `--gray-400` | `oklch(72% 0.02 260)` | `#9da5b1` |
| `--gray-500` | `oklch(63% 0.024 262)` | `#818a98` |
| `--gray-600` | `oklch(54% 0.022 260)` | `#676f7c` |
| `--gray-700` | `oklch(41% 0.018 260)` | `#454b54` |
| `--gray-800` | `oklch(29% 0.014 260)` | `#272c32` |
| `--gray-850` | `oklch(24.5% 0.012 260)` | `#1d2126` |
| `--gray-900` | `oklch(20.37% 0.011 260.7)` | `#14171c` (pinned) |
| `--gray-950` | `oklch(15.82% 0.0072 258.4)` | `#0b0d10` (pinned) |

Cyan: hue 210, **500 pinned** to ADR-0146's `oklch(78% 0.09 210)`. 700–950 are
chroma-limited to stay in sRGB gamut (checked: max in-gamut chroma at L50 is 0.087).

| Token | Value | sRGB |
|---|---|---|
| `--cyan-50` | `oklch(97% 0.02 210)` | `#e7f9fd` |
| `--cyan-100` | `oklch(93% 0.04 210)` | `#caf0f7` |
| `--cyan-200` | `oklch(88% 0.065 210)` | `#a5e4ef` |
| `--cyan-300` | `oklch(84% 0.08 210)` | `#8ad9e7` |
| `--cyan-400` | `oklch(81% 0.088 210)` | `#78d1e0` |
| `--cyan-500` | `oklch(78% 0.09 210)` | `#6cc7d7` (pinned) |
| `--cyan-600` | `oklch(66% 0.095 210)` | `#3ca2b2` |
| `--cyan-700` | `oklch(50% 0.083 210)` | `#0f707d` |
| `--cyan-800` | `oklch(41% 0.068 210)` | `#09545f` |
| `--cyan-900` | `oklch(32% 0.053 210)` | `#043a42` |
| `--cyan-950` | `oklch(24% 0.04 210)` | `#02242a` |

Triad stops: one each, with no ramp. Three decimals are needed for the 8-bit round
trip; two give `#01c853`.

| Token | Value | sRGB |
|---|---|---|
| `--green-500` | `oklch(72.656% 0.20847 148.34)` | `#00c853` |
| `--red-500` | `oklch(67.864% 0.20948 24.66)` | `#ff5252` |
| `--amber-500` | `oklch(80.584% 0.15273 68.43)` | `#ffab40` |
| `--black` | `oklch(0% 0 0)` | `#000000` |
| `--white` | `oklch(100% 0 0)` | `#ffffff` |

### 2.2 Semantic colour roles per theme

| Role | Dark (`:root`) | Light | High-contrast |
|---|---|---|---|
| `--color-bg-base` | gray-950 | gray-50 | black |
| `--color-bg-elevated` | gray-900 | white | black |
| `--color-bg-raised` | gray-850 | white | gray-950 |
| `--color-bg-video` | black | black | black |
| `--color-scrim` | mix(black 60%, transparent) | mix(gray-950 40%, transparent) | mix(black 80%, transparent) |
| `--color-fg-primary` | gray-50 | gray-900 | white |
| `--color-fg-muted` | gray-500 | gray-600 | gray-200 |
| `--color-fg-disabled` | gray-600 | gray-500 | gray-400 |
| `--color-fg-on-accent` | gray-950 | white | black |
| `--color-border-subtle` | gray-800 | gray-200 | gray-500 |
| `--color-border-strong` | gray-600 | gray-500 | white |
| `--color-accent` | cyan-500 | cyan-700 | cyan-300 |
| `--color-accent-hover` | mix(accent 88%, white) | mix(accent 88%, black) | mix(accent 88%, white) |
| `--color-accent-pressed` | mix(accent 84%, black) | mix(accent 76%, black) | mix(accent 84%, black) |
| `--color-accent-disabled` | mix(accent 40%, bg-base) | ← | ← |
| `--color-accent-subtle` | mix(accent 16%, bg-base) | mix(accent 12%, bg-base) | ← |
| `--color-focus-ring` | cyan-400 | cyan-700 | cyan-200 |
| `--color-accent-active` | green-500 | ← | ← |
| `--color-accent-fault` | red-500 | ← | ← |
| `--color-accent-warning` | amber-500 | ← | ← |

"←" means the role is not redeclared in that theme block. `mix(a N%, b)` means
`color-mix(in oklch, var(a) N%, var(b))`. Hover and pressed in the dark theme move
in opposite directions (lighter, darker), which is #2336's requirement 1: "visibly
distinct from hover, not a deeper fade".

**Contrast, measured** (WCAG 2.x relative luminance, computed from the sRGB
column above with a throwaway OKLCH→sRGB script; derived states approximated by
interpolating L and C in OKLCH, which is what `color-mix(in oklch)` does):

| Check | Dark on base / elevated / raised | Light on gray-50 / white | HC on black |
|---|---|---|---|
| fg-primary (≥ 4.5) | 18.1 / 16.7 / 15.1 | 16.7 / 18.0 | 21.0 |
| fg-muted (≥ 4.5) | 5.58 / 5.15 / 4.64 | 4.72 / 5.07 | 14.7 |
| border-strong (≥ 3, WCAG 1.4.11) | 3.84 / 3.54 / 3.19 | 3.25 / 3.49 | 21.0 |
| accent (≥ 3 UI; ≥ 4.5 as text) | 10.0 / 9.23 / 8.31 | 5.33 / 5.72 | 13.2 |
| focus-ring (≥ 3) | 11.1 / 10.3 / 9.24 | 5.33 / 5.72 | 14.9 |
| fg-on-accent on accent / hover / pressed | 10.0 / 10.9 / 6.35 | 5.72 / 7.46 / 9.69 | 13.2 |

`#7a8294` (the old muted) scored 4.66 on elevated and would fail on the new raised
tone. That is why `fg-muted` moves to `#818a98`.

### 2.3 Type (sizes equal Tailwind's current stock, so nothing jumps before #2333)

| Token | Size | Leading token | Leading |
|---|---|---|---|
| `--text-xs` | 0.75rem | `--text-xs-leading` | 1rem |
| `--text-sm` | 0.875rem | `--text-sm-leading` | 1.25rem |
| `--text-base` | 1rem | `--text-base-leading` | 1.5rem |
| `--text-lg` | 1.125rem | `--text-lg-leading` | 1.75rem |
| `--text-xl` | 1.25rem | `--text-xl-leading` | 1.75rem |
| `--text-2xl` | 1.5rem | `--text-2xl-leading` | 2rem |
| `--text-3xl` | 1.875rem | `--text-3xl-leading` | 2.25rem |

Every leading is a multiple of 4 px (the rhythm). `2xl`/`3xl` default to
`--tracking-tight`.

- Weights: `--font-weight-regular: 400`, `--font-weight-medium: 500`,
  `--font-weight-semibold: 600`. `font-bold` is unused on this tree and closes. The
  static Plex cuts #2333 ships should be these three.
- Tracking: `--tracking-tight: -0.01em`, `--tracking-normal: 0em`,
  `--tracking-wide: 0.025em`.
- Families: `--font-sans` and `--font-mono` hold **Tailwind 4.3.3's current default
  stacks verbatim** (read from `node_modules/tailwindcss/theme.css`), so this spec
  changes no glyph. #2333 prepends `'IBM Plex Sans'`/`'IBM Plex Mono'`.
- Figures: `--font-numeric: tabular-nums`, applied once as
  `html { font-variant-numeric: var(--font-numeric); }` at the foot of `tokens.css`.
  It is the only rule in the file that is not a custom property, and it carries a
  comment saying why (ADR-0147: "from the type tokens, not per call site"). A call
  site that wants proportional figures uses `proportional-nums`.

### 2.4 Spacing: 4-point rhythm

`--space-0-5: 0.125rem` (2 px, the one sub-rhythm step, for optical alignment per
ADR-0146 §2), `--space-1: 0.25rem`, `-2: 0.5rem`, `-3: 0.75rem`, `-4: 1rem`,
`-5: 1.25rem`, `-6: 1.5rem`, `-8: 2rem`, `-10: 2.5rem`, `-12: 3rem`, `-16: 4rem`.

Tailwind keys `0.5, 1, 2, 3, 4, 5, 6, 8, 10, 12, 16` map to these by **extend**.
Every other numeric key (`p-7`, `w-40`, `p-1.5`) still resolves through Tailwind's
`--spacing: 0.25rem` multiplier, which is itself the 4-point rhythm. This was
measured, not assumed (§4). Sizes (`w-40`) and the off-rhythm `1.5` stay on the
multiplier, and the latter is in the §6 inventory.

### 2.5 Radius, one rule each

| Token | Value | Means |
|---|---|---|
| `--radius-sm` | 2px | inline chips, tags, checkboxes |
| `--radius-md` | 4px | controls: buttons, inputs, tooltips, table frames. Also `rounded` (DEFAULT) |
| `--radius-lg` | 8px | containers: dialogs, sheets, cards, panels |
| `--radius-full` | 9999px | pills, status dots |

`md` moves 6 → 4 px (Instrument: tighter, more precise). `lg` is unchanged.

### 2.6 Elevation: tonal steps first, shadow only for what floats (ADR-0146)

Ground `bg-base` → `bg-elevated` (panels, inputs, table headers) → `bg-raised`
(floating surfaces), plus a 1 px `border-subtle`. Shadows exist only for floating
surfaces:

- dark `--shadow-popover: 0 4px 12px 0 color-mix(in oklch, var(--black) 48%, transparent)`
- dark `--shadow-overlay: 0 16px 40px 0 color-mix(in oklch, var(--black) 64%, transparent)`
- light: same geometry, `var(--gray-950)` at 12% / 20%
- high-contrast: both `none` (borders carry separation)

### 2.7 Motion (the category only; the language is #2334)

`--duration-fast: 120ms` (state change), `--duration-moderate: 160ms` (surface
enter), `--duration-slow: 200ms` (surface leave, route). All three sit inside
ADR-0146's 120–200 ms.

Curves:

- `--ease-out: cubic-bezier(0.2, 0, 0, 1)` (entering, state change)
- `--ease-in: cubic-bezier(0.4, 0, 1, 1)` (leaving)
- `--ease-in-out: cubic-bezier(0.4, 0, 0.2, 1)`

There is no reduced-motion override, deliberately (spec §3).

### 2.8 Z-layers

`--z-sticky: 100`, `--z-overlay: 200` (scrim and dialog), `--z-popover: 300`
(menus, popovers), `--z-tooltip: 400`. Nothing in the tree sets a `z-index` today,
so nothing is reordered except by the US3 adoptions.

## 3. `tokens.css` layout

One file, in this order:

1. The header comment: ADR-0148, the layer rule, the `<html>` caveat from §1, and
   why there is no `--blur-*`.
2. `:root { … }` with the primitives (§2.1).
3. A second `:root { … }` with the semantic dark defaults (§2.2), the non-colour
   categories (§2.3–2.8) and the two bridges. Keeping two `:root` blocks makes the
   layer boundary visible to a reader. The test does not rely on the split; it
   classifies by name.
4. `[data-theme='light'] { … }`.
5. `[data-theme='high-contrast'] { … }`.
6. `html { font-variant-numeric: var(--font-numeric); }`.

**No `@layer`** anywhere in the file. It is imported *before* `@import 'tailwindcss'`,
and a layer name declared there first would reorder Tailwind's
`theme, base, components, utilities`. Unlayered is also what makes the token win
every name collision with Tailwind's `@layer theme` defaults (§4).

`colors.css` is deleted, and both `index.css` files change their first import in the
same commit.

## 4. Tailwind consumption: measured on the installed 4.3.3

The facts below came from compiling a probe config with the repo's installed
`@tailwindcss/postcss` 4.3.3 (scratch experiment, 2026-09-25). The plan relies on
each one, and the build test re-proves each.

| # | Observed | Consequence |
|---|---|---|
| F1 | A config value is emitted **verbatim**: `rounded-md` → `border-radius: var(--radius-md)`. No theme variable is generated for config values, so there is no self-referencing cycle. | Mapping `md: 'var(--radius-md)'` is safe. |
| F2 | A **non-`extend`** namespace replaces the stock one: `shadow-xl`, `rounded-3xl`, `text-7xl`, `font-bold` and `ease-in` (stock) emit nothing. | Closed scales close. |
| F3 | Bare numeric functional utilities survive any override: `duration-150`, `z-10`, `p-7`. | Numbers cannot be closed from config. Accepted. The scales close the *named* values. |
| F4 | `fontSize` tuples work: `text-sm` → `font-size: var(--text-sm); line-height: var(--tw-leading, var(--text-sm-leading))`, and a `letterSpacing` in the tuple is honoured. | Leading and tracking travel with size. |
| F5 | **A `DEFAULT` key in `transitionDuration` or `transitionTimingFunction` deletes Tailwind's `--default-transition-*` variables.** `transition-colors` then resolves `var(--default-transition-duration)` to nothing, and every transition silently becomes instant. Without `DEFAULT`, the stock `150ms` remains. | **No `DEFAULT` in either namespace.** `tokens.css` binds the two bridge variables to `--duration-fast`/`--ease-out`. |
| F6 | Tailwind emits its own defaults for names it shares with us (`--tracking-tight: -0.025em`, `--ease-out`, `--radius-md`) into `@layer theme` when a utility cites them. | Our unlayered declaration wins. The build test asserts the unlayered declarations are present in the compiled output. |
| F7 | `extend.spacing` keys win over the multiplier for those keys (`p-4` → `var(--space-4)`) and leave the rest on the multiplier. | §2.4. |
| F8 | A relative TypeScript import from `tailwind.config.ts` (`import { tailwindTheme } from './theme'`) resolves under the plugin's loader. | Both configs import the shared module relatively (below). |
| F9 | A probe sheet that is only `@import './index.css'` plus `@source inline("…")` compiles the inline candidates (`.p-4`, `.shadow-popover` emitted; `.shadow-xl` not). The imported file's unlayered `:root` declarations appear at the top of the output. | §5.3 can compile each app's real `index.css` unchanged. |

### 4.1 `apps/shared/src/ui/tokens/tailwindTheme.ts`

A plain `as const` object with no `tailwindcss` import, because `apps/shared` does
not depend on Tailwind. Each app's `Config` type-checks it at the assignment:

```ts
// Theme object for both tailwind.config.ts files (ADR-0078, ADR-0148). Cites
// semantic tokens only. Closed scales replace Tailwind's; colours and spacing extend.
export const tailwindTheme = {
  extend: {
    colors: {
      bg: { base, elevated, raised, video },                    // var(--color-bg-*)
      fg: { primary, muted, disabled, 'on-accent' },            // var(--color-fg-*)
      border: { subtle, strong },
      accent: { DEFAULT, hover, pressed, disabled, subtle, active, fault, warning },
      focus: { ring },
      scrim: 'var(--color-scrim)',
    },
    spacing: { '0.5', '1', '2', '3', '4', '5', '6', '8', '10', '12', '16' },  // var(--space-*)
  },
  fontFamily: { sans, mono },
  fontSize: { xs … 3xl: ['var(--text-x)', { lineHeight: 'var(--text-x-leading)' /* + letterSpacing on 2xl, 3xl */ }] },
  fontWeight: { normal: 'var(--font-weight-regular)', medium, semibold },
  letterSpacing: { tight, normal, wide },
  borderRadius: { DEFAULT: 'var(--radius-md)', sm, md, lg, full },
  boxShadow: { none: 'none', popover, overlay },
  transitionDuration: { fast, moderate, slow },                 // NO DEFAULT (F5)
  transitionTimingFunction: { out, in, 'in-out' },              // NO DEFAULT (F5)
  zIndex: { sticky, overlay, popover, tooltip },
} as const;
```

(Shorthand above. The file spells every value as `'var(--…)'`.) Both configs
become:

```ts
import type { Config } from 'tailwindcss';
import { tailwindTheme } from '../shared/src/ui/tokens/tailwindTheme';

// Tokens are CSS custom properties in apps/shared/src/ui/tokens/tokens.css (ADR-0078, ADR-0148).
const config: Config = {
  content: ['./index.html', './src/**/*.{ts,tsx}', '../shared/src/**/*.{ts,tsx}'],
  theme: tailwindTheme,
  plugins: [],
};
export default config;
```

The relative path mirrors the `content` glob, which already reaches into
`../shared/src`. If `Config['theme']` rejects the `as const` tuple types, widen at
the export with a `satisfies`-free annotation in the config. That is a typing
adjustment, not a design change.

## 5. Tests

### 5.1 `tests/Architecture.Tests/DesignTokenLayerTests.cs` (US1, US2 static)

This follows `ContainerImagePinTests`: it reads the tree from disk, uses a private
`RepositoryRoot()`, regex over comment-stripped text, **bans categories and never
values**, and gives failure messages that say what to do. The token file is found
**by following each app's first `@import`**, not by a hard-coded name, so on
`develop` the tests read `colors.css` and fail on *content*. A red from a missing
file would be the weak red spec §6 forbids.

Facts:

1. `Both_surfaces_import_the_same_token_file_first` (**green pin**).
2. `The_token_file_declares_every_category`: each of the 10 prefixes has at least
   one name in `:root`. Red on develop: only `color`.
3. `Every_name_is_a_token_a_primitive_or_a_named_bridge`.
4. `Primitives_are_oklch_literals_declared_only_in_root`.
5. `Semantic_colours_cite_the_scale_never_a_literal`. Red on develop: raw hex.
6. `A_theme_redeclares_only_semantic_names_root_already_has`. Red on develop: the
   light block redefines hex values. This fact is about *values*, so it is also
   caught by 5. It names both themes and fails if `high-contrast` is absent.
7. `The_triad_keeps_its_rendered_values` (**green pin**). The test resolves the
   `var()` chain to a literal and converts OKLCH or hex to 8-bit sRGB, using a
   private helper of about 20 lines (OKLab matrices). It compares against
   `#00c853`/`#ff5252`/`#ffab40`. Those constants are ADR-0146's, not the file's, so
   the subject can change without the assertion changing.
8. `The_dark_ground_keeps_its_rendered_values`: same resolver, `#0b0d10`/`#14171c`.
   Green on develop too, so declare it as a pin alongside 7.
9. `No_blur_token_exists` (**green pin**).
10. `Nothing_outside_the_token_file_cites_a_primitive`: scans `apps/*/src/**/*.{ts,tsx,css}`,
    both configs and `tailwindTheme.ts`, for `var(--<primitive>)`, with primitive
    names taken from the parsed file. Vacuous on develop (no primitives exist). Its
    counterfactual is constructed in 4a (a temporary `var(--gray-900)` in a scratch
    copy) and the output quoted, per "prove a guard by counterfactual".
11. `Both_configs_import_the_shared_theme_and_map_nothing_locally`: each config has
    an import ending `shared/src/ui/tokens/tailwindTheme` and contains no `var(--`.
    Red on develop.
12. `Every_token_the_theme_cites_is_declared_and_semantic`. Red on develop: the file
    does not exist. **This is the one fact whose red is a missing file.** It is
    acceptable because 11 carries the discriminating red for the same story.

### 5.2 `tests/Architecture.Tests/SharedUiTokenUsageTests.cs` (US3)

Scans `apps/shared/src/ui/**/*.{ts,tsx}`, excluding `*.test.*` and `tokens/`.
Comments are stripped first, because `#2342` in a comment looks like a hex colour.
The rules apply **only inside string literals** (single, double, template).

- `No_stock_palette_colour_utility`:
  `\b(bg|text|border|ring|fill|stroke|outline|divide)-(black|white|slate|gray|zinc|neutral|stone|red|orange|amber|yellow|lime|green|emerald|teal|cyan|sky|blue|indigo|violet|purple|fuchsia|pink|rose)\b`
- `No_call_site_alpha_on_a_semantic_colour`:
  `\b(bg|text|border|ring|fill|stroke|outline|divide)-(bg|fg|accent|border|focus)-[a-z-]*/\d+`
- `No_colour_literal`: `#[0-9a-fA-F]{3,8}\b`, `rgba?\(`, `hsla?\(`, `oklch\(`
- `Each_carve_out_still_exists_and_still_violates`: the carve-out list is
  `OverlayEditor.tsx`, `BackdropControls.tsx`, `OverlayGeometryFields.tsx` and
  `overlayLabelStyle.ts`, each tagged `#2342`. When #2342 converts a file, this fact
  forces it off the list.

Measured on develop with an approximation of these rules. Violations outside the
carve-outs: `CameraViewer.tsx` (2 palette), `DataTable.tsx` (2 alpha),
`Dialog.tsx` (1), `ConfirmDialog.tsx` (1), `Tooltip.tsx` (1 alpha). That list is
exactly US3's migration set, so the three rule facts are red on develop.

### 5.3 `apps/{management-web,kiosk-web}/src/styles/tokens.build.test.ts` (US2 compiled)

Put `// @vitest-environment node` at the top (the apps default to jsdom). The test
compiles, with the app's own `postcss` + `@tailwindcss/postcss`, a virtual sheet
at `src/styles/__probe__.css`:

```css
@import './index.css';
@source inline("p-4 p-0.5 text-sm text-2xl rounded-md shadow-popover shadow-overlay shadow-xl shadow-md rounded-3xl text-7xl font-bold duration-fast ease-out z-overlay bg-bg-raised bg-scrim bg-accent bg-accent-hover border-border-subtle text-fg-on-accent ring-focus-ring");
```

It asserts on the output CSS, per rule, with a small helper that extracts one
rule's body by selector:

- **Red on develop:** `.p-4` → `var(--space-4)`, `.text-sm` line-height
  `var(--text-sm-leading)`, `.text-2xl` letter-spacing `var(--tracking-tight)`, the
  role utilities exist, `.shadow-xl`/`.shadow-md`/`.rounded-3xl`/`.text-7xl`/`.font-bold`
  absent, and an unlayered `--default-transition-duration: var(--duration-fast)`
  present.
- **Green pin:** `.rounded-md` → `var(--radius-md)` (F6, the name collision).

The two files are identical except for their doc comment. They are one per app
because each app has its own config and `index.css`, and a shared config does not
prove each app wires it.

## 6. Inventory for the Phase-02 migration (not done here)

Measured 2026-09-25, `apps/*/src`, non-test:

- **Call-site alpha on semantic colours:** 24 × `border-fg-muted/30`, 5 × `/40`,
  1 × `/20`, `bg-fg-muted/10` ×3, `/20` ×2, `/15` ×1, `bg-bg-elevated/60`,
  `/20`. These map to `border-border-subtle` / `bg-accent-subtle` and friends.
- `text-bg-base` ×7 (text on a filled chip) maps to `text-fg-on-accent`.
- Off-rhythm `*-1.5` ×1. Sizes `*-16`/`*-20`/`*-40` are dimensions, not rhythm.
- Triad as affordance: Button `primary`; `ring-accent-active` in `Input`,
  `DataTable`, and wherever else a focus ring cites it. These go to #2336.
- `backdrop-blur-sm` stays at the two console-only dialog call sites, which ADR-0148
  explicitly allows ("explicit at the call site").

## 7. Expected visible change (the Phase-5 screenshot checklist)

- Unchanged exactly: dark `bg-base`, `bg-elevated`, `fg-primary`; the triad;
  video ground; the kiosk pulse; every font; every type size.
- `rounded-md` corners are 6 → 4 px (about 60 sites).
- `fg-muted` is `#7a8294` → `#818a98`.
- `text-2xl`/`3xl` get −0.01em tracking.
- Digits are tabular everywhere.
- `transition-colors` goes 150 ms `(0.4,0,0.2,1)` → 120 ms `(0.2,0,0,1)`.
- Dialog and ConfirmDialog use the raised tone `#1d2126`, a subtle border in place of
  the muted one, the overlay shadow, the scrim, and `z-overlay`.
- Tooltip uses the raised tone, a subtle border, the popover shadow, and `z-tooltip`.
- DataTable borders become `border-subtle` (`#272c32` in place of fg-muted at 30%/20%).
- Light theme (devtools only): base is `#f7f8fa` → `#f5f7fa`, muted is `#5a626d` →
  `#676f7c`.

## 8. Commit sequence (each commit builds on its own, ADR-0087)

1. `docs(256): specify, plan and task the two-layer token system` (this phase).
2. `test(tokens): pin the token layers, the shared theme and the shared UI's token use red-first`.
   The 4a tests are committed red (the build compiles, and only the declared facts fail).
3. `feat(tokens): two-layer OKLCH token file consumed by both apps through one theme`.
   This commit holds `tokens.css`, deletes `colors.css`, and changes `tailwindTheme.ts`,
   both configs and both `index.css`. It is atomic because the rename and the imports
   cannot be split.
4. `refactor(ui): cite semantic tokens in the shared floating primitives, table and viewer`.
   US3.

## 9. Verification (Phase 5)

Spec §7, steps 1–5, plus the render-leg check's figure from the PR's CI run.

## 10. Risks

- **R1: the probe relies on content auto-detection** from `base`. F9 passed with
  `base` set to the sheet's directory. The test must pass `base: <app root>` so the
  config's relative `content` globs resolve as they do under Vite.
- **R2: `Config['theme']` typing of the `as const` object** (§4.1). This is a type-only
  adjustment.
- **R3: Something external scrapes `colors.css`.** Checked: only the two `index.css`
  import it. The package's `./ui/tokens/*` export wildcard covers `tokens.css`
  unchanged.
- **R4: A reviewer reads the kept `bg`/`fg` families as ignoring ADR-0148's examples.**
  Answered in spec §4 and in the file header.
