import { configureStore, createListenerMiddleware } from '@reduxjs/toolkit';
import { render, screen } from '@testing-library/react';
import { Provider } from 'react-redux';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  countRequests,
  fireVisible,
  fireWindowFocus,
  installFocusListeners,
  settleRunningQueries,
} from '@smart-sentinel-eye/shared/test/focusRefetch';

// gateway.ts resolves the API origin at module load; stub it before any
// dynamic import touches cameras.api/streams.api.
vi.stubEnv('VITE_API_GATEWAY_URL', 'http://gateway.test');

// RegisterCameraDialog (always mounted, closed) reads the operator's fabs
// via useAssignedFabs -> useAuth, the same stub CamerasPage.test.tsx uses.
vi.mock('react-oidc-context', () => ({
  useAuth: () => ({ user: { profile: { groups: ['/fabs/munich'] } } }),
}));

const { camerasApi } = await import('@smart-sentinel-eye/shared/api/cameras.api');
const { streamsApi } = await import('@smart-sentinel-eye/shared/api/streams.api');
const { CamerasPage } = await import('./CamerasPage.js');

const CAMERAS_PATH = 'camera-catalog/cameras';
const STREAMS_PATH = 'stream-distribution/streams';

/**
 * Spec 317 (#2751) T006, plan.md §4 row 5. Real `camerasApi`/`streamsApi`
 * against a real store, `fetch` stubbed and routed by URL so the streams
 * poll (`useListStreamsQuery`, unrelated to this spec, FR-004) is excluded
 * from every count. RED today — `CamerasPage.tsx` passes no
 * `refetchOnFocus` and nothing calls `setupListeners`.
 */

function populatedPage() {
  return {
    items: [
      {
        cameraIdentifier: '11111111-1111-1111-1111-111111111111',
        version: 1,
        fab: 'munich',
        name: 'Line-1-Entrance',
        rtspUrl: 'rtsp://10.0.5.12/h264',
        registeredAt: '2026-05-24T10:00:00Z',
        status: 'Registered',
      },
    ],
    count: 1,
    offset: 0,
    limit: 50,
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
    reducer: { [camerasApi.reducerPath]: camerasApi.reducer, [streamsApi.reducerPath]: streamsApi.reducer },
    middleware: (getDefault) =>
      getDefault().prepend(listenerMiddleware.middleware).concat(camerasApi.middleware, streamsApi.middleware),
  });
}

function renderPage(store: ReturnType<typeof createStore>) {
  return render(
    <Provider store={store}>
      <MemoryRouter>
        <CamerasPage />
      </MemoryRouter>
    </Provider>,
  );
}

function stubFetchForCameras(camerasResponder: () => Response) {
  return vi.fn((request: Request) => {
    if (request.url.includes(STREAMS_PATH)) {
      return Promise.resolve(jsonResponse([]));
    }
    return Promise.resolve(camerasResponder());
  });
}

describe('CamerasPage refetches on window focus (spec 317, #2751)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('A focus sends exactly one new camera-list request, excluding the streams poll', async () => {
    const fetchMock = stubFetchForCameras(() => jsonResponse(populatedPage()));
    vi.stubGlobal('fetch', fetchMock);

    const store = createStore();
    installFocusListeners(store);
    renderPage(store);

    await screen.findByText('Line-1-Entrance');
    expect(countRequests(fetchMock, CAMERAS_PATH)).toBe(1);

    fireWindowFocus();
    await settleRunningQueries(store, camerasApi);

    expect(countRequests(fetchMock, CAMERAS_PATH)).toBe(2);
  });

  it('A document visibility change to "visible" sends exactly one new camera-list request', async () => {
    const fetchMock = stubFetchForCameras(() => jsonResponse(populatedPage()));
    vi.stubGlobal('fetch', fetchMock);

    const store = createStore();
    installFocusListeners(store);
    renderPage(store);

    await screen.findByText('Line-1-Entrance');
    expect(countRequests(fetchMock, CAMERAS_PATH)).toBe(1);

    fireVisible();
    await settleRunningQueries(store, camerasApi);

    expect(countRequests(fetchMock, CAMERAS_PATH)).toBe(2);
  });

  it('Three consecutive 403 focus refreshes drop the stale rows and show the failure banner', async () => {
    let cameraCalls = 0;
    const fetchMock = stubFetchForCameras(() => {
      cameraCalls += 1;
      return cameraCalls === 1 ? jsonResponse(populatedPage()) : problemResponse(403);
    });
    vi.stubGlobal('fetch', fetchMock);

    const store = createStore();
    installFocusListeners(store);
    renderPage(store);

    await screen.findByText('Line-1-Entrance');

    for (let strike = 0; strike < 3; strike += 1) {
      fireWindowFocus();
      await settleRunningQueries(store, camerasApi);
    }

    expect(screen.queryByText('Line-1-Entrance')).toBeNull();
    expect(screen.getByRole('alert')).toHaveTextContent(/could not load cameras/i);
  });

  it('403, 403, 200, 403, 403 via focus is not refused', async () => {
    const refetchOutcomes: Array<200 | 403> = [403, 403, 200, 403, 403];
    let loaded = false;
    const fetchMock = stubFetchForCameras(() => {
      if (!loaded) {
        loaded = true;
        return jsonResponse(populatedPage());
      }
      const outcome = refetchOutcomes.shift();
      if (outcome === undefined) {
        throw new Error('CamerasPageFocusRefetch: scripted outcome queue is empty');
      }
      return outcome === 200 ? jsonResponse(populatedPage()) : problemResponse(outcome);
    });
    vi.stubGlobal('fetch', fetchMock);

    const store = createStore();
    installFocusListeners(store);
    renderPage(store);

    await screen.findByText('Line-1-Entrance');

    for (let i = 0; i < 5; i += 1) {
      fireWindowFocus();
      await settleRunningQueries(store, camerasApi);
    }

    // All 5 scripted outcomes must actually have been consumed -- otherwise
    // this passes vacuously if focus never triggers a refetch at all (no
    // strikes, rows stay, "not refused" trivially holds).
    expect(refetchOutcomes).toHaveLength(0);

    // The strike count resets at the 200 and sits at 2, not 3 -- never refused.
    // RetryBanner is unconditional on the *current* error, independent of the
    // strike count, and the final scripted response here is itself a 403 --
    // so RetryBanner legitimately renders. That's a different, already-correct,
    // unchanged behaviour this test isn't about (see the fence test's own
    // narrower scope for the identical sequence: useRevocationFallbackFocus.test.tsx).
    expect(screen.getByRole('alert')).toBeInTheDocument();
    expect(screen.getByText('Line-1-Entrance')).toBeInTheDocument();
  });
});
