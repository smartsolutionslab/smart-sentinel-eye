import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
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
  beforeEach(() => {
    searchMock.mockReset();
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

    expect(searchMock).toHaveBeenLastCalledWith(
      expect.objectContaining({ eventKind: 'CameraRegisteredV1' }),
    );
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
});
