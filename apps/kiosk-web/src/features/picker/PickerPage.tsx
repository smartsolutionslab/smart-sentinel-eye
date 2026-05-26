import { useListLayoutsQuery } from '@smart-sentinel-eye/shared/api/layouts.api';
import type { ReactNode } from 'react';
import { useAuth } from 'react-oidc-context';
import { useNavigate } from 'react-router-dom';
import { useLayoutLifecycle } from '../revocation/useLayoutLifecycle.js';

/**
 * Kiosk picker (spec 003 US2). Lists Published layouts and routes to
 * the cell view on tap. Live updates via the SignalR hub:
 * ``LayoutRevisionPublished`` invalidates the cache so the picker
 * picks up newly-published layouts within ~1 s.
 */
export function PickerPage() {
  const auth = useAuth();
  const navigate = useNavigate();
  const { data, isLoading, error, refetch } = useListLayoutsQuery('Published');

  useLayoutLifecycle({
    accessTokenFactory: () => auth.user?.access_token ?? '',
    enabled: auth.isAuthenticated,
    onPublished: () => {
      void refetch();
    },
    onArchived: () => {
      void refetch();
    },
  });

  if (isLoading) {
    return <FullScreen message="Loading layouts…" />;
  }
  if (error !== undefined) {
    return (
      <FullScreen
        message="Could not load layouts."
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

  const published = data?.published ?? [];
  if (published.length === 0) {
    return (
      <FullScreen
        message="No layouts published yet."
        action={<p className="text-sm text-fg-muted">Ask your administrator to publish one.</p>}
      />
    );
  }

  return (
    <main className="min-h-screen bg-bg-base p-8">
      <h1 className="mb-6 text-3xl font-semibold">Pick a layout</h1>
      <ul className="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3">
        {published.map((layout) => (
          <li key={layout.layoutIdentifier}>
            <button
              type="button"
              onClick={() => navigate(`/layouts/${layout.layoutIdentifier}`)}
              className="w-full rounded-lg border border-fg-muted/30 bg-bg-elevated p-6 text-left transition hover:border-accent-active"
            >
              <h2 className="text-xl font-medium">{layout.name}</h2>
              <p className="mt-1 text-xs text-fg-muted">v{layout.revisionNumber}</p>
            </button>
          </li>
        ))}
      </ul>
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
