import { type ComponentRef, useMemo, useRef, useState } from 'react';
import { useAuth } from 'react-oidc-context';
import { useListHeldEventTypesQuery, HELD_EVENT_TYPES_LIMIT } from '@smart-sentinel-eye/shared/api/eventIngestion.api';
import { usePromoteEventTypeMutation } from '@smart-sentinel-eye/shared/api/eventTypes.api';
import { isConflict, problemDetail } from '@smart-sentinel-eye/shared/api/problemDetail';
import { grantedScopes } from '@smart-sentinel-eye/shared/navigation/grantedScopes';
import { RetryBanner } from '@smart-sentinel-eye/shared/ui/composites/RetryBanner';
import { FaultNotice } from '@smart-sentinel-eye/shared/ui/composites/FaultNotice';
import { DataTable, type DataTableColumn } from '@smart-sentinel-eye/shared/ui/composites/DataTable';
import { Button } from '@smart-sentinel-eye/shared/ui/primitives/Button';
import { groupHeldEventTypes, type HeldEventTypeGroup } from './groupHeldEventTypes.js';

const TYPES_WRITE_SCOPE = 'sse.events.types.write';

function groupRowKey(group: HeldEventTypeGroup): string {
  return `${group.fab ?? ''}/${group.kind ?? ''}`;
}

/**
 * Issue #2780 (US1) — an operator inspects event types held for being
 * unknown to a `discovery`-mode source, grouped by `(fab, kind)` so a
 * repeatedly-rejected kind reads as one decision rather than a flood of
 * individual rows.
 *
 * Issue #2855 (slice 2) — Promote. Shown only when the token carries
 * `sse.events.types.write` (client-side hide; the server's 403 remains the
 * real authority, ADR-0168's Consequences) and only for a group that has a
 * fab — a fab-neutral held row (`fab: null`, a `ParseFailure` row's own
 * kind-less cousin per spec 317 FR-002) has no fab to name in `?fabId=`, and
 * `POST /event-types` requires one explicitly (ADR-0114). Same reasoning
 * hides it for a `kind: null` row (PR #2857 review nit): the server would
 * otherwise see an empty string and reject it with 400 `EVENT_TYPE_INVALID_INPUT`.
 */
export function HeldEventTypesPage() {
  const headingRef = useRef<ComponentRef<'h1'>>(null);
  const { data, isLoading, isFetching, error, refetch } = useListHeldEventTypesQuery(undefined);
  const { user } = useAuth();
  const [promoteEventType, { error: promoteError }] = usePromoteEventTypeMutation();
  const [promotingKey, setPromotingKey] = useState<string | null>(null);

  // Should-fix 2 (PR #2857 review): RegisterEventTypeCommandHandler also
  // promotes on its 409 "already registered" path (spec 317 convergence), so
  // that outcome is not a fault — it is treated as a non-fault, informational
  // outcome below instead of the FaultNotice should-fix 1 adds for every
  // other rejection.
  const promoteConflict = isConflict(promoteError);
  const promoteFault =
    promoteError !== undefined && !promoteConflict
      ? problemDetail(promoteError, 'Could not promote this event type.')
      : null;

  const scope = user?.scope;
  const scopes = useMemo(() => grantedScopes({ scope }), [scope]);
  const canPromote = scopes.has(TYPES_WRITE_SCOPE);

  const groups = useMemo(() => groupHeldEventTypes(data ?? []), [data]);

  // Should-fix 1 (PR #2856 review): the query already asks for the server's
  // stated maximum (`HELD_EVENT_TYPES_LIMIT`). A response that still comes
  // back exactly at that limit means older held rows exist beyond it — and
  // every group's own count/first-seen describes only this page, not the
  // true total — so that must read as partial, not silently as complete.
  const atLimit = (data?.length ?? 0) >= HELD_EVENT_TYPES_LIMIT;

  async function handlePromote(group: HeldEventTypeGroup) {
    const key = groupRowKey(group);
    if (promotingKey !== null || group.fab === null || group.kind === null) return;

    setPromotingKey(key);
    try {
      await promoteEventType({ fab: group.fab, kind: group.kind }).unwrap();
    } catch {
      // Surfaced below via the mutation hook's own `error` state (FaultNotice
      // for a real fault, a non-fault notice for 409) — nothing further to do
      // here. Catching keeps this an observed rejection, not an unhandled one.
    } finally {
      setPromotingKey(null);
      // Should-fix 2: refetch on every outcome, not only success — the
      // handler's 409 path promotes held rows server-side too, and the list
      // must not keep showing them as still held.
      refetch();
    }
  }

  const columns: DataTableColumn<HeldEventTypeGroup>[] = [
    { id: 'fab', header: 'Fab', cell: (group) => group.fab ?? '—' },
    { id: 'kind', header: 'Kind', cell: (group) => group.kind ?? '—' },
    { id: 'count', header: 'Count', cell: (group) => group.count },
    { id: 'firstSeen', header: 'First seen', cell: (group) => new Date(group.firstRejectedAt).toLocaleString() },
    { id: 'lastSeen', header: 'Last seen', cell: (group) => new Date(group.lastRejectedAt).toLocaleString() },
    ...(canPromote
      ? [
          {
            id: 'actions',
            header: '',
            cell: (group: HeldEventTypeGroup) => {
              if (group.fab === null || group.kind === null) return null;
              const busy = promotingKey === groupRowKey(group);
              return (
                <Button
                  variant="secondary"
                  unavailable={promotingKey !== null}
                  busy={busy}
                  onClick={() => {
                    void handlePromote(group);
                  }}
                >
                  Promote
                </Button>
              );
            },
          },
        ]
      : []),
  ];

  return (
    <section className="p-6">
      <header className="mb-6">
        <h1 ref={headingRef} tabIndex={-1} className="text-2xl font-semibold focus:outline-none">
          Event types
        </h1>
        <p className="text-sm text-fg-muted">
          Event types held for being unknown to a discovery-mode source, in the fabs you hold.
        </p>
      </header>

      <RetryBanner
        failed={error !== undefined}
        retrying={isFetching}
        onRecovered={() => headingRef.current?.focus()}
        message="Could not load held event types."
        onRetry={() => refetch()}
      />
      {error === undefined && (
        <>
          {promoteFault !== null && <FaultNotice>{promoteFault}</FaultNotice>}
          {promoteConflict && (
            <p role="status" className="mb-3 text-sm text-fg-muted">
              Already registered — held rows released.
            </p>
          )}
          {atLimit && (
            <p role="status" className="mb-3 text-sm text-fg-muted">
              Showing the newest {HELD_EVENT_TYPES_LIMIT} deliveries; counts and groups may be partial.
            </p>
          )}
          <DataTable
            columns={columns}
            rows={groups}
            getRowKey={groupRowKey}
            isLoading={isLoading}
            emptyMessage="No held event types to show."
            caption="Held event types"
          />
        </>
      )}
    </section>
  );
}
