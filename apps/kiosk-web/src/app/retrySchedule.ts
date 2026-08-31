/**
 * When a screen should try its identity provider again.
 *
 * <p>
 * <b>Two pressures pull against each other and both matter.</b> A wall that
 * nobody is standing at has to come back quickly once the provider returns, so
 * the wait between attempts cannot grow without limit. But twenty screens on one
 * wall must not all arrive in the same instant against a service that has just
 * come back up, turning one outage into a cycle of them.
 * </p>
 */

/** How long after the first failure to try again. Short enough that a blip is invisible. */
export const FIRST_DELAY_MS = 2_000;

/**
 * The longest a screen ever waits between attempts.
 *
 * <p>
 * <b>Chosen against the recovery budget, not for comfort.</b> Worst case the
 * provider recovers a moment after an attempt fails, so the wall waits one whole
 * interval — the ceiling plus its jitter — before it can possibly notice.
 * Raising this is not a free kindness to the provider: it directly spends the
 * budget in {@link RECOVERY_BUDGET_MS}, and the two live in different files.
 * <c>retrySchedule.test.ts</c> fails if this is raised past what that budget
 * affords.
 * </p>
 */
export const CEILING_MS = 30_000;

/** How far either side of the base delay an attempt may land, as a fraction. */
export const JITTER_FRACTION = 0.3;

/**
 * How long a wall may take to come back after the provider is healthy again
 * (spec 051 SC-001).
 *
 * <p>
 * Recorded here so the ceiling above can be checked against it. Nothing else
 * connects a constant in this file to a success criterion in a specification,
 * and a later decision to be gentler to the provider would otherwise break that
 * criterion with every test still green.
 * </p>
 */
export const RECOVERY_BUDGET_MS = 120_000;

/**
 * What a renewal round-trip may cost once the provider is answering again.
 *
 * <p>
 * Deliberately generous. It is the slack between the longest wait and the
 * budget, so an over-estimate here only makes the ceiling check stricter.
 * </p>
 */
export const ROUND_TRIP_ALLOWANCE_MS = 30_000;

/** The longest a screen can wait before its next attempt, jitter at its worst. */
export const worstCaseIntervalMs = (): number => CEILING_MS * (1 + JITTER_FRACTION);

/**
 * How long to wait before attempt <c>attempt</c> (1-based).
 *
 * <p>
 * Doubles from {@link FIRST_DELAY_MS} to {@link CEILING_MS}, then stays there —
 * <b>it never stops</b>. There is nobody at the wall to restart it, so a screen
 * that gave up would be a screen needing a person, which is the failure this
 * exists to remove; it would reintroduce that failure after a delay rather than
 * at once. The cost of being wrong is one request every thirty seconds from a
 * screen whose provider is gone for good.
 * </p>
 *
 * <p>
 * <c>random</c> is a parameter so a test can pin the jitter. Production passes
 * nothing.
 * </p>
 */
export const delayForAttemptMs = (attempt: number, random: () => number = Math.random): number => {
  const doublings = Math.max(0, attempt - 1);
  const base = Math.min(FIRST_DELAY_MS * 2 ** doublings, CEILING_MS);
  const offset = (random() * 2 - 1) * JITTER_FRACTION;

  return Math.round(base * (1 + offset));
};
