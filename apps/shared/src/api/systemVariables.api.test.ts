import { configureStore } from '@reduxjs/toolkit';
import { afterEach, describe, expect, it, vi } from 'vitest';

// gateway.ts resolves the origin at module load; stub it before the dynamic
// import so fetchBaseQuery builds absolute URLs (Node's Request rejects
// relative ones).
vi.stubEnv('VITE_API_GATEWAY_URL', 'http://gateway.test');
const { systemVariablesApi } = await import('./systemVariables.api.js');

function snapshotResponse(): Response {
  return new Response(JSON.stringify({ overlayIdentifier: 'ovl-1', resolvedText: 'Line 1', version: 1 }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  });
}

function createStore() {
  return configureStore({
    reducer: { [systemVariablesApi.reducerPath]: systemVariablesApi.reducer },
    middleware: (getDefault) => getDefault().concat(systemVariablesApi.middleware),
  });
}

describe('getOverlaySnapshot cache tags (spec 011 FR-008)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("Refetches a mounted snapshot query when the 'ALL' sentinel is invalidated", async () => {
    const fetchMock = vi.fn(() => Promise.resolve(snapshotResponse()));
    vi.stubGlobal('fetch', fetchMock);
    const store = createStore();

    await store.dispatch(
      systemVariablesApi.endpoints.getOverlaySnapshot.initiate({ overlayIdentifier: 'ovl-1', fabId: 'munich' }),
    );
    expect(fetchMock).toHaveBeenCalledTimes(1);

    store.dispatch(systemVariablesApi.util.invalidateTags([{ type: 'OverlaySnapshot', id: 'ALL' }]));

    await vi.waitFor(() => {
      expect(fetchMock).toHaveBeenCalledTimes(2);
    });
  });

  it('Keeps per-identifier invalidation working alongside the sentinel', async () => {
    const fetchMock = vi.fn(() => Promise.resolve(snapshotResponse()));
    vi.stubGlobal('fetch', fetchMock);
    const store = createStore();

    await store.dispatch(
      systemVariablesApi.endpoints.getOverlaySnapshot.initiate({ overlayIdentifier: 'ovl-1', fabId: 'munich' }),
    );
    expect(fetchMock).toHaveBeenCalledTimes(1);

    store.dispatch(systemVariablesApi.util.invalidateTags([{ type: 'OverlaySnapshot', id: 'ovl-1' }]));

    await vi.waitFor(() => {
      expect(fetchMock).toHaveBeenCalledTimes(2);
    });
  });
});

function resolveResponse(): Response {
  return new Response(
    JSON.stringify({
      resolvedText: 'Line 1: 23.4',
      placeholders: [{ name: 'temperature', outcome: 'Resolved', fab: 'munich', renderedValue: '23.4' }],
    }),
    { status: 200, headers: { 'Content-Type': 'application/json' } },
  );
}

/**
 * Spec 148 T011. `resolveOverlayText` deliberately provides no tags — a
 * preview is derived and keyed by its own input, so a variable write must
 * never refetch a mounted preview the way it refetches a mounted snapshot
 * (contrast with the `getOverlaySnapshot` describe block above).
 */
describe('resolveOverlayText (spec 148 US1)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('Requests /resolve with the text as a query param', async () => {
    const fetchMock = vi.fn((_input: RequestInfo | URL, _init?: RequestInit) => Promise.resolve(resolveResponse()));
    vi.stubGlobal('fetch', fetchMock);
    const store = createStore();

    await store.dispatch(systemVariablesApi.endpoints.resolveOverlayText.initiate({ text: '{{temperature}}' }));

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const requested = fetchMock.mock.calls[0]?.[0];
    const requestedUrl = new URL(requested instanceof Request ? requested.url : (requested as string));
    expect(requestedUrl.pathname.endsWith('/resolve')).toBe(true);
    expect(requestedUrl.searchParams.get('text')).toBe('{{temperature}}');
    expect(requestedUrl.searchParams.has('fabId')).toBe(false);
  });

  it('Does not refetch a mounted preview when an unrelated variable write invalidates OverlaySnapshot', async () => {
    const fetchMock = vi.fn(() => Promise.resolve(resolveResponse()));
    vi.stubGlobal('fetch', fetchMock);
    const store = createStore();

    await store.dispatch(systemVariablesApi.endpoints.resolveOverlayText.initiate({ text: '{{temperature}}' }));
    expect(fetchMock).toHaveBeenCalledTimes(1);

    store.dispatch(systemVariablesApi.util.invalidateTags([{ type: 'OverlaySnapshot', id: 'ALL' }]));

    // No second call is expected — waitFor would need to assert an absence
    // over time, so this is a direct synchronous check instead.
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });
});
