import type { CSSProperties } from 'react';
import type { Backdrop } from '../BackdropControls';

// FR-002. Two rings drawn with `outline` (flush against the element, paints
// on top of `boxShadow` per CSS paint order — the white inner ring) and
// `boxShadow` (a wider solid extension — the black outer ring underneath
// it). Never `border` — `OverlayLabelParity.test.tsx` pins the label's
// border empty against the wall in every state.
//
// Both rings are drawn *inward* — `outlineOffset: -2` and an `inset`
// `boxShadow` — because the canvas is `overflow: hidden` (phase 6 should-fix
// 3): the keyboard path's own bounds let the label sit flush against every
// edge of the canvas, exactly where an outward ring would be clipped by the
// overflow it is meant to be visible against.
export const FOCUS_RING_STYLE: CSSProperties = {
  outline: '2px solid var(--color-bg-video-inverse)',
  outlineOffset: -2,
  boxShadow: 'inset 0 0 0 4px var(--color-bg-video)',
};

// FR-002. Pinned by `OverlayEditorCharacterisation.test.tsx` (T001) — a
// mangled rebase against #2354 fails that test loudly. Spec 293 §6: the two
// colours now cite the semantic token layer (`--color-border-subtle` /
// `--color-bg-elevated`), so the checkerboard themes with `data-theme`; the
// angle and stop geometry stay inline (spec 293 §3 — pattern geometry, not a
// design value).
const CHECKERBOARD_BACKGROUND =
  'repeating-linear-gradient(45deg, var(--color-border-subtle), var(--color-border-subtle) 12px, ' +
  'var(--color-bg-elevated) 12px, var(--color-bg-elevated) 24px)';

/**
 * The canvas backdrop as a function of the operator's choice (spec 147
 * FR-001–FR-004, FR-019). `captured` without a frame yet falls back to the
 * checkerboard — the radio for it is disabled in that state, so this is a
 * defensive default rather than a reachable path.
 */
export function canvasBackgroundStyle(backdrop: Backdrop, capturedFrame: string | null): CSSProperties {
  if (backdrop === 'white') return { backgroundColor: 'var(--color-bg-video-inverse)' };
  if (backdrop === 'black') return { backgroundColor: 'var(--color-bg-video)' };
  if (backdrop === 'captured' && capturedFrame !== null) {
    // FR-019: the still is fitted *into* the fixed canvas, never the reverse —
    // `contain`/`center`/`no-repeat` over black is the same letterboxing
    // semantics as the wall's `object-contain` over `bg-black`
    // (`CameraViewer.tsx`), so the label's normalized coordinates address the
    // same box on both.
    return {
      backgroundColor: 'var(--color-bg-video)',
      backgroundImage: `url("${capturedFrame}")`,
      backgroundSize: 'contain',
      backgroundPosition: 'center center',
      backgroundRepeat: 'no-repeat',
    };
  }
  return { background: CHECKERBOARD_BACKGROUND };
}
