import { useGetWallQuery } from '@smart-sentinel-eye/shared/api/walls.api';
import { createLayoutHubClient } from '@smart-sentinel-eye/shared/realtime/layoutHub';
import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
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
 * sessions tear down deterministically (plan.md §6.3). Renders `LayoutGrid`
 * directly rather than wrapping it in a second `<main>`/header: `LayoutGrid`
 * already renders that structure, and a wall's header is `headerTitle`
 * (the wall's own name) with the "Back" button suppressed — a wall page
 * offers no way to leave the wall (PD-6).
 *
 * Owns its own `WallSceneChanged` subscription, separate from `LayoutGrid`'s
 * own layout-lifecycle hub connection: a wall's scene pointer is a
 * different concern from any one layout's overlay/archival lifecycle, and
 * this page's callbacks (discard-by-version, reconnect-reread) apply only
 * to the pointer. FR-008: a frame whose `sceneVersion` is not strictly
 * greater than the one currently rendered is discarded (US1-16), and the
 * wall is re-read from scratch on every hub reconnect (US1-17) and on the
 * hub's very first connect (US1-?, a switch landing before SignalR finishes
 * negotiating would otherwise never be received) rather than trusting that
 * no frame was missed while the connection was down.
 *
 * PD-6: an archived/unpublished showing scene does not take the kiosk off
 * the wall — `onUnavailable` is a no-op, so `LayoutGrid` shows its own
 * archived-layout placeholder in place rather than navigating to the picker
 * (the default every other caller, i.e. `CellPage`, keeps).
 */
export function WallPage() {
  const { wallIdentifier = '' } = useParams<{ wallIdentifier: string }>();
  // Keyed on the route param: React Router does not remount this component
  // on a param-only navigation between two `/walls/:wallIdentifier` routes,
  // but `showing`/`seenSceneVersion` below are local state seeded from *this*
  // wall's query result. Without the key, navigating from wall A to wall B
  // would carry wall A's `showing`/`seenSceneVersion` forward, and if wall
  // B's `sceneVersion` happens to equal wall A's last-seen value (plausible —
  // `SceneVersion.Initial = 0` for every fresh wall) the
  // `data.sceneVersion !== seenSceneVersion` guard below would never fire,
  // leaving wall A's layout rendered under wall B's URL. The key forces a
  // full remount, resetting both pieces of state and restarting the hub
  // subscription's effect cleanly for the new wall.
  return <WallPageForWall key={wallIdentifier} wallIdentifier={wallIdentifier} />;
}

function WallPageForWall({ wallIdentifier }: { wallIdentifier: string }) {
  const auth = useAuth();

  // Same rationale as LayoutGrid's own accessTokenRef: a fresh function each
  // render would restart the hub-connect effect below on every silent token
  // renewal.
  const accessTokenRef = useRef(auth.user?.access_token);
  // eslint-disable-next-line react-hooks/refs -- see above
  accessTokenRef.current = auth.user?.access_token;

  const { data, isLoading, refetch } = useGetWallQuery(wallIdentifier, {
    skip: wallIdentifier === '',
  });

  // Additive no-op for LayoutGrid's onUnavailable (PD-6): a stable identity
  // (empty deps) so the effect below, and LayoutGrid's own, never see it as
  // "changed" on an unrelated re-render.
  const stayOnWall = useCallback(() => undefined, []);

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
        // US1-5: also re-read on the hub's very first connect, not only on
        // recovery — `GET /walls/{id}` usually resolves before SignalR
        // finishes negotiating, so a switch landing in that gap would
        // otherwise never be received. `onStateChange` fires on every
        // connected transition, including the first, and the sceneVersion
        // gate above makes a redundant read here harmless.
        onStateChange: (state) => {
          if (state === 'connected') {
            void refetch();
          }
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

  // Render from `data` whenever it's present, even alongside a concurrent
  // `error` — RTK Query keeps the last good `data` through a failed
  // background refetch (e.g. a transient reconnect re-read), and swapping to
  // the fallback on that error would blank an otherwise-fine wall (spec 258
  // review). The fallback is reserved for when there is truly nothing to
  // show yet.
  if (data === undefined) {
    if (isLoading) {
      return <FullScreen message="Loading wall…" />;
    }
    return (
      <FullScreen
        message="Wall is no longer available."
        action={
          <button
            type="button"
            className="rounded-md bg-accent-active/20 px-4 py-2 text-accent-active"
            onClick={() => void refetch()}
          >
            Retry
          </button>
        }
      />
    );
  }

  const currentLayout = showing?.layout ?? data.showing;

  return (
    <LayoutGrid
      layoutIdentifier={currentLayout}
      key={currentLayout}
      onUnavailable={stayOnWall}
      headerTitle={data.name}
    />
  );
}

function FullScreen({ message, action }: { message: string; action?: ReactNode }) {
  return (
    <main className="flex min-h-screen flex-col items-center justify-center gap-4 bg-bg-base p-8 text-center">
      <p className="text-lg">{message}</p>
      {action}
    </main>
  );
}
