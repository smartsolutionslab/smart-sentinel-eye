import type { CSSProperties } from 'react';

/**
 * The subset of an overlay label needed to compute its surface and type
 * treatment. Both `CameraViewerOverlay` (`CameraViewer.tsx`) and
 * `OverlayLabel` (`overlays.api.ts`) satisfy this structurally — no import
 * of either is needed here, and none is added (spec 146).
 */
export interface OverlayLabelAppearance {
  fontSizePx: number;
}

/**
 * The wall's surface and type treatment for an overlay label — the eight
 * properties `CameraViewer.OverlayLabel` and `OverlayEditor` used to
 * hand-write separately and disagreed on four of (spec 146, issue #2339).
 *
 * The wall's values win here unchanged, including the `vw`-derived clamp:
 * it answers the wrong question (viewport width, not tile width — see
 * `specs/146-one-label-renderer/spec.md`), but changing it is a behaviour
 * change over published overlay revisions and is filed separately.
 *
 * Placement (`position`/`left`/`top`/`width`/`height`) and each surface's
 * own affordance (`pointerEvents` on the wall; `cursor`/`userSelect` on the
 * editor) are not part of this function — they have not diverged and each
 * has exactly one caller.
 *
 * Pure, allocation-only and DOM-free: called up to 250 times per wall
 * render on the composite + render leg (constitution §IV, ≤ 50 ms).
 */
export function overlayLabelSurfaceStyle(label: OverlayLabelAppearance): CSSProperties {
  const { fontSizePx } = label;

  return {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    background: 'rgba(255, 255, 255, 0.85)',
    color: '#111827',
    fontSize: `clamp(${Math.min(12, fontSizePx / 4)}px, ${fontSizePx / 16}vw, ${fontSizePx}px)`,
    fontWeight: 600,
    padding: '0 4px',
  };
}
