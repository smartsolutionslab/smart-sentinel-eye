import { describe, it, expect } from 'vitest';
import {
  lagBetween,
  lagSampleFrom,
  skewAcross,
  wallTargetFrom,
  PRESENTATION_BUFFER_BUDGET_MS,
  settleAlignment,
  initialAlignmentState,
  type LagSample,
} from './wallAlignment.js';

/**
 * Spec 045. The arithmetic behind a wall showing one instant.
 *
 * <p>
 * <b>Nothing here proves a wall looks aligned.</b> CI has no video and no SFU,
 * so these cover the figures once they exist. That the tiles of a real wall
 * converge is T026, and T026 is a person.
 * </p>
 *
 * <p>
 * Every assertion about skew below <b>induces the skew first</b>. On an idle box
 * with two identical sources the spread was already 9.4 ms before this feature
 * existed (research R6), so a test that merely asserts a small spread passes
 * with the controller deleted.
 * </p>
 */

function report(stat: Record<string, unknown>): Map<string, unknown> {
  return new Map<string, unknown>([['x', { type: 'inbound-rtp', kind: 'video', ...stat }]]);
}

const sample = (over: Partial<LagSample> = {}): LagSample => ({
  jitterBufferDelaySeconds: 1,
  jitterBufferEmittedCount: 100,
  processingDelaySeconds: 0.5,
  framesDecoded: 100,
  ...over,
});

describe('lagSampleFrom', () => {
  it('Reads the four counters off an inbound video stream', () => {
    const got = lagSampleFrom(
      report({
        jitterBufferDelay: 2.5,
        jitterBufferEmittedCount: 200,
        totalProcessingDelay: 1.25,
        framesDecoded: 200,
      }),
    );

    expect(got).toEqual({
      jitterBufferDelaySeconds: 2.5,
      jitterBufferEmittedCount: 200,
      processingDelaySeconds: 1.25,
      framesDecoded: 200,
    });
  });

  it('Reads nothing from an audio stream', () => {
    const audio = new Map<string, unknown>([['x', { type: 'inbound-rtp', kind: 'audio', jitterBufferDelay: 1 }]]);
    expect(lagSampleFrom(audio)).toBeNull();
  });

  /** A partial report is not a tile with no lag. */
  it('Reads nothing when a counter is missing', () => {
    expect(lagSampleFrom(report({ jitterBufferDelay: 2.5, framesDecoded: 200 }))).toBeNull();
  });
});

describe('lagBetween', () => {
  it('Reports the per-frame lag in milliseconds', () => {
    const previous = sample();
    const current = sample({
      jitterBufferDelaySeconds: 1 + 0.5, // 500 ms over 50 frames = 10 ms/frame
      jitterBufferEmittedCount: 150,
      processingDelaySeconds: 0.5 + 0.1, // 100 ms over 50 frames = 2 ms/frame
      framesDecoded: 150,
    });

    expect(lagBetween(previous, current)).toBeCloseTo(12, 6);
  });

  /**
   * Null rather than zero, deliberately: a zero would read as a perfect score
   * for a journey nobody timed.
   */
  it('Reports nothing when no frames were emitted', () => {
    expect(lagBetween(sample(), sample())).toBeNull();
  });

  it('Reports nothing when a counter went backwards after a reconnect', () => {
    const previous = sample({ jitterBufferEmittedCount: 500, framesDecoded: 500 });
    const current = sample({ jitterBufferEmittedCount: 20, framesDecoded: 20 });
    expect(lagBetween(previous, current)).toBeNull();
  });

  it('Reports nothing when the buffer counter went backwards but the frame count did not', () => {
    const previous = sample({ jitterBufferDelaySeconds: 5 });
    const current = sample({
      jitterBufferDelaySeconds: 1,
      jitterBufferEmittedCount: 150,
      framesDecoded: 150,
    });
    expect(lagBetween(previous, current)).toBeNull();
  });

  /**
   * **This is the test that fails if someone "simplifies" `lagBetween` into a
   * lifetime average**, which is the whole reason the delta exists.
   *
   * <p>
   * A session that ran 10 ms/frame for 1 000 frames and then blew out to
   * 200 ms/frame for 50: the cumulative ratio still reads ~19 ms and the wall
   * looks fine, while the delta reports the 200 ms excursion that is actually
   * happening. A budget is a claim about the tail.
   * </p>
   */
  it('Disagrees with a lifetime average across an excursion, and reports the excursion', () => {
    const calm: LagSample = {
      jitterBufferDelaySeconds: 10, // 1 000 frames at 10 ms
      jitterBufferEmittedCount: 1_000,
      processingDelaySeconds: 0,
      framesDecoded: 1_000,
    };
    const afterExcursion: LagSample = {
      jitterBufferDelaySeconds: 10 + 10, // 50 more frames at 200 ms
      jitterBufferEmittedCount: 1_050,
      processingDelaySeconds: 0,
      framesDecoded: 1_050,
    };

    const cumulative = (afterExcursion.jitterBufferDelaySeconds / afterExcursion.jitterBufferEmittedCount) * 1000;
    const delta = lagBetween(calm, afterExcursion);

    expect(delta).toBeCloseTo(200, 6);
    expect(cumulative).toBeLessThan(20);
    // The point: the average would report a healthy wall during a breach.
    expect(delta! - cumulative).toBeGreaterThan(150);
  });
});

