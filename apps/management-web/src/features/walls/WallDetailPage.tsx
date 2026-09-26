import { useGetWallQuery, useSwitchWallSceneMutation } from '@smart-sentinel-eye/shared/api/walls.api';
import { useListLayoutsQuery } from '@smart-sentinel-eye/shared/api/layouts.api';
import { CONFLICT_FALLBACK, isStaleConflict, problemDetail } from '@smart-sentinel-eye/shared/api/problemDetail';
import { Button } from '@smart-sentinel-eye/shared/ui/primitives/Button';
import { useEffect } from 'react';
import { useParams } from 'react-router-dom';

/**
 * Spec 258 US1: an admin's Next/Show controls for one wall, and the
 * live-updated Showing badge (subscribed via the SignalR hub client
 * elsewhere — see `WallSceneChanged`; this page re-reads through RTK
 * Query's normal cache invalidation rather than holding its own
 * connection). On a stale `If-Match` (`409 WALL_STALE`) it shows the shared
 * conflict fallback and re-fetches once, without retrying the switch
 * automatically (ADR-0113 — resubmitting would replay the same stale intent
 * over whoever wrote in between).
 */
export function WallDetailPage() {
  const { wallIdentifier = '' } = useParams<{ wallIdentifier: string }>();
  const {
    data: wall,
    isLoading,
    error,
    refetch,
  } = useGetWallQuery(wallIdentifier, { skip: wallIdentifier === '' });
  const [switchWallScene, switchState] = useSwitchWallSceneMutation();
  const { data: layoutsData } = useListLayoutsQuery('Published');

  const switchError = switchState.error;
  const staleConflict = isStaleConflict(switchError);

  // "Wall changed, refreshed" — re-read once on a stale conflict, never
  // retry the switch itself (the "Try again" advice would replay the same
  // stale intent over whoever wrote in between).
  useEffect(() => {
    if (staleConflict) {
      void refetch();
    }
  }, [staleConflict, refetch]);

  const nameFor = (layoutIdentifier: string): string =>
    layoutsData?.published.find((layout) => layout.layoutIdentifier === layoutIdentifier)?.name ?? layoutIdentifier;

  if (isLoading) {
    return <p className="p-6 text-sm text-fg-muted">Loading…</p>;
  }
  if (error !== undefined || wall === undefined) {
    return <p className="p-6 text-sm text-accent-fault">Could not load this wall.</p>;
  }

  const backendError =
    switchError !== undefined
      ? problemDetail(switchError, staleConflict ? CONFLICT_FALLBACK : 'Could not switch the scene. Try again.')
      : null;

  return (
    <section className="p-6">
      <header className="mb-2 flex items-center justify-between">
        <h1 className="text-2xl font-semibold">{wall.name}</h1>
        <Button
          disabled={switchState.isLoading}
          onClick={() =>
            void switchWallScene({ wallIdentifier: wall.wallIdentifier, version: wall.version, target: 'next' })
          }
        >
          Next
        </Button>
      </header>

      <p data-testid="wall-showing" className="mb-4 text-sm text-fg-muted">
        Showing: {nameFor(wall.showing)}
      </p>

      {backendError !== null && (
        <div
          role="alert"
          className="mb-4 rounded-md border border-accent-fault/40 bg-accent-fault/10 px-3 py-2 text-sm text-accent-fault"
        >
          {backendError}
        </div>
      )}

      <ul className="flex flex-col gap-2">
        {wall.scenes.map((scene) => (
          <li
            key={scene}
            className="flex items-center justify-between rounded-md border border-fg-muted/30 bg-bg-elevated px-4 py-3"
          >
            <span>{nameFor(scene)}</span>
            <Button
              variant="secondary"
              disabled={switchState.isLoading || scene === wall.showing}
              onClick={() =>
                void switchWallScene({
                  wallIdentifier: wall.wallIdentifier,
                  version: wall.version,
                  target: 'layout',
                  layout: scene,
                })
              }
            >
              Show
            </Button>
          </li>
        ))}
      </ul>
    </section>
  );
}
