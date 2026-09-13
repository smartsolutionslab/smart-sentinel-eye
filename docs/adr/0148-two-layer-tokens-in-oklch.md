# ADR-0148: Two-layer tokens in OKLCH

**Status:** **Accepted**
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
