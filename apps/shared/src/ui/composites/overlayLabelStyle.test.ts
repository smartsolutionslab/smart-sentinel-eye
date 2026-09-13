import { describe, expect, it } from 'vitest';
import { overlayLabelSurfaceStyle } from './overlayLabelStyle.js';

/**
 * Spec 146 (issue #2339). Unit coverage for the shared surface/type
 * formula, including the schema's two bounds (`overlays.schema.ts:6-13`).
 * Returns a plain object — no DOM, no `@vitest-environment` pragma needed.
 */
describe('overlayLabelSurfaceStyle', () => {
  it('Reproduces the wall clamp formula at the floor of the schema range', () => {
    // fontSizePx: 8 -> Math.min(12, 8/4) takes the f/4 branch (2), not the 12 floor.
    const style = overlayLabelSurfaceStyle({ fontSizePx: 8 });

    expect(style.fontSize).toBe('clamp(2px, 0.5vw, 8px)');
  });

  it('Reproduces the wall clamp formula at the ceiling of the schema range', () => {
    // fontSizePx: 256 -> Math.min(12, 256/4) takes the 12 floor.
    const style = overlayLabelSurfaceStyle({ fontSizePx: 256 });

    expect(style.fontSize).toBe('clamp(12px, 16vw, 256px)');
  });

  it('Reproduces the wall clamp formula for a typical authored size', () => {
    const style = overlayLabelSurfaceStyle({ fontSizePx: 48 });

    expect(style.fontSize).toBe('clamp(12px, 3vw, 48px)');
  });

  it('Returns the wall surface and ink treatment unconditionally', () => {
    const style = overlayLabelSurfaceStyle({ fontSizePx: 48 });

    expect(style.display).toBe('flex');
    expect(style.alignItems).toBe('center');
    expect(style.justifyContent).toBe('center');
    expect(style.background).toBe('rgba(255, 255, 255, 0.85)');
    expect(style.color).toBe('#111827');
    expect(style.fontWeight).toBe(600);
    expect(style.padding).toBe('0 4px');
  });
});
