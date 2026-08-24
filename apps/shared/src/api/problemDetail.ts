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
  return (
    (isConflict(error) && (problemCode(error)?.endsWith('_STALE') ?? false)) ||
    isPreconditionFailedStale(error)
  );
}

/**
 * The second spelling of a lost update, added by spec 029.
 *
 * CameraCatalog answers a failed `If-Match` with **412** and
 * `CAMERA_VERSION_MISMATCH`, where the six older aggregates answer **409** with
 * a `*_STALE` code. Neither is wrong on its own — RFC 9110 §15.5.13 specifies
 * 412 for exactly this, so the camera is the more correct one — but it means
 * the status alone identifies a stale version in neither direction, and 412 is
 * itself already used for something else (Identity's upsert preconditions,
 * `WEBHOOK_CLIENT_ALREADY_EXISTS` / `WEBHOOK_CLIENT_NOT_FOUND`).
 *
 * **Provisional, pending #1857.** That issue argues the right resolution is to
 * key on the code alone and rename `CAMERA_VERSION_MISMATCH` to
 * `CAMERA_VERSION_STALE`, so all seven share one convention. The rename is a
 * backend change and out of scope for spec 030; code-only keying *without* it
 * would still miss the camera, because `CAMERA_VERSION_MISMATCH` does not end
 * in `_STALE`. So this is the narrow fix that works today.
 *
 * **Do not read this as the settled convention.**
 */
function isPreconditionFailedStale(error: unknown): boolean {
  if (
    typeof error !== 'object' ||
    error === null ||
    !('status' in error) ||
    (error as { status: unknown }).status !== 412
  ) {
    return false;
  }

  // Keyed on the code, not on 412 alone: Identity answers 412 for an upsert
  // precondition that was wrong about existence, which is not a lost update and
  // must not be offered "reload to see their version".
  const code = problemCode(error);

  return code === 'CAMERA_VERSION_MISMATCH' || (code?.endsWith('_STALE') ?? false);
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
