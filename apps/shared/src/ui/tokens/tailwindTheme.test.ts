import { describe, expect, it } from 'vitest';
import { tailwindTheme } from './tailwindTheme.js';

/**
 * Spec 299 (issue #2695) T021/US2. ADR-0146's 2026-10-04 amendment gives each
 * triad hue a per-theme TEXT role (`--color-accent-<role>-text`) beside the
 * existing signal role, and plan.md §3 spikes the Tailwind mechanism that
 * makes `text-accent-<role>` resolve to it while `bg-`/`border-accent-<role>`
 * keep resolving to the signal: a *second*, narrower theme-extension point,
 * `extend.textColor`, which Tailwind 4.3.3's `text-*` utility consults before
 * `extend.colors` (`valueThemeKeys: ['--text-color', '--color']`).
 *
 * This file pins that the two extension points stay distinct — `textColor`
 * citing the new `-text` vars, `colors` still citing the original signal vars
 * — so a future edit to one does not silently move the other. The compiled-
 * CSS diff (spec §7 step 2, phase 5) is the proof Tailwind actually honours
 * this split in the real build; this test only pins the object the two
 * `tailwind.config.ts` files import.
 *
 * Red on develop: `tailwindTheme.ts` has no `textColor` key at all yet.
 *
 * Spec 308 (issue #2709) adds two more `extend.textColor.accent` keys,
 * `warning-on-video` / `fault-on-video`, for text painted directly on the
 * theme-invariant `--color-bg-video` ground (spec 308 §2-3). The `toEqual`
 * below gains those two entries — the declared assertion edit spec 308's
 * tasks.md calls out; the three existing entries stay byte-identical.
 */
describe('tailwindTheme', () => {
  it('maps each triad role under extend.textColor.accent to its -text var', () => {
    expect(tailwindTheme.extend.textColor.accent).toEqual({
      active: 'var(--color-accent-active-text)',
      warning: 'var(--color-accent-warning-text)',
      fault: 'var(--color-accent-fault-text)',
      'warning-on-video': 'var(--color-accent-warning-on-video)',
      'fault-on-video': 'var(--color-accent-fault-on-video)',
    });
  });

  it('still maps extend.colors.accent.<role> to the signal var, not the text var', () => {
    expect(tailwindTheme.extend.colors.accent.active).toBe('var(--color-accent-active)');
    expect(tailwindTheme.extend.colors.accent.warning).toBe('var(--color-accent-warning)');
    expect(tailwindTheme.extend.colors.accent.fault).toBe('var(--color-accent-fault)');
  });

  it('maps each on-video role under extend.textColor.accent to its signal-tracking var', () => {
    // Cast rather than index the `as const` object directly: until the two
    // keys exist, TypeScript rejects the literal index at compile time
    // (TS7053) — a build error, not the missing-key runtime failure this red
    // test is supposed to show. The cast keeps the compile green on develop
    // and lets the assertion itself carry the signal (ADR-0139).
    const accentTextColors = tailwindTheme.extend.textColor.accent as Record<string, string>;
    expect(accentTextColors['warning-on-video']).toBe('var(--color-accent-warning-on-video)');
    expect(accentTextColors['fault-on-video']).toBe('var(--color-accent-fault-on-video)');
  });
});
