import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { store } from '../../app/store.js';
import type { AuditPage as AuditPageData, AuditRow } from '@smart-sentinel-eye/shared/api/audit.api';

const searchMock = vi.fn();

vi.mock('@smart-sentinel-eye/shared/api/audit.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/audit.api')>();
  return {
    ...actual,
    useSearchAuditQuery: (...args: unknown[]) => searchMock(...args),
  };
});

const { AuditPage } = await import('./AuditPage.js');

function auditRow(overrides: Partial<AuditRow> = {}): AuditRow {
  return {
    auditIdentifier: '11111111-1111-1111-1111-111111111111',
    occurredAt: '2026-05-30T10:00:00Z',
    receivedAt: '2026-05-30T10:00:00Z',
    fab: 'munich',
    eventKind: 'CameraRegisteredV1',
    resourceKind: 'camera',
    resourceIdentifier: '33333333-3333-3333-3333-333333333333',
    actorIdentifier: '22222222-2222-2222-2222-222222222222',
    actorIsSystem: false,
    actorUsername: 'admin@munich.test',
    eventIdentifier: '44444444-4444-4444-4444-444444444444',
    payload: '{"cameraIdentifier":"33333333-3333-3333-3333-333333333333"}',
    payloadSizeBytes: 2,
    schemaVersion: 1,
    ...overrides,
  };
}

function result(rows: AuditRow[], nextCursor: string | null = null) {
  return {
    data: { rows, nextCursor } satisfies AuditPageData,
    isLoading: false,
    isFetching: false,
    error: undefined,
    refetch: vi.fn(),
  };
}

function renderPage() {
  return render(
    <Provider store={store}>
      <AuditPage />
    </Provider>,
  );
}

