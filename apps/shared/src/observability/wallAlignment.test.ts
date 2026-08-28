import { describe, it, expect } from 'vitest';
import {
  lagBetween,
  lagSampleFrom,
  skewAcross,
  wallTargetFrom,
  PRESENTATION_BUFFER_BUDGET_MS,
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
