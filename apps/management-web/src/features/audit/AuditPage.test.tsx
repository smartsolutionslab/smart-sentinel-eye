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

    // Spec 317 (#2751): AuditPage now passes `{ refetchOnFocus: true }` as a
    // second argument on every call — `expect.anything()` tolerates it
    // without this test caring what it is.
    expect(searchMock).toHaveBeenLastCalledWith(
      expect.objectContaining({ eventKind: 'CameraRegisteredV1' }),
      expect.anything(),
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

  it('Applying a Since filter sends an ISO instant, not a bare local wall clock', async () => {
    const user = userEvent.setup();
    searchMock.mockReturnValue(result([]));

    renderPage();
    fireEvent.change(screen.getByLabelText(/^Since/i), { target: { value: '2026-09-17T08:00' } });
    await user.click(screen.getByRole('button', { name: 'Search' }));

    // Spec 317 (#2751): see the comment on the first `toHaveBeenLastCalledWith` above.
    expect(searchMock).toHaveBeenLastCalledWith(
      expect.objectContaining({ since: new Date('2026-09-17T08:00').toISOString() }),
      expect.anything(),
    );
  });

  it('A Since filter typed east of UTC is sent as the instant it means', async () => {
    globalThis.process.env.TZ = 'Europe/Berlin';
    const user = userEvent.setup();
    searchMock.mockReturnValue(result([]));

    renderPage();
    fireEvent.change(screen.getByLabelText(/^Since/i), { target: { value: '2026-09-17T08:00' } });
    await user.click(screen.getByRole('button', { name: 'Search' }));

    // Spec 317 (#2751): see the comment on the first `toHaveBeenLastCalledWith` above.
    expect(searchMock).toHaveBeenLastCalledWith(
      expect.objectContaining({ since: '2026-09-17T06:00:00.000Z' }),
      expect.anything(),
    );
  });

  it('A Since filter typed west of UTC is sent as the instant it means', async () => {
    globalThis.process.env.TZ = 'America/New_York';
    const user = userEvent.setup();
    searchMock.mockReturnValue(result([]));

    renderPage();
    fireEvent.change(screen.getByLabelText(/^Since/i), { target: { value: '2026-09-17T08:00' } });
    await user.click(screen.getByRole('button', { name: 'Search' }));

    // Spec 317 (#2751): see the comment on the first `toHaveBeenLastCalledWith` above.
    expect(searchMock).toHaveBeenLastCalledWith(
      expect.objectContaining({ since: '2026-09-17T12:00:00.000Z' }),
      expect.anything(),
    );
  });

  it('Both bounds are converted', async () => {
    const user = userEvent.setup();
    searchMock.mockReturnValue(result([]));

    renderPage();
    fireEvent.change(screen.getByLabelText(/^Since/i), { target: { value: '2026-09-17T08:00' } });
    fireEvent.change(screen.getByLabelText(/^Until/i), { target: { value: '2026-09-17T18:00' } });
    await user.click(screen.getByRole('button', { name: 'Search' }));

    const issued = searchMock.mock.calls.at(-1)?.[0] as { since?: string; until?: string };
    expect(issued.since).toBe(new Date('2026-09-17T08:00').toISOString());
    expect(issued.until).toBe(new Date('2026-09-17T18:00').toISOString());
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

    // Spec 317 (#2751): see the comment on the first `toHaveBeenLastCalledWith` above.
    expect(searchMock).toHaveBeenLastCalledWith(
      expect.objectContaining({ eventKind: 'CameraRegisteredV1', actorUsername: 'admin@munich.test' }),
      expect.anything(),
    );
  });

  it('The date filters name the clock they are interpreted in', () => {
    searchMock.mockReturnValue(result([]));
    renderPage();

    expect(screen.getByLabelText(/Since \(your local time\)/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/Until \(your local time\)/i)).toBeInTheDocument();
  });
});

/**
 * Spec 273 (#2632) / ADR-0151 — A1, new behaviour, RED. `AuditPage` still
 * passes native `disabled={data?.nextCursor === null || data?.nextCursor ===
 * undefined}` to Next (`:200`) — the terminal mechanism (spec.md §3): once
 * the loaded page is the last one, Next disables itself as a *result* of its
 * own activation, not merely on click.
 */
describe('AuditPage — Next keeps focus on the terminal page (spec 273 A1)', () => {
  beforeEach(() => {
    searchMock.mockReset();
  });

  it('Announces Next as unavailable with aria-disabled, not native disabled, once nextCursor is null', () => {
    searchMock.mockReturnValue(result([auditRow()], null));
    renderPage();

    const next = screen.getByRole('button', { name: /^next$/i });
    expect(next).toHaveAttribute('aria-disabled', 'true');
    expect(next).not.toHaveAttribute('disabled');
  });

  /**
   * Plan.md §3.6 — this guard prevents a real regression: an unguarded
   * activation here sets `cursor: data?.nextCursor ?? undefined`, which is
   * the FIRST page (spec.md §3). A green pin today: native `disabled` already
   * blocks the click before any guard code exists, so no re-render (and so no
   * re-search) follows it.
   */
  it('Triggers no re-search — and so no reset to the first page — while Next is unavailable on the terminal page', () => {
    searchMock.mockReturnValue(result([auditRow()], null));
    renderPage();

    const callsBefore = searchMock.mock.calls.length;
    fireEvent.click(screen.getByRole('button', { name: /^next$/i }));

    expect(searchMock.mock.calls.length).toBe(callsBefore);
  });
});

