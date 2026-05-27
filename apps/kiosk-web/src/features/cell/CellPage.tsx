import { useGetLayoutQuery } from '@smart-sentinel-eye/shared/api/layouts.api';
import {
  overlaysApi,
  useGetOverlayQuery,
} from '@smart-sentinel-eye/shared/api/overlays.api';
import { CameraViewer } from '@smart-sentinel-eye/shared/ui/composites/CameraViewer';
import { useEffect, useState, type ReactNode } from 'react';
import { useDispatch } from 'react-redux';
import type { AppDispatch } from '../../app/store.js';
import { useAuth } from 'react-oidc-context';
import { useNavigate, useParams } from 'react-router-dom';
import { useLayoutLifecycle } from '../revocation/useLayoutLifecycle.js';

/**
 * Single-cell kiosk view (spec 003 US2 + US3, extended in spec 004
 * US2/US3). Renders the camera tied to the layout's Published revision;
 * if the revision is bound to an overlay (PR B'), fetches it and renders
 * the label over the live frame. Force-disconnects to the picker when
 * the layout is archived (FR-011) or the SignalR channel reconnects and
 * the layout is no longer Published (FR-012).
 *
 * Overlay push (spec 004 US3): a republish of the bound overlay
 * invalidates the cache so RTK Query re-fetches and the label updates
 * within ~1 s. An archive of the bound overlay hides the label and
 * shows a transient "overlay unavailable" banner.
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
  const [overlayUnavailable, setOverlayUnavailable] = useState(false);

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
        setOverlayUnavailable(false);
      }
    },
    onOverlayArchived: (message) => {
      if (overlayIdentifier !== null && message.overlay === overlayIdentifier) {
        setOverlayUnavailable(true);
      }
    },
    onReconnected: () => {
      void refetch();
    },
  });

  useEffect(() => {
    // Reconcile after a refetch finishes: if the layout no longer has
    // a Published revision (admin reverted or archived it), bounce.
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
  const renderOverlay =
    !overlayUnavailable && publishedOverlay !== undefined
      ? {
          text: publishedOverlay.text,
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
