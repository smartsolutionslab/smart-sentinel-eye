import { configureStore } from '@reduxjs/toolkit';
import { afterEach, describe, expect, it, vi } from 'vitest';

// gateway.ts resolves the origin at module load; stub it before the dynamic
// import so fetchBaseQuery builds absolute URLs (Node's Request rejects
// relative ones).
vi.stubEnv('VITE_API_GATEWAY_URL', 'http://gateway.test');
const { systemVariablesApi } = await import('./systemVariables.api.js');

function snapshotResponse(): Response {
  return new Response(
    JSON.stringify({ overlayIdentifier: 'ovl-1', resolvedText: 'Line 1', version: 1 }),
    { status: 200, headers: { 'Content-Type': 'application/json' } },
  );
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

    await store.dispatch(systemVariablesApi.endpoints.getOverlaySnapshot.initiate('ovl-1'));
    expect(fetchMock).toHaveBeenCalledTimes(1);

    store.dispatch(
      systemVariablesApi.util.invalidateTags([{ type: 'OverlaySnapshot', id: 'ALL' }]),
    );

    await vi.waitFor(() => {
      expect(fetchMock).toHaveBeenCalledTimes(2);
    });
  });

  it('Keeps per-identifier invalidation working alongside the sentinel', async () => {
    const fetchMock = vi.fn(() => Promise.resolve(snapshotResponse()));
    vi.stubGlobal('fetch', fetchMock);
    const store = createStore();

    await store.dispatch(systemVariablesApi.endpoints.getOverlaySnapshot.initiate('ovl-1'));
    expect(fetchMock).toHaveBeenCalledTimes(1);

    store.dispatch(
      systemVariablesApi.util.invalidateTags([{ type: 'OverlaySnapshot', id: 'ovl-1' }]),
    );

    await vi.waitFor(() => {
      expect(fetchMock).toHaveBeenCalledTimes(2);
    });
  });
});
