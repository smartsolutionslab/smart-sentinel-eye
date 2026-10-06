import {
  useArchiveOverlayRevisionMutation,
  useBranchDraftOverlayRevisionMutation,
  useListOverlaysQuery,
  usePublishOverlayRevisionMutation,
  useRevertOverlayRevisionMutation,
  type Overlay,
  type OverlayElement,
  type OverlayRevision,
  type OverlayTextElement,
  type OverlayRevisionState,
} from '@smart-sentinel-eye/shared/api/overlays.api';
import {
  CONFLICT_FALLBACK,
  isConflict,
  isStaleConflict,
  problemDetail,
} from '@smart-sentinel-eye/shared/api/problemDetail';
import { revisionSummary } from '@smart-sentinel-eye/shared/format/revisionSummary';
import { FaultNotice } from '@smart-sentinel-eye/shared/ui/composites/FaultNotice';
import { RetryBanner } from '@smart-sentinel-eye/shared/ui/composites/RetryBanner';
import { Button } from '@smart-sentinel-eye/shared/ui/primitives/Button';
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { ArchiveConfirmation } from '../ArchiveConfirmation';
import { chainView } from '../chainView.js';

const STATE_FILTERS: ReadonlyArray<OverlayRevisionState | 'All'> = ['All', 'Draft', 'Published', 'Archived'];

