/**
 * How long to hold an overlay label back so it describes the same moment as the
 * picture beneath it (spec 046, ADR-0129).
 *
 * <p>
 * <b>This is not frame accuracy and must never be called that.</b> It makes a
 * label as old as the picture; it does not pair a value with the frame whose
 * instant it belongs to. Pairing needs a clock shared between the camera and the
 * event source — PTP hardware this system does not have, established by probe
 * (spec 046 research). Restating the withdrawn overclaim in a subtler form is
 * the specific failure this feature exists to prevent.
 * </p>
 *
 * <p>
 * Pure, and deliberately so: no React, no browser objects. The arithmetic is
 * where this is right or wrong, and it is testable without a video stream.
 * </p>
 */

/**
 * The longest a label may be held.
 *
 * <p>
 * Equal to the presentation-buffer leg's budget, and for the same reason: a held
 * label is a <em>later</em> label, and lateness is what the 800 ms budget bounds
 * (ADR-0015, FR-009). Beyond this the tile's picture is so far behind that
 * matching it would make an operator wait longer for a value than the picture is
 * worth — <b>freshness wins, and the label is shown at once</b>.
 * </p>
 */
export const LABEL_DELAY_CAP_MS = 200;

/**
 * The delay to apply to a tile's label, or <c>null</c> to show it immediately.
 *
 * <p>
 * <b>Null rather than zero, in three different cases</b>, because they mean
 * different things to a reader and none of them means "held for no time":
 * </p>
 *
 * <ul>
 *   <li><b>The frame age is unreadable</b> — statistics unavailable, or the
 *       session restarted and its counters reset. Guessing a delay would be an
 *       assumption wearing a measurement's clothes.</li>
 *   <li><b>The frame age is zero or negative</b> — nothing to match, and a zero
 *       delay would read as a measured decision when nothing was measured. This
 *       codebase's standing rule: a zero reads as a perfect score for something
 *       nobody timed.</li>
 *   <li><b>The frame age exceeds the cap</b> — holding a label that long costs
 *       more than the mismatch does.</li>
 * </ul>
 *
 * <p>
 * Every one of these shows the label immediately (FR-011), which is also what a
 * tile with no wall does.
 * </p>
 */
export function labelDelayFor(frameAgeMilliseconds: number | null): number | null {
  if (frameAgeMilliseconds === null) return null;
  if (!Number.isFinite(frameAgeMilliseconds)) return null;
  if (frameAgeMilliseconds <= 0) return null;
  if (frameAgeMilliseconds > LABEL_DELAY_CAP_MS) return null;

  return frameAgeMilliseconds;
}

/**
 * Whether a delay is worth scheduling at all.
 *
 * <p>
 * A timer for a fraction of a millisecond costs more than the mismatch it
 * corrects, and schedules work on every overlay change for no observable
 * benefit. <b>Nobody can perceive any of this</b> — the whole gap is below what
 * an eye resolves — so the mechanism should at least not be wasteful.
 * </p>
 */
export const MINIMUM_WORTHWHILE_DELAY_MS = 1;

export function isWorthDelaying(delayMilliseconds: number | null): delayMilliseconds is number {
  return delayMilliseconds !== null && delayMilliseconds >= MINIMUM_WORTHWHILE_DELAY_MS;
}
