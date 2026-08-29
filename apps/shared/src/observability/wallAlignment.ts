/**
 * The arithmetic behind a wall showing one instant (spec 045, ADR-0128).
 *
 * <p>
 * Every tile of a wall opens its own session and runs its own jitter buffer,
 * so each settles wherever it likes and the tiles show different moments. This
 * module computes what each tile's lag is and what target the wall should hold,
 * so the controller can equalise them.
 * </p>
 *
 * <p>
 * <b>Pure, and deliberately so.</b> No React, no browser objects beyond the
 * statistics report itself. This is where the feature is right or wrong, and it
 * is testable without a browser, a video stream or a running SFU — none of
 * which CI has.
 * </p>
 *
 * <p>
 * <b>What it aligns against.</b> Not PTP, and not capture time. Every tile of a
 * wall is served by one SFU, so every tile already shares that SFU's clock —
 * a common reference that exists with no grandmaster and no PTP-aware switch
 * (ADR-0128). Cameras have unsynchronised capture clocks and nothing downstream
 * can recover a moment that was never recorded, so a wall is made
 * <em>self-consistent</em>, which is what an operator comparing tiles needs.
 * </p>
 */

/** The leg's budget (ADR-015). A hard ceiling here, not guidance — see {@link wallTargetFrom}. */
export const PRESENTATION_BUFFER_BUDGET_MS = 200;

/**
 * The bound a wall holds its tiles within (spec 045 FR-002).
 *
 * <p>
 * One frame at 30 Hz, which is the cadence floor ADR-0123 requires of a kiosk.
 * Tiles are painted by one compositor in one frame, so two tiles cannot visibly
 * differ by less than a frame interval — a tighter bound would describe
 * something no operator can see and no browser can demonstrate.
 * </p>
 *
 * <p>
 * <b>Deliberately not ADR-014's `< 5 ms`.</b> That is an inter-display target
 * for PTP-synced hardware and is out of scope here (ADR-0128).
 * </p>
 */
export const WALL_SKEW_BOUND_MS = 33;

/** One reading from one tile. Meaningless alone — every field is a lifetime counter. */
export interface LagSample {
  jitterBufferDelaySeconds: number;
  /**
   * The denominator for the buffer delay. **Not `framesDecoded`** — they are
   * different counters, and dividing by the wrong one skews the figure with no
   * symptom.
   */
  jitterBufferEmittedCount: number;
  processingDelaySeconds: number;
  framesDecoded: number;
}

/**
 * Reads a tile's lag counters from its receiver statistics, or null when the
 * report carries nothing usable.
 *
 * <p>
 * Mirrors <c>decodeSampleFrom</c> in <c>kioskLatency.ts</c>, including its null:
 * a report that cannot be read is not a tile with no lag.
 * </p>
 */
export function lagSampleFrom(report: Map<string, unknown>): LagSample | null {
  for (const value of report.values()) {
    const stat = value as Record<string, unknown>;
    if (stat['type'] !== 'inbound-rtp' || stat['kind'] !== 'video') continue;

    const jitterBufferDelay = stat['jitterBufferDelay'];
    const jitterBufferEmittedCount = stat['jitterBufferEmittedCount'];
    const processingDelay = stat['totalProcessingDelay'];
    const framesDecoded = stat['framesDecoded'];
    if (
      typeof jitterBufferDelay !== 'number' ||
      typeof jitterBufferEmittedCount !== 'number' ||
      typeof processingDelay !== 'number' ||
      typeof framesDecoded !== 'number'
    ) {
      return null;
    }

    return {
      jitterBufferDelaySeconds: jitterBufferDelay,
      jitterBufferEmittedCount,
      processingDelaySeconds: processingDelay,
      framesDecoded,
    };
  }
  return null;
}

