/**
 * Discreet "live updates degraded" indicator (spec 011 FR-007). Small and
 * fixed in a corner so it never obscures the wall; `role="status"` lets
 * assistive tech announce degradation without stealing focus. Rendered
 * from `useLayoutLifecycle().degraded`, so it clears on reconnection.
 */
export function LiveUpdatesBadge({ degraded }: { degraded: boolean }) {
  if (!degraded) {
    return null;
  }
  return (
    <div
      role="status"
      data-testid="live-updates-degraded"
      className="fixed bottom-3 right-3 z-20 rounded-md border border-accent-warning/40 bg-accent-warning/15 px-3 py-1 text-xs text-accent-warning"
    >
      Live updates degraded
    </div>
  );
}
