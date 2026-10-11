import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { Provider } from 'react-redux';
import { createApiStore } from '@smart-sentinel-eye/shared/store';
import { eventIngestionApi, type DeadLetter } from '@smart-sentinel-eye/shared/api/eventIngestion.api';

const store = createApiStore([eventIngestionApi]);

const listHeldEventTypesMock = vi.fn();

vi.mock('@smart-sentinel-eye/shared/api/eventIngestion.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/eventIngestion.api')>();
  return {
    ...actual,
    useListHeldEventTypesQuery: (...args: unknown[]) => listHeldEventTypesMock(...args),
  };
});

const { HeldEventTypesPage } = await import('./HeldEventTypesPage.js');

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

function renderPage() {
  return render(
    <Provider store={store}>
      <HeldEventTypesPage />
    </Provider>,
  );
}

describe('HeldEventTypesPage (issue #2780, US1)', () => {
  it('Shows a loading state while the query is in flight', () => {
    listHeldEventTypesMock.mockReturnValue({ data: undefined, isLoading: true, error: undefined, refetch: vi.fn() });

    renderPage();

    expect(screen.getByText('Loading…')).toBeInTheDocument();
  });

  it('Shows a retry banner when the query fails', () => {
    listHeldEventTypesMock.mockReturnValue({
      data: undefined,
      isLoading: false,
      error: { status: 500 },
      refetch: vi.fn(),
    });

    renderPage();

    expect(screen.getByText('Could not load held event types.')).toBeInTheDocument();
  });

  it('Shows an empty-state message when there are no held event types', () => {
    listHeldEventTypesMock.mockReturnValue({ data: [], isLoading: false, error: undefined, refetch: vi.fn() });

    renderPage();

    expect(screen.getByText('No held event types to show.')).toBeInTheDocument();
  });

  it('Groups rows by (fab, kind) and shows the group count, not one row per delivery', () => {
    listHeldEventTypesMock.mockReturnValue({
      data: [
        row({ deadLetterIdentifier: 'dl-1', rejectedAt: '2026-01-01T00:00:00Z' }),
        row({ deadLetterIdentifier: 'dl-2', rejectedAt: '2026-01-03T00:00:00Z' }),
        row({ deadLetterIdentifier: 'dl-3', fab: 'berlin', kind: 'PressureAlarm', rejectedAt: '2026-01-02T00:00:00Z' }),
      ],
      isLoading: false,
      error: undefined,
      refetch: vi.fn(),
    });

    renderPage();

    const rows = screen.getAllByRole('row');
    // One header row + two group rows (munich/TemperatureAlarm count 2, berlin/PressureAlarm count 1).
    expect(rows).toHaveLength(3);

    expect(screen.getByText('munich')).toBeInTheDocument();
    expect(screen.getByText('TemperatureAlarm')).toBeInTheDocument();
    expect(screen.getByText('berlin')).toBeInTheDocument();
    expect(screen.getByText('PressureAlarm')).toBeInTheDocument();
  });
});
