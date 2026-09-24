import type { RecoveryOutcome } from './sweep-recovery';

/**
 * The layouts teardown's mid-sweep re-sign-in, extracted as a pure seam so the
 * call site's actual composition (guard, then bound, in that order) is
 * testable without a browser — see `scripts/archive-e2e-layouts-recovery.test.mjs`.
 *
 * Today's shape only, unwired: it still runs the guard and the sign-in with no
 * deadline re-check and no bound on the call, exactly as
 * `archive-e2e-layouts.teardown.ts:80-81` does today. That is why the guard's
 * own tests are red here — this commit is the characterisation of the defect,
 * not the fix (#2385, following #2382's `8e2f879e`).
 *
 * `now`, `deadline` and `timeoutMs` are unused in this shape — the fix (T006)
 * reads them once the body delegates to `recoverAndRetry`. Underscore-prefixed
 * so today's shape typechecks and lints clean; the fix drops the prefixes when
 * it starts reading them.
 */
export async function recoverLayout<T>(
  _now: number,
  _deadline: number,
  _timeoutMs: number,
  looksSignedOut: () => Promise<boolean>,
  signIn: () => Promise<void>,
  retry: () => Promise<T>,
): Promise<RecoveryOutcome<T>> {
  if (await looksSignedOut()) await signIn();
  return { attempted: true, result: await retry() };
}
