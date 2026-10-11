import { useMemo } from 'react';
import { useListHeldEventTypesQuery } from '@smart-sentinel-eye/shared/api/eventIngestion.api';
import { RetryBanner } from '@smart-sentinel-eye/shared/ui/composites/RetryBanner';
import { groupHeldEventTypes } from './groupHeldEventTypes.js';

/**
 * Issue #2780 (US1) — an operator inspects event types held for being
 * unknown to a `discovery`-mode source, grouped by `(fab, kind)` so a
 * repeatedly-rejected kind reads as one decision rather than a flood of
 * individual rows. Read-only: the Promote action is slice 2 (#2855).
 */
export function HeldEventTypesPage() {
  const { data, isLoading, error, refetch } = useListHeldEventTypesQuery(undefined);

  const groups = useMemo(() => groupHeldEventTypes(data ?? []), [data]);

  return (
    <section className="p-6">
      <header className="mb-6">
        <h1 className="text-2xl font-semibold">Event types</h1>
        <p className="text-sm text-fg-muted">
          Event types held for being unknown to a discovery-mode source, in the fabs you hold.
        </p>
      </header>

      {error !== undefined && <RetryBanner message="Could not load held event types." onRetry={() => void refetch()} />}

      {isLoading && <p className="text-sm text-fg-muted">Loading…</p>}
      {!isLoading && error === undefined && groups.length === 0 && (
        <p className="text-sm text-fg-muted">No held event types to show.</p>
      )}

      {groups.length > 0 && (
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-fg-muted/30 text-left text-fg-muted">
              <th className="py-2 pr-4">Fab</th>
              <th className="py-2 pr-4">Kind</th>
              <th className="py-2 pr-4">Reason</th>
              <th className="py-2 pr-4">Count</th>
              <th className="py-2 pr-4">First seen</th>
              <th className="py-2 pr-4">Last seen</th>
            </tr>
          </thead>
          <tbody>
            {groups.map((group) => (
              <tr key={`${group.fab ?? ''}/${group.kind ?? ''}`} className="border-b border-fg-muted/10">
                <td className="py-2 pr-4">{group.fab ?? '—'}</td>
                <td className="py-2 pr-4">{group.kind ?? '—'}</td>
                <td className="py-2 pr-4">{group.reason}</td>
                <td className="py-2 pr-4">{group.count}</td>
                <td className="py-2 pr-4">{new Date(group.firstRejectedAt).toLocaleString()}</td>
                <td className="py-2 pr-4">{new Date(group.lastRejectedAt).toLocaleString()}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}
