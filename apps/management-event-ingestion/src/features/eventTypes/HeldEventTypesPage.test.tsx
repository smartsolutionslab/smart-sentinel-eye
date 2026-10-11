import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { createApiStore } from '@smart-sentinel-eye/shared/store';
import {
  eventIngestionApi,
  HELD_EVENT_TYPES_LIMIT,
  type DeadLetter,
} from '@smart-sentinel-eye/shared/api/eventIngestion.api';
import { eventTypesApi } from '@smart-sentinel-eye/shared/api/eventTypes.api';

const store = createApiStore([eventIngestionApi, eventTypesApi]);

const listHeldEventTypesMock = vi.fn();

vi.mock('@smart-sentinel-eye/shared/api/eventIngestion.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/eventIngestion.api')>();
  return {
    ...actual,
    useListHeldEventTypesQuery: (...args: unknown[]) => listHeldEventTypesMock(...args),
  };
});

const promoteEventTypeMock = vi.fn();
// A plain mutable ref, not component state: usePromoteEventTypeMutation's
// mocked hook reads it on every render, mirroring RTK Query's own mutation
// state updating independently of whatever the caller does with the
// returned promise (should-fix 1, PR #2857 review).
const promoteErrorRef: { current: unknown } = { current: undefined };

vi.mock('@smart-sentinel-eye/shared/api/eventTypes.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/eventTypes.api')>();
  return {
    ...actual,
    usePromoteEventTypeMutation: () => [
      promoteEventTypeMock,
      {
        isLoading: false,
        get error() {
          return promoteErrorRef.current;
        },
      },
    ],
  };
});

const useAuthMock = vi.fn();

vi.mock('react-oidc-context', () => ({
  useAuth: () => useAuthMock(),
}));

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
  beforeEach(() => {
    useAuthMock.mockReturnValue({ user: { scope: 'sse.events.read' } });
    promoteErrorRef.current = undefined;
    promoteEventTypeMock.mockReturnValue({ unwrap: () => Promise.resolve('d290f1ee-6c54-4b01-90e6-d701748f0851') });
  });

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

    // Nit (PR #2856 review): the query is hardcoded to
    // reason=UnknownEventType, so every row shows the identical value — not
    // information, and dropped from the table.
    expect(screen.queryByText('Reason', { selector: 'th' })).not.toBeInTheDocument();
  });

  it('Renders through the shared DataTable composite, with an accessible caption', () => {
    listHeldEventTypesMock.mockReturnValue({ data: [], isLoading: false, error: undefined, refetch: vi.fn() });

    renderPage();

    expect(screen.getByRole('table', { name: 'Held event types' })).toBeInTheDocument();
  });

  it('A response at the server limit shows a partial-data notice, not a silent truncation', () => {
    const rows = Array.from({ length: HELD_EVENT_TYPES_LIMIT }, (_unused, index) =>
      row({ deadLetterIdentifier: `dl-${index}`, kind: `Kind${index}` }),
    );
    listHeldEventTypesMock.mockReturnValue({ data: rows, isLoading: false, error: undefined, refetch: vi.fn() });

    renderPage();

    expect(screen.getByRole('status')).toHaveTextContent(`Showing the newest ${HELD_EVENT_TYPES_LIMIT} deliveries`);
  });

  it('A response under the limit shows no partial-data notice', () => {
    listHeldEventTypesMock.mockReturnValue({
      data: [row({ deadLetterIdentifier: 'dl-1' })],
      isLoading: false,
      error: undefined,
      refetch: vi.fn(),
    });

    renderPage();

    expect(screen.queryByRole('status')).not.toBeInTheDocument();
  });
});

