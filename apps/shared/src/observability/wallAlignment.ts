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

/** A tile's measured lag, as the controller sees it. */
export interface TileLag {
  camera: string;
  lagMilliseconds: number;
}

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
 * <b>Aligning means waiting for the slowest, so the target is the worst lag</b> —
 * capped at the leg's 200 ms budget. The cap is the whole safety argument: a
 * controller without one buys perfect alignment at any price, and this leg
 * spends from the same 800 ms budget it belongs to. Aligning two tiles was
 * measured to roughly double absolute lag (~30 ms → ~59 ms, spec 045 research
 * R4), so "at any price" is not hypothetical.
 * </p>
 *
 * <p>
 * A tile lagging beyond the cap is <b>released</b>, not held: holding it would
 * drag every other tile past the budget. A released tile keeps playing — the
 * wall gives up the claim about it, never the picture (FR-012b).
 * </p>
 *
 * <p>
 * <b>Fewer than two tiles yields no target at all</b>, not a target of zero.
 * There is nothing to align with, and a single-tile wall must not pay a
 * millisecond for this feature (FR-004).
 * </p>
 */
export function wallTargetFrom(lags: readonly TileLag[]): WallTarget | null {
  if (lags.length < 2) return null;

  const held: string[] = [];
  const released: string[] = [];
  for (const { camera, lagMilliseconds } of lags) {
    if (lagMilliseconds > PRESENTATION_BUFFER_BUDGET_MS) {
      released.push(camera);
    } else {
      held.push(camera);
    }
  }

  // Every tile is beyond the cap: there is no target any of them could share
  // without breaching the budget, so the wall makes no claim this cycle rather
  // than inventing one it cannot honour.
  if (held.length === 0) return null;

  const targetMilliseconds = Math.max(
    ...lags.filter((lag) => held.includes(lag.camera)).map((lag) => lag.lagMilliseconds),
  );

  return { targetMilliseconds, held, released };
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
}

export const initialAlignmentState: AlignmentState = { released: [], breaches: {} };

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
  const wasReleased = new Set(previous.released);
  const released: string[] = [];
  const breaches: Record<string, number> = {};

  for (const lag of lags) {
    if (wasReleased.has(lag.camera)) {
      // Coming back requires clearing the cap by the margin, not merely
      // touching it — otherwise a tile hovering at the cap oscillates.
      if (lag.lagMilliseconds > PRESENTATION_BUFFER_BUDGET_MS - RELEASE_HYSTERESIS_MARGIN_MS) {
        released.push(lag.camera);
      }
      continue;
    }

    if (lag.lagMilliseconds > PRESENTATION_BUFFER_BUDGET_MS) {
      const consecutive = (previous.breaches[lag.camera] ?? 0) + 1;
      if (consecutive >= RELEASE_CONSECUTIVE_CYCLES) {
        released.push(lag.camera);
      } else {
        breaches[lag.camera] = consecutive;
      }
    }
  }

  // Two different questions, and conflating them is what makes a badge blink.
  //
  // **What is marked** is `released` above — hysteresis-settled, so a single
  // bad sample does not evict a tile and a tile hovering at the cap does not
  // flip. That is what an operator sees (FR-012).
  //
  // **What is actuated** is `held` below — the cap decides it outright, every
  // cycle, with no hysteresis at all. A tile past 200 ms cannot be given a
  // target below its own lag whatever its history, so it simply does not
  // participate in the target this cycle even while it is still unmarked.
  const releasedNow = new Set(released);
  const held = lags.filter(
    (lag) => !releasedNow.has(lag.camera) && lag.lagMilliseconds <= PRESENTATION_BUFFER_BUDGET_MS,
  );

  const bare = wallTargetFrom(held);

  return {
    state: { released, breaches },
    // `released` comes from the settled state, not from `wallTargetFrom`'s
    // per-cycle view — and it survives a null target. A two-tile wall that
    // releases one has nothing left to align, but the released tile must still
    // be marked, so the state carries it rather than the target.
    target: bare === null ? null : { ...bare, released },
  };
}

/** The spread between the most- and least-lagged of the given tiles, or null below two. */
export function skewAcross(lags: readonly TileLag[]): number | null {
  if (lags.length < 2) return null;
  const values = lags.map((lag) => lag.lagMilliseconds);
  return Math.max(...values) - Math.min(...values);
}
