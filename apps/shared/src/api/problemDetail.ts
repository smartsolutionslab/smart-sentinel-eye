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
