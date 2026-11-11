import { useCallback, useState } from 'react';
import type { OverlayGeometry } from '../OverlayGeometryFields';
import type { Boundary } from '../useOverlayEditHistory';
import { clamp01, clampSize } from './overlayGeometry';

interface CanvasGesturesInput {
  emitNormalized: (nextX: number, nextY: number, nextWidth: number, nextHeight: number, boundary: Boundary) => void;
  canvasWidthPx: number;
  canvasHeightPx: number;
  pixelWidth: number;
  pixelHeight: number;
}

export function useCanvasGestures({
  emitNormalized,
  canvasWidthPx,
  canvasHeightPx,
  pixelWidth,
  pixelHeight,
}: CanvasGesturesInput) {
  const emitGeometry = useCallback(
    (xPx: number, yPx: number, widthPx: number, heightPx: number) => {
      emitNormalized(
        xPx / canvasWidthPx,
        yPx / canvasHeightPx,
        widthPx / canvasWidthPx,
        heightPx / canvasHeightPx,
        'atomic',
      );
    },
    [canvasWidthPx, canvasHeightPx, emitNormalized],
  );

  // Spec 151 FR-013/FR-014: the live readout. `onDrag`/`onResize` hold the
  // in-flight geometry in local state, through the same `clamp01`/`clampSize`
  // the stop handlers use (via `emitNormalized`), so the readout never shows
  // a number the release would not produce. `onChange` does not fire from
  // either — only the two existing `*Stop` handlers below call it,
  // unchanged, and then clear this back to `null` (FR-014: "discarded when
  // the gesture ends" — spec.md:217).
  // `OverlayGeometryFields` then falls back to the `value` prop, which in the
  // real, controlled `OverlayDraftForm.tsx` (a `<Controller>`) is updated
  // by this same `onChange` on the very next render, so the field keeps
  // reading correctly across the handoff.
  const [preview, setPreview] = useState<OverlayGeometry | null>(null);

  const geometryFromPixels = useCallback(
    (xPx: number, yPx: number, widthPx: number, heightPx: number): OverlayGeometry => ({
      x: clamp01(xPx / canvasWidthPx),
      y: clamp01(yPx / canvasHeightPx),
      width: clampSize(widthPx / canvasWidthPx),
      height: clampSize(heightPx / canvasHeightPx),
    }),
    [canvasWidthPx, canvasHeightPx],
  );

  const handleDrag = useCallback(
    (_e: unknown, data: { x: number; y: number }) => {
      setPreview(geometryFromPixels(data.x, data.y, pixelWidth, pixelHeight));
    },
    [geometryFromPixels, pixelWidth, pixelHeight],
  );

  const handleDragStop = useCallback(
    (_e: unknown, data: { x: number; y: number }) => {
      emitGeometry(data.x, data.y, pixelWidth, pixelHeight);
      setPreview(null);
    },
    [pixelWidth, pixelHeight, emitGeometry],
  );

  const handleResize = useCallback(
    (
      _e: unknown,
      _dir: string,
      ref: { offsetWidth: number; offsetHeight: number },
      _delta: unknown,
      position: { x: number; y: number },
    ) => {
      setPreview(geometryFromPixels(position.x, position.y, ref.offsetWidth, ref.offsetHeight));
    },
    [geometryFromPixels],
  );

  const handleResizeStop = useCallback(
    (
      _e: unknown,
      _dir: string,
      ref: { offsetWidth: number; offsetHeight: number },
      _delta: unknown,
      position: { x: number; y: number },
    ) => {
      emitGeometry(position.x, position.y, ref.offsetWidth, ref.offsetHeight);
      setPreview(null);
    },
    [emitGeometry],
  );

  return { preview, handleDrag, handleDragStop, handleResize, handleResizeStop };
}