describe('wallTargetFrom', () => {
  /** Induced: 20/30/120, not three tiles that happened to agree. */
  it('Holds the wall to its slowest tile', () => {
    const target = wallTargetFrom([
      { camera: 'a', lagMilliseconds: 20 },
      { camera: 'b', lagMilliseconds: 30 },
      { camera: 'c', lagMilliseconds: 120 },
    ]);

    expect(target?.targetMilliseconds).toBe(120);
    expect(target?.held).toEqual(['a', 'b', 'c']);
    expect(target?.released).toEqual([]);
  });

  /**
   * The tile beyond the budget is released rather than held. Holding it would
   * make every other tile ~400 ms late — alignment bought past the budget this
   * leg belongs to, which is the silent regression US3 exists to catch.
   */
  it('Releases a tile that would drag the wall past the leg budget', () => {
    const target = wallTargetFrom([
      { camera: 'a', lagMilliseconds: 20 },
      { camera: 'b', lagMilliseconds: 30 },
      { camera: 'slow', lagMilliseconds: 400 },
    ]);

    expect(target?.released).toEqual(['slow']);
    expect(target?.held).toEqual(['a', 'b']);
    expect(target?.targetMilliseconds).toBe(30);
    expect(target!.targetMilliseconds).toBeLessThanOrEqual(PRESENTATION_BUFFER_BUDGET_MS);
  });

  it('Holds a tile sitting exactly on the budget', () => {
    const target = wallTargetFrom([
      { camera: 'a', lagMilliseconds: 20 },
      { camera: 'b', lagMilliseconds: PRESENTATION_BUFFER_BUDGET_MS },
    ]);

    expect(target?.released).toEqual([]);
    expect(target?.targetMilliseconds).toBe(PRESENTATION_BUFFER_BUDGET_MS);
  });

  /**
   * FR-004. Not a target of zero — no target at all. A single-tile wall has
   * nothing to align with and must not pay for the feature.
   */
  it('Sets no target at all for a single-tile wall', () => {
    expect(wallTargetFrom([{ camera: 'only', lagMilliseconds: 40 }])).toBeNull();
  });

  it('Sets no target at all for an empty wall', () => {
    expect(wallTargetFrom([])).toBeNull();
  });

  /** No tile can be held inside the budget, so the wall claims nothing this cycle. */
  it('Sets no target when every tile is beyond the budget', () => {
    const target = wallTargetFrom([
      { camera: 'a', lagMilliseconds: 400 },
      { camera: 'b', lagMilliseconds: 500 },
    ]);

    expect(target).toBeNull();
  });
});

