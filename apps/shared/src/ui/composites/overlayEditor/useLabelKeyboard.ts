import { useCallback } from 'react';
import type { KeyboardEvent as ReactKeyboardEvent } from 'react';
import type { Boundary, OverlayEditorValue } from '../useOverlayEditHistory';
import { arrowRunKey, buildAnnouncement, edgeName, type AnnounceAxis } from './overlayAnnouncement';
import { COARSE_STEP, FINE_STEP, nudgePosition, resizeSize } from './overlayGeometry';

const ARROW_KEYS = new Set(['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown']);

interface LabelKeyboardInput {
  value: OverlayEditorValue;
  emitNormalized: (nextX: number, nextY: number, nextWidth: number, nextHeight: number, boundary: Boundary) => void;
  queueAnnouncement: (message: string) => void;
  endRun: () => void;
}

export function useLabelKeyboard({ value, emitNormalized, queueAnnouncement, endRun }: LabelKeyboardInput) {
  // FR-004–FR-009: the one `onKeyDown` on the label. Non-arrow keys, and
  // Alt/Meta held with an arrow, are left entirely alone (spec.md §The
  // keyboard map — Alt+arrow stays browser Back/Forward).
  const handleLabelKeyDown = useCallback(
    (event: ReactKeyboardEvent<HTMLDivElement>) => {
      if (!ARROW_KEYS.has(event.key)) return;
      if (event.altKey || event.metaKey) return;
      event.preventDefault();

      const step = event.shiftKey ? COARSE_STEP : FINE_STEP;
      const resizing = event.ctrlKey;
      const sign = event.key === 'ArrowLeft' || event.key === 'ArrowUp' ? -1 : 1;
      const delta = sign * step;

      let nextX = value.normalizedX;
      let nextY = value.normalizedY;
      let nextWidth = value.normalizedWidth;
      let nextHeight = value.normalizedHeight;
      let axis: AnnounceAxis;
      let announceValue: number;
      let refused: boolean;

      if (resizing) {
        if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') {
          axis = 'width';
          nextWidth = resizeSize(value.normalizedWidth, delta, value.normalizedX);
          announceValue = nextWidth;
          refused = nextWidth === value.normalizedWidth;
        } else {
          axis = 'height';
          nextHeight = resizeSize(value.normalizedHeight, delta, value.normalizedY);
          announceValue = nextHeight;
          refused = nextHeight === value.normalizedHeight;
        }
      } else if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') {
        axis = 'x';
        nextX = nudgePosition(value.normalizedX, delta, value.normalizedWidth);
        announceValue = nextX;
        refused = nextX === value.normalizedX;
      } else {
        axis = 'y';
        nextY = nudgePosition(value.normalizedY, delta, value.normalizedHeight);
        announceValue = nextY;
        refused = nextY === value.normalizedY;
      }

      emitNormalized(nextX, nextY, nextWidth, nextHeight, { run: arrowRunKey(resizing, axis) });
      queueAnnouncement(buildAnnouncement(axis, announceValue, refused ? edgeName(axis, delta) : null));
    },
    [value, emitNormalized, queueAnnouncement],
  );

  // Spec 154 FR-005: the held-arrow run closes on `keyup` of any arrow key —
  // not on an idle timer (OS key repeat fires `keydown` roughly every 30ms
  // with no intervening `keyup`, so a timer wide enough to hold a long run
  // together would also merge two deliberate, separate nudges). `endRun()`
  // with no key closes whichever run is open; a non-arrow keyup (Shift,
  // Ctrl alone) is not the end of a nudge and is left alone.
  const handleLabelKeyUp = useCallback(
    (event: ReactKeyboardEvent<HTMLDivElement>) => {
      if (ARROW_KEYS.has(event.key)) {
        endRun();
      }
    },
    [endRun],
  );

  return { handleLabelKeyDown, handleLabelKeyUp };
}
