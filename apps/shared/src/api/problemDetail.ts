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
 * True only for the lost-update conflict — the caller acted on a version that
 * has since moved (ADR-0113).
 *
 * Keyed on the **code**, and on nothing else. Per ADR-0119 a code ending
 * `_STALE` is what identifies a lost update across every context; the status is
 * deliberately not consulted, because both statuses in play are overloaded.
 * 409 also carries name collisions (`LAYOUT_NAME_TAKEN`) and terminal-state
 * refusals (`CAMERA_RETIRED`); 412 also carries Identity's upsert
 * preconditions (`WEBHOOK_CLIENT_ALREADY_EXISTS`). Neither status answers this
 * question, so neither is asked.
 *
 * Getting it wrong is not cosmetic. Offering "try again" to someone whose
 * version moved makes them resubmit unchanged, replaying their edit over the
 * other writer's — the exact lost update the mechanism exists to prevent.
 */
export function isStaleConflict(error: unknown): boolean {
  return problemCode(error)?.endsWith('_STALE') ?? false;
}

/**
 * True when the refusal is because the thing is in a terminal state — retired,
 * archived — rather than because someone else got there first.
 *
 * Needed because the two are indistinguishable by status. `CAMERA_RETIRED` is a
 * **409**, so it matches {@link isConflict} and would otherwise inherit
 * {@link CONFLICT_FALLBACK}: *"someone else changed this, reload to see their
 * version"*. Nobody changed it, and reloading will not help — the camera is
 * retired and no version of it can be corrected.
 */
export function isTerminalRefusal(error: unknown): boolean {
  return problemCode(error) === 'CAMERA_RETIRED';
}

/** Fallback used when a terminal-state refusal arrives without a detail. */
export const TERMINAL_REFUSAL_FALLBACK =
  'This camera is retired. Retired cameras keep their record but cannot be changed.';
