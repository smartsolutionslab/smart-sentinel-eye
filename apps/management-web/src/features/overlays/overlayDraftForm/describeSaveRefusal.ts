import {
  CONFLICT_FALLBACK,
  isStaleConflict,
  problemCode,
  problemDetail,
} from '@smart-sentinel-eye/shared/api/problemDetail';

/**
 * What a refused save says and whether the operator is offered Reload.
 *
 * Keyed on the code rather than the status (ADR-0119): "reload to see their
 * version" is useless advice for a name clash, and "try again" is useless
 * advice for a stale one or a state that moved out from under the operator.
 */
export function describeSaveRefusal(error: unknown): { backendError: string | null; offerReload: boolean } {
  const staleConflict = isStaleConflict(error);
  const notDraft = problemCode(error) === 'OVERLAY_REVISION_NOT_DRAFT';
  // Create-mode only — this form's create 409 is always OVERLAY_NAME_TAKEN,
  // never the stale-version or not-a-draft conflicts edit mode can hit.
  const nameTaken = problemCode(error) === 'OVERLAY_NAME_TAKEN';
  const backendError = problemDetail(
    error,
    staleConflict
      ? CONFLICT_FALLBACK
      : notDraft
        ? 'This revision is no longer a draft. Reload to see its current state.'
        : nameTaken
          ? 'That overlay name is already taken. Choose a different one.'
          : 'Could not save the overlay. Try again.',
  );
  // Reload, never retry: retrying replays the same stale intent over
  // whoever wrote in between (staleConflict), or resubmits against a
  // revision that has already left draft (notDraft) — neither can succeed.
  const offerReload = staleConflict || notDraft;
  return { backendError, offerReload };
}
