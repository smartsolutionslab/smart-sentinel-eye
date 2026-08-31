import { describe, it, expect } from 'vitest';
import {
  CEILING_MS,
  FIRST_DELAY_MS,
  JITTER_FRACTION,
  RECOVERY_BUDGET_MS,
  ROUND_TRIP_ALLOWANCE_MS,
  delayForAttemptMs,
  worstCaseIntervalMs,
} from './retrySchedule.js';

/**
 * Spec 051 — how long a screen waits before trying its provider again.
 */

describe('The ceiling respects the recovery budget (spec 051 T006)', () => {
  /**
   * **The only thing connecting a constant here to a success criterion in the
   * specification.**
   *
   * <p>
   * SC-001 promises a wall is back within two minutes of the provider becoming
   * healthy. Worst case, the provider recovers a moment after an attempt fails,
   * so the wall waits one whole interval — ceiling plus jitter — and then needs
   * a renewal round-trip. Nothing else in the codebase relates those numbers, so
   * without this a later "let us be gentler, make it ninety seconds" breaks
   * SC-001 with every other test still green.
   * </p>
   */
  it('Leaves room for a renewal round-trip inside the two-minute recovery budget', () => {
    expect(worstCaseIntervalMs() + ROUND_TRIP_ALLOWANCE_MS).toBeLessThanOrEqual(RECOVERY_BUDGET_MS);
  });

  /**
   * Stated separately so the check above cannot be satisfied by widening the
   * budget instead of narrowing the ceiling. SC-001's two minutes is the
   * promise; it is not a knob.
   */
  it('Keeps the budget at the two minutes SC-001 promises', () => {
    expect(RECOVERY_BUDGET_MS).toBe(120_000);
  });
});

describe('The delay grows and then stops growing (spec 051 T005)', () => {
  // Jitter pinned to its midpoint so the base sequence is what is asserted.
  const noJitter = () => 0.5;

  it.each([
    [1, 2_000],
    [2, 4_000],
    [3, 8_000],
    [4, 16_000],
    [5, CEILING_MS],
    [6, CEILING_MS],
    [40, CEILING_MS],
  ])('Waits %ims-worth before attempt %i', (attempt, expected) => {
    expect(delayForAttemptMs(attempt, noJitter)).toBe(expected);
  });

  it('Starts from the first delay rather than from zero', () => {
    expect(delayForAttemptMs(1, noJitter)).toBe(FIRST_DELAY_MS);
  });

  /**
   * **It never gives up**, and that is a decision rather than an omission: there
   * is nobody at the wall to restart it, so a screen that stopped would be a
   * screen needing a person — the failure this feature removes.
   */
  it('Never stops, however long the outage lasts', () => {
    expect(delayForAttemptMs(10_000, noJitter)).toBe(CEILING_MS);
    expect(Number.isFinite(delayForAttemptMs(10_000, noJitter))).toBe(true);
  });
});

describe('Screens do not arrive together (spec 051 T007)', () => {
  /**
   * The property US3 rests on. Asserted as a spread across many draws rather
   * than as "jitter is applied", because the second is true of a jitter that
   * always returns the same number.
   */
  it('Spreads attempts made at the same point in an outage', () => {
    const delays = new Set(Array.from({ length: 200 }, () => delayForAttemptMs(5)));

    expect(delays.size).toBeGreaterThan(1);
  });

  it('Keeps every attempt inside the jitter band', () => {
    const lowest = CEILING_MS * (1 - JITTER_FRACTION);
    const highest = CEILING_MS * (1 + JITTER_FRACTION);

    for (let draw = 0; draw < 500; draw += 1) {
      const delay = delayForAttemptMs(5);

      expect(delay).toBeGreaterThanOrEqual(lowest);
      expect(delay).toBeLessThanOrEqual(highest);
    }
  });

  /**
   * The band has to be wide enough to actually separate a wall's worth of
   * screens. A jitter of a few milliseconds satisfies "delays differ" and still
   * lands twenty screens inside the same instant.
   *
   * <p>
   * <b>The threshold is an absolute number of seconds on purpose.</b> The first
   * version of this test compared the spread against
   * <c>CEILING_MS * JITTER_FRACTION</c> — derived from the very constant under
   * test, so shrinking the jitter shrank the bar with it. Reducing the fraction
   * to 0.0001 left every screen arriving within about three milliseconds of its
   * neighbours and the suite fully green. Found by mutation; it is the only
   * reason the assertion is written this way.
   * </p>
   *
   * <p>
   * Five seconds is the floor because it is the point at which twenty screens
   * are meaningfully staggered rather than merely unequal.
   * </p>
   */
  it('Spreads widely enough to separate a wall of screens, not merely differ', () => {
    const draws = Array.from({ length: 500 }, () => delayForAttemptMs(5));
    const spread = Math.max(...draws) - Math.min(...draws);

    expect(spread).toBeGreaterThan(5_000);
  });
});
