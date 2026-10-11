import { describe, expect, it } from 'vitest';
import type { DeadLetter } from '@smart-sentinel-eye/shared/api/eventIngestion.api';
import { groupHeldEventTypes } from './groupHeldEventTypes.js';

function row(overrides: Partial<DeadLetter>): DeadLetter {
  return {
    deadLetterIdentifier: 'dl-1',
    topic: 'fab/munich/plc/line-1',
    rawPayload: '{}',
    error: 'unregistered type',
    rejectedAt: '2026-01-01T00:00:00Z',
    fab: 'munich',
    reason: 'UnknownEventType',
    kind: 'TemperatureAlarm',
    state: 'Held',
    ...overrides,
  };
}

describe('groupHeldEventTypes (issue #2780)', () => {
  it('Groups repeated rows for the same (fab, kind) into one entry with the right count', () => {
    const groups = groupHeldEventTypes([
      row({ deadLetterIdentifier: 'dl-1', rejectedAt: '2026-01-01T00:00:00Z' }),
      row({ deadLetterIdentifier: 'dl-2', rejectedAt: '2026-01-02T00:00:00Z' }),
      row({ deadLetterIdentifier: 'dl-3', rejectedAt: '2026-01-03T00:00:00Z' }),
    ]);

    expect(groups).toHaveLength(1);
    expect(groups[0]).toMatchObject({ fab: 'munich', kind: 'TemperatureAlarm', count: 3 });
  });

  it('Reports the earliest row as firstRejectedAt and the latest as lastRejectedAt, regardless of input order', () => {
    const groups = groupHeldEventTypes([
      row({ deadLetterIdentifier: 'dl-2', rejectedAt: '2026-01-02T00:00:00Z' }),
      row({ deadLetterIdentifier: 'dl-1', rejectedAt: '2026-01-01T00:00:00Z' }),
      row({ deadLetterIdentifier: 'dl-3', rejectedAt: '2026-01-05T00:00:00Z' }),
    ]);

    expect(groups[0]).toMatchObject({
      firstRejectedAt: '2026-01-01T00:00:00Z',
      lastRejectedAt: '2026-01-05T00:00:00Z',
    });
  });

  it('Keeps a different fab in a separate group even when the kind matches', () => {
    const groups = groupHeldEventTypes([row({ fab: 'munich' }), row({ fab: 'berlin' })]);

    expect(groups).toHaveLength(2);
    expect(groups.map((g) => g.fab).sort()).toEqual(['berlin', 'munich']);
  });

  it('Keeps a different kind in a separate group even when the fab matches', () => {
    const groups = groupHeldEventTypes([row({ kind: 'TemperatureAlarm' }), row({ kind: 'PressureAlarm' })]);

    expect(groups).toHaveLength(2);
    expect(groups.map((g) => g.kind).sort()).toEqual(['PressureAlarm', 'TemperatureAlarm']);
  });

  it('Groups a null fab and a null kind together, rather than treating null as a unique-per-row value', () => {
    const groups = groupHeldEventTypes([
      row({ deadLetterIdentifier: 'dl-1', fab: null, kind: null }),
      row({ deadLetterIdentifier: 'dl-2', fab: null, kind: null }),
    ]);

    expect(groups).toHaveLength(1);
    expect(groups[0]?.count).toBe(2);
  });

  it('Returns an empty array for no rows', () => {
    expect(groupHeldEventTypes([])).toEqual([]);
  });
});
