import { describe, expect, it } from 'vitest';
import { DEFAULT_OVERLAY_COLOR } from '../../api/overlays.schema.js';
import { overlayLabelSurfaceStyle } from './overlayLabelStyle.js';

/**
 * Spec 463 (#3108), ADR-0190 D5, T007. The default colour is painted as its hex, like every other colour:
 * the `var(--color-bg-label)` branch (white at 85 %) is gone, so the wall cannot paint white while the
 * element stores grey. A file of its own so the pre-existing default-token expectations are not edited.
 */
describe('overlayLabelSurfaceStyle at the default colour (spec 463)', () => {
  it('paints the default as its own hex with the dark ink, not the label token', () => {
    const style = overlayLabelSurfaceStyle({ fontSizePx: 48, color: DEFAULT_OVERLAY_COLOR });

    expect(style.background).toBe('#9F9F9FD9');
    expect(style.color).toBe('var(--color-overlay-ink-dark)');
  });

  it('paints an absent colour exactly as the explicit default', () => {
    expect(overlayLabelSurfaceStyle({ fontSizePx: 48 })).toEqual(
      overlayLabelSurfaceStyle({ fontSizePx: 48, color: DEFAULT_OVERLAY_COLOR }),
    );
    expect(overlayLabelSurfaceStyle({ fontSizePx: 48 }).background).toBe('#9F9F9FD9');
  });
});
