import { useGetLayoutQuery } from '@smart-sentinel-eye/shared/api/layouts.api';
import { CameraViewer } from '@smart-sentinel-eye/shared/ui/composites/CameraViewer';
import { useEffect, type ReactNode } from 'react';
import { useAuth } from 'react-oidc-context';
import { useNavigate, useParams } from 'react-router-dom';
import { useLayoutLifecycle } from '../revocation/useLayoutLifecycle.js';

/**
 * Single-cell kiosk view (spec 003 US2 + US3). Renders the camera tied
 * to the layout's Published revision; force-disconnects to the picker
 * when the layout is archived (FR-011) or the SignalR channel
 * reconnects and the layout is no longer Published (FR-012).
 */
export function CellPage() {
  const { layoutIdentifier = '' } = useParams<{ layoutIdentifier: string }>();
  const navigate = useNavigate();
  const auth = useAuth();
  const { data, isLoading, error, refetch } = useGetLayoutQuery(layoutIdentifier, {
    skip: layoutIdentifier === '',
  });

  const published = data?.revisions.find((revision) => revision.state === 'Published');

  useLayoutLifecycle({
    accessTokenFactory: () => auth.user?.access_token ?? '',
    enabled: auth.isAuthenticated,
    onArchived: (message) => {
      if (message.layout === layoutIdentifier) {
        navigate('/', { replace: true });
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
      <div className="flex h-screen items-center justify-center">
        <CameraViewer
          cameraIdentifier={published.cameraIdentifier}
          getToken={() => Promise.resolve(auth.user?.access_token ?? null)}
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
