import { describe, expect, it } from 'vitest';
import { overlayLabelSurfaceStyle } from './overlayLabelStyle.js';

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
});
