import { configureStore, createListenerMiddleware } from '@reduxjs/toolkit';
import { render, screen } from '@testing-library/react';
import { Provider } from 'react-redux';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { fireWindowFocus, installFocusListeners, settleRunningQueries } from '../../test/focusRefetch.js';

// gateway.ts resolves the API origin at module load; stub it before any
// dynamic import touches systemVariables.api.
vi.stubEnv('VITE_API_GATEWAY_URL', 'http://gateway.test');

// SystemVariableDialog (always mounted, closed) reads the operator's fabs
// via useAssignedFabs -> useAuth, the same stub SystemVariablesPage.test.tsx
// uses.
vi.mock('react-oidc-context', () => ({
  useAuth: () => ({ user: { profile: { groups: ['/fabs/munich'] } } }),
}));

const { systemVariablesApi } = await import('@smart-sentinel-eye/shared/api/systemVariables.api');
const { SystemVariablesPage } = await import('./SystemVariablesPage.js');

/**
 * Spec 317 (#2751) T009a, plan.md §4 row 9. Real `systemVariablesApi`
 * against a real store, `fetch` stubbed at the network boundary. RED today
 * — `SystemVariablesPage.tsx` passes no `refetchOnFocus` and nothing calls
 * `setupListeners`.
 */

function variable() {
  return {
    variableIdentifier: '11111111-1111-1111-1111-111111111111',
    version: 0,
    fab: 'munich',
    name: 'oeeLine1',
    type: 'Number',
    state: 'Defined',
    value: null,
    truthyLabel: null,
    falsyLabel: null,
    createdAt: '2026-05-27T10:00:00Z',
    createdBy: '22222222-2222-2222-2222-222222222222',
  };
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
    reducer: { [systemVariablesApi.reducerPath]: systemVariablesApi.reducer },
    middleware: (getDefault) =>
      getDefault().prepend(listenerMiddleware.middleware).concat(systemVariablesApi.middleware),
  });
}

function renderPage(store: ReturnType<typeof createStore>) {
  return render(
    <Provider store={store}>
      <SystemVariablesPage />
    </Provider>,
  );
}

describe('SystemVariablesPage refetches on window focus (spec 317, #2751)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('A focus sends exactly one new list request', async () => {
    let callCount = 0;
    const fetchMock = vi.fn(() => {
      callCount += 1;
      return Promise.resolve(jsonResponse([variable()]));
    });
    vi.stubGlobal('fetch', fetchMock);

    const store = createStore();
    installFocusListeners(store);
    renderPage(store);

    await screen.findByRole('heading', { name: 'oeeLine1' });
    expect(callCount).toBe(1);

    fireWindowFocus();
    await settleRunningQueries(store, systemVariablesApi);

    expect(callCount).toBe(2);
  });

  it('Three consecutive 403 focus refreshes drop the stale rows and show the failure banner', async () => {
    let callCount = 0;
    const fetchMock = vi.fn(() => {
      callCount += 1;
      return Promise.resolve(callCount === 1 ? jsonResponse([variable()]) : problemResponse(403));
    });
    vi.stubGlobal('fetch', fetchMock);

    const store = createStore();
    installFocusListeners(store);
    renderPage(store);

    await screen.findByRole('heading', { name: 'oeeLine1' });

    for (let strike = 0; strike < 3; strike += 1) {
      fireWindowFocus();
      await settleRunningQueries(store, systemVariablesApi);
    }

    expect(screen.queryByRole('heading', { name: 'oeeLine1' })).toBeNull();
    expect(screen.getByRole('alert')).toHaveTextContent(/could not load variables/i);
  });

  it('403, 403, 200, 403, 403 via focus is not refused', async () => {
    const refetchOutcomes: Array<200 | 403> = [403, 403, 200, 403, 403];
    let loaded = false;
    const fetchMock = vi.fn(() => {
      if (!loaded) {
        loaded = true;
        return Promise.resolve(jsonResponse([variable()]));
      }
      const outcome = refetchOutcomes.shift();
      if (outcome === undefined) {
        throw new Error('SystemVariablesPageFocusRefetch: scripted outcome queue is empty');
      }
      return Promise.resolve(outcome === 200 ? jsonResponse([variable()]) : problemResponse(outcome));
    });
    vi.stubGlobal('fetch', fetchMock);

    const store = createStore();
    installFocusListeners(store);
    renderPage(store);

    await screen.findByRole('heading', { name: 'oeeLine1' });

    for (let i = 0; i < 5; i += 1) {
      fireWindowFocus();
      await settleRunningQueries(store, systemVariablesApi);
    }

    expect(screen.getByRole('heading', { name: 'oeeLine1' })).toBeInTheDocument();
    expect(screen.queryByRole('alert')).toBeNull();
  });
});
