import { Badge } from '@smart-sentinel-eye/shared/ui/composites/Badge';

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
    <Badge tone="warning" size="md" asChild className="fixed bottom-3 right-3 z-20">
      <div role="status" data-testid="live-updates-degraded">
        Live updates degraded
      </div>
    </Badge>
  );
}
