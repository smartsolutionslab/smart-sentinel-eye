import { describe, it, expect } from 'vitest';
import { isWorthDelaying, labelDelayFor, LABEL_DELAY_CAP_MS } from './labelDelay.js';

/**
 * Spec 046 Part 2. How long a label is held so it describes the same moment as
 * the picture beneath it.
 *
 * <p>
 * <b>Nothing here proves an operator is better off.</b> The gap is below what an
 * eye resolves, so no test — and no person — can confirm the improvement. These
 * cover the arithmetic; that it is applied to a real tile is T014, and that it
 * is imperceptible is stated rather than tested away.
 * </p>
 */

describe('labelDelayFor', () => {
  it('Holds a label for exactly as long as its picture is old', () => {
    expect(labelDelayFor(30)).toBe(30);
    expect(labelDelayFor(120)).toBe(120);
  });

  /**
   * Null rather than zero, and the distinction is the point: a zero delay reads
   * as a measured decision, when in fact nothing was measured.
   */
  it('Returns nothing when the frame age is unreadable', () => {
    expect(labelDelayFor(null)).toBeNull();
  });

  it('Returns nothing rather than a zero delay for a zero-age tile', () => {
    expect(labelDelayFor(0)).toBeNull();
    expect(labelDelayFor(0)).not.toBe(0);
  });

  it('Returns nothing for a negative age, which cannot describe a picture', () => {
    expect(labelDelayFor(-5)).toBeNull();
  });

  it('Returns nothing for a figure that is not a number', () => {
    expect(labelDelayFor(Number.NaN)).toBeNull();
    expect(labelDelayFor(Number.POSITIVE_INFINITY)).toBeNull();
  });

  /**
   * **FR-009.** A held label is a later label, and lateness is what the 800 ms
   * budget bounds. Past the cap the wait costs more than the mismatch, so
   * freshness wins.
   */
  it('Holds a label sitting exactly on the cap', () => {
    expect(labelDelayFor(LABEL_DELAY_CAP_MS)).toBe(LABEL_DELAY_CAP_MS);
  });

  it('Shows the label at once rather than holding it past the cap', () => {
    expect(labelDelayFor(LABEL_DELAY_CAP_MS + 1)).toBeNull();
    expect(labelDelayFor(5_000)).toBeNull();
  });

  /**
   * The cap is the presentation-buffer leg's budget, deliberately — the delay
   * spends from the same 800 ms path the picture's own lateness does.
   */
  it('Caps at the presentation-buffer budget, not an invented number', () => {
    expect(LABEL_DELAY_CAP_MS).toBe(200);
  });
});

describe('isWorthDelaying', () => {
  it('Does not schedule a timer for a fraction of a millisecond', () => {
    expect(isWorthDelaying(0.4)).toBe(false);
  });

  it('Schedules one for a delay that is actually measurable', () => {
    expect(isWorthDelaying(30)).toBe(true);
  });

  it('Treats nothing-to-do as nothing to schedule', () => {
    expect(isWorthDelaying(null)).toBe(false);
  });
});
