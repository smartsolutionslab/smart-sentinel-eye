import {
  useArchiveOverlayRevisionMutation,
  useListOverlaysQuery,
  usePublishOverlayRevisionMutation,
  type Overlay,
  type OverlayRevisionState,
} from '@smart-sentinel-eye/shared/api/overlays.api';
import { Button } from '@smart-sentinel-eye/shared/ui/primitives/Button';
import { useState } from 'react';
import { OverlayEditorDialog } from './OverlayEditorDialog.js';

const STATE_FILTERS: ReadonlyArray<OverlayRevisionState | 'All'> = [
  'All',
  'Draft',
  'Published',
  'Archived',
];

export function OverlaysPage() {
  const [dialogOpen, setDialogOpen] = useState(false);
  const [filter, setFilter] = useState<OverlayRevisionState | 'All'>('All');

  const { data, isLoading, isFetching, error, refetch } = useListOverlaysQuery(undefined);
  const [publishRevision, { isLoading: publishing }] = usePublishOverlayRevisionMutation();
  const [archiveRevision, { isLoading: archiving }] = useArchiveOverlayRevisionMutation();

  const chains = data?.chains ?? [];
  const visible =
    filter === 'All' ? chains : chains.filter((c) => containsRevisionIn(c, filter));

  return (
    <section className="p-6">
      <header className="flex items-center justify-between mb-6">
        <h1 className="text-2xl font-semibold">Overlays</h1>
        <Button onClick={() => setDialogOpen(true)}>New overlay</Button>
      </header>

      <div className="mb-4 flex gap-2">
        {STATE_FILTERS.map((option) => (
          <button
            key={option}
            type="button"
            onClick={() => setFilter(option)}
            className={
              option === filter
                ? 'rounded-md border border-accent-active bg-accent-active/10 px-3 py-1 text-sm text-accent-active'
                : 'rounded-md border border-fg-muted/30 px-3 py-1 text-sm text-fg-muted'
            }
          >
            {option}
          </button>
        ))}
      </div>

      {error !== undefined && (
        <div
          role="alert"
          className="mb-4 rounded-md border border-accent-fault/40 bg-accent-fault/10 px-3 py-2 text-sm text-accent-fault"
        >
          Could not load overlays.{' '}
          <button type="button" className="underline" onClick={() => void refetch()}>
            Retry
          </button>
        </div>
      )}

      {(isLoading || isFetching) && <p className="text-sm text-fg-muted">Loading…</p>}

      {!isLoading && visible.length === 0 && (
        <p className="text-sm text-fg-muted">No overlays to show.</p>
      )}

      <ul className="flex flex-col gap-2">
        {visible.map((chain) => {
          const newest = newestRevision(chain);
          const disabled = publishing || archiving;
          return (
            <li
              key={chain.overlayIdentifier}
              className="rounded-md border border-fg-muted/30 bg-bg-elevated px-4 py-3"
            >
              <header className="flex items-center justify-between">
                <h2 className="text-lg font-medium">{chain.name}</h2>
                <span className="text-xs text-fg-muted">
                  v{newest.revisionNumber} · {newest.state}
                </span>
              </header>
              <p className="mt-1 text-xs text-fg-muted font-mono">{chain.overlayIdentifier}</p>
              <p className="mt-1 text-sm text-fg-muted truncate">{newest.text}</p>
              <div className="mt-3 flex gap-2">
                {newest.state === 'Draft' && (
                  <Button
                    variant="secondary"
                    disabled={disabled}
                    onClick={() =>
                      void publishRevision({
                        overlayIdentifier: chain.overlayIdentifier,
                        revisionNumber: newest.revisionNumber,
                      })
                    }
                  >
                    Publish
                  </Button>
                )}
                {newest.state !== 'Archived' && (
                  <Button
                    variant="secondary"
                    disabled={disabled}
                    onClick={() =>
                      void archiveRevision({
                        overlayIdentifier: chain.overlayIdentifier,
                        revisionNumber: newest.revisionNumber,
                      })
                    }
                  >
                    Archive
                  </Button>
                )}
              </div>
            </li>
          );
        })}
      </ul>

      <OverlayEditorDialog open={dialogOpen} onOpenChange={setDialogOpen} />
    </section>
  );
}

function newestRevision(chain: Overlay) {
  return chain.revisions.reduce((acc, r) => (r.revisionNumber > acc.revisionNumber ? r : acc));
}

function containsRevisionIn(chain: Overlay, state: OverlayRevisionState): boolean {
  return chain.revisions.some((r) => r.state === state);
}
