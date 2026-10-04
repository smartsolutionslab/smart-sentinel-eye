# ADR-0148: Two-layer tokens in OKLCH

**Status:** **Accepted** (amended 2026-10-04 — decision 2: derived colours mix
`in oklab`, not `in oklch`; its premise was wrong for this scale — see
[the amendment](#amendment-2026-10-04-derived-colours-mix-in-oklab))
**Date:** 2026-09-13
**Amends:** ADR-0078's token mechanism — the file's structure and colour space

**Supersedes:** —
**Superseded by:** —

## Context

ADR-0078 established design tokens as CSS custom properties consumed by
Tailwind. That decision stands. What it did not settle is how those properties
are organised, because at the time there were three of them.

`apps/shared/src/ui/tokens/colors.css` is now **seven properties in one flat
`:root` block**, with a `[data-theme='light']` override redefining four:

```css
:root {
  --color-bg-base: #0b0d10;
  --color-bg-elevated: #14171c;
  --color-fg-primary: #f5f7fa;
  --color-fg-muted: #7a8294;
  --color-accent-active: #00c853;
  --color-accent-fault: #ff5252;
  --color-accent-warning: #ffab40;
}
```

That works because there are seven. ADR-0146's design language takes it to
roughly a hundred — colour ramps, a type scale, spacing, radii, elevation,
motion, z-layers — and a flat namespace at that size stops being navigable and
starts colliding. The architecture is cheap to decide now and expensive to change
once every component cites the names.

Two specific problems the current shape cannot solve:

- **State variants are hand-picked.** There is no hover colour, no pressed
  colour, no disabled colour. `Button.tsx` fakes all three with
  `hover:opacity-90`, which dims the label along with the surface and reads
  identically on a primary and a destructive action.
- **A theme is a second palette.** `[data-theme='light']` redefines raw hex, so
  light and dark are two independently maintained sets of values with nothing
  keeping them in correspondence.

## Decision

**Two layers — primitive scale feeding semantic roles — expressed in OKLCH.
Components cite semantic tokens only.**

```css
/* Layer 1 — primitive. The raw scale. Fixed across themes. */
--gray-950: oklch(16% 0.012 250);
--gray-900: oklch(20% 0.012 250);
--cyan-500: oklch(78% 0.09 210);

/* Layer 2 — semantic. The role. Redefined per theme. */
--color-surface-sunken: var(--gray-950);
--color-surface-raised: var(--gray-900);
--color-accent: var(--cyan-500);
--color-accent-hover: color-mix(in oklch, var(--cyan-500) 88%, white);

/* Components cite layer 2, never layer 1. */
background: var(--color-accent);
```

Three things follow from that shape, and they are the reasons for it:

1. **A theme redefines the semantic layer only.** Primitives stay fixed. Light
   and dark become two mappings of one scale rather than two palettes, so they
   cannot drift apart silently.
2. **State variants are derived, not chosen.** `color-mix(in oklch, ...)` gives
   hover, pressed and disabled from the base role. OKLCH is perceptually uniform,
   so the same mix percentage produces the same perceived shift on any hue —
   which is exactly what makes deriving them legitimate rather than a shortcut.

   > **Amended 2026-10-04 (issue #2695, spec 299).** The premise above holds
   > only when the other operand has no hue, and no operand in this scale
   > qualifies. Derived colours now mix `in oklab`; the example block above
   > (`in oklch`) is superseded by it. The original text is kept as written.
3. **Ramps are even by construction.** Stepping lightness in OKLCH steps it
   perceptually. The same stepping in hex or HSL does not, which is why
   hand-built ramps always need eyeballing.

### Naming

`--<category>-<role>-<variant>`, semantic layer: `--color-surface-raised`,
`--color-text-muted`, `--color-accent-hover`, `--space-4`, `--text-sm`,
`--radius-md`, `--duration-fast`, `--ease-out`, `--z-popover`.

### One file, with the wall's prohibitions expressed as absence

Console and wall share the token file. ADR-0146's wall subtractions are not
tokens with different values — they are tokens the wall has no reason to cite.
There is no `--blur-*` token at all, because nothing on the wall may blur and the
console's use is narrow enough to be explicit at the call site. A prohibition
enforced by there being nothing to reach for is stronger than one enforced by
review.

### High-contrast stays a theme

ADR-0078's own example carries a `[data-theme='high-contrast']` block, and this
ADR does not drop it — under the two-layer split it becomes the clearest case
for the mechanism. A high-contrast theme is a third mapping of the same
primitive scale, not a third palette, and `forced-colors` is handled alongside
it. Saying so explicitly because a theme named in the ADR being amended, and
unmentioned in the amendment, is exactly how a record goes quietly wrong.

### The semantic triad keeps its names

`--color-accent-active`, `--color-accent-fault` and `--color-accent-warning`
remain, with their current values, as semantic-layer tokens. They are domain
vocabulary (ADR-0146) and renaming them would churn every status surface in both
apps for no gain.

> **2026-10-04:** ADR-0146's amendment of the same date adds a per-theme text
> role beside each (`--color-accent-<role>-text`); the three signal roles
> themselves are unchanged and still pinned in `:root`.

## Consequences

**ADR-0078's mechanism is unchanged.** Custom properties, consumed by
`tailwind.config.ts`. This amends how they are structured and written, not what
they are.

**Tailwind v4's `@theme` is deliberately not adopted.** Tailwind 4.3 can own
tokens directly and emit the custom properties itself, which would delete the
`tailwind.config.ts` colour block and the hand-written token file. It was
rejected because the wall's hand-written CSS — the overlay-highlight keyframes,
per-tile classes — consumes these tokens outside Tailwind's generation, and
because it would make the design system Tailwind's to name. The indirection
ADR-0078 chose is paying for itself; keep it.

**Rejected: a three-layer system** with a component layer
(`--button-primary-bg-hover`) above the semantic one. It is more robust, every
component surface becomes nameable and overridable in isolation, and it is what a
design system shipped to other teams should do. Here it would roughly triple the
token count and make a primitive rename touch three files, for two apps in one
monorepo that are always released together. Revisit if `apps/shared` is ever
consumed from outside this repository.

**A migration, not a rewrite.** The seven existing properties keep their names
where they still make sense; the work is adding the ninety that were never there.
Issue #2332 carries it.

## Amendment (2026-10-04): derived colours mix in OKLab

Issue #2695; implemented by spec 299. Decisions 1 and 3, the naming, the single
file, the high-contrast theme and every rejection above stand. Primitives stay
written as OKLCH literals — stepping lightness along a ramp (decision 3) is not
mixing, and is unaffected.

### What was wrong with decision 2

"OKLCH is perceptually uniform, so the same mix percentage produces the same
perceived shift on any hue" is true only when the other operand has **no hue**.
`color-mix(in oklch, …)` interpolates the hue angle linearly by the mix
percentage. CSS Color 4 treats a hue as missing — and so borrows the other
operand's — only when it is powerless *after conversion*; an `oklch()` literal
that writes a hue keeps it. In this scale nothing is hue-less:

- every ground carries a real hue (`--gray-950` is `oklch(15.82% 0.0072 258.4)`);
- `--black` and `--white` are `oklch(0% 0 0)` and `oklch(100% 0 0)`: hue `0`,
  written, so not missing.

So a mix toward white pulls cyan's 210° toward 360°, a mix toward black does the
same, and a mix into a ground takes most of its hue from the ground whatever the
ground's chroma. Observed in Chromium 1243 (2026-09-30): `color-mix(in oklch,
var(--red-500) 10%, var(--gray-950))` renders `rgb(20,24,37)`, slate; the green
equivalent toward `var(--black)` at 30% renders `rgb(48,14,0)`, brown-red. The
tokens this decision derived drifted the same way (computed with the CSS Color 4
interpolation rules from `tokens.css` as of `e892965b`):

| Role | Theme | Base hue | Rendered hue, `in oklch` | Rendered hue, `in oklab` |
|---|---|---|---|---|
| `--color-accent-hover` | dark / light / high-contrast | 210° | 228° / 228° / 228° | 210° / 210° / 210° |
| `--color-accent-pressed` | dark / light / high-contrast | 210° | 234° / 246° / 234° | 210° / 210° / 210° |
| `--color-accent-disabled` | dark / light / high-contrast | 210° | 239° / 240° / **300°** | 214.8° / 213.7° / 210° |
| `--color-accent-subtle` | dark / light / high-contrast | 210° | 250.7° / 254° / **336°** | 223.8° / 224.8° / 210° |
| `--color-accent-fault-hover` / `-pressed` | all | 24.66° | 21.7° / 22.7° | 24.66° / 24.66° |

High-contrast is the worst case and was not in the issue: its ground is
`--black`, so its disabled and subtle accents rendered purple and magenta.

The guards could not see any of this. `InteractionStateTests.ResolveOklch` and its
copy in `StatusTintTests` took the mixed hue from the higher-chroma operand — the
behaviour this decision *assumed* — so they computed the intended colour, not the
rendered one.

### Decision

**Every `color-mix()` in the token file mixes `in oklab`.** Lightness interpolates
exactly as it did; what changes is that hue and chroma interpolate on OKLab's
Cartesian `a`/`b` plane rather than around the hue circle. The corrected premise:

1. **A mix toward an achromatic operand preserves hue exactly.** `--black`,
   `--white` and `transparent` sit at `a = b = 0` whatever hue they were written
   with, so they shift lightness (or alpha) and nothing else. Hover, pressed and
   every mix toward black or white keep their base role's hue — this is the
   property decision 2 wanted, and OKLab is where it is actually true.
2. **A mix into a hued ground moves the hue in proportion to the ground's real
   chromatic contribution** — its chroma times its weight — not by the mix
   percentage. For the near-neutral grounds of this scale (chroma ≤ 0.012) that is
   the residual 4–15° in the table above, at chroma ≤ 0.04: ΔE_OK ≤ 0.005,
   below a visible difference. It is not an artefact; it is what those two colours
   mixed look like.
3. **Where a role must hold a hue exactly against a hued ground, it is a primitive
   stop, not a mix.** The status tints (`--color-accent-<role>-subtle`, spec 297)
   are the existing case, and stay stops: an OKLab mix of the fault red into
   `--gray-950` at 10% still lands at 7.7°, 17° short of the triad.

**One space for every mix, including toward `transparent`.** A mix toward
`transparent` renders identically in either space — the operand is premultiplied
to nothing — so keeping `in oklch` there would buy nothing and cost a second rule
the guards would have to know. `DesignTokenLayerTests` accepts `color-mix(in
oklab, …)` only.

### Consequences

- **Rendered colours change, slightly and in one direction:** every derived accent
  moves back to its own hue. Contrast is unaffected where it is guarded — the
  existing guard model already equalled OKLab for mixes toward black and white, so
  the fault-label ratios (6.10 / 7.04 / 4.93 : 1) are unchanged. The one thin
  margin is light-theme `--color-accent` on `--color-accent-subtle`: 4.549:1 under
  exact OKLab, against 4.565:1 under the old approximation.
- **The guards evaluate what the browser renders.** Spec 299 replaces the
  higher-chroma-hue approximation with exact OKLab interpolation, and adds a fact
  that a mix toward an achromatic operand keeps its base hue.
- **Tailwind already agrees.** Its own alpha modifiers (`text-x/50`) compile to
  `color-mix(in oklab, …)`.
