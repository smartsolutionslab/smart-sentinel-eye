import { useMemo } from 'react';
import { useListHeldEventTypesQuery, HELD_EVENT_TYPES_LIMIT } from '@smart-sentinel-eye/shared/api/eventIngestion.api';
import { RetryBanner } from '@smart-sentinel-eye/shared/ui/composites/RetryBanner';
import { DataTable, type DataTableColumn } from '@smart-sentinel-eye/shared/ui/composites/DataTable';
import { groupHeldEventTypes, type HeldEventTypeGroup } from './groupHeldEventTypes.js';

/**
 * Issue #2780 (US1) — an operator inspects event types held for being
 * unknown to a `discovery`-mode source, grouped by `(fab, kind)` so a
 * repeatedly-rejected kind reads as one decision rather than a flood of
 * individual rows. Read-only: the Promote action is slice 2 (#2855).
 */
export function HeldEventTypesPage() {
  const { data, isLoading, error, refetch } = useListHeldEventTypesQuery(undefined);

  const groups = useMemo(() => groupHeldEventTypes(data ?? []), [data]);

  // Should-fix 1 (PR #2856 review): the query already asks for the server's
  // stated maximum (`HELD_EVENT_TYPES_LIMIT`). A response that still comes
  // back exactly at that limit means older held rows exist beyond it — and
  // every group's own count/first-seen describes only this page, not the
  // true total — so that must read as partial, not silently as complete.
  const atLimit = (data?.length ?? 0) >= HELD_EVENT_TYPES_LIMIT;

  const columns: DataTableColumn<HeldEventTypeGroup>[] = [
    { id: 'fab', header: 'Fab', cell: (group) => group.fab ?? '—' },
    { id: 'kind', header: 'Kind', cell: (group) => group.kind ?? '—' },
    { id: 'count', header: 'Count', cell: (group) => group.count },
    { id: 'firstSeen', header: 'First seen', cell: (group) => new Date(group.firstRejectedAt).toLocaleString() },
    { id: 'lastSeen', header: 'Last seen', cell: (group) => new Date(group.lastRejectedAt).toLocaleString() },
  ];

  return (
    <section className="p-6">
      <header className="mb-6">
        <h1 className="text-2xl font-semibold">Event types</h1>
        <p className="text-sm text-fg-muted">
          Event types held for being unknown to a discovery-mode source, in the fabs you hold.
        </p>
      </header>

      {error !== undefined ? (
        <RetryBanner message="Could not load held event types." onRetry={() => void refetch()} />
      ) : (
        <>
          {atLimit && (
            <p role="status" className="mb-3 text-sm text-fg-muted">
              Showing the newest {HELD_EVENT_TYPES_LIMIT} deliveries; counts and groups may be partial.
            </p>
          )}
          <DataTable
            columns={columns}
            rows={groups}
            getRowKey={(group) => `${group.fab ?? ''}/${group.kind ?? ''}`}
            isLoading={isLoading}
            emptyMessage="No held event types to show."
            caption="Held event types"
          />
        </>
      )}
    </section>
  );
}
