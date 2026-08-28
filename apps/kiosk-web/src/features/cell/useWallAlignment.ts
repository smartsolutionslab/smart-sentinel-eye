import { useCallback, useEffect, useRef, useState } from 'react';
import {
  initialAlignmentState,
  settleAlignment,
  skewAcross,
  type AlignmentState,
  type TileLag,
} from '@smart-sentinel-eye/shared/observability/wallAlignment';

/**
 * The per-wall playout control loop (spec 045 US1, ADR-0128).
 *
 * <p>
 * Tiles report their measured lag; this decides the one target the wall should
 * hold and hands it back for every tile to apply. <b>Only a wall can do this</b>
 * — a tile cannot see the others, and `management-web` shows one camera with
 * nothing to align it against, which is why this hook lives in `kiosk-web` and
 * not beside `CameraViewer`.
 * </p>
 *
 * <p>
 * <b>Every failure path is a no-op.</b> Unreadable statistics, a lag that comes
 * back null, a receiver that refuses a target: the wall loses its alignment
 * claim and keeps showing video (FR-013). An observer that can break the thing
 * it observes is worse than no observer.
 * </p>
 */

// One cycle per two lag samples, so the controller acts on figures that exist
// rather than on the gap between them.
const SETTLE_INTERVAL_MS = 2_000;

// A tile whose lag has not been reported for this long is dropped from the
// wall's reckoning: it has gone away, or its session restarted and its
// counters reset. Holding a stale figure would let a departed tile set the
// target for every tile still present.
const LAG_STALE_AFTER_MS = 15_000;

export interface WallAlignment {
  /** Records a tile's measured lag. Passed to every `CameraViewer` on the wall. */
  reportLag: (cameraIdentifier: string, lagMilliseconds: number) => void;
  /**
   * The target this tile should hold, or null to leave it alone.
   *
   * <p>
   * Null for a released tile, for a wall that has not yet converged, and for a
   * single-tile wall — none of which is the same as a target of zero.
   * </p>
   */
  targetFor: (cameraIdentifier: string) => number | null;
  /** Tiles that could not be held inside the leg's budget (FR-012). */
  released: ReadonlySet<string>;
  /** The spread across held tiles, or null when there is nothing to compare. */
  skewMilliseconds: number | null;
}

/**
 * @param tileCount how many tiles the wall is rendering. Below two the loop
 * never runs and no target is ever produced (FR-004) — a single-tile wall must
 * not pay a millisecond for a feature about walls.
 */
export function useWallAlignment(tileCount: number): WallAlignment {
  // Lags live in a ref, not in state: they arrive every couple of seconds per
  // tile, and re-rendering a wall of live video on each one would cost far
  // more than the leg being managed. The loop below reads them on its own
  // schedule and publishes only the decision.
  const lagsRef = useRef<Map<string, { lagMilliseconds: number; at: number }>>(new Map());
  const stateRef = useRef<AlignmentState>(initialAlignmentState);

  const [target, setTarget] = useState<number | null>(null);
  const [released, setReleased] = useState<ReadonlySet<string>>(() => new Set());
  const [skewMilliseconds, setSkew] = useState<number | null>(null);

  const reportLag = useCallback((cameraIdentifier: string, lagMilliseconds: number) => {
    // performance.now(), never Date.now(): fab clocks are PTP-stepped, and an
    // epoch comparison could age every tile out at once when the clock moves.
    lagsRef.current.set(cameraIdentifier, { lagMilliseconds, at: performance.now() });
  }, []);

  const aligning = tileCount >= 2;

  useEffect(() => {
    if (!aligning) {
      // Nothing to align. Deliberately does not clear a previously applied
      // target: a wall shrinking to one tile should not jolt that tile's
      // playout, and CameraViewer treats null as "leave alone".
      return;
    }

    const timer = window.setInterval(() => {
      const now = performance.now();
      const lags: TileLag[] = [];
      for (const [camera, sample] of lagsRef.current) {
        if (now - sample.at > LAG_STALE_AFTER_MS) {
          lagsRef.current.delete(camera);
          continue;
        }
        lags.push({ camera, lagMilliseconds: sample.lagMilliseconds });
      }

      const { state, target: next } = settleAlignment(stateRef.current, lags);
      stateRef.current = state;

      setReleased((current) => (sameMembers(current, state.released) ? current : new Set(state.released)));
      setTarget(next?.targetMilliseconds ?? null);
      setSkew(skewAcross(lags.filter((lag) => !state.released.includes(lag.camera))));
    }, SETTLE_INTERVAL_MS);

    return () => window.clearInterval(timer);
  }, [aligning]);

  const targetFor = useCallback(
    (cameraIdentifier: string) => (released.has(cameraIdentifier) ? null : target),
    [released, target],
  );

  return { reportLag, targetFor, released, skewMilliseconds };
}

/** Avoids re-rendering every tile when the released set is unchanged. */
function sameMembers(current: ReadonlySet<string>, next: readonly string[]): boolean {
  if (current.size !== next.length) return false;
  return next.every((value) => current.has(value));
}
