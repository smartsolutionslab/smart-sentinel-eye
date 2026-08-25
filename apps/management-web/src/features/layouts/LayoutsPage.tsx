import {
  useArchiveRevisionMutation,
  useBranchDraftRevisionMutation,
  useListLayoutsQuery,
  usePublishRevisionMutation,
  useRevertRevisionMutation,
  type Layout,
  type LayoutRevision,
  type LayoutRevisionState,
} from '@smart-sentinel-eye/shared/api/layouts.api';
import { isConflict, problemDetail } from '@smart-sentinel-eye/shared/api/problemDetail';
import { Button } from '@smart-sentinel-eye/shared/ui/primitives/Button';
import { useState } from 'react';
import { ArchiveConfirmation } from '../ArchiveConfirmation';
import { LayoutEditorDialog, type LayoutEditTarget } from './LayoutEditorDialog.js';

const STATE_FILTERS: ReadonlyArray<LayoutRevisionState | 'All'> = [
  'All',
  'Draft',
  'Published',
  'Archived',
];

export function LayoutsPage() {
  const [createOpen, setCreateOpen] = useState(false);
  const [editTarget, setEditTarget] = useState<LayoutEditTarget>();
  const [filter, setFilter] = useState<LayoutRevisionState | 'All'>('All');
  // Spec 036. Carries the revision's state as well as its number, because the
  // kiosk warning is conditional on it (FR-008) — a draft strands nothing and
  // disturbs no kiosk.
  const [archiveFor, setArchiveFor] = useState<{
    layoutIdentifier: string;
    name: string;
    revisionNumber: number;
    version: number;
    published: boolean;
  } | null>(null);

  const { data, isLoading, isFetching, error, refetch } = useListLayoutsQuery(undefined);
  const [publishRevision, publishState] = usePublishRevisionMutation();
  const [archiveRevision, archiveState] = useArchiveRevisionMutation();
  const [branchDraft, branchState] = useBranchDraftRevisionMutation();
  const [revertRevision, revertState] = useRevertRevisionMutation();

  const { isLoading: publishing } = publishState;
  const { isLoading: archiving } = archiveState;
  const { isLoading: branching } = branchState;
  const { isLoading: reverting } = revertState;

  // Every one of these used to discard its failure, so a rejected publish or
  // archive looked identical to a successful one. With optimistic concurrency
  // live (ADR-0113) a rejection is routine, not exceptional.
  const mutationError =
    publishState.error ?? archiveState.error ?? branchState.error ?? revertState.error;

  const chains = data?.chains ?? [];
  const visible = filter === 'All' ? chains : chains.filter((c) => containsRevisionIn(c, filter));

  // Edit-after-publish (US4): branch a new draft off the baseline, then open the
  // designer pre-loaded with that baseline's grid + tiles. The branch copies the
  // baseline verbatim, so the new draft's revision number is the baseline's + 1
  // (the returned number).
  //
  // Spec 037: `baseline` is the chain's newest revision, which is the Published
  // one on the ordinary path and the newest Archived one when recovering a
  // stranded chain (ADR-0121). The server picks the same revision by the same
  // rule; this only decides what the designer opens with. The parameter was
  // called `published` before, which was never quite what the call site passed.
  const onEdit = async (chain: Layout, baseline: LayoutRevision) => {
    const result = await branchDraft({ layoutIdentifier: chain.layoutIdentifier, version: chain.version });
    if ('error' in result) return;
    setEditTarget({
      layoutIdentifier: chain.layoutIdentifier,
      revisionNumber: result.data,
      name: chain.name,
      grid: { rows: baseline.gridRows, cols: baseline.gridCols },
      tiles: baseline.tiles,
    });
  };

  return (
    <section className="p-6">
      <header className="flex items-center justify-between mb-6">
        <h1 className="text-2xl font-semibold">Layouts</h1>
        <Button onClick={() => setCreateOpen(true)}>New layout</Button>
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
          Could not load layouts.{' '}
          <button type="button" className="underline" onClick={() => void refetch()}>
            Retry
          </button>
        </div>
      )}

      {mutationError !== undefined && (
        <div
          role="alert"
          className="mb-4 rounded-md border border-accent-fault/40 bg-accent-fault/10 px-3 py-2 text-sm text-accent-fault"
        >
          {problemDetail(mutationError, 'Could not apply that change.')}{' '}
          {isConflict(mutationError) && (
            // Reload, never retry: retrying replays the same stale intent over
            // whoever wrote in between.
            <button type="button" className="underline" onClick={() => void refetch()}>
              Reload
            </button>
          )}
        </div>
      )}

      {(isLoading || isFetching) && (
        <p className="text-sm text-fg-muted">Loading…</p>
      )}

      {!isLoading && visible.length === 0 && (
        <p className="text-sm text-fg-muted">No layouts to show.</p>
      )}

      <ul className="flex flex-col gap-2">
        {visible.map((chain) => {
          const newest = newestRevision(chain);
          const disabled = publishing || archiving || branching || reverting;
          return (
            <li
              key={chain.layoutIdentifier}
              className="rounded-md border border-fg-muted/30 bg-bg-elevated px-4 py-3"
            >
              <header className="flex items-center justify-between">
                <h2 className="text-lg font-medium">{chain.name}</h2>
                <span className="text-xs text-fg-muted">
                  v{newest.revisionNumber} · {newest.state}
                </span>
              </header>
              <p className="mt-1 text-xs text-fg-muted font-mono">
                {chain.layoutIdentifier}
              </p>
              <p className="mt-1 text-xs text-fg-muted">{tileSummary(newest)}</p>
              <div className="mt-3 flex gap-2">
                {newest.state === 'Draft' && (
                  <Button
                    variant="secondary"
                    disabled={disabled}
                    onClick={() =>
                      void publishRevision({
                        layoutIdentifier: chain.layoutIdentifier,
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
                    onClick={() => void onEdit(chain, newest)}
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
                        layoutIdentifier: chain.layoutIdentifier,
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
                        layoutIdentifier: chain.layoutIdentifier,
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
        Spec 037 FR-011/FR-012 replaces spec 036's sentence here.

        It used to say the layout could never be edited or published again,
        which was true and is not any more: ADR-0121 makes a fully-archived
        chain recoverable by editing it, tiles and all.

        Do not collapse this into "This cannot be undone" — that is now false in
        the other direction, and a warning that overstates is one operators learn
        to click through. What is still true, and still worth asking about, is
        that the wall goes out of service and kiosks are sent away at once.

        The kiosk sentence stays conditional (spec 036 FR-008). Archiving a draft
        disturbs no kiosk.
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
            layoutIdentifier: archiveFor.layoutIdentifier,
            revisionNumber: archiveFor.revisionNumber,
            version: archiveFor.version,
          });
          setArchiveFor(null);
        }}
      >
        <p>
          This takes the layout out of service. You can bring it back later by editing it, and{' '}
          <strong>the tiles are kept</strong>.
        </p>
        {archiveFor?.published === true && (
          <p>Kiosks showing this layout will be sent away from it immediately.</p>
        )}
      </ArchiveConfirmation>

      <LayoutEditorDialog open={createOpen} onOpenChange={setCreateOpen} />
      <LayoutEditorDialog
        open={editTarget !== undefined}
        onOpenChange={(next) => {
          if (!next) setEditTarget(undefined);
        }}
        editTarget={editTarget}
      />
    </section>
  );
}

function newestRevision(chain: Layout) {
  return chain.revisions.reduce((acc, r) => (r.revisionNumber > acc.revisionNumber ? r : acc));
}

function containsRevisionIn(chain: Layout, state: LayoutRevisionState): boolean {
  return chain.revisions.some((r) => r.state === state);
}

// Spec 037 (ADR-0121). Stranded: no Published revision and no Draft one — which,
// since every revision is one of the three, is the same set as "every revision
// archived". Tests the chain, not its newest row: a chain can hold a Published
// revision under an abandoned newer draft and is not stranded at all.
function isFullyArchived(chain: Layout): boolean {
  return chain.revisions.every((r) => r.state === 'Archived');
}

// Row summary (T023): the tile count + grid shape replaces the old single
// camera/identifier line, so a 2×2 wall reads "4 tiles, 2×2".
function tileSummary(revision: LayoutRevision): string {
  const count = revision.tiles.length;
  const noun = count === 1 ? 'tile' : 'tiles';
  return `${count} ${noun}, ${revision.gridRows}×${revision.gridCols}`;
}