/**
 * The per-frame lag between two samples, in milliseconds, or null when there is
 * nothing to report.
 *
 * <p>
 * <b>A delta between two samples, never a cumulative ratio.</b> The counters run
 * for the life of the session, so a ratio of the totals reports the session
 * average and flattens exactly the excursion a budget is about — a wall that
 * fell out of alignment ten seconds ago would still read as aligned. This is
 * the same rule, for the same reason, as <c>decodeElapsedBetween</c>.
 * </p>
 *
 * <p>
 * <b>Null rather than zero.</b> No frames since the last sample, or a counter
 * that went backwards because the session restarted. A zero would read as a
 * perfect score for a journey nobody timed.
 * </p>
 */
export function lagBetween(previous: LagSample, current: LagSample): number | null {
  const emitted = current.jitterBufferEmittedCount - previous.jitterBufferEmittedCount;
  const decoded = current.framesDecoded - previous.framesDecoded;
  if (emitted <= 0 || decoded <= 0) return null;

  const bufferSeconds = current.jitterBufferDelaySeconds - previous.jitterBufferDelaySeconds;
  const processingSeconds = current.processingDelaySeconds - previous.processingDelaySeconds;
  if (bufferSeconds < 0 || processingSeconds < 0) return null;

  return (bufferSeconds / emitted) * 1000 + (processingSeconds / decoded) * 1000;
}

/**
 * The per-frame time frames spent **waiting in the buffer**, in milliseconds,
 * or null when there is nothing to report.
 *
 * <p>
 * <b>This is the presentation-buffer leg, and it is not {@link lagBetween}.</b>
 * `lagBetween` adds processing delay because the controller has to equalise the
 * whole of what makes a tile late. But processing delay is <em>already</em> the
 * decode leg — `decodeElapsedBetween` records it as `receive_to_decoded` — so
 * reporting the combined figure against the 200 ms presentation budget would
 * charge one leg for another leg's time.
 * </p>
 *
 * <p>
 * That is precisely the confusion <c>KioskReceiveToDecoded</c> warns about from
 * the other direction: it refuses to record `jitterBufferDelay` because that is
 * "the presentation buffer, a <em>different</em> leg". Two legs, two figures,
 * and the split lives here so neither can quietly absorb the other.
 * </p>
 */
export function bufferDelayBetween(previous: LagSample, current: LagSample): number | null {
  const emitted = current.jitterBufferEmittedCount - previous.jitterBufferEmittedCount;
  if (emitted <= 0) return null;

  const bufferSeconds = current.jitterBufferDelaySeconds - previous.jitterBufferDelaySeconds;
  if (bufferSeconds < 0) return null;

  return (bufferSeconds / emitted) * 1000;
}

/**
 * A tile's measured lag, as the controller sees it — **both figures, because
 * the two are used for different things**.
 *
 * <p>
 * `lagMilliseconds` is what makes the tile late, so it is what has to be
 * equalised. `bufferMilliseconds` is the part this leg actually spends, so it
 * is what the 200 ms budget bounds. Their difference is decode-side processing,
 * which belongs to another leg and which the controller can neither shorten nor
 * be charged for.
 * </p>
 */
export interface TileLag {
  camera: string;
  lagMilliseconds: number;
  bufferMilliseconds: number;
}

/** Decode-side time: the part of a tile's lag the buffer is not responsible for. */
const processingOf = (lag: TileLag): number => Math.max(0, lag.lagMilliseconds - lag.bufferMilliseconds);

/** What the wall should do this cycle. */
export interface WallTarget {
  /** The lag every held tile is driven to. */
  targetMilliseconds: number;
  /** Tiles that can reach the target. */
  held: readonly string[];
  /** Tiles that cannot, without pushing the wall past the leg's budget. */
  released: readonly string[];
}

