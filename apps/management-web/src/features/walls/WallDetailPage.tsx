import { useGetWallQuery, useSwitchWallSceneMutation, wallsApi } from '@smart-sentinel-eye/shared/api/walls.api';
import { useListLayoutsQuery } from '@smart-sentinel-eye/shared/api/layouts.api';
import { CONFLICT_FALLBACK, isStaleConflict, problemDetail } from '@smart-sentinel-eye/shared/api/problemDetail';
import { createLayoutHubClient } from '@smart-sentinel-eye/shared/realtime/layoutHub';
import { Button } from '@smart-sentinel-eye/shared/ui/primitives/Button';
import { useEffect, useRef } from 'react';
import { useAuth } from 'react-oidc-context';
import { useDispatch } from 'react-redux';
import { useParams } from 'react-router-dom';
import type { AppDispatch } from '../../app/store.js';

/**
 * Spec 258 US1: an admin's Next/Show controls for one wall, and the
 * live-updated Showing badge. This page holds its own `WallSceneChanged`
 * subscription (T060's `onWallSceneChanged`, the same hook `WallPage` uses
 * kiosk-side) and invalidates the cached wall on a frame that names it, so a
 * switch made from another admin session or from the kiosk itself shows up
 * here without a manual refresh — on top of the mutation-success
 * invalidation RTK Query already does for a switch made from *this* page.
 * On a stale `If-Match` (`409 WALL_STALE`) it shows the shared conflict
 * fallback and re-fetches once, without retrying the switch automatically
 * (ADR-0113 — resubmitting would replay the same stale intent over whoever
 * wrote in between).
 */
export function WallDetailPage() {
  const { wallIdentifier = '' } = useParams<{ wallIdentifier: string }>();
  const auth = useAuth();
  const dispatch = useDispatch<AppDispatch>();
  const {
    data: wall,
    isLoading,
    error,
    refetch,
  } = useGetWallQuery(wallIdentifier, { skip: wallIdentifier === '' });
  const [switchWallScene, switchState] = useSwitchWallSceneMutation();
  const { data: layoutsData } = useListLayoutsQuery('Published');

  // Same rationale as WallPage's own accessTokenRef (kiosk-web): a fresh
  // function each render would restart the hub-connect effect below on
  // every silent token renewal. Optional chaining throughout this
  // component's use of `auth`: `useAuth()` returns `undefined` (rather than
  // throwing) outside an `<AuthProvider>`, which `WallDetailPage.test.tsx`
  // deliberately doesn't render one of.
  const accessTokenRef = useRef(auth?.user?.access_token);
  // eslint-disable-next-line react-hooks/refs -- see above
  accessTokenRef.current = auth?.user?.access_token;

  useEffect(() => {
    if (auth?.isAuthenticated !== true || wallIdentifier === '') {
      return undefined;
    }

    const hub = createLayoutHubClient(
      { accessTokenFactory: () => accessTokenRef.current ?? '' },
      {
        onWallSceneChanged: (message) => {
          if (message.wall !== wallIdentifier) return;
          dispatch(wallsApi.util.invalidateTags([{ type: 'Wall', id: wallIdentifier }]));
        },
        onReconnected: () => {
          dispatch(wallsApi.util.invalidateTags([{ type: 'Wall', id: wallIdentifier }]));
        },
      },
    );

    // Same StrictMode-safe deferred start as WallPage/useLayoutLifecycle
    // (spec 011): a tick's delay lets the dev double-mount's cleanup cancel
    // this start before it begins negotiating.
    let started = false;
    const startTimer = setTimeout(() => {
      started = true;
      void hub.start();
    }, 0);

    return () => {
      clearTimeout(startTimer);
      if (started) {
        void hub.stop().catch(() => undefined);
      }
    };
  }, [auth?.isAuthenticated, wallIdentifier, dispatch]);

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
