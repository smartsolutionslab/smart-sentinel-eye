import { useCallback, useEffect, useRef, useState } from 'react';
import {
  initialAlignmentState,
  settleAlignment,
  skewAcross,
  WALL_SKEW_BOUND_MS,
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

// Matches the tiles' lag-sample cadence (CameraViewer LAG_SAMPLE_INTERVAL_MS).
// The two timers run on independent phases, so a cycle can act on a figure up
// to one sample old — which the deadband tolerates, and which is why the loop
// does not chase small movements.
const SETTLE_INTERVAL_MS = 2_000;

// A tile whose lag has not been reported for this long is dropped from the
// wall's reckoning: it has gone away, or its session restarted and its
// counters reset. Holding a stale figure would let a departed tile set the
// target for every tile still present.
const LAG_STALE_AFTER_MS = 15_000;

export interface WallAlignment {
  /** Records a tile's measured lag. Passed to every `CameraViewer` on the wall. */
  reportLag: (tileKey: string, camera: string, lagMilliseconds: number, bufferMilliseconds: number) => void;
  /**
   * The target this tile should hold, or null to leave it alone.
   *
   * <p>
   * Null for a released tile, for a wall that has not yet converged, and for a
   * single-tile wall — none of which is the same as a target of zero.
   * </p>
   */
  targetFor: (tileKey: string) => number | null;
  /**
   * How old this tile's picture is, in milliseconds, or null when it cannot be
   * read (spec 046).
   *
   * <p>
   * Buffer plus decode processing — the same delta the controller equalises.
   * Exposed so a label can be held back by it, which is <b>not</b> frame
   * accuracy: it makes the label as old as the picture, and pairs nothing with
   * a frame (ADR-0129).
   * </p>
   */
  frameAgeFor: (tileKey: string) => number | null;
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
  const lagsRef = useRef<
    Map<string, { camera: string; lagMilliseconds: number; bufferMilliseconds: number; at: number }>
  >(new Map());
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
  const [held, setHeld] = useState<ReadonlySet<string>>(() => new Set());
  const [released, setReleased] = useState<ReadonlySet<string>>(() => new Set());
  const [skewMilliseconds, setSkew] = useState<number | null>(null);

  const reportLag = useCallback(
    (tileKey: string, camera: string, lagMilliseconds: number, bufferMilliseconds: number) => {
      // performance.now(), never Date.now(): fab clocks are PTP-stepped, and an
      // epoch comparison could age every tile out at once when the clock moves.
      lagsRef.current.set(tileKey, { camera, lagMilliseconds, bufferMilliseconds, at: performance.now() });
    },
    [],
  );

  const aligning = tileCount >= 2;

  useEffect(() => {
    if (!aligning) {
      // **The wall stops claiming anything.** A previous version returned here
      // without clearing, which left `target` and `released` frozen at whatever
      // the departed tiles produced: the surviving tile kept being handed a
      // target computed from cameras that are gone — pinning its buffer for the
      // life of the page — and a badged tile kept its badge forever. That is
      // FR-004 broken by the very branch whose comment claimed to honour it.
      //
      // **Derived at the boundary rather than cleared here.** Writing state
      // from an effect would cascade renders (and the lint rule says so); the
      // readers below simply return nothing while the wall is too small, which
      // has no timing window at all. Only the carried-over control state is
      // reset, and that lives in a ref.
      //
      // Nothing jolts the tile: `CameraViewer` reads null as "leave alone" and
      // never writes, so the buffer keeps whatever depth it last had.
      stateRef.current = initialAlignmentState;
      return;
    }

    const timer = window.setInterval(() => {
      const now = performance.now();
      const lags: TileLag[] = [];
      // **Keyed by tile, not by camera.** A layout only forbids duplicate
      // *positions*, so the same camera may legitimately appear in two cells —
      // and keying by camera collapsed them into one entry, so a two-tile wall
      // of one camera never aligned at all. `TileLag.camera` carries the tile's
      // identity; the real camera rides alongside for the skew report, which is
      // the only place that needs it. Found in code review.
      for (const [tileKey, sample] of lagsRef.current) {
        if (now - sample.at > LAG_STALE_AFTER_MS) {
          lagsRef.current.delete(tileKey);
          continue;
        }
        lags.push({
          camera: tileKey,
          lagMilliseconds: sample.lagMilliseconds,
          bufferMilliseconds: sample.bufferMilliseconds,
        });
      }

      const { state, target: next } = settleAlignment(stateRef.current, lags);
      stateRef.current = state;

      setReleased((current) => (sameMembers(current, state.released) ? current : new Set(state.released)));

      // **`held` is the authority, not the complement of `released`.** They are
      // different sets: a tile the wall could not hold this cycle but which has
      // not yet accumulated enough consecutive breaches to be badged is in
      // neither. Treating it as held handed it a target it cannot reach —
      // collapsing the buffer of the one tile the wall had just decided it
      // could not carry, on every cycle, for as long as it hovered.
      const heldNow = next?.held ?? [];
      setHeld((current) => (sameMembers(current, heldNow) ? current : new Set(heldNow)));

      // **The deadband is what stops the target ratcheting.** `jitterBufferTarget`
      // is a playout *floor*, so a held tile measures at or above its setpoint;
      // taking `max(lag)` every cycle therefore feeds each cycle's noise back in
      // as next cycle's target and the whole wall climbs. Once the wall is
      // inside its bound there is nothing to fix, so the target is left alone
      // and the loop stops chasing its own tail. T026 caught the fast form of
      // this (120 ms → 654 ms); this is the slow one.
      const heldLags = lags.filter((lag) => heldNow.includes(lag.camera));
      const skew = skewAcross(heldLags);
      setSkew(skew);
      setTarget((current) => {
        if (next === null) return null;
        if (current === null) return next.targetMilliseconds;

        // **Symmetric, deliberately.** A one-sided deadband that only resisted
        // increases also froze the target *high*: when the laggiest tile left
        // the wall, the survivors kept being driven to a target computed from a
        // camera that was gone. Moving only on a change larger than the bound
        // we promise anyway rejects noise in both directions and still lets the
        // wall come down promptly when it can.
        return Math.abs(next.targetMilliseconds - current) > WALL_SKEW_BOUND_MS ? next.targetMilliseconds : current;
      });

      // Attributed to the tile that *defines* the spread — the laggiest held
      // one. One sample per cycle rather than one per tile: a wall reporting
      // one blended figure hides the tile that is out (#1931), and naming the
      // tile that set the number is what makes it actionable, while reporting
      // it N times would just weight the histogram by wall size.
      if (skew !== null && getTokenRef.current !== undefined) {
        const laggiest = heldLags.reduce((worst, lag) => (lag.lagMilliseconds > worst.lagMilliseconds ? lag : worst));
        // The tile's identity is the map key; the endpoint wants the camera,
        // and refuses a report that names none.
        const camera = lagsRef.current.get(laggiest.camera)?.camera;
        if (camera !== undefined) {
          reportKioskLatency('wall_skew', camera, skew, getTokenRef.current);
        }
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
    (tileKey: string) => {
      // Held, not merely un-badged. A tile the wall could not carry this cycle
      // gets nothing, even before it has earned its badge.
      // A wall too small to align claims nothing, derived rather than cleared.
      if (!aligning || !held.has(tileKey) || target === null) return null;

      const sample = lagsRef.current.get(tileKey);
      if (sample === undefined) return null;

      const processing = Math.max(0, sample.lagMilliseconds - sample.bufferMilliseconds);
      return Math.max(0, target - processing);
    },
    [aligning, held, target],
  );

  // The tile's own measured lateness, straight from the sample the controller
  // already keeps. Stale samples age out of `lagsRef` on the settle cycle, so a
  // departed tile stops reporting an age rather than reporting an old one.
  const frameAgeFor = useCallback((tileKey: string) => lagsRef.current.get(tileKey)?.lagMilliseconds ?? null, []);

  return {
    reportLag,
    targetFor,
    frameAgeFor,
    // Derived, not stored: a wall below two tiles makes no claim, so it shows
    // no badges and reports no spread — without an effect writing state.
    released: aligning ? released : NO_TILES,
    skewMilliseconds: aligning ? skewMilliseconds : null,
  };
}

/** Stable empty set, so a too-small wall does not hand out a fresh one each render. */
const NO_TILES: ReadonlySet<string> = new Set();

/** Avoids re-rendering every tile when the released set is unchanged. */
function sameMembers(current: ReadonlySet<string>, next: readonly string[]): boolean {
  if (current.size !== next.length) return false;
  return next.every((value) => current.has(value));
}
