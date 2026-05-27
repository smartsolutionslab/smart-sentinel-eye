import { useGetLayoutQuery } from '@smart-sentinel-eye/shared/api/layouts.api';
import {
  overlaysApi,
  useGetOverlayQuery,
} from '@smart-sentinel-eye/shared/api/overlays.api';
import {
  systemVariablesApi,
  useGetOverlaySnapshotQuery,
} from '@smart-sentinel-eye/shared/api/systemVariables.api';
import { CameraViewer } from '@smart-sentinel-eye/shared/ui/composites/CameraViewer';
import { useEffect, useRef, useState, type ReactNode } from 'react';
import { useDispatch } from 'react-redux';
import type { AppDispatch } from '../../app/store.js';
import { useAuth } from 'react-oidc-context';
import { useNavigate, useParams } from 'react-router-dom';
import { useLayoutLifecycle } from '../revocation/useLayoutLifecycle.js';

/**
 * Single-cell kiosk view (spec 003 US2 + US3 → spec 004 US2/US3 →
 * spec 005 US3). Renders the camera tied to the layout's Published
 * revision; if the revision is bound to an overlay, fetches the
 * overlay (for geometry + font size) AND its resolved-text snapshot
 * from SystemVariables (for the live label text) and renders both.
 *
 * Spec 005 variable push: a variable-value change pushes a
 * ResolvedOverlayTextChanged frame; the kiosk validates the
 * monotonic version, updates the snapshot in-place, and the label
 * re-renders without a re-fetch.
 */
export function CellPage() {
  const { layoutIdentifier = '' } = useParams<{ layoutIdentifier: string }>();
  const navigate = useNavigate();
  const auth = useAuth();
  const dispatch = useDispatch<AppDispatch>();
  const { data, isLoading, error, refetch } = useGetLayoutQuery(layoutIdentifier, {
    skip: layoutIdentifier === '',
  });

  const published = data?.revisions.find((revision) => revision.state === 'Published');
  const overlayIdentifier = published?.overlayIdentifier ?? null;
  const { data: overlay } = useGetOverlayQuery(overlayIdentifier ?? '', {
    skip: overlayIdentifier === null,
  });
  const { data: snapshot } = useGetOverlaySnapshotQuery(overlayIdentifier ?? '', {
    skip: overlayIdentifier === null,
  });
  const [overlayUnavailable, setOverlayUnavailable] = useState(false);

  // Track the highest version we've applied so out-of-order pushes
  // are dropped on the floor.
  const latestVersionRef = useRef<number>(snapshot?.version ?? 0);
  useEffect(() => {
    if (snapshot !== undefined) {
      latestVersionRef.current = Math.max(latestVersionRef.current, snapshot.version);
    }
  }, [snapshot]);

  useLayoutLifecycle({
    accessTokenFactory: () => auth.user?.access_token ?? '',
    enabled: auth.isAuthenticated,
    onArchived: (message) => {
      if (message.layout === layoutIdentifier) {
        navigate('/', { replace: true });
      }
    },
    onOverlayPublished: (message) => {
      if (overlayIdentifier !== null && message.overlay === overlayIdentifier) {
        dispatch(overlaysApi.util.invalidateTags([{ type: 'Overlay', id: overlayIdentifier }]));
        dispatch(
          systemVariablesApi.util.invalidateTags([{ type: 'OverlaySnapshot', id: overlayIdentifier }]),
        );
        setOverlayUnavailable(false);
      }
    },
    onOverlayArchived: (message) => {
      if (overlayIdentifier !== null && message.overlay === overlayIdentifier) {
        setOverlayUnavailable(true);
      }
    },
    onResolvedOverlayTextChanged: (message) => {
      if (overlayIdentifier === null || message.overlay !== overlayIdentifier) return;
      if (message.version <= latestVersionRef.current) return;
      latestVersionRef.current = message.version;
      // Patch the snapshot cache in place so the render updates
      // without a full re-fetch.
      dispatch(
        systemVariablesApi.util.upsertQueryData(
          'getOverlaySnapshot',
          overlayIdentifier,
          {
            overlayIdentifier: message.overlay,
            resolvedText: message.resolvedText,
            version: message.version,
          },
        ),
      );
    },
    onReconnected: () => {
      void refetch();
    },
  });

  useEffect(() => {
    if (!isLoading && error === undefined && data !== undefined && published === undefined) {
      navigate('/', { replace: true });
    }
  }, [data, error, isLoading, navigate, published]);

  if (isLoading) {
    return <FullScreen message="Loading camera…" />;
  }
  if (error !== undefined || data === undefined || published === undefined) {
    return (
      <FullScreen
        message="Layout is no longer available."
        action={
          <button
            type="button"
            className="rounded-md bg-accent-active/20 px-4 py-2 text-accent-active"
            onClick={() => navigate('/')}
          >
            Back to picker
          </button>
        }
      />
    );
  }

  const publishedOverlay = overlay?.revisions.find((r) => r.state === 'Published');
  // Prefer the SystemVariables-resolved text over the raw label so any
  // `{{name}}` placeholders show their live values. Falls back to the
  // raw label if SystemVariables is unreachable (overlayUnavailable
  // banner also shows when the variable went Unset/Archived).
  const resolvedText = snapshot?.resolvedText ?? publishedOverlay?.text;
  const renderOverlay =
    !overlayUnavailable && publishedOverlay !== undefined && resolvedText !== undefined
      ? {
          text: resolvedText,
          normalizedX: publishedOverlay.normalizedX,
          normalizedY: publishedOverlay.normalizedY,
          normalizedWidth: publishedOverlay.normalizedWidth,
          normalizedHeight: publishedOverlay.normalizedHeight,
          fontSizePx: publishedOverlay.fontSizePx,
        }
      : undefined;

  return (
    <main className="relative min-h-screen bg-black">
      <header className="absolute left-0 right-0 top-0 z-10 flex items-center justify-between bg-black/50 px-6 py-3 text-fg-primary">
        <h1 className="text-lg font-medium">{data.name}</h1>
        <button
          type="button"
          className="rounded-md bg-bg-elevated/60 px-3 py-1 text-sm"
          onClick={() => navigate('/')}
        >
          Back
        </button>
      </header>
      {overlayUnavailable && (
        <div
          role="status"
          className="absolute left-1/2 top-16 z-10 -translate-x-1/2 rounded-md bg-accent-warn/30 px-4 py-1 text-sm text-accent-warn"
        >
          Overlay unavailable
        </div>
      )}
      <div className="flex h-screen items-center justify-center">
        <CameraViewer
          cameraIdentifier={published.cameraIdentifier}
          getToken={() => Promise.resolve(auth.user?.access_token ?? null)}
          overlay={renderOverlay}
        />
      </div>
    </main>
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
