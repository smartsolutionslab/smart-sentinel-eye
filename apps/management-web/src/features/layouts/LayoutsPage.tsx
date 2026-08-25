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

  // Edit-after-publish (US4): branch a new draft off the Published baseline,
  // then open the designer pre-loaded with that baseline's grid + tiles. The
  // branch copies the baseline verbatim, so the new draft's revision number is
  // the published revision's + 1 (the returned number).
  const onEdit = async (chain: Layout, published: LayoutRevision) => {
    const result = await branchDraft({ layoutIdentifier: chain.layoutIdentifier, version: chain.version });
    if ('error' in result) return;
    setEditTarget({
      layoutIdentifier: chain.layoutIdentifier,
      revisionNumber: result.data,
      name: chain.name,
      grid: { rows: published.gridRows, cols: published.gridCols },
      tiles: published.tiles,
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
                {newest.state === 'Published' && (
                  <>
                    <Button
                      variant="secondary"
                      disabled={disabled}
                      onClick={() => void onEdit(chain, newest)}
                    >
                      Edit (new draft)
                    </Button>
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
                  </>
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
        Spec 036 FR-007, and the sharpest sentence in this feature.

        "This cannot be undone" is true of all four archive confirmations and
        understates this one. A layout does not merely stay archived: archiving
        its published revision leaves no published revision to branch or revert
        from and no draft to edit or publish, so the layout can never be edited
        or published again. Do not soften it. Issue 1877 tracks whether that
        should remain true.

        The kiosk sentence is conditional (FR-008). Archiving a draft strands
        nothing and disturbs no kiosk, and a warning that fires either way is
        one operators learn to click through.
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
          This cannot be undone, and <strong>this layout can never be edited or published again</strong>.
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

// Row summary (T023): the tile count + grid shape replaces the old single
// camera/identifier line, so a 2×2 wall reads "4 tiles, 2×2".
function tileSummary(revision: LayoutRevision): string {
  const count = revision.tiles.length;
  const noun = count === 1 ? 'tile' : 'tiles';
  return `${count} ${noun}, ${revision.gridRows}×${revision.gridCols}`;
}
