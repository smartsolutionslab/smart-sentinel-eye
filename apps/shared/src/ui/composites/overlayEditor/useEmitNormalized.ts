import { useCallback } from 'react';
import { clamp01, clampSize } from './overlayGeometry';
import type { Boundary, OverlayEditorValue } from '../useOverlayEditHistory';

export function useEmitNormalized<T extends OverlayEditorValue>(
  value: T,
  commit: (next: T, boundary: Boundary) => void,
) {
  // T005: the single place that builds the `onChange` payload. `clamp01`
  // bounds `x`/`y` and `clampSize` bounds `width`/`height` (#2361) — the drag
  // path's pixel-derived values need these as their only bound (unchanged,
  // FR-011); the keyboard path's values are already bounded tighter by
  // `nudgePosition` / `resizeSize` before they arrive here, so both clamps
  // are a no-op on them.
  const emitNormalized = useCallback(
    (nextX: number, nextY: number, nextWidth: number, nextHeight: number, boundary: Boundary) => {
      commit(
        {
          ...value,
          normalizedX: clamp01(nextX),
          normalizedY: clamp01(nextY),
          normalizedWidth: clampSize(nextWidth),
          normalizedHeight: clampSize(nextHeight),
        },
        boundary,
      );
    },
    [commit, value],
  );

  return emitNormalized;
}
