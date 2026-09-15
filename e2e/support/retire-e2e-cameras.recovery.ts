/**
 * Recovers from a suspected lapsed session inside the camera sweep, and
 * retries the action once (issue #2382).
 *
 * Extracted so the recovery's timing behaviour can be exercised without a
 * browser — see `scripts/retire-e2e-cameras-recovery.test.mjs`.
 */
export type RecoveryOutcome<T> = { attempted: false } | { attempted: true; result: T };

export async function recoverAndRetry<T>(
  now: number,
  deadline: number,
  timeoutMs: number,
  signIn: () => Promise<void>,
  retry: () => Promise<T>,
): Promise<RecoveryOutcome<T>> {
  await signIn();
  return { attempted: true, result: await retry() };
}
