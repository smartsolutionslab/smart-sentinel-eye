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
 * `fontSizePx` is the author-time reference size at a 1080p (1920-wide)
 * frame (spec 004). The type is sized proportionally to the label's own
 * container — `container-type: inline-size` established on `CameraViewer`'s
 * root and on the editor canvas — via `cqw`, not to the viewport: a label's
 * box is already sized relative to that same container
 * (`normalizedWidth × containerWidth`), so type and box now scale together
 * at every grid density (spec 294, issue #2353).
 *
 * Placement (`position`/`left`/`top`/`width`/`height`) and each surface's
 * own affordance (`pointerEvents` on the wall; `cursor`/`userSelect` on the
 * editor) are not part of this function — they have not diverged and each
 * has exactly one caller.
 *
 * Pure, allocation-only and DOM-free: called once per tile per wall render,
 * up to nine times on the 3×3 ceiling (ADR-0156), on the composite + render
 * leg (constitution §IV, ≤ 50 ms).
 */
export function overlayLabelSurfaceStyle(label: OverlayLabelAppearance): CSSProperties {
  const { fontSizePx } = label;

  return {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    background: 'var(--color-bg-label)',
    color: 'var(--color-fg-on-label)',
    fontSize: `max(${Math.min(12, fontSizePx / 4)}px, calc(${fontSizePx}cqw / 19.2))`,
    fontWeight: 'var(--font-weight-semibold)',
    padding: '0 var(--space-1)',
  };
}