export function OverlaysPage() {
  const navigate = useNavigate();
  const [filter, setFilter] = useState<OverlayRevisionState | 'All'>('All');
  // Spec 036, narrowed by spec 038. The `published` flag is gone: Archive is
  // offered only when a live revision exists and targets that revision, so the
  // flag was true every time this opened. The case it used to cover is now a
  // different confirmation entirely.
  const [archiveFor, setArchiveFor] = useState<{
    overlayIdentifier: string;
    name: string;
    revisionNumber: number;
    version: number;
  } | null>(null);
  // Spec 038 FR-005. Its own state, because it is its own action on its own
  // revision. `liveRevision` is undefined when nothing is published, which
  // decides whether the confirmation can honestly reassure the operator that
  // the overlay stays as it is.
  const [discardFor, setDiscardFor] = useState<{
    overlayIdentifier: string;
    name: string;
    revisionNumber: number;
    version: number;
    liveRevision: number | undefined;
  } | null>(null);

  const { data, isLoading, isFetching, error, refetch } = useListOverlaysQuery(undefined);
  const [publishRevision, publishState] = usePublishOverlayRevisionMutation();
  const [archiveRevision, archiveState] = useArchiveOverlayRevisionMutation();
  const [branchDraft, branchState] = useBranchDraftOverlayRevisionMutation();
  const [revertRevision, revertState] = useRevertOverlayRevisionMutation();

  const { isLoading: publishing } = publishState;
  const { isLoading: archiving } = archiveState;
  const { isLoading: branching } = branchState;
  const { isLoading: reverting } = revertState;

  // The twin of LayoutsPage's banner, and missing here for the same reason it
  // was missing there: every one of these discarded its failure, so a refused
  // publish or archive looked identical to a successful one.
  const mutationError = publishState.error ?? archiveState.error ?? branchState.error ?? revertState.error;

  const chains = data?.chains ?? [];
  const visible = filter === 'All' ? chains : chains.filter((c) => containsRevisionIn(c, filter));

  // Spec 152 US2, spec 305 (#2350) FR-002: the branch is itself a write, so
  // it happens first and only on success does the page navigate — to the
  // branched revision the server actually returned, not a seed computed
  // here. A URL carries no list state (spec 305's declared scope decision),
  // so `OverlayEditPage` re-reads the chain itself; this page no longer
  // needs a baseline to seed anything with.
  const onEdit = async (chain: Overlay) => {
    const result = await branchDraft({ overlayIdentifier: chain.overlayIdentifier, version: chain.version });
    if ('error' in result) return;
    navigate(`/overlays/${chain.overlayIdentifier}/revisions/${result.data}/edit`);
  };

  return (
    <section className="p-6">
      <header className="flex items-center justify-between mb-6">
        <h1 className="text-2xl font-semibold">Overlays</h1>
        <Button onClick={() => navigate('/overlays/new')}>New overlay</Button>
      </header>

      <div className="mb-4 flex gap-2">
        {STATE_FILTERS.map((option) => (
          <button
            key={option}
            type="button"
            onClick={() => setFilter(option)}
            className={
              option === filter
                ? 'rounded-md border border-accent bg-accent-subtle px-3 py-1 text-sm text-accent'
                : 'rounded-md border border-fg-muted/30 px-3 py-1 text-sm text-fg-muted'
            }
          >
            {option}
          </button>
        ))}
      </div>

      {error !== undefined && <RetryBanner message="Could not load overlays." onRetry={() => void refetch()} />}

      {mutationError !== undefined && (
        <FaultNotice>
          {problemDetail(
            mutationError,
            isStaleConflict(mutationError) ? CONFLICT_FALLBACK : 'Could not apply that change.',
          )}{' '}
          {isConflict(mutationError) && (
            // Reload, never retry: retrying replays the same stale intent over
            // whoever wrote in between.
            <button type="button" className="underline" onClick={() => void refetch()}>
              Reload
            </button>
          )}
        </FaultNotice>
      )}

      {(isLoading || isFetching) && <p className="text-sm text-fg-muted">Loading…</p>}

      {!isLoading && visible.length === 0 && <p className="text-sm text-fg-muted">No overlays to show.</p>}

      <ul className="flex flex-col gap-2">
        {visible.map((chain) => {
          // Spec 038: read the CHAIN, not its newest revision. The twin of
          // LayoutsPage and for the same reason — deciding from `newest` left a
          // live overlay under a discarded draft offering nothing, and an
          // Archive button that discarded a draft under a false warning.
          //
          // Spec 305 (#2350): `newest` itself is no longer read here. US2
          // used to seed the dialog from `live ?? newest` as the branch's
          // baseline; a route carries no seed (spec 305's declared scope
          // decision), so `onEdit` now only needs `chain.version` for the
          // branch call, and `OverlayEditPage` re-reads the branched
          // revision's content itself. `fullyArchived` is still read below —
          // it is true only when a baseline (`newest`) exists, which is what
          // the "Edit (new draft)" gate itself needs.
          const { live, draft, summarised, fullyArchived } = chainView(chain.revisions);
          const disabled = publishing || archiving || branching || reverting;
          return (
            <li key={chain.overlayIdentifier} className="rounded-md border border-fg-muted/30 bg-bg-elevated px-4 py-3">
              <header className="flex items-center justify-between">
                <h2 className="text-lg font-medium">{chain.name}</h2>
                <span className="text-xs text-fg-muted">{revisionSummary(live, draft, summarised)}</span>
              </header>
              <p className="mt-1 text-xs text-fg-muted font-mono">{chain.overlayIdentifier}</p>
              {summarised !== undefined && (
                <p className="mt-1 text-sm text-fg-muted truncate">{firstTextOrShapesOnly(summarised.elements)}</p>
              )}
              <div className="mt-3 flex gap-2">
                {draft !== undefined && (
                  <Button
                    variant="secondary"
                    unavailable={disabled}
                    onClick={() => {
                      if (disabled) return;
                      void publishRevision({
                        overlayIdentifier: chain.overlayIdentifier,
                        revisionNumber: draft.revisionNumber,
                        version: chain.version,
                      });
                    }}
                  >
                    Publish
                  </Button>
                )}
                {draft !== undefined && (
                  <Button
                    variant="secondary"
                    unavailable={disabled}
                    onClick={() => {
                      // ADR-0151 focus-return: refuse a second confirmation
                      // while a mutation on this row is already in flight.
                      if (disabled) return;
                      setDiscardFor({
                        overlayIdentifier: chain.overlayIdentifier,
                        name: chain.name,
                        revisionNumber: draft.revisionNumber,
                        version: chain.version,
                        liveRevision: live?.revisionNumber,
                      });
                    }}
                  >
                    Discard draft
                  </Button>
                )}
                {/*
                  Spec 152 US1, spec 305 (#2350) FR-002. Edits the draft that
                  already exists — the action neither this row nor
                  LayoutsPage's offered before, and the one the issue's own
                  scenario needed: a freshly created overlay is `{D}`, where
                  `Edit (new draft)`'s gate below is false. Sends nothing on
                  click — a direct navigation to the draft's own edit URL,
                  and only Save writes.
                */}
                {draft !== undefined && (
                  <Button
                    variant="secondary"
                    disabled={disabled}
                    onClick={() => navigate(`/overlays/${chain.overlayIdentifier}/revisions/${draft.revisionNumber}/edit`)}
                  >
                    Edit draft
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
                    unavailable={disabled}
                    // The gate above already proves a baseline exists: `live`
                    // defined covers the first half directly, and
                    // `fullyArchived` (chainView.ts) is true only when
                    // `newest` is defined too. A guarded no-op here would be
                    // a click that does nothing and says nothing — the
                    // defect class FR-013 exists to reject by name.
                    onClick={() => {
                      if (disabled) return;
                      void onEdit(chain);
                    }}
                  >
                    Edit (new draft)
                  </Button>
                )}
                {live !== undefined && (
                  <Button
                    variant="secondary"
                    unavailable={disabled}
                    onClick={() => {
                      if (disabled) return;
                      void revertRevision({
                        overlayIdentifier: chain.overlayIdentifier,
                        revisionNumber: live.revisionNumber,
                        version: chain.version,
                      });
                    }}
                  >
                    Revert
                  </Button>
                )}
                {/*
                  Archive targets the LIVE revision. It used to target `newest`,
                  so on a chain with an open draft it archived the draft while
                  the confirmation said the overlay was going out of service.
                  Both requests succeed, which is why that went unnoticed.
                */}
                {live !== undefined && (
                  <Button
                    variant="secondary"
                    unavailable={disabled}
                    onClick={() => {
                      // ADR-0151 focus-return: refuse a second confirmation
                      // while a mutation on this row is already in flight.
                      if (disabled) return;
                      setArchiveFor({
                        overlayIdentifier: chain.overlayIdentifier,
                        name: chain.name,
                        revisionNumber: live.revisionNumber,
                        version: chain.version,
                      });
                    }}
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
        subject={archiveFor === null ? null : `revision ${archiveFor.revisionNumber} of ${archiveFor.name}`}
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
        <p>Kiosks using this overlay will stop showing it.</p>
      </ArchiveConfirmation>

      {/*
        Spec 038 FR-005/FR-006. A DIFFERENT confirmation, not the same one with
        softer words — the two must not sound alike, because one word doing both
        jobs is what let this row tell an operator their live overlay was going
        out of service when it was discarding a draft.

        It must not say "out of service", must not mention kiosks, and must not
        offer to bring anything back: none of those happen. A discarded draft is
        gone, and editing afterwards branches a NEW draft from the live revision.
      */}
      <ArchiveConfirmation
        verb="Discard"
        subject={discardFor === null ? null : `draft revision ${discardFor.revisionNumber} of ${discardFor.name}`}
        onCancel={() => setDiscardFor(null)}
        pending={archiving}
        onConfirm={() => {
          if (discardFor === null) {
            return;
          }
          void archiveRevision({
            overlayIdentifier: discardFor.overlayIdentifier,
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
            {discardFor.name} stays exactly as it is — revision {discardFor.liveRevision} is still published and kiosks
            are unaffected.
          </p>
        )}
      </ArchiveConfirmation>
    </section>
  );
}

function containsRevisionIn(chain: Overlay, state: OverlayRevisionState): boolean {
  return chain.revisions.some((r) => r.state === state);
}

function isTextElement(element: OverlayElement): element is OverlayTextElement {
  return element.kind === 'Text';
}

// Spec 300 (#2349), ADR-0165: the row summary shows the first Text
// element's text, or "Shapes only" when the revision carries none (FR-019).
function firstTextOrShapesOnly(elements: OverlayElement[]): string {
  const firstText = elements.find(isTextElement);
  return firstText?.text ?? 'Shapes only';
}