describe('settleAlignment — hysteresis at the cap', () => {
  const steady = { camera: 'steady', lagMilliseconds: 30 };

  const alsoSteady = { camera: 'also-steady', lagMilliseconds: 45 };

  /**
   * One bad sample must not evict a tile: marking takes consecutive cycles.
   *
   * <p>
   * Three tiles, not two, and deliberately: with two, excluding the breaching
   * tile leaves one, and a wall of one has nothing to align — the assertion
   * would be about wall size rather than about hysteresis.
   * </p>
   */
  it('Does not mark a tile on its first breach, but does not let it set the target either', () => {
    const { state, target } = settleAlignment(initialAlignmentState, [
      steady,
      alsoSteady,
      { camera: 'spiky', lagMilliseconds: 260 },
    ]);

    expect(state.released).toEqual([]);
    expect(target?.released).toEqual([]);
    // Unmarked, but still over the cap, so it takes no part in the target.
    expect(target?.held).toEqual(['steady', 'also-steady']);
    expect(target?.targetMilliseconds).toBe(45);
  });

  it('Releases a tile that breaches on consecutive cycles', () => {
    const lags = [steady, { camera: 'also-steady', lagMilliseconds: 45 }, { camera: 'slow', lagMilliseconds: 260 }];
    const first = settleAlignment(initialAlignmentState, lags);
    const second = settleAlignment(first.state, lags);

    expect(second.state.released).toEqual(['slow']);
    expect(second.target?.released).toEqual(['slow']);
    expect(second.target?.held).toEqual(['steady', 'also-steady']);
    expect(second.target?.targetMilliseconds).toBe(45);
  });

  /**
   * A released tile must stay marked even when releasing it leaves nothing to
   * align. Two tiles, one released, one left — no target, but the operator
   * still has to be told which tile fell out (FR-012).
   */
  it('Still marks a released tile when the wall has nothing left to align', () => {
    const lags = [steady, { camera: 'slow', lagMilliseconds: 260 }];
    const first = settleAlignment(initialAlignmentState, lags);
    const second = settleAlignment(first.state, lags);

    expect(second.target).toBeNull();
    expect(second.state.released).toEqual(['slow']);
  });

  it('Forgets a breach that does not repeat', () => {
    const blip = settleAlignment(initialAlignmentState, [steady, { camera: 'blip', lagMilliseconds: 260 }]);
    const recovered = settleAlignment(blip.state, [steady, { camera: 'blip', lagMilliseconds: 40 }]);
    const breachesAgain = settleAlignment(recovered.state, [steady, { camera: 'blip', lagMilliseconds: 260 }]);

    expect(recovered.state.breaches).toEqual({});
    // The counter restarted, so this breach is a first one and does not release.
    expect(breachesAgain.state.released).toEqual([]);
  });

  /**
   * **The oscillation test.** A tile hovering either side of the cap must not
   * flip its mark from cycle to cycle — an operator would watch a badge blink,
   * and nothing else in the suite catches it.
   *
   * <p>
   * It settles **held**, and that is the correct answer rather than a lenient
   * one: alternating 195/205 never accumulates two <em>consecutive</em>
   * breaches, so the tile is never marked. The wall is still protected —
   * on a 205 cycle the tile is over the cap and so takes no part in the target,
   * which stays at the steady tile's 30 ms. The budget is enforced by the cap
   * every cycle; hysteresis governs only what the operator is told.
   * </p>
   */
  it('Does not flip a tile hovering at the cap, and never lets it set the target', () => {
    // Induced deliberately: 195 and 205 straddle the 200 ms cap.
    const hovering = [195, 205, 195, 205, 195, 205];
    let state = initialAlignmentState;
    const marked: boolean[] = [];
    const targets: (number | undefined)[] = [];

    for (const lagMilliseconds of hovering) {
      const cycle = settleAlignment(state, [steady, alsoSteady, { camera: 'edge', lagMilliseconds }]);
      state = cycle.state;
      marked.push(state.released.includes('edge'));
      targets.push(cycle.target?.targetMilliseconds);
    }

    // The mark never changes — that is the property under test.
    expect(new Set(marked).size).toBe(1);
    expect(marked).toEqual([false, false, false, false, false, false]);
    // And the wall is never dragged past the budget on the 205 cycles: the
    // edge tile sets the target only while it is inside the cap.
    expect(targets).toEqual([195, 45, 195, 45, 195, 45]);
  });

  it('Takes a released tile back only once it clears the cap by the margin', () => {
    const lags = [steady, { camera: 'slow', lagMilliseconds: 260 }];
    let state = settleAlignment(initialAlignmentState, lags).state;
    state = settleAlignment(state, lags).state;
    expect(state.released).toEqual(['slow']);

    // Inside the cap, but not by the margin — still out.
    const nearly = settleAlignment(state, [steady, { camera: 'slow', lagMilliseconds: 190 }]);
    expect(nearly.state.released).toEqual(['slow']);

    // Clear of the margin — back in, and now it can set the target.
    const back = settleAlignment(nearly.state, [steady, { camera: 'slow', lagMilliseconds: 175 }]);
    expect(back.state.released).toEqual([]);
    expect(back.target?.held).toEqual(['steady', 'slow']);
    expect(back.target?.targetMilliseconds).toBe(175);
  });

  it('Sets no target for a single-tile wall however many cycles pass', () => {
    let state = initialAlignmentState;
    for (let cycle = 0; cycle < 5; cycle += 1) {
      const result = settleAlignment(state, [{ camera: 'only', lagMilliseconds: 40 }]);
      state = result.state;
      expect(result.target).toBeNull();
    }
  });
});

describe('skewAcross', () => {
  it('Reports the spread between the most- and least-lagged tile', () => {
    expect(
      skewAcross([
        { camera: 'a', lagMilliseconds: 20 },
        { camera: 'b', lagMilliseconds: 31 },
        { camera: 'c', lagMilliseconds: 120 },
      ]),
    ).toBe(100);
  });

  it('Reports nothing for a single tile, which has nothing to be skewed against', () => {
    expect(skewAcross([{ camera: 'only', lagMilliseconds: 40 }])).toBeNull();
  });
});
