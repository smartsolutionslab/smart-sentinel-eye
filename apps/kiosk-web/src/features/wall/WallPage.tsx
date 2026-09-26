import { useGetWallQuery } from '@smart-sentinel-eye/shared/api/walls.api';
import { createLayoutHubClient } from '@smart-sentinel-eye/shared/realtime/layoutHub';
import { useEffect, useRef, useState } from 'react';
import { useAuth } from 'react-oidc-context';
import { useParams } from 'react-router-dom';
import { LayoutGrid } from '../cell/LayoutGrid.js';

interface Showing {
  layout: string;
  sceneVersion: number;
}

/**
 * Kiosk wall view (spec 258 US1) at `/walls/:wallIdentifier`. Reads the wall
 * once via `GET /walls/{id}`, then renders whichever scene is currently
 * showing through `LayoutGrid` — the same renderer `CellPage` uses (T063) —
 * remounted via `key` on every scene change so the old grid's WebRTC
 * sessions tear down deterministically (plan.md §6.3).
 *
 * Owns its own `WallSceneChanged` subscription, separate from `LayoutGrid`'s
 * own layout-lifecycle hub connection: a wall's scene pointer is a
 * different concern from any one layout's overlay/archival lifecycle, and
 * this page's callbacks (discard-by-version, reconnect-reread) apply only
 * to the pointer. FR-008: a frame whose `sceneVersion` is not strictly
 * greater than the one currently rendered is discarded (US1-16), and the
 * wall is re-read from scratch on every hub reconnect (US1-17) rather than
 * trusting that no frame was missed while the connection was down.
 */
export function WallPage() {
  const { wallIdentifier = '' } = useParams<{ wallIdentifier: string }>();
  const auth = useAuth();

  // Same rationale as LayoutGrid's own accessTokenRef: a fresh function each
  // render would restart the hub-connect effect below on every silent token
  // renewal.
  const accessTokenRef = useRef(auth.user?.access_token);
  // eslint-disable-next-line react-hooks/refs -- see above
  accessTokenRef.current = auth.user?.access_token;

  const { data, isLoading, error, refetch } = useGetWallQuery(wallIdentifier, {
    skip: wallIdentifier === '',
  });

  // The locally-tracked pointer. Seeded from the query result and moved only
  // forward by a WallSceneChanged frame's sceneVersion (US1-16) — never
  // backward, and never by a lower-or-equal frame.
  const [showing, setShowing] = useState<Showing | undefined>();

  // Adjusted during render, not from an effect (React's documented pattern
  // for "adjusting state when a prop changes" — react.dev/learn/you-might-not-need-an-effect,
  // the same shape `RegisterCameraDialog.tsx`'s `wasOpen` comparison uses):
  // `useGetWallQuery`'s `data` is a query result the hook can hand back a new
  // object for on every render regardless of whether `sceneVersion` moved, so
  // this compares the value, not the reference, and only updates when it
  // actually changed. Calling setState synchronously inside a useEffect body
  // here would fire `react-hooks/set-state-in-effect`.
  const [seenSceneVersion, setSeenSceneVersion] = useState<number | undefined>(undefined);
  if (data !== undefined && data.sceneVersion !== seenSceneVersion) {
    setSeenSceneVersion(data.sceneVersion);
    setShowing((current) => {
      if (current !== undefined && current.sceneVersion >= data.sceneVersion) {
        return current;
      }
      return { layout: data.showing, sceneVersion: data.sceneVersion };
    });
  }

  useEffect(() => {
    if (!auth.isAuthenticated || wallIdentifier === '') {
      return undefined;
    }

    const hub = createLayoutHubClient(
      { accessTokenFactory: () => accessTokenRef.current ?? '' },
      {
        onWallSceneChanged: (message) => {
          if (message.wall !== wallIdentifier) return;
          setShowing((current) => {
            if (current !== undefined && message.sceneVersion <= current.sceneVersion) {
              return current;
            }
            return { layout: message.showing, sceneVersion: message.sceneVersion };
          });
        },
        onReconnected: () => {
          void refetch();
        },
      },
    );

    // Same StrictMode-safe deferred start as useLayoutLifecycle (spec 011):
    // a tick's delay lets the dev double-mount's cleanup cancel this start
    // before it begins negotiating.
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
  }, [auth.isAuthenticated, wallIdentifier, refetch]);

  if (isLoading) {
    return <FullScreen message="Loading wall…" />;
  }
  if (error !== undefined || data === undefined) {
    return <FullScreen message="Wall is no longer available." />;
  }

  const currentLayout = showing?.layout ?? data.showing;

  return (
    <main className="relative min-h-screen bg-black">
      <header className="absolute left-0 right-0 top-0 z-10 bg-black/50 px-6 py-3 text-fg-primary">
        <h1 className="text-lg font-medium">{data.name}</h1>
      </header>
      <LayoutGrid layoutIdentifier={currentLayout} key={currentLayout} />
    </main>
  );
}

function FullScreen({ message }: { message: string }) {
  return (
    <main className="flex min-h-screen flex-col items-center justify-center gap-4 bg-bg-base p-8 text-center">
      <p className="text-lg">{message}</p>
    </main>
  );
}
