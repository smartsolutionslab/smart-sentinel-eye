/**
 * Extracts the RFC-7807 `detail` string from an RTK Query error — the
 * shape the backend's `ApiError` → ProblemDetails maps to (ADR-0089). This
 * is the single place the frontend understands the server error envelope;
 * dialogs pass a context-specific `fallback` for when the error carries no
 * usable detail.
 *
 * Returns `null` only when there is no error at all, so callers can render
 * the banner conditionally.
 */
export function problemDetail(error: unknown, fallback: string): string | null {
  if (error === undefined || error === null) {
    return null;
  }
  if (typeof error === 'object' && 'data' in error) {
    const data = (error as { data: unknown }).data;
    if (typeof data === 'object' && data !== null && 'detail' in data) {
      const detail = (data as { detail: unknown }).detail;
      if (typeof detail === 'string') {
        return detail;
      }
    }
  }
  return fallback;
}

/**
 * True when the server refused a mutation because the caller acted on a
 * version that has since moved (ADR-0113 Layer 1). `problemDetail` alone
 * cannot tell a conflict from a validation failure or a server fault, and a
 * conflict needs different words: the operator has to re-read, not retry.
 *
 * Telling someone to "try again" on a 409 is how the other writer's work
 * gets overwritten — which is the bug this whole mechanism removes.
 */
export function isConflict(error: unknown): boolean {
  return (
    typeof error === 'object' &&
    error !== null &&
    'status' in error &&
    (error as { status: unknown }).status === 409
  );
}

/** Fallback used when a 409 arrives without an RFC-7807 detail. */
export const CONFLICT_FALLBACK =
  'Someone else changed this while you were working. Reload to see their version, then reapply your change.';

/**
 * The server's error code, carried as the RFC-7807 `title` by
 * `ApiErrorResults.ToProblem` (ADR-0089) — e.g. `LAYOUT_REVISION_STALE`.
 */
export function problemCode(error: unknown): string | null {
  if (typeof error === 'object' && error !== null && 'data' in error) {
    const data = (error as { data: unknown }).data;
    if (typeof data === 'object' && data !== null && 'title' in data) {
      const title = (data as { title: unknown }).title;
      if (typeof title === 'string') {
        return title;
      }
    }
  }
  return null;
}

/**
 * True only for the lost-update conflict (ADR-0113 Layer 1).
 *
 * `isConflict` is status-only, and 409 is not exclusively a stale version:
 * the create paths return `LAYOUT_NAME_TAKEN` / `OVERLAY_NAME_TAKEN` with the
 * same status. Offering "reload to see their version" for a name collision
 * sends the operator somewhere useless, so anything that changes the *advice*
 * has to key on the code rather than the status.
 */
export function isStaleConflict(error: unknown): boolean {
  return isConflict(error) && (problemCode(error)?.endsWith('_STALE') ?? false);
}
