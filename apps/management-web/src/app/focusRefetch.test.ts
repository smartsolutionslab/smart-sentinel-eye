import { afterEach, describe, expect, it, vi } from 'vitest';
import { countRequests, fireWindowFocus, settleRunningQueries } from '../test/focusRefetch.js';

// gateway.ts resolves the API origin at module load, the same way every
// other file touching the real `app/store` singleton stubs it — stub the
// env before any dynamic import touches it, so fetchBaseQuery builds
// absolute URLs (Node's `Request` rejects a relative one).
vi.stubEnv('VITE_API_GATEWAY_URL', 'http://gateway.test');

/**
 * Spec 317 (#2751) T004, plan.md §4 row 3. Drives the REAL app store
 * (`app/store.ts`'s singleton) and the real `camerasApi`, with `fetch`
 * stubbed at the network boundary — not a mocked hook, per FR-006.
 *
 * RED until `app/store.ts` exports `listenForWindowFocus` (FR-001, D3):
 * without it `setupListeners` is never called anywhere in the app, so
 * `refetchOnFocus` on any subscription is inert. The import below fails
 * both `tsc --noEmit` (no such export) and at runtime (ESM: "does not
 * provide an export named 'listenForWindowFocus'").
 */
const { store, listenForWindowFocus } = await import('./store.js');
const { camerasApi } = await import('@smart-sentinel-eye/shared/api/cameras.api');

const CAMERA_IDENTIFIER = '11111111-1111-1111-1111-111111111111';

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

function cameraDetail() {
  return {
    cameraIdentifier: CAMERA_IDENTIFIER,
    version: 7,
    fab: 'munich',
    name: 'Line-1-Entrance',
    rtspUrl: 'rtsp://10.0.5.12/h264',
    registeredAt: '2026-05-24T10:00:00Z',
    status: 'Registered',
  };
}

function emptyListPage() {
  return { items: [], count: 0, offset: 0, limit: 50 };
}

describe("The app store's own focus wiring (spec 317, #2751)", () => {
  const cleanups: Array<() => void> = [];

  afterEach(() => {
    while (cleanups.length > 0) {
      cleanups.pop()?.();
    }
    store.dispatch(camerasApi.util.resetApiState());
    vi.unstubAllGlobals();
  });

  it('a focus refetches only the subscription that opted in with refetchOnFocus, not one that did not', async () => {
    let cameraCalls = 0;
    let listCalls = 0;
    const fetchMock = vi.fn((request: Request) => {
      if (request.url.includes(`/${CAMERA_IDENTIFIER}`)) {
        cameraCalls += 1;
        return Promise.resolve(jsonResponse(cameraDetail()));
      }
      listCalls += 1;
      return Promise.resolve(jsonResponse(emptyListPage()));
    });
    vi.stubGlobal('fetch', fetchMock);

    cleanups.push(listenForWindowFocus());

    store.dispatch(
      camerasApi.endpoints.getCamera.initiate(
        { cameraIdentifier: CAMERA_IDENTIFIER },
        { subscriptionOptions: { refetchOnFocus: true } },
      ),
    );
    // Opted out — no `subscriptionOptions` at all, same as every page
    // this spec does not touch.
    store.dispatch(camerasApi.endpoints.listCameras.initiate(undefined));

    await settleRunningQueries(store, camerasApi);
    expect(cameraCalls).toBe(1);
    expect(listCalls).toBe(1);

    fireWindowFocus();
    await settleRunningQueries(store, camerasApi);

    expect(countRequests(fetchMock, `/${CAMERA_IDENTIFIER}`)).toBe(2);
    expect(cameraCalls).toBe(2);
    expect(listCalls).toBe(1);
  });

  it('installing the listeners twice still sends exactly one request per focus for the opted-in query', async () => {
    let cameraCalls = 0;
    const fetchMock = vi.fn((request: Request) => {
      if (request.url.includes(`/${CAMERA_IDENTIFIER}`)) {
        cameraCalls += 1;
        return Promise.resolve(jsonResponse(cameraDetail()));
      }
      return Promise.resolve(jsonResponse(emptyListPage()));
    });
    vi.stubGlobal('fetch', fetchMock);

    cleanups.push(listenForWindowFocus());
    cleanups.push(listenForWindowFocus());

    store.dispatch(
      camerasApi.endpoints.getCamera.initiate(
        { cameraIdentifier: CAMERA_IDENTIFIER },
        { subscriptionOptions: { refetchOnFocus: true } },
      ),
    );
    await settleRunningQueries(store, camerasApi);
    expect(cameraCalls).toBe(1);

    fireWindowFocus();
    await settleRunningQueries(store, camerasApi);

    expect(cameraCalls).toBe(2);
  });
});
