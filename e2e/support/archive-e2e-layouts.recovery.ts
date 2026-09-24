import { recoverAndRetry, type RecoveryOutcome } from './sweep-recovery.ts';

/**
 * The layouts teardown's mid-sweep re-sign-in, extracted as a pure seam so the
 * call site's actual composition (guard, then bound, in that order) is
 * testable without a browser — see `scripts/archive-e2e-layouts-recovery.test.mjs`.
 *
 * Delegates to `recoverAndRetry` (#2382) rather than re-implementing the race:
 * the `looksSignedOut` guard moves *inside* the bounded thunk, so it still
 * decides whether to sign in, but that decision — and the sign-in itself — now
 * runs after the deadline check and inside the `timeoutMs` cap, instead of
 * unbounded as `archive-e2e-layouts.teardown.ts` ran it before this fix
 * (#2385, following #2382's `8e2f879e`).
 */
export async function recoverLayout<T>(
  now: number,
  deadline: number,
  timeoutMs: number,
  looksSignedOut: () => Promise<boolean>,
  signIn: () => Promise<void>,
  retry: () => Promise<T>,
): Promise<RecoveryOutcome<T>> {
  return recoverAndRetry(
    now,
    deadline,
    timeoutMs,
    async () => {
      if (await looksSignedOut()) await signIn();
    },
    retry,
  );
}
