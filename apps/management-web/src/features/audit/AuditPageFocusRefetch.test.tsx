import { configureStore, createListenerMiddleware } from '@reduxjs/toolkit';
import { render, screen } from '@testing-library/react';
import { Provider } from 'react-redux';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { fireWindowFocus, installFocusListeners, settleRunningQueries } from '../../test/focusRefetch.js';

// gateway.ts resolves the API origin at module load; stub it before any
// dynamic import touches audit.api.
vi.stubEnv('VITE_API_GATEWAY_URL', 'http://gateway.test');

const { auditApi } = await import('@smart-sentinel-eye/shared/api/audit.api');
const { AuditPage } = await import('./AuditPage.js');

/**
 * Spec 317 (#2751) T007, plan.md §4 row 6. Real `auditApi` against a real
 * store, `fetch` stubbed at the network boundary. RED today —
 * `AuditPage.tsx` passes no `refetchOnFocus` and nothing calls
 * `setupListeners`.
 */

function auditRow() {
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
  };
}

function searchResult() {
  return { rows: [auditRow()], nextCursor: null };
}

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

function problemResponse(status: number): Response {
  return jsonResponse({ title: 'PROBLEM', status }, status);
}

function createStore() {
  const listenerMiddleware = createListenerMiddleware();
  return configureStore({
    reducer: { [auditApi.reducerPath]: auditApi.reducer },
    middleware: (getDefault) => getDefault().prepend(listenerMiddleware.middleware).concat(auditApi.middleware),
  });
}

function renderPage(store: ReturnType<typeof createStore>) {
  return render(
    <Provider store={store}>
      <AuditPage />
    </Provider>,
  );
}

describe('AuditPage refetches on window focus (spec 317, #2751)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('A focus sends exactly one new search request', async () => {
    let callCount = 0;
    const fetchMock = vi.fn(() => {
      callCount += 1;
      return Promise.resolve(jsonResponse(searchResult()));
    });
    vi.stubGlobal('fetch', fetchMock);

    const store = createStore();
    installFocusListeners(store);
    renderPage(store);

    await screen.findByText('CameraRegisteredV1');
    expect(callCount).toBe(1);

    fireWindowFocus();
    await settleRunningQueries(store, auditApi);

    expect(callCount).toBe(2);
  });

  it('Three consecutive 403 focus refreshes drop the stale rows and show the failure banner', async () => {
    let callCount = 0;
    const fetchMock = vi.fn(() => {
      callCount += 1;
      return Promise.resolve(callCount === 1 ? jsonResponse(searchResult()) : problemResponse(403));
    });
    vi.stubGlobal('fetch', fetchMock);

    const store = createStore();
    installFocusListeners(store);
    renderPage(store);

    await screen.findByText('CameraRegisteredV1');

    for (let strike = 0; strike < 3; strike += 1) {
      fireWindowFocus();
      await settleRunningQueries(store, auditApi);
    }

    expect(screen.queryByText('CameraRegisteredV1')).toBeNull();
    expect(screen.getByRole('alert')).toHaveTextContent(/could not load the audit trail/i);
  });

  it('403, 403, 200, 403, 403 via focus is not refused', async () => {
    const refetchOutcomes: Array<200 | 403> = [403, 403, 200, 403, 403];
    let loaded = false;
    const fetchMock = vi.fn(() => {
      if (!loaded) {
        loaded = true;
        return Promise.resolve(jsonResponse(searchResult()));
      }
      const outcome = refetchOutcomes.shift();
      if (outcome === undefined) {
        throw new Error('AuditPageFocusRefetch: scripted outcome queue is empty');
      }
      return Promise.resolve(outcome === 200 ? jsonResponse(searchResult()) : problemResponse(outcome));
    });
    vi.stubGlobal('fetch', fetchMock);

    const store = createStore();
    installFocusListeners(store);
    renderPage(store);

    await screen.findByText('CameraRegisteredV1');

    for (let i = 0; i < 5; i += 1) {
      fireWindowFocus();
      await settleRunningQueries(store, auditApi);
    }

    expect(screen.getByText('CameraRegisteredV1')).toBeInTheDocument();
    expect(screen.queryByRole('alert')).toBeNull();
  });
});
