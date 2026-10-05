import { describe, expect, it } from 'vitest';
import { overlayLabelSurfaceStyle, overlayShapeStyle } from './overlayLabelStyle.js';

/**
 * Spec 146 (issue #2339). Unit coverage for the shared surface/type
 * formula, including the schema's two bounds (`overlays.schema.ts:6-13`).
 * Returns a plain object — no DOM, no `@vitest-environment` pragma needed.
 */
describe('overlayLabelSurfaceStyle', () => {
  // Spec 294 (issue #2353), plan.md §6 (1) — supersedes the three
  // "Reproduces the wall clamp formula …" cases (FR-001). The old formula
  // was `clamp()` against the viewport (`vw`); the new one is `max()`
  // against the label's own container (`cqw`, `container-type: inline-size`
  // on `CameraViewer`'s root / the editor canvas — CameraViewer.tsx:393,
  // OverlayEditor.tsx:612). RED against unmodified `develop`, which still
  // returns the `vw`-derived clamp string.

  it('Sizes the type proportionally to its container at the floor of the schema range', () => {
    // fontSizePx: 8 -> Math.min(12, 8/4) takes the f/4 branch (2), not the 12 floor.
    const style = overlayLabelSurfaceStyle({ fontSizePx: 8 });

    expect(style.fontSize).toBe('max(2px, calc(8cqw / 19.2))');
  });

  it('Sizes the type proportionally to its container at the ceiling of the schema range', () => {
    // fontSizePx: 256 -> Math.min(12, 256/4) takes the 12 floor.
    const style = overlayLabelSurfaceStyle({ fontSizePx: 256 });

    expect(style.fontSize).toBe('max(12px, calc(256cqw / 19.2))');
  });

  it('Sizes the type proportionally to its container for a typical authored size', () => {
    const style = overlayLabelSurfaceStyle({ fontSizePx: 48 });

    expect(style.fontSize).toBe('max(12px, calc(48cqw / 19.2))');
  });

  // Spec 300 (issue #2349), ADR-0165 §2, T019. New cases only — the three
  // above must not be edited (CLAUDE.md "Phase 4a has two colours": an
  // edited characterisation assertion is a block, not a fix).
  it('Paints the authored colour directly and the light ink for a dark surface (#D32F2FFF)', () => {
    const style = overlayLabelSurfaceStyle({ fontSizePx: 48, color: '#D32F2FFF' });

    expect(style.background).toBe('#D32F2FFF');
    expect(style.color).toBe('var(--color-overlay-ink-light)');
  });

  it('Paints the authored colour directly and the dark ink for a light surface (#FFEB3BFF)', () => {
    const style = overlayLabelSurfaceStyle({ fontSizePx: 48, color: '#FFEB3BFF' });

    expect(style.background).toBe('#FFEB3BFF');
    expect(style.color).toBe('var(--color-overlay-ink-dark)');
  });

  it("Returns today's exact object at the default colour (#FFFFFFD9) — deep-equal, no drift", () => {
    const atDefault = overlayLabelSurfaceStyle({ fontSizePx: 48, color: '#FFFFFFD9' });
    const absent = overlayLabelSurfaceStyle({ fontSizePx: 48 });

    expect(atDefault).toEqual({
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      background: 'var(--color-bg-label)',
      color: 'var(--color-fg-on-label)',
      fontSize: 'max(12px, calc(48cqw / 19.2))',
      fontWeight: 'var(--font-weight-semibold)',
      padding: '0 var(--space-1)',
    });
    // An absent colour (OverlayEditor's own byte-identical fixtures, FR-016)
    // renders identically to the explicit default.
    expect(absent).toEqual(atDefault);
  });

  it('Rejects white one step below its floor at the surface, too (#FFFFFF74 is not a valid Text colour — covered in textLegibility.test.ts); #FFFFFF75 paints the dark ink', () => {
    const style = overlayLabelSurfaceStyle({ fontSizePx: 48, color: '#FFFFFF75' });

    expect(style.color).toBe('var(--color-overlay-ink-dark)');
  });
});

describe('overlayShapeStyle (spec 300, #2349, ADR-0165 §2, T019)', () => {
  it('Draws a Box as a square-cornered stroke in the authored colour, any alpha', () => {
    const style = overlayShapeStyle({ kind: 'Box', color: '#D32F2F00' });

    expect(style.border).toBe('max(2px, 0.25cqw) solid #D32F2F00');
    expect(style.borderRadius).toBe(0);
    expect(style.background).toBe('transparent');
    expect(style.boxSizing).toBe('border-box');
  });

  it('Draws an Ellipse as a fully rounded stroke', () => {
    const style = overlayShapeStyle({ kind: 'Ellipse', color: '#FFA00080' });

    expect(style.border).toBe('max(2px, 0.25cqw) solid #FFA00080');
    expect(style.borderRadius).toBe('50%');
  });

  it('Carries no shadow, transition or animation (FR-013)', () => {
    const style = overlayShapeStyle({ kind: 'Box', color: '#D32F2FFF' }) as Record<string, unknown>;

    expect(style.boxShadow).toBeUndefined();
    expect(style.transition).toBeUndefined();
    expect(style.animation).toBeUndefined();
  });
});
