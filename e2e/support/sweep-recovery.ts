/**
 * Bounds the camera sweep's re-sign-in recovery on both ends (issue #2382).
 *
 * The sweep already checks its own deadline before attempting a camera; what
 * it did not do is check it again — or bound the attempt at all — before the
 * re-sign-in it falls back to when a camera looks missing. That call ran on
 * the same page whose OIDC session had just lapsed, so it raced the app's own
 * silent `prompt=none` renewal rather than a plain login — and that renewal
 * can stall for minutes against Keycloak's self-signed certificate in this
 * environment. With nothing bounding it, the stall fell through to the
 * test's own 900s ceiling: 15.5 minutes on roughly a third of green CI runs.
 *
 * Extracted so the bound is exercised without a browser — see
 * `scripts/sweep-recovery.test.mjs`, which stubs a sign-in that
 * never resolves and proves the recovery gives up on schedule regardless.
 */
export type RecoveryOutcome<T> = { attempted: false } | { attempted: true; result: T };

export async function recoverAndRetry<T>(
  now: number,
  deadline: number,
  timeoutMs: number,
  signIn: () => Promise<void>,
  retry: () => Promise<T>,
): Promise<RecoveryOutcome<T>> {
  // Stop starting work the budget cannot cover — the sweep's own deadline,
  // rechecked here because the caller's last check (before the first attempt)
  // is stale by the time that attempt has failed.
  if (now > deadline) return { attempted: false };

  // Bound the attempt itself — even within budget, a single stalled sign-in
  // must not be able to spend it all. A rejection from the losing side of the
  // race (e.g. the page navigating away mid-attempt once `retry` runs) is
  // expected and not this function's concern.
  await Promise.race([signIn().catch(() => undefined), new Promise<void>((resolve) => setTimeout(resolve, timeoutMs))]);

  return { attempted: true, result: await retry() };
}
