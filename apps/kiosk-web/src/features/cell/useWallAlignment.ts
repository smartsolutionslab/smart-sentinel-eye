import { useCallback, useEffect, useRef, useState } from 'react';
import {
  initialAlignmentState,
  settleAlignment,
  skewAcross,
  type AlignmentState,
  type TileLag,
} from '@smart-sentinel-eye/shared/observability/wallAlignment';
import { reportKioskLatency } from '@smart-sentinel-eye/shared/observability/kioskLatency';

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
  reportLag: (cameraIdentifier: string, lagMilliseconds: number, bufferMilliseconds: number) => void;
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
 * @param getToken resolves the kiosk's bearer token, so the wall's skew can be
 * reported through the service like every other browser measurement
 * (ADR-0122). Omitted in tests that only exercise the arithmetic.
 */
export function useWallAlignment(tileCount: number, getToken?: () => Promise<string | null>): WallAlignment {
  // Lags live in a ref, not in state: they arrive every couple of seconds per
  // tile, and re-rendering a wall of live video on each one would cost far
  // more than the leg being managed. The loop below reads them on its own
  // schedule and publishes only the decision.
  const lagsRef = useRef<Map<string, { lagMilliseconds: number; bufferMilliseconds: number; at: number }>>(new Map());
  const stateRef = useRef<AlignmentState>(initialAlignmentState);

  // Behind a ref for the reason the whole of this feature keeps running into:
  // callers pass an inline closure, so naming it in the effect's deps would
  // rebuild the interval on every render and the controller would never
  // complete a cycle (issue 1889).
  const getTokenRef = useRef(getToken);
  useEffect(() => {
    getTokenRef.current = getToken;
  });

  const [target, setTarget] = useState<number | null>(null);
  const [released, setReleased] = useState<ReadonlySet<string>>(() => new Set());
  const [skewMilliseconds, setSkew] = useState<number | null>(null);

  const reportLag = useCallback((cameraIdentifier: string, lagMilliseconds: number, bufferMilliseconds: number) => {
    // performance.now(), never Date.now(): fab clocks are PTP-stepped, and an
    // epoch comparison could age every tile out at once when the clock moves.
    lagsRef.current.set(cameraIdentifier, { lagMilliseconds, bufferMilliseconds, at: performance.now() });
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
        lags.push({
          camera,
          lagMilliseconds: sample.lagMilliseconds,
          bufferMilliseconds: sample.bufferMilliseconds,
        });
      }

      const { state, target: next } = settleAlignment(stateRef.current, lags);
      stateRef.current = state;

      setReleased((current) => (sameMembers(current, state.released) ? current : new Set(state.released)));
      setTarget(next?.targetMilliseconds ?? null);

      // Skew across the tiles the wall actually claims to hold. A released
      // tile is excluded deliberately: the wall has stopped claiming it is in
      // step, so including it would report a spread the wall is not asserting
      // — and the badge, not this figure, is what says a tile fell out.
      const held = lags.filter((lag) => !state.released.includes(lag.camera));
      const skew = skewAcross(held);
      setSkew(skew);

      // Attributed to the tile that *defines* the spread — the laggiest held
      // one. One sample per cycle rather than one per tile: a wall reporting
      // one blended figure hides the tile that is out (#1931), and naming the
      // tile that set the number is what makes it actionable, while reporting
      // it N times would just weight the histogram by wall size.
      if (skew !== null && getTokenRef.current !== undefined) {
        const laggiest = held.reduce((worst, lag) => (lag.lagMilliseconds > worst.lagMilliseconds ? lag : worst));
        reportKioskLatency('wall_skew', laggiest.camera, skew, getTokenRef.current);
      }
    }, SETTLE_INTERVAL_MS);

    return () => window.clearInterval(timer);
  }, [aligning]);

  // The setpoint is a BUFFER depth, and the target is a TOTAL lag. Handing a
  // tile the target directly is a runaway: setting its buffer to T makes its
  // lag T + processing, so next cycle the target is T + processing, and the
  // wall climbs by one processing time every cycle for as long as it runs.
  //
  // T026 watched it happen — two tiles induced at 120 ms were at ~654 ms forty
  // seconds later, still beautifully aligned with each other and half a second
  // behind the world. Subtracting the tile's own processing makes T a fixed
  // point instead: buffer_i = T − p_i, so lag_i = T, and the next cycle asks
  // for the same thing.
  const targetFor = useCallback(
    (cameraIdentifier: string) => {
      if (released.has(cameraIdentifier) || target === null) return null;

      const sample = lagsRef.current.get(cameraIdentifier);
      if (sample === undefined) return null;

      const processing = Math.max(0, sample.lagMilliseconds - sample.bufferMilliseconds);
      return Math.max(0, target - processing);
    },
    [released, target],
  );

  return { reportLag, targetFor, released, skewMilliseconds };
}

/** Avoids re-rendering every tile when the released set is unchanged. */
function sameMembers(current: ReadonlySet<string>, next: readonly string[]): boolean {
  if (current.size !== next.length) return false;
  return next.every((value) => current.has(value));
}
