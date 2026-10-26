import type { CSSProperties } from 'react';
import { DEFAULT_OVERLAY_COLOR } from '../../api/overlays.schema.js';
import { inkFor } from './textLegibility.js';

/**
 * The subset of an overlay label needed to compute its surface and type
 * treatment. Both `CameraViewerOverlay` (`CameraViewer.tsx`) and
 * `OverlayLabel` (`overlays.api.ts`) satisfy this structurally — no import
 * of either is needed here, and none is added (spec 146).
 *
 * `color` is optional (spec 300, #2349, ADR-0165): absent is treated the
 * same as {@link DEFAULT_OVERLAY_COLOR}, which is what lets
 * `OverlayEditor.tsx`'s own guards — fixtures with no `color` at all —
 * keep rendering exactly as before (FR-016).
 */
export interface OverlayLabelAppearance {
  fontSizePx: number;
  color?: string;
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
 * `color` (spec 300, #2349, ADR-0165 §2). Every colour, the default included,
 * paints its authored eight-digit hex directly (valid CSS, no `color-mix`
 * needed) with the computed ink (`textLegibility.ts` `inkFor`) as the text
 * colour; absent is the default. The old `var(--color-bg-label)` branch is
 * gone (spec 463, ADR-0190 D5): the default is grey, and the wall must not
 * paint white while the element stores grey.
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
  const { fontSizePx, color = DEFAULT_OVERLAY_COLOR } = label;

  const base: CSSProperties = {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    fontSize: `max(${Math.min(12, fontSizePx / 4)}px, calc(${fontSizePx}cqw / 19.2))`,
    fontWeight: 'var(--font-weight-semibold)',
    padding: '0 var(--space-1)',
  };

  return {
    ...base,
    background: color,
    color: `var(--color-overlay-ink-${inkFor(color)})`,
  };
}

/** The subset of an overlay shape needed to compute its stroke (spec 300, #2349, ADR-0165). */
export interface OverlayShapeAppearance {
  kind: 'Box' | 'Ellipse';
  color: string;
}

/**
 * The wall's stroke treatment for a Box or Ellipse overlay element (spec
 * 300, #2349, ADR-0165 §2). Border-only, any alpha including fully
 * transparent — strokes carry no readability floor. Stroke width is in
 * `cqw`, so it scales with the tile, as `overlayLabelSurfaceStyle`'s font
 * size already does (spec 294). No shadow, transition or animation
 * (FR-013) — an over-budget leg (constitution §IV) does not get a new
 * per-paint cost.
 */
export function overlayShapeStyle({ kind, color }: OverlayShapeAppearance): CSSProperties {
  return {
    border: `max(2px, 0.25cqw) solid ${color}`,
    borderRadius: kind === 'Ellipse' ? '50%' : 0,
    background: 'transparent',
    boxSizing: 'border-box',
  };
}
