import type { DeadLetter } from '@smart-sentinel-eye/shared/api/eventIngestion.api';

/**
 * Issue #2780 — the operator inspects held event types grouped by
 * `(fab, kind)`, not as one row per rejected delivery: a discovery source
 * producing the same unregistered kind repeatedly would otherwise flood the
 * list with what is really one decision ("register this kind, or not").
 */
export interface HeldEventTypeGroup {
  readonly fab: string | null;
  readonly kind: string | null;
  readonly reason: string;
  readonly count: number;
  readonly firstRejectedAt: string;
  readonly lastRejectedAt: string;
}

function groupKey(row: DeadLetter): string {
  return `${row.fab ?? ''}\u0000${row.kind ?? ''}`;
}

/**
 * Pure grouping over whatever page of rows the query returned — no sorting
 * assumption on the input, since `ListDeadLettersQuery`'s own ordering is an
 * implementation detail of the handler, not a contract this groups by.
 */
export function groupHeldEventTypes(rows: readonly DeadLetter[]): HeldEventTypeGroup[] {
  const groups = new Map<string, HeldEventTypeGroup>();

  for (const row of rows) {
    const key = groupKey(row);
    const existing = groups.get(key);
    if (existing === undefined) {
      groups.set(key, {
        fab: row.fab,
        kind: row.kind,
        reason: row.reason,
        count: 1,
        firstRejectedAt: row.rejectedAt,
        lastRejectedAt: row.rejectedAt,
      });
      continue;
    }

    groups.set(key, {
      ...existing,
      count: existing.count + 1,
      firstRejectedAt: row.rejectedAt < existing.firstRejectedAt ? row.rejectedAt : existing.firstRejectedAt,
      lastRejectedAt: row.rejectedAt > existing.lastRejectedAt ? row.rejectedAt : existing.lastRejectedAt,
    });
  }

  return [...groups.values()];
}
