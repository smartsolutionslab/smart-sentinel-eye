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
 * the number in the source is decoration. <b>The two constants travel as a
 * pair</b> — a site that takes one without the other reads as fixed and is not.
 * </para>
 *
 * <para>
 * <b>Size this one; do not copy it.</b> A ceiling has to hold the preamble
 * (sign-in, navigation), every <i>earlier</i> budgeted site's actual time, and
 * one whole {@link FIRST_WRITE_TIMEOUT_MS} for the site that fails. Miss that
 * and the test dies reporting `Test timeout of Nms exceeded` <b>naming no
 * locator</b> — losing exactly the diagnostic the budget exists to deliver.
 * The pessimistic bound is N × 90 s plus the preamble; in practice a cold write
 * that succeeds lands well inside its budget, so 300 s covers the widest test
 * that uses this constant — `layouts.spec.ts`'s four sites, three of them
 * arriving cold at ~40 s, plus a sign-in and a fourth full budget spent to
 * failure. A test with more than that states its own number with the
 * arithmetic at the site: both spec-056 seeds carry six sites, and
 * `kiosk-reconciliation.spec.ts` two plus two outage recoveries.
 * </para>
 *
 * <para>
 * Raising it costs a passing run nothing — a per-test timeout is a ceiling,
 * never a delay.
 * </para>
 */
export const FIRST_WRITE_TEST_TIMEOUT_MS = 300_000;