/**
 * Spec 310 (#2725) T004, plan.md §5 row 4. Three consecutive 403 refreshes of
 * the same search must drop the stale rows — the page renders exactly as a
 * first-load refusal does (FR-004). RED today: `AuditPage.tsx` wires no
 * revocation fallback, so `data` keeps the last successful search regardless
 * of how many 403s accumulate.
 */
describe('AuditPage — revocation fallback, three consecutive 403s (spec 310 #2725)', () => {
  beforeEach(() => {
    searchMock.mockReset();
  });

  function forbiddenRefresh(requestId: string) {
    return {
      data: { rows: [auditRow()], nextCursor: null } satisfies AuditPageData,
      // A repeated failure of the SAME argument set keeps RTK Query's own
      // cache entry's `data` (what `currentData` exposes) — only a successful
      // response for a *different* arg set evicting it clears it, never a
      // rejected refetch of the one that produced it.
      currentData: { rows: [auditRow()], nextCursor: null } satisfies AuditPageData,
      isLoading: false,
      isFetching: false,
      error: { status: 403 },
      requestId,
      refetch: vi.fn(),
    };
  }

  it('Keeps the stale rows after only two consecutive 403s', () => {
    searchMock.mockReturnValue(forbiddenRefresh('audit-r1'));
    const { rerender } = renderPage();

    searchMock.mockReturnValue(forbiddenRefresh('audit-r2'));
    rerender(
      <Provider store={store}>
        <AuditPage />
      </Provider>,
    );

    expect(screen.getByText('CameraRegisteredV1')).toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent(/could not load the audit trail/i);
  });

  it('Shows no stale row and the existing failure banner, exactly as a first-load refusal, after a third consecutive 403', () => {
    searchMock.mockReturnValue(forbiddenRefresh('audit-r1'));
    const { rerender, container } = renderPage();

    searchMock.mockReturnValue(forbiddenRefresh('audit-r2'));
    rerender(
      <Provider store={store}>
        <AuditPage />
      </Provider>,
    );

    searchMock.mockReturnValue(forbiddenRefresh('audit-r3'));
    rerender(
      <Provider store={store}>
        <AuditPage />
      </Provider>,
    );

    expect(screen.queryByText('CameraRegisteredV1')).toBeNull();
    expect(screen.getByRole('alert')).toHaveTextContent(/could not load the audit trail/i);

    // FR-004: byte-for-byte the same render as a first-load failure.
    searchMock.mockReturnValue({
      data: undefined,
      isLoading: false,
      isFetching: false,
      error: { status: 500 },
      refetch: vi.fn(),
    });
    const { container: firstLoadRefusal } = renderPage();

    expect(container.innerHTML).toBe(firstLoadRefusal.innerHTML);
  });

  /**
   * Phase 6 finding S1. `AuditPage` passed `fetched` (`useSearchAuditQuery`'s
   * raw `data`) straight through once `refused` was false, with no regard for
   * `error`. RTK Query's `data` falls back to `lastResult?.data` whenever the
   * *current* argument set's request has not succeeded — so once the operator
   * applies a new filter, the fallback resets to `false` for the new subject
   * (FR-003), and if the new argument set's first request then 403s, this bug
   * rendered the OLD filter's rows instead of nothing. `currentData` is
   * `undefined` here because the new argument set has never once succeeded.
   */
  it("Does not show a previous filter's stale rows when the new filter's own request is refused", async () => {
    const user = userEvent.setup();
    searchMock.mockReturnValue(forbiddenRefresh('audit-leak-r1'));
    const { rerender } = renderPage();

    searchMock.mockReturnValue(forbiddenRefresh('audit-leak-r2'));
    rerender(
      <Provider store={store}>
        <AuditPage />
      </Provider>,
    );

    searchMock.mockReturnValue(forbiddenRefresh('audit-leak-r3'));
    rerender(
      <Provider store={store}>
        <AuditPage />
      </Provider>,
    );

    expect(screen.queryByText('CameraRegisteredV1')).toBeNull();

    // The operator applies a new filter **for real** — submitting the form,
    // not a prop change, so `useRevocationFallback`'s own subject
    // (`JSON.stringify(applied)`, keyed off this page's actual `applied`
    // state) changes for real and resets its strike count (FR-003). The new
    // argument set's first request 403s — `data` (fetched) still carries the
    // OLD filter's row via RTK's `lastResult` fallback, but `currentData` is
    // undefined because this new argument set has never succeeded.
    searchMock.mockReturnValue({
      data: { rows: [auditRow()], nextCursor: null } satisfies AuditPageData,
      currentData: undefined,
      isLoading: false,
      isFetching: false,
      error: { status: 403 },
      requestId: 'audit-leak-new-filter-1',
      refetch: vi.fn(),
    });

    await user.type(screen.getByLabelText('Event kind'), 'CameraRetiredV1');
    await user.click(screen.getByRole('button', { name: 'Search' }));

    // Spec 317 (#2751): see the comment on the first `toHaveBeenLastCalledWith` above.
    await vi.waitFor(() =>
      expect(searchMock).toHaveBeenLastCalledWith(
        expect.objectContaining({ eventKind: 'CameraRetiredV1' }),
        expect.anything(),
      ),
    );

    expect(screen.queryByText('CameraRegisteredV1')).toBeNull();
    expect(screen.getByRole('alert')).toHaveTextContent(/could not load the audit trail/i);
  });
});
