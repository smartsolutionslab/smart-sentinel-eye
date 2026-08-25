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
import { chainView } from '../chainView.js';
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
  // Spec 036, narrowed by spec 038. The `published` flag is gone: Archive is
  // offered only when a live revision exists and targets that revision, so the
  // flag was true every time this opened. A flag that is always true is worse
  // than no flag — it reads as a live condition and invites a caller to pass
  // false. The case it used to cover is now a different confirmation entirely.
  const [archiveFor, setArchiveFor] = useState<{
    layoutIdentifier: string;
    name: string;
    revisionNumber: number;
    version: number;
  } | null>(null);
  // Spec 038 FR-005. Its own state, because it is its own action on its own
  // revision. `liveRevision` is undefined when nothing is published, which
  // decides whether the confirmation can honestly reassure the operator that
  // the layout stays as it is.
  const [discardFor, setDiscardFor] = useState<{
    layoutIdentifier: string;
    name: string;
    revisionNumber: number;
    version: number;
    liveRevision: number | undefined;
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
          // Spec 038: read the CHAIN, not its newest revision. A chain is not
          // its newest revision, and deciding from `newest` left a live wall
          // under a discarded draft offering nothing at all, and an Archive
          // button that archived a draft while promising the wall was going
          // out of service.
          const { live, draft, newest, summarised, fullyArchived } = chainView(chain.revisions);
          const disabled = publishing || archiving || branching || reverting;
          return (
            <li
              key={chain.layoutIdentifier}
              className="rounded-md border border-fg-muted/30 bg-bg-elevated px-4 py-3"
            >
              <header className="flex items-center justify-between">
                <h2 className="text-lg font-medium">{chain.name}</h2>
                <span className="text-xs text-fg-muted">{badge(live, draft, summarised)}</span>
              </header>
              <p className="mt-1 text-xs text-fg-muted font-mono">
                {chain.layoutIdentifier}
              </p>
              {summarised !== undefined && (
                <p className="mt-1 text-xs text-fg-muted">{tileSummary(summarised)}</p>
              )}
              <div className="mt-3 flex gap-2">
                {draft !== undefined && (
                  <Button
                    variant="secondary"
                    disabled={disabled}
                    onClick={() =>
                      void publishRevision({
                        layoutIdentifier: chain.layoutIdentifier,
                        revisionNumber: draft.revisionNumber,
                        version: chain.version,
                      })
                    }
                  >
                    Publish
                  </Button>
                )}
                {draft !== undefined && (
                  <Button
                    variant="secondary"
                    disabled={disabled}
                    onClick={() =>
                      setDiscardFor({
                        layoutIdentifier: chain.layoutIdentifier,
                        name: chain.name,
                        revisionNumber: draft.revisionNumber,
                        version: chain.version,
                        liveRevision: live?.revisionNumber,
                      })
                    }
                  >
                    Discard draft
                  </Button>
                )}
                {/*
                  Offered while a draft is open as well (spec 038 FR-003). That
                  is also the app's route to a chain with two open drafts —
                  recorded as observed rather than fixed, because suppressing it
                  reverses a stated requirement.
                */}
                {(live !== undefined || fullyArchived) && (
                  <Button
                    variant="secondary"
                    disabled={disabled}
                    onClick={() => {
                      const baseline = live ?? newest;
                      if (baseline !== undefined) {
                        void onEdit(chain, baseline);
                      }
                    }}
                  >
                    Edit (new draft)
                  </Button>
                )}
                {live !== undefined && (
                  <Button
                    variant="secondary"
                    disabled={disabled}
                    onClick={() =>
                      void revertRevision({
                        layoutIdentifier: chain.layoutIdentifier,
                        revisionNumber: live.revisionNumber,
                        version: chain.version,
                      })
                    }
                  >
                    Revert
                  </Button>
                )}
                {/*
                  Archive targets the LIVE revision. It used to target `newest`,
                  so on a chain with an open draft it archived the draft while
                  the confirmation said the layout was going out of service.
                  Both requests succeed, which is why that went unnoticed.
                */}
                {live !== undefined && (
                  <Button
                    variant="secondary"
                    disabled={disabled}
                    onClick={() =>
                      setArchiveFor({
                        layoutIdentifier: chain.layoutIdentifier,
                        name: chain.name,
                        revisionNumber: live.revisionNumber,
                        version: chain.version,
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
        <p>Kiosks showing this layout will be sent away from it immediately.</p>
      </ArchiveConfirmation>

      {/*
        Spec 038 FR-005/FR-006. A DIFFERENT confirmation, not the same one with
        softer words — the two must not sound alike, because one word doing both
        jobs is what let this row tell an operator their live wall was going out
        of service when it was discarding a draft.

        It must not say "out of service", must not mention kiosks, and must not
        offer to bring anything back: none of those happen. A discarded draft is
        gone, and editing afterwards branches a NEW draft from the live revision.
      */}
      <ArchiveConfirmation
        verb="Discard"
        subject={
          discardFor === null
            ? null
            : `draft revision ${discardFor.revisionNumber} of ${discardFor.name}`
        }
        onCancel={() => setDiscardFor(null)}
        pending={archiving}
        onConfirm={() => {
          if (discardFor === null) {
            return;
          }
          void archiveRevision({
            layoutIdentifier: discardFor.layoutIdentifier,
            revisionNumber: discardFor.revisionNumber,
            version: discardFor.version,
          });
          setDiscardFor(null);
        }}
      >
        <p>
          This throws away the draft. <strong>The work in it cannot be recovered.</strong>
        </p>
        {discardFor?.liveRevision !== undefined && (
          <p>
            {discardFor.name} stays exactly as it is — revision {discardFor.liveRevision} is still
            published and kiosks are unaffected.
          </p>
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

function containsRevisionIn(chain: Layout, state: LayoutRevisionState): boolean {
  return chain.revisions.some((r) => r.state === state);
}

// Spec 038 FR-009. Names the LIVE revision, because that is the one on kiosks,
// and says when a draft is open without hiding either. The row used to report
// its newest revision, so a live wall under a discarded draft read as "Archived"
// while it was playing on the floor.
//
// `Published` appears exactly when a live revision exists, which is what keeps
// the two e2e assertions that read this text matching.
function badge(
  live: LayoutRevision | undefined,
  draft: LayoutRevision | undefined,
  summarised: LayoutRevision | undefined,
): string {
  if (live !== undefined) {
    return draft === undefined
      ? `v${live.revisionNumber} · Published`
      : `v${live.revisionNumber} · Published · draft v${draft.revisionNumber}`;
  }
  return summarised === undefined ? '' : `v${summarised.revisionNumber} · ${summarised.state}`;
}

// Row summary (T023): the tile count + grid shape replaces the old single
// camera/identifier line, so a 2×2 wall reads "4 tiles, 2×2".
function tileSummary(revision: LayoutRevision): string {
  const count = revision.tiles.length;
  const noun = count === 1 ? 'tile' : 'tiles';
  return `${count} ${noun}, ${revision.gridRows}×${revision.gridCols}`;
}
