import { configureStore } from '@reduxjs/toolkit';
import { act, render, screen, waitFor } from '@testing-library/react';
import { Provider } from 'react-redux';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';

// gateway.ts resolves the API origin at module load, the same way
// LayoutEditorDialogChainRetention.test.tsx (spec 153) stubs it: stub the env
// before any dynamic import touches cameras.api, directly or via
// CameraDetailPage, so fetchBaseQuery builds absolute URLs — Node's `Request`
// rejects a relative one.
vi.stubEnv('VITE_API_GATEWAY_URL', 'http://gateway.test');

/**
 * Spec 256 (#2522). `useGetCameraQuery` is REAL in this file — that is the
 * whole point. `CameraDetailPage.test.tsx` mocks the hook, and a mocked hook
 * returns whatever the test hands it: it can prove the page's *mapping* from
 * a `{ data, currentData, isLoading, isFetching, error }` shape to what
 * renders, given the shape spec 256's plan derived from reading RTK Query's
 * source — but it cannot prove RTK Query actually produces that shape on a
 * same-instance identifier change. An assertion must not be satisfiable by
 * its own input — a mocked hook proves the mapping, not that RTK Query
 * actually produces the mapped-from shape.
 *
 * Modelled directly on `LayoutEditorDialogChainRetention.test.tsx` (spec
 * 153), which pins the same shape (`currentData` + `isFetching`, on a reused
 * component instance) for `useGetLayoutQuery`.
 *
 * The page is mounted with a real `createMemoryRouter` + `RouterProvider`,
 * **once** per test, and moved between URLs with `router.navigate(...)`
 * inside `act`. A second `render()` call would give the hook a fresh instance
 * with no `lastResult` to carry over, and the test would pass for the wrong
 * reason — same-instance reuse is exactly the condition issue #2522 needs.
 */
vi.mock('react-oidc-context', () => ({
  useAuth: () => ({ user: { access_token: 'operator-access-token' } }),
}));

// CameraViewer mounts a WhepClient that talks to RTCPeerConnection, and jsdom
// has no such global — stubbed for the same reason
// `CameraDetailPage.test.tsx` stubs it. Every render is recorded, in order,
// so a test can tell whether a viewer for a PARTICULAR identifier rendered
// after a particular point, not merely whether one ever did.
const viewerRenders = vi.hoisted(() => [] as { cameraIdentifier: string }[]);
vi.mock('@smart-sentinel-eye/shared/ui/composites/CameraViewer', () => ({
  CameraViewer: (props: { cameraIdentifier: string; getToken: () => Promise<string | null>; cameraName?: string }) => {
    viewerRenders.push({ cameraIdentifier: props.cameraIdentifier });
    return <div data-testid="camera-viewer">viewer:{props.cameraIdentifier}</div>;
  },
}));

const { camerasApi } = await import('@smart-sentinel-eye/shared/api/cameras.api');
const { CameraDetailPage } = await import('./CameraDetailPage.js');

const CAMERA_A = '11111111-1111-1111-1111-111111111111';
const CAMERA_B = '44444444-4444-4444-4444-444444444444';

function cameraRecord(cameraIdentifier: string, name: string, fab: string, rtspUrl: string, version = 1) {
  return {
    cameraIdentifier,
    version,
    fab,
    name,
    rtspUrl,
    registeredAt: '2026-05-24T10:00:00Z',
    status: 'Registered',
  };
}

const CAMERA_A_RECORD = cameraRecord(CAMERA_A, 'Line-1-Entrance', 'munich', 'rtsp://10.0.5.12/h264');
const CAMERA_B_RECORD = cameraRecord(CAMERA_B, 'Line-2-Loading-Dock', 'dresden', 'rtsp://10.0.7.4/h264');

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

function createStore() {
  return configureStore({
    reducer: { [camerasApi.reducerPath]: camerasApi.reducer },
    middleware: (getDefault) => getDefault().concat(camerasApi.middleware),
  });
}

function renderAt(store: ReturnType<typeof createStore>, initial: string) {
  const router = createMemoryRouter([{ path: '/cameras/:cameraIdentifier', element: <CameraDetailPage /> }], {
    initialEntries: [initial],
  });
  render(
    <Provider store={store}>
      <RouterProvider router={router} />
    </Provider>,
  );
  return router;
}

