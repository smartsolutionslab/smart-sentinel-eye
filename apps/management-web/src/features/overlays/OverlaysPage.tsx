import {
  useArchiveOverlayRevisionMutation,
  useBranchDraftOverlayRevisionMutation,
  useListOverlaysQuery,
  usePublishOverlayRevisionMutation,
  useRevertOverlayRevisionMutation,
  type Overlay,
  type OverlayRevisionState,
} from '@smart-sentinel-eye/shared/api/overlays.api';
import { Button } from '@smart-sentinel-eye/shared/ui/primitives/Button';
import { useState } from 'react';
import { ArchiveConfirmation } from '../ArchiveConfirmation';
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
  // Spec 036. Carries the revision's state, because the kiosk warning is
  // conditional on it (FR-008) — a draft strands nothing and no kiosk is
  // showing one.
  const [archiveFor, setArchiveFor] = useState<{
    overlayIdentifier: string;
    name: string;
    revisionNumber: number;
    version: number;
    published: boolean;
  } | null>(null);

  const { data, isLoading, isFetching, error, refetch } = useListOverlaysQuery(undefined);
  const [publishRevision, { isLoading: publishing }] = usePublishOverlayRevisionMutation();
  const [archiveRevision, { isLoading: archiving }] = useArchiveOverlayRevisionMutation();
  const [branchDraft, { isLoading: branching }] = useBranchDraftOverlayRevisionMutation();
  const [revertRevision, { isLoading: reverting }] = useRevertOverlayRevisionMutation();

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
          const disabled = publishing || archiving || branching || reverting;
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
                        version: chain.version,
                      })
                    }
                  >
                    Publish
                  </Button>
                )}
                {/*
                  Spec 037: a fully-archived chain is recoverable (ADR-0121), and
                  the edit action is how. Not `newest.state === 'Archived'` —
                  a chain can hold a Published revision under an abandoned newer
                  draft, and that one is not stranded (issue 1879 covers the
                  separate problem that it is offered nothing at all).
                */}
                {(newest.state === 'Published' || isFullyArchived(chain)) && (
                  <Button
                    variant="secondary"
                    disabled={disabled}
                    onClick={() => void branchDraft({ overlayIdentifier: chain.overlayIdentifier, version: chain.version })}
                  >
                    Edit (new draft)
                  </Button>
                )}
                {newest.state === 'Published' && (
                  <Button
                    variant="secondary"
                    disabled={disabled}
                    onClick={() =>
                      void revertRevision({
                        overlayIdentifier: chain.overlayIdentifier,
                        revisionNumber: newest.revisionNumber,
                        version: chain.version,
                      })
                    }
                  >
                    Revert
                  </Button>
                )}
                {newest.state !== 'Archived' && (
                  <Button
                    variant="secondary"
                    disabled={disabled}
                    onClick={() =>
                      setArchiveFor({
                        overlayIdentifier: chain.overlayIdentifier,
                        name: chain.name,
                        revisionNumber: newest.revisionNumber,
                        version: chain.version,
                        published: newest.state === 'Published',
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

      {/*
        Spec 037 FR-011/FR-012 replaces spec 036's sentence here, same as
        LayoutsPage and for the same reason: ADR-0121 makes a fully-archived
        chain recoverable by editing it, so "can never be edited or published
        again" stopped being true.

        Do not collapse this into "This cannot be undone" — now false in the
        other direction. What is still true is that the overlay goes out of
        service and kiosks stop showing it.

        The kiosk consequence differs in kind from a layout's and the wording
        follows it — an archived overlay is marked unavailable in the cells
        using it, rather than navigating the kiosk away. Still conditional on
        Published (spec 036 FR-008).
      */}
      <ArchiveConfirmation
        subject={
          archiveFor === null ? null : `revision ${archiveFor.revisionNumber} of ${archiveFor.name}`
        }
        onCancel={() => setArchiveFor(null)}
        pending={archiving}
        onConfirm={() => {
          if (archiveFor === null) {
            return;
          }
          void archiveRevision({
            overlayIdentifier: archiveFor.overlayIdentifier,
            revisionNumber: archiveFor.revisionNumber,
            version: archiveFor.version,
          });
          setArchiveFor(null);
        }}
      >
        <p>
          This takes the overlay out of service. You can bring it back later by editing it, and{' '}
          <strong>the label is kept</strong>.
        </p>
        {archiveFor?.published === true && (
          <p>Kiosks using this overlay will stop showing it.</p>
        )}
      </ArchiveConfirmation>

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

// Spec 037 (ADR-0121). Stranded: no Published revision and no Draft one — which,
// since every revision is one of the three, is the same set as "every revision
// archived". Tests the chain, not its newest row: a chain can hold a Published
// revision under an abandoned newer draft and is not stranded at all.
function isFullyArchived(chain: Overlay): boolean {
  return chain.revisions.every((r) => r.state === 'Archived');
}