describe('HeldEventTypesPage — Promote (issue #2855, slice 2)', () => {
  beforeEach(() => {
    promoteErrorRef.current = undefined;
    promoteEventTypeMock.mockReturnValue({ unwrap: () => Promise.resolve('d290f1ee-6c54-4b01-90e6-d701748f0851') });
    listHeldEventTypesMock.mockReturnValue({
      data: [row({ deadLetterIdentifier: 'dl-1', fab: 'munich', kind: 'TemperatureAlarm' })],
      isLoading: false,
      error: undefined,
      refetch: vi.fn(),
    });
  });

  it('Shows no Promote button when the operator lacks sse.events.types.write', () => {
    useAuthMock.mockReturnValue({ user: { scope: 'sse.events.read' } });

    renderPage();

    expect(screen.queryByRole('button', { name: /promote/i })).not.toBeInTheDocument();
  });

  it('Shows a Promote button per held row when the operator holds sse.events.types.write', () => {
    useAuthMock.mockReturnValue({ user: { scope: 'sse.events.read sse.events.types.write' } });

    renderPage();

    expect(screen.getByRole('button', { name: /promote/i })).toBeInTheDocument();
  });

  it("Clicking Promote calls the mutation with the row's own fab and kind, not re-derived", async () => {
    useAuthMock.mockReturnValue({ user: { scope: 'sse.events.types.write' } });
    renderPage();

    await userEvent.click(screen.getByRole('button', { name: /promote/i }));

    expect(promoteEventTypeMock).toHaveBeenCalledWith({ fab: 'munich', kind: 'TemperatureAlarm' });
  });

  it('A fab-neutral held group (fab is null) shows no Promote button — there is no fab to name', () => {
    useAuthMock.mockReturnValue({ user: { scope: 'sse.events.types.write' } });
    listHeldEventTypesMock.mockReturnValue({
      data: [row({ deadLetterIdentifier: 'dl-1', fab: null, kind: 'ParseFailureKind' })],
      isLoading: false,
      error: undefined,
      refetch: vi.fn(),
    });

    renderPage();

    expect(screen.queryByRole('button', { name: /promote/i })).not.toBeInTheDocument();
  });

  it('A group with a fab but a null kind shows no Promote button either — the server rejects an empty kind (nit, PR #2857 review)', () => {
    useAuthMock.mockReturnValue({ user: { scope: 'sse.events.types.write' } });
    listHeldEventTypesMock.mockReturnValue({
      data: [row({ deadLetterIdentifier: 'dl-1', fab: 'munich', kind: null })],
      isLoading: false,
      error: undefined,
      refetch: vi.fn(),
    });

    renderPage();

    expect(screen.queryByRole('button', { name: /promote/i })).not.toBeInTheDocument();
  });

  it('On a successful promote, refetches the held list so the row leaves without a full page reload', async () => {
    useAuthMock.mockReturnValue({ user: { scope: 'sse.events.types.write' } });
    const refetch = vi.fn();
    listHeldEventTypesMock.mockReturnValue({
      data: [row({ deadLetterIdentifier: 'dl-1', fab: 'munich', kind: 'TemperatureAlarm' })],
      isLoading: false,
      error: undefined,
      refetch,
    });

    renderPage();
    await userEvent.click(screen.getByRole('button', { name: /promote/i }));

    expect(refetch).toHaveBeenCalled();
  });

  it('Disables the clicked Promote button while the mutation is in flight (ADR-0151 unavailable, holds focus)', async () => {
    useAuthMock.mockReturnValue({ user: { scope: 'sse.events.types.write' } });
    let resolvePromote: (value: string) => void = () => {};
    promoteEventTypeMock.mockReturnValue({
      unwrap: () =>
        new Promise<string>((resolve) => {
          resolvePromote = resolve;
        }),
    });
    renderPage();

    const button = screen.getByRole('button', { name: /promote/i });
    await userEvent.click(button);

    expect(button).toHaveAttribute('aria-disabled', 'true');

    resolvePromote('d290f1ee-6c54-4b01-90e6-d701748f0851');
  });

  it("Marks every row's Promote button unavailable while one is in flight, but busy only on the clicked row (nit, PR #2857 review)", async () => {
    useAuthMock.mockReturnValue({ user: { scope: 'sse.events.types.write' } });
    let resolvePromote: (value: string) => void = () => {};
    promoteEventTypeMock.mockReturnValue({
      unwrap: () =>
        new Promise<string>((resolve) => {
          resolvePromote = resolve;
        }),
    });
    listHeldEventTypesMock.mockReturnValue({
      data: [
        row({ deadLetterIdentifier: 'dl-1', fab: 'munich', kind: 'TemperatureAlarm' }),
        row({ deadLetterIdentifier: 'dl-2', fab: 'berlin', kind: 'PressureAlarm' }),
      ],
      isLoading: false,
      error: undefined,
      refetch: vi.fn(),
    });
    renderPage();

    const buttons = screen.getAllByRole('button', { name: /promote/i });
    expect(buttons).toHaveLength(2);
    const [clicked, other] = buttons as [HTMLElement, HTMLElement];
    await userEvent.click(clicked);

    expect(clicked).toHaveAttribute('aria-disabled', 'true');
    expect(clicked).toHaveAttribute('aria-busy', 'true');
    expect(other).toHaveAttribute('aria-disabled', 'true');
    expect(other).not.toHaveAttribute('aria-busy', 'true');

    resolvePromote('d290f1ee-6c54-4b01-90e6-d701748f0851');
  });

  it('Shows a FaultNotice with the problem detail when the promote mutation rejects, and still refetches (should-fix 1/2, PR #2857 review)', async () => {
    useAuthMock.mockReturnValue({ user: { scope: 'sse.events.types.write' } });
    const refetch = vi.fn();
    listHeldEventTypesMock.mockReturnValue({
      data: [row({ deadLetterIdentifier: 'dl-1', fab: 'munich', kind: 'TemperatureAlarm' })],
      isLoading: false,
      error: undefined,
      refetch,
    });
    const rejection = { status: 403, data: { detail: 'Not authorised for this fab.' } };
    promoteEventTypeMock.mockReturnValue({
      unwrap: () => {
        promoteErrorRef.current = rejection;
        return Promise.reject(rejection);
      },
    });
    renderPage();

    // No unhandled rejection: the click resolves (the component's own catch
    // observes it), and the error is what the FaultNotice below reads.
    await userEvent.click(screen.getByRole('button', { name: /promote/i }));

    expect(screen.getByRole('alert')).toHaveTextContent('Not authorised for this fab.');
    expect(refetch).toHaveBeenCalled();
  });

  it('Treats a 409 "already registered" rejection as a non-fault outcome, not a FaultNotice, and still refetches (should-fix 2, PR #2857 review)', async () => {
    useAuthMock.mockReturnValue({ user: { scope: 'sse.events.types.write' } });
    const refetch = vi.fn();
    listHeldEventTypesMock.mockReturnValue({
      data: [row({ deadLetterIdentifier: 'dl-1', fab: 'munich', kind: 'TemperatureAlarm' })],
      isLoading: false,
      error: undefined,
      refetch,
    });
    const conflict = { status: 409, data: { detail: 'Already registered.' } };
    promoteEventTypeMock.mockReturnValue({
      unwrap: () => {
        promoteErrorRef.current = conflict;
        return Promise.reject(conflict);
      },
    });
    renderPage();

    await userEvent.click(screen.getByRole('button', { name: /promote/i }));

    expect(screen.getByText('Already registered — held rows released.')).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(refetch).toHaveBeenCalled();
  });
});
