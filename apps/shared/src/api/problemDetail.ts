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
