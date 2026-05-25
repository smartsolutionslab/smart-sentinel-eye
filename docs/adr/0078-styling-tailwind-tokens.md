# ADR-0078: Styling — Tailwind CSS with CSS-Custom-Property Tokens

**Status:** Accepted
**Date:** 2026-05-25

## Context

The custom design system (ADR-0077) needs a styling layer that
expresses design tokens centrally and applies them via utility
classes. We also need theme switches (dark, high-contrast control-
room) that don't require recompiling components.

## Decision

**Tailwind CSS** with design tokens defined as **CSS custom
properties**.

```css
/* apps/shared/ui/tokens/colors.css */
:root {
    --color-bg-base: #0b0d10;
    --color-bg-elevated: #14171c;
    --color-fg-primary: #f5f7fa;
    --color-fg-muted: #7a8294;
    --color-accent-active: #00c853;
    --color-accent-fault: #ff5252;
    --color-accent-warning: #ffab40;
}

[data-theme="high-contrast"] {
    --color-bg-base: #000;
    --color-fg-primary: #fff;
    --color-accent-fault: #ff0000;
}
```

```typescript
// tailwind.config.ts
export default {
  theme: {
    extend: {
      colors: {
        bg: {
          base: 'var(--color-bg-base)',
          elevated: 'var(--color-bg-elevated)',
        },
        accent: {
          active: 'var(--color-accent-active)',
          fault: 'var(--color-accent-fault)',
        },
      },
    },
  },
};
```

- Tokens cover colors, spacing, typography (font family, size, line
  height), border radius, shadows, motion durations.
- Theme switches happen via `data-theme="..."` on `<html>` — no
  rebuild, no flash.
- Components use semantic class names (`bg-bg-base text-fg-primary`),
  not raw color values.

## Consequences

- **Positive:** single source of truth for the design language.
- **Positive:** smallest production CSS via Tailwind's JIT.
- **Positive:** theme switches are CSS-only, instant.
- **Negative:** developers must remember to use token-referenced
  classes, not hard-coded colors. Linting can catch.

## Alternatives Considered

- **CSS Modules** — component-scoped CSS; more boilerplate.
- **vanilla-extract** — type-safe CSS-in-TS; smaller ecosystem.
- **Styled-components** — runtime CSS-in-JS; performance cost.
