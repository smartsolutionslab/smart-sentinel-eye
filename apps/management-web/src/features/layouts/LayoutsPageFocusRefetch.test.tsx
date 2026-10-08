import { configureStore, createListenerMiddleware } from '@reduxjs/toolkit';
import { render, screen } from '@testing-library/react';
import { Provider } from 'react-redux';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { fireWindowFocus, installFocusListeners, settleRunningQueries } from '../../test/focusRefetch.js';

// gateway.ts resolves the API origin at module load; stub it before any
// dynamic import touches layouts.api/cameras.api.
vi.stubEnv('VITE_API_GATEWAY_URL', 'http://gateway.test');

// LayoutsPage mounts LayoutEditorDialog (closed), which reads camera choices
// via a real camerasApi query (`useListAllCameraChoicesQuery`) — stub it the
// same way LayoutsPage.test.tsx does, so this file is about the list query
// alone.
vi.mock('@smart-sentinel-eye/shared/api/cameras.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/cameras.api')>();
  return {
    ...actual,
    useListAllCameraChoicesQuery: () => ({
      data: { items: [], count: 0, complete: true },
      currentData: { items: [], count: 0, complete: true },
      isLoading: false,
      isFetching: false,
      isError: false,
    }),
  };
});

vi.mock('@smart-sentinel-eye/shared/api/overlays.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/overlays.api')>();
  return {
    ...actual,
    useListOverlaysQuery: () => ({ data: { chains: [], published: [] }, isLoading: false }),
  };
});

const { layoutsApi } = await import('@smart-sentinel-eye/shared/api/layouts.api');
const { LayoutsPage } = await import('./LayoutsPage.js');

/**
 * Spec 317 (#2751) T008, plan.md §4 row 7. Real `layoutsApi` against a real
 * store, `fetch` stubbed at the network boundary. RED today —
 * `LayoutsPage.tsx` passes no `refetchOnFocus` and nothing calls
 * `setupListeners`.
 */

function chain() {
  return {
    layoutIdentifier: '11111111-1111-1111-1111-111111111111',
    version: 0,
    fab: 'munich',
    name: 'Line-1',
    createdAt: '2026-05-26T10:00:00Z',
    createdBy: '22222222-2222-2222-2222-222222222222',
    revisions: [
      {
        revisionIdentifier: '33333333-3333-3333-3333-333333333333',
        revisionNumber: 1,
        state: 'Draft',
        gridRows: 1,
        gridCols: 1,
        tiles: [
          {
            cameraIdentifier: '44444444-4444-4444-4444-444444444444',
            overlayIdentifier: null,
            row: 0,
            col: 0,
            rowSpan: 1,
            colSpan: 1,
          },
        ],
        createdAt: '2026-05-26T10:00:00Z',
        createdBy: '22222222-2222-2222-2222-222222222222',
        publishedAt: null,
        archivedAt: null,
      },
    ],
  };
}

function listResponse() {
  return { chains: [chain()], published: [] };
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
    reducer: { [layoutsApi.reducerPath]: layoutsApi.reducer },
    middleware: (getDefault) => getDefault().prepend(listenerMiddleware.middleware).concat(layoutsApi.middleware),
  });
}

function renderPage(store: ReturnType<typeof createStore>) {
  return render(
    <Provider store={store}>
      <LayoutsPage />
    </Provider>,
  );
}

describe('LayoutsPage refetches on window focus (spec 317, #2751)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('A focus sends exactly one new list request', async () => {
    let callCount = 0;
    const fetchMock = vi.fn(() => {
      callCount += 1;
      return Promise.resolve(jsonResponse(listResponse()));
    });
    vi.stubGlobal('fetch', fetchMock);

    const store = createStore();
    installFocusListeners(store);
    renderPage(store);

    await screen.findByText('Line-1');
    expect(callCount).toBe(1);

    fireWindowFocus();
    await settleRunningQueries(store, layoutsApi);

    expect(callCount).toBe(2);
  });

  it('Three consecutive 403 focus refreshes drop the stale rows and show the failure banner', async () => {
    let callCount = 0;
    const fetchMock = vi.fn(() => {
      callCount += 1;
      return Promise.resolve(callCount === 1 ? jsonResponse(listResponse()) : problemResponse(403));
    });
    vi.stubGlobal('fetch', fetchMock);

    const store = createStore();
    installFocusListeners(store);
    renderPage(store);

    await screen.findByText('Line-1');

    for (let strike = 0; strike < 3; strike += 1) {
      fireWindowFocus();
      await settleRunningQueries(store, layoutsApi);
    }

    expect(screen.queryByText('Line-1')).toBeNull();
    expect(screen.getByRole('alert')).toHaveTextContent(/could not load layouts/i);
  });

  it('403, 403, 200, 403, 403 via focus is not refused', async () => {
    const refetchOutcomes: Array<200 | 403> = [403, 403, 200, 403, 403];
    let loaded = false;
    const fetchMock = vi.fn(() => {
      if (!loaded) {
        loaded = true;
        return Promise.resolve(jsonResponse(listResponse()));
      }
      const outcome = refetchOutcomes.shift();
      if (outcome === undefined) {
        throw new Error('LayoutsPageFocusRefetch: scripted outcome queue is empty');
      }
      return Promise.resolve(outcome === 200 ? jsonResponse(listResponse()) : problemResponse(outcome));
    });
    vi.stubGlobal('fetch', fetchMock);

    const store = createStore();
    installFocusListeners(store);
    renderPage(store);

    await screen.findByText('Line-1');

    for (let i = 0; i < 5; i += 1) {
      fireWindowFocus();
      await settleRunningQueries(store, layoutsApi);
    }

    // The strike count resets at the 200 and sits at 2, not 3 -- never refused.
    // RetryBanner is unconditional on the *current* error, independent of the
    // strike count, and the final scripted response here is itself a 403 --
    // so RetryBanner legitimately renders. That's a different, already-correct,
    // unchanged behaviour this test isn't about (see the fence test's own
    // narrower scope for the identical sequence: useRevocationFallbackFocus.test.tsx).
    expect(screen.getByText('Line-1')).toBeInTheDocument();
  });
});