/**
 * Computes the target a wall should hold, and which tiles cannot be held to it.
 *
 * <p>
 * <b>Aligning means waiting for the slowest, so the target is the worst lag.</b>
 * The cap is the whole safety argument: a controller without one buys perfect
 * alignment at any price, and this leg spends from the same 800 ms budget it
 * belongs to. Aligning two tiles was measured to roughly double absolute lag
 * (~30 ms → ~59 ms, spec 045 research R4), so "at any price" is not
 * hypothetical.
 * </p>
 *
 * <p>
 * <b>But the cap bounds the buffer, not the lag</b> — and getting that wrong is
 * a defect this code already had. Holding tile <em>i</em> at target <em>T</em>
 * makes its buffer <em>T − processing_i</em>, and only that buffer is this
 * leg's spend; the processing is the decode leg's. Testing the combined lag
 * against 200 ms charges this leg for another's time, which is exactly the
 * conflation {@link bufferDelayBetween} exists to prevent on the reporting side.
 * </p>
 *
 * <p>
 * <b>T026 found it on a real wall.</b> Both tiles measured ~257 ms of lag with
 * only ~131 ms of buffer, so the old test released <em>every</em> tile, marked
 * them all out of alignment, and never aligned anything — while each tile was
 * comfortably inside the budget it was being judged against.
 * </p>
 *
 * <p>
 * So a tile is released when it cannot be held without some held tile buffering
 * past the budget, and the laggiest goes first because it is the one forcing
 * the target up. A released tile keeps playing — the wall gives up the claim
 * about it, never the picture (FR-012b).
 * </p>
 *
 * <p>
 * <b>Fewer than two tiles yields no target at all</b>, not a target of zero.
 * There is nothing to align with, and a single-tile wall must not pay a
 * millisecond for this feature (FR-004).
 * </p>
 */
export function wallTargetFrom(lags: readonly TileLag[]): WallTarget | null {
  return classifyWall(lags).target;
}

/**
 * The same decision, but reporting **which tiles were dropped even when no
 * target could be formed**.
 *
 * <p>
 * <b>The distinction matters to an operator.</b> A two-tile wall with one bad
 * tile yields no target — there is nothing left to align — but only one tile
 * fell out, and badging both would blame a healthy tile for its neighbour. An
 * earlier version did exactly that, and a test caught it.
 * </p>
 */
export function classifyWall(lags: readonly TileLag[]): { target: WallTarget | null; released: readonly string[] } {
  if (lags.length < 2) return { target: null, released: [] };

  // Laggiest first: it is the tile that sets the target, so it is the tile to
  // drop when the target cannot be met.
  const byLag = [...lags].sort((a, b) => b.lagMilliseconds - a.lagMilliseconds);
  const released: string[] = [];

  for (let dropped = 0; dropped <= byLag.length - 2; dropped += 1) {
    const candidates = byLag.slice(dropped);
    const target = candidates[0]!.lagMilliseconds;

    // Feasible when every candidate can reach the target without its own buffer
    // exceeding the budget. buffer_i = target − processing_i.
    const feasible = candidates.every((lag) => target - processingOf(lag) <= PRESENTATION_BUFFER_BUDGET_MS);

    if (feasible) {
      return {
        target: { targetMilliseconds: target, held: candidates.map((lag) => lag.camera), released },
        released,
      };
    }

    released.push(candidates[0]!.camera);
  }

  // Dropping down to a single tile leaves nothing to align, so the wall makes
  // no claim this cycle rather than inventing one it cannot honour — but the
  // tiles actually dropped are still named.
  return { target: null, released };
}

/**
 * How far below the cap a released tile must come before it is held again, and
 * how many consecutive cycles a tile must sit past the cap before it is
 * released.
 *
 * <p>
 * <b>Neither is decoration.</b> Without them a tile sitting on 200 ms flips
 * between held and released every cycle, and an operator watches a badge blink
 * — the boundary case spec 045's edge cases name. The margin gives the return
 * journey a different threshold from the outward one; the cycle count stops a
 * single noisy sample from evicting a healthy tile.
 * </p>
 */