describe('AuditPage', () => {
  const originalTz = globalThis.process.env.TZ;

  beforeEach(() => {
    searchMock.mockReset();
  });

  afterEach(() => {
    if (originalTz === undefined) {
      delete globalThis.process.env.TZ;
    } else {
      globalThis.process.env.TZ = originalTz;
    }
  });

  it('Shows an empty-state message when there are no audit events', () => {
    searchMock.mockReturnValue(result([]));
    renderPage();
    expect(screen.getByText(/no audit events match these filters/i)).toBeInTheDocument();
  });

  it('Renders one row per audit event with event, resource and actor', () => {
    searchMock.mockReturnValue(result([auditRow()]));
    renderPage();
    expect(screen.getByText('CameraRegisteredV1')).toBeInTheDocument();
    expect(screen.getByText(/camera \/ 33333333/)).toBeInTheDocument();
    expect(screen.getByText('admin@munich.test')).toBeInTheDocument();
  });

  it('Applying a filter searches with the typed event kind', async () => {
    const user = userEvent.setup();
    searchMock.mockReturnValue(result([auditRow()]));

    renderPage();
    await user.type(screen.getByLabelText('Event kind'), 'CameraRegisteredV1');
    await user.click(screen.getByRole('button', { name: 'Search' }));

    expect(searchMock).toHaveBeenLastCalledWith(expect.objectContaining({ eventKind: 'CameraRegisteredV1' }));
  });

  it('Expands a row to show its JSON payload', async () => {
    const user = userEvent.setup();
    searchMock.mockReturnValue(result([auditRow()]));

    renderPage();
    await user.click(screen.getByRole('button', { name: 'View' }));
    expect(screen.getByText(/"cameraIdentifier": "33333333-3333-3333-3333-333333333333"/)).toBeInTheDocument();
  });

  it('Shows a retry control when the search query fails', async () => {
    const user = userEvent.setup();
    const refetch = vi.fn();
    searchMock.mockReturnValue({
      data: undefined,
      isLoading: false,
      isFetching: false,
      error: { status: 500 },
      refetch,
    });

    renderPage();
    await user.click(screen.getByRole('button', { name: /retry/i }));
    expect(refetch).toHaveBeenCalledOnce();
  });

  it('Applying a Since filter sends an ISO instant, not a bare local wall clock', async () => {
    const user = userEvent.setup();
    searchMock.mockReturnValue(result([]));

    renderPage();
    fireEvent.change(screen.getByLabelText(/^Since/i), { target: { value: '2026-09-17T08:00' } });
    await user.click(screen.getByRole('button', { name: 'Search' }));

    expect(searchMock).toHaveBeenLastCalledWith(
      expect.objectContaining({ since: new Date('2026-09-17T08:00').toISOString() }),
    );
  });

  it('A Since filter typed east of UTC is sent as the instant it means', async () => {
    globalThis.process.env.TZ = 'Europe/Berlin';
    const user = userEvent.setup();
    searchMock.mockReturnValue(result([]));

    renderPage();
    fireEvent.change(screen.getByLabelText(/^Since/i), { target: { value: '2026-09-17T08:00' } });
    await user.click(screen.getByRole('button', { name: 'Search' }));

    expect(searchMock).toHaveBeenLastCalledWith(expect.objectContaining({ since: '2026-09-17T06:00:00.000Z' }));
  });

  it('A Since filter typed west of UTC is sent as the instant it means', async () => {
    globalThis.process.env.TZ = 'America/New_York';
    const user = userEvent.setup();
    searchMock.mockReturnValue(result([]));

    renderPage();
    fireEvent.change(screen.getByLabelText(/^Since/i), { target: { value: '2026-09-17T08:00' } });
    await user.click(screen.getByRole('button', { name: 'Search' }));

    expect(searchMock).toHaveBeenLastCalledWith(expect.objectContaining({ since: '2026-09-17T12:00:00.000Z' }));
  });

  it('Both bounds are converted', async () => {
    const user = userEvent.setup();
    searchMock.mockReturnValue(result([]));

    renderPage();
    fireEvent.change(screen.getByLabelText(/^Since/i), { target: { value: '2026-09-17T08:00' } });
    fireEvent.change(screen.getByLabelText(/^Until/i), { target: { value: '2026-09-17T18:00' } });
    await user.click(screen.getByRole('button', { name: 'Search' }));

    const issued = searchMock.mock.calls.at(-1)?.[0] as { since?: string; until?: string };
    expect(issued.since).not.toBe('2026-09-17T08:00');
    expect(issued.until).not.toBe('2026-09-17T18:00');
    expect(issued.since).toMatch(/Z$/);
    expect(issued.until).toMatch(/Z$/);
  });

  it('Clearing the filters sends no date bounds', async () => {
    const user = userEvent.setup();
    searchMock.mockReturnValue(result([]));

    renderPage();
    fireEvent.change(screen.getByLabelText(/^Since/i), { target: { value: '2026-09-17T08:00' } });
    fireEvent.change(screen.getByLabelText(/^Until/i), { target: { value: '2026-09-17T18:00' } });
    await user.click(screen.getByRole('button', { name: 'Search' }));
    await user.click(screen.getByRole('button', { name: 'Clear' }));

    const issued = searchMock.mock.calls.at(-1)?.[0] as { since?: string; until?: string };
    expect(issued.since).toBeUndefined();
    expect(issued.until).toBeUndefined();
  });

  it('An unparseable date value drops the filter instead of throwing', async () => {
    const user = userEvent.setup();
    searchMock.mockReturnValue(result([]));

    renderPage();
    // A native <input type="datetime-local"> sanitises an invalid value straight
    // back to '' on assignment (jsdom included), so an unparseable value can only
    // reach FilterDraft.since through some other writer than the DOM sanitiser —
    // flip the type attribute to bypass it and drive the value through unchanged.
    const since = screen.getByLabelText(/^Since/i);
    since.setAttribute('type', 'text');
    fireEvent.change(since, { target: { value: 'not-a-date' } });
    await user.click(screen.getByRole('button', { name: 'Search' }));

    const issued = searchMock.mock.calls.at(-1)?.[0] as { since?: string };
    expect(issued.since).toBeUndefined();
    expect(screen.getByRole('button', { name: 'Search' })).toBeInTheDocument();
  });

  it('The non-date filters are sent verbatim', async () => {
    const user = userEvent.setup();
    searchMock.mockReturnValue(result([]));

    renderPage();
    await user.type(screen.getByLabelText('Event kind'), 'CameraRegisteredV1');
    await user.type(screen.getByLabelText('Actor'), 'admin@munich.test');
    await user.click(screen.getByRole('button', { name: 'Search' }));

    expect(searchMock).toHaveBeenLastCalledWith(
      expect.objectContaining({ eventKind: 'CameraRegisteredV1', actorUsername: 'admin@munich.test' }),
    );
  });

  it('The date filters name the clock they are interpreted in', () => {
    searchMock.mockReturnValue(result([]));
    renderPage();

    expect(screen.getByLabelText(/Since \(your local time\)/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/Until \(your local time\)/i)).toBeInTheDocument();
  });
});