describe('CameraDetailPage navigation — the same instance never shows the previous camera (spec 256, #2522)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    viewerRenders.length = 0;
  });

  /**
   * US1-A / row 8 of the plan's truth table, observed against the REAL hook.
   * A's GET resolves, B's GET is held open for the whole test — so B's cache
   * entry never lands — and the same page instance is navigated from A's URL
   * to B's. Today the page renders A's own record (heading, fab, RTSP URL,
   * and a `CameraViewer` opened on A's stream) under B's URL, because
   * `isLoading` is false (RTK's `lastResult` fallback still has `data` from
   * A) while `currentData` is undefined for B.
   */
  it('Does not show the previous camera while the next one loads', async () => {
    let resolveB: ((response: Response) => void) | undefined;
    const fetchMock = vi.fn((request: Request) => {
      if (request.method === 'GET' && request.url.includes(CAMERA_A)) {
        return Promise.resolve(jsonResponse(CAMERA_A_RECORD));
      }
      if (request.method === 'GET' && request.url.includes(CAMERA_B)) {
        // Held open for the whole test — B's own record is never answered, so
        // nothing has told the page what it is.
        return new Promise<Response>((resolve) => {
          resolveB = resolve;
        });
      }
      throw new Error(`unexpected fetch: ${request.method} ${request.url}`);
    });
    vi.stubGlobal('fetch', fetchMock);

    const store = createStore();
    const router = renderAt(store, `/cameras/${CAMERA_A}`);

    await screen.findByRole('heading', { name: CAMERA_A_RECORD.name });
    const viewerRendersBeforeNavigation = viewerRenders.length;

    await act(async () => {
      await router.navigate(`/cameras/${CAMERA_B}`);
    });

    expect(screen.getByText('Loading…')).toBeInTheDocument();
    expect(screen.queryByText(CAMERA_A_RECORD.name)).toBeNull();
    expect(screen.queryByText(CAMERA_A_RECORD.fab)).toBeNull();
    expect(screen.queryByText(CAMERA_A_RECORD.rtspUrl)).toBeNull();

    // The render-site counterpart: even if some text assertion above turned
    // out to be too narrow, no CameraViewer opened on A's identifier may have
    // rendered after the navigation to B.
    const viewerRendersAfterNavigation = viewerRenders.slice(viewerRendersBeforeNavigation);
    expect(viewerRendersAfterNavigation.some((render) => render.cameraIdentifier === CAMERA_A)).toBe(false);

    // Keep the promise reachable so nothing in this test leaves a dangling
    // unhandled rejection if the surrounding harness ever inspects it.
    void resolveB;
  });

  /**
   * US1-B. The sanity/regression counterpart to the test above: once B's own
   * GET actually answers, its record — and only its record — renders. Green
   * both before and after the fix; it is here so the fix cannot be "always
   * show Loading" or some other change that would make the test above pass
   * for the wrong reason.
   */
  it('Shows the next camera once it arrives', async () => {
    let resolveB: ((response: Response) => void) | undefined;
    const fetchMock = vi.fn((request: Request) => {
      if (request.method === 'GET' && request.url.includes(CAMERA_A)) {
        return Promise.resolve(jsonResponse(CAMERA_A_RECORD));
      }
      if (request.method === 'GET' && request.url.includes(CAMERA_B)) {
        return new Promise<Response>((resolve) => {
          resolveB = resolve;
        });
      }
      throw new Error(`unexpected fetch: ${request.method} ${request.url}`);
    });
    vi.stubGlobal('fetch', fetchMock);

    const store = createStore();
    const router = renderAt(store, `/cameras/${CAMERA_A}`);

    await screen.findByRole('heading', { name: CAMERA_A_RECORD.name });

    await act(async () => {
      await router.navigate(`/cameras/${CAMERA_B}`);
    });

    resolveB?.(jsonResponse(CAMERA_B_RECORD));

    await screen.findByRole('heading', { name: CAMERA_B_RECORD.name });
    expect(screen.getByText(CAMERA_B_RECORD.fab)).toBeInTheDocument();
    expect(screen.getByText(CAMERA_B_RECORD.rtspUrl)).toBeInTheDocument();
    expect(screen.queryByText(CAMERA_A_RECORD.name)).toBeNull();
    expect(screen.queryByText(CAMERA_A_RECORD.rtspUrl)).toBeNull();
  });

  /**
   * US1-C fence, observed against the real hook. A is already on screen from
   * its own cache entry; a background refetch of A (the way `invalidateTags`
   * drives one after a rename) must not blank the record it is refreshing.
   * `store.dispatch(camerasApi.util.invalidateTags(...))` is a test-only use
   * of `api.util` (plan §Boundary allows it in tests, not in production code)
   * — it is exactly what a mutation's `invalidatesTags` triggers on a
   * mounted subscriber.
   */
  it('Does not blank the camera on screen while it is refreshed', async () => {
    let aCalls = 0;
    let resolveSecondA: ((response: Response) => void) | undefined;
    const fetchMock = vi.fn((request: Request) => {
      if (request.method === 'GET' && request.url.includes(CAMERA_A)) {
        aCalls += 1;
        if (aCalls === 1) {
          return Promise.resolve(jsonResponse(CAMERA_A_RECORD));
        }
        // The refetch, held open for the assertion below.
        return new Promise<Response>((resolve) => {
          resolveSecondA = resolve;
        });
      }
      throw new Error(`unexpected fetch: ${request.method} ${request.url}`);
    });
    vi.stubGlobal('fetch', fetchMock);

    const store = createStore();
    renderAt(store, `/cameras/${CAMERA_A}`);

    await screen.findByRole('heading', { name: CAMERA_A_RECORD.name });

    await act(async () => {
      store.dispatch(camerasApi.util.invalidateTags([{ type: 'Camera', id: CAMERA_A }]));
      await Promise.resolve();
    });

    // The refetch must actually have been requested — a bare
    // `toHaveBeenCalled()` below would be satisfied by call 1 (A's initial
    // load) alone and pass even if `invalidateTags` silently failed to
    // trigger a second GET. Waiting for call 2, and for the resolver the mock
    // only assigns on that second call, proves the background refetch was
    // really in flight for the "no Loading" assertions that follow.
    await waitFor(() => {
      expect(fetchMock).toHaveBeenCalledTimes(2);
    });
    expect(resolveSecondA).toBeDefined();

    // The second GET is still held open at this point (`resolveSecondA` has
    // not been called), so the background refetch stays in flight for as
    // long as this assertion is checked: a gate that shows Loading whenever
    // `isFetching` is true, regardless of `currentData`, would keep Loading
    // on screen the whole time. Waiting for it to be absent — rather than
    // sampling once, racing the pending-refetch render's commit — means a
    // broken gate cannot pass by outrunning a single synchronous check.
    await waitFor(() => {
      expect(screen.queryByText('Loading…')).toBeNull();
    });
    expect(screen.getByRole('heading', { name: CAMERA_A_RECORD.name })).toBeInTheDocument();

    resolveSecondA?.(jsonResponse(CAMERA_A_RECORD));
    await waitFor(() => {
      expect(fetchMock).toHaveBeenCalledTimes(2);
    });
  });
});