export const RELEASE_HYSTERESIS_MARGIN_MS = 20;
export const RELEASE_CONSECUTIVE_CYCLES = 2;

/** What the controller remembers between cycles. Carried, never global. */
export interface AlignmentState {
  released: readonly string[];
  /** Consecutive cycles each held tile has been observed past the cap. */
  breaches: Readonly<Record<string, number>>;
  /** Consecutive cycles each marked tile has looked holdable again. */
  recoveries: Readonly<Record<string, number>>;
}

export const initialAlignmentState: AlignmentState = { released: [], breaches: {}, recoveries: {} };

/**
 * Advances one control cycle: classifies tiles with hysteresis, then computes
 * the target for those still held.
 *
 * <p>
 * Pure — the previous state comes in and the next state goes out, so the whole
 * control loop is testable without a browser, a timer or a video stream.
 * </p>
 */
export function settleAlignment(
  previous: AlignmentState,
  lags: readonly TileLag[],
): { state: AlignmentState; target: WallTarget | null } {
  const wasMarked = new Set(previous.released);

  // One criterion, applied once. This used to re-test `lag > budget` here as
  // well, which was both a duplicate and the wrong comparison — the cap bounds
  // the buffer, not the lag (see wallTargetFrom). Feasibility is decided in one
  // place now, so the badge and the actuation cannot disagree.
  const candidates = lags.filter((lag) => !wasMarked.has(lag.camera));
  const bare = classifyWall(candidates);
  const couldNotHold = new Set(bare.released);

  const released: string[] = [];
  const breaches: Record<string, number> = {};
  const recoveries: Record<string, number> = {};

  // Unmarked tiles: a single infeasible cycle is not enough to mark one, so a
  // noisy sample cannot evict a healthy tile from the wall's claim.
  for (const lag of candidates) {
    if (!couldNotHold.has(lag.camera)) continue;

    const consecutive = (previous.breaches[lag.camera] ?? 0) + 1;
    if (consecutive >= RELEASE_CONSECUTIVE_CYCLES) {
      released.push(lag.camera);
    } else {
      breaches[lag.camera] = consecutive;
    }
  }

  // Marked tiles: retried by asking whether the wall could hold them again, and
  // required to answer yes for several consecutive cycles before the badge
  // clears. Without that a tile sitting on the boundary flips every cycle and
  // an operator watches it blink.
  for (const camera of wasMarked) {
    const lag = lags.find((candidate) => candidate.camera === camera);
    if (lag === undefined) continue; // the tile is gone; nothing to mark

    const trial = wallTargetFrom([...candidates.filter((c) => !couldNotHold.has(c.camera)), lag]);
    const wouldHold = trial !== null && trial.held.includes(camera);

    if (!wouldHold) {
      released.push(camera);
      continue;
    }

    const consecutive = (previous.recoveries[camera] ?? 0) + 1;
    if (consecutive < RELEASE_CONSECUTIVE_CYCLES) {
      recoveries[camera] = consecutive;
      released.push(camera);
    }
  }

  // The final target excludes everything now marked, so a tile that crossed
  // into the badge this cycle does not also set the target it failed to reach.
  const markedNow = new Set(released);
  const settled = wallTargetFrom(lags.filter((lag) => !markedNow.has(lag.camera)));

  return {
    state: { released, breaches, recoveries },
    // `released` comes from the settled state, not from `wallTargetFrom`'s
    // per-cycle view — and it survives a null target. A two-tile wall that
    // releases one has nothing left to align, but the released tile must still
    // be marked, so the state carries it rather than the target.
    target: settled === null ? null : { ...settled, released },
  };
}

/** The spread between the most- and least-lagged of the given tiles, or null below two. */
export function skewAcross(lags: readonly TileLag[]): number | null {
  if (lags.length < 2) return null;
  const values = lags.map((lag) => lag.lagMilliseconds);
  return Math.max(...values) - Math.min(...values);
}
