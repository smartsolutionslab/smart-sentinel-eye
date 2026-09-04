/**
 * The budget for an assertion that follows the first write of its kind in a run.
 *
 * <para>
 * A freshly booted stack pays a near-constant cost the first time a given
 * integration message type is published — about 5 s, measured and confirmed by
 * intervention in `specs/023-first-event-cold-start/verification.md` §3, and
 * <b>still unexplained</b>: eight candidate mechanisms were refuted there and
 * none is standing. The cost attaches to the message <i>type</i>, not to the
 * process, so a file being second in the run does not make it safe — and no
 * single readiness write could warm the eight types the suite touches.
 * </para>
 *
 * <para>
 * 90 s is not a new number: it is what the two spec-056 seeds already carried,
 * and roughly six times the worst cold journey on record (14 s).
 * </para>
 *
 * <para>
 * Use it for the <b>first</b> assertion after each distinct kind of write in a
 * file. Not for a repeat write of the same kind inside one test (measured warm
 * at 134–270 ms), and never for an assertion on an error surface or an absence —
 * widening a wait for something that should never appear turns every failure
 * into a stall.
 * </para>
 */
export const FIRST_WRITE_TIMEOUT_MS = 90_000;

/**
 * The per-test timeout a test needs in order for {@link FIRST_WRITE_TIMEOUT_MS}
 * to mean anything.
 *
 * <para>
 * `playwright.config.ts` sets `timeout: 60_000`, so a 90 s assertion budget
 * inside a default test is capped at 60 s minus everything already elapsed and
 * the number in the source is decoration. The two spec-056 seeds escape that
 * only because they also call `setTimeout(180_000)`; none of the nine exposed
 * spec files did. <b>The two constants travel as a pair</b> — a site that takes
 * one without the other reads as fixed and is not.
 * </para>
 */
export const FIRST_WRITE_TEST_TIMEOUT_MS = 180_000;
