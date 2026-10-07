import { configureStore } from '@reduxjs/toolkit';
import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';

// gateway.ts resolves the API origin at module load; stub it before any
// dynamic import touches cameras.api, mirroring
// LayoutEditorDialogChainRetention.test.tsx and staleBearerRetry.test.tsx —
// fetchBaseQuery needs an absolute URL, and Node's `Request` rejects a
// relative one.
vi.stubEnv('VITE_API_GATEWAY_URL', 'http://gateway.test');

vi.mock('react-oidc-context', () => ({
  useAuth: () => ({ user: { access_token: 'operator-access-token' } }),
}));

// CameraViewer mounts a WhepClient against RTCPeerConnection, which jsdom has
// no global for — stubbed the same way CameraDetailPage.test.tsx stubs it.
vi.mock('@smart-sentinel-eye/shared/ui/composites/CameraViewer', () => ({
  CameraViewer: () => <div data-testid="camera-viewer">viewer</div>,
}));

const { camerasApi } = await import('@smart-sentinel-eye/shared/api/cameras.api');
const { CameraDetailPage } = await import('./CameraDetailPage.js');

const CAMERA_IDENTIFIER = '11111111-1111-1111-1111-111111111111';

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

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

function forbiddenResponse(): Response {
  return jsonResponse({ title: 'FORBIDDEN', status: 403 }, 403);
}

/**
 * A true "has this request settled" signal, read from the store rather than
 * the DOM — `RetryBanner`'s button carries no `disabled` state to poll, and
 * the record/banner markup is identical whether the second strike's refetch
 * is still in flight or has already resolved (N1, phase 6). The base
 * `endpoint.select(...)` selector's `status` goes to `'pending'` the instant
 * a request for this cache entry — initial or refetch alike — is dispatched,
 * and only leaves `'pending'` once the response has been written back
 * (`writePendingCacheEntry`/`writeFulfilledCacheEntry` in RTK Query's own
 * slice), which `isLoading` here mirrors.
 */
function isCameraQueryPending(store: ReturnType<typeof createStore>): boolean {
  return camerasApi.endpoints.getCamera.select({ cameraIdentifier: CAMERA_IDENTIFIER })(store.getState()).isLoading;
}

function createStore() {
  return configureStore({
    reducer: { [camerasApi.reducerPath]: camerasApi.reducer },
    middleware: (getDefault) => getDefault().concat(camerasApi.middleware),
  });
}

function renderDetailPage(store: ReturnType<typeof createStore>, identifier: string) {
  return render(
    <Provider store={store}>
      <MemoryRouter initialEntries={[`/cameras/${identifier}`]}>
        <Routes>
          <Route path="/cameras/:cameraIdentifier" element={<CameraDetailPage />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

/**
 * Spec 310 (#2725) T005, plan.md §5 row 5. Real `camerasApi` against a real
 * store — nothing about `useGetCameraQuery` is mocked here, unlike
 * `CameraDetailPage.test.tsx` — with `fetch` stubbed at the network
 * boundary. The point, stated explicitly in the plan, is to pin §1.2's
 * `requestId`/`isFetching` assumption against RTK Query 2.12 itself, not
 * against a hand-built double of its internals.
 *
 * **How the first of the three 403s happens without a button.**
 * `CameraDetailPage` polls nothing and never calls `setupListeners` (spec.md
 * §1.2, item 2), so nothing refetches on its own once the first load has
 * succeeded — and the Retry banner that exposes the only refetch affordance
 * exists only once `error` is already set, which it is not yet. The first
 * refusal is produced the same way a real mid-session revocation becomes
 * visible without a manual reload: by invalidating the `Camera` tag while
 * this page is the query's only active subscriber. RTK Query's own
 * documented behaviour for that case — also relied on by
 * `LayoutEditorDialogChainRetention.test.tsx` — is to refetch the active
 * subscription immediately, which is exactly what happens here. This is
 * `camerasApi.util.invalidateTags`, the library's public action creator, not
 * a reach into its private state. Once that refetch's 403 has set `error`,
 * the Retry banner exists for real, and the second and third 403s are each
 * produced by genuinely clicking it.
 */
describe('CameraDetailPage against the real store and a stubbed network (spec 310 #2725)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('Shows "No such camera" after three consecutive 403 refreshes of the camera on screen', async () => {
    let callCount = 0;
    const fetchMock = vi.fn((request: Request) => {
      callCount += 1;
      expect(request.method).toBe('GET');
      return Promise.resolve(callCount === 1 ? jsonResponse(cameraDetail()) : forbiddenResponse());
    });
    vi.stubGlobal('fetch', fetchMock);

    const store = createStore();
    renderDetailPage(store, CAMERA_IDENTIFIER);

    // The initial load, settled on a condition (ADR-0150) rather than a
    // fixed tick count.
    expect(await screen.findByRole('heading', { name: 'Line-1-Entrance' })).toBeInTheDocument();
    expect(callCount).toBe(1);

    // Strike 1 — see the file-level comment: the real RTK Query auto-refetch
    // triggered by invalidating this page's own active subscription.
    store.dispatch(camerasApi.util.invalidateTags([{ type: 'Camera', id: CAMERA_IDENTIFIER }]));
    await act(async () => {
      await Promise.resolve();
    });

    const retryAfterFirstStrike = await screen.findByRole('button', { name: /retry/i });
    expect(callCount).toBe(2);
    // US1-A (spec 211): one refusal keeps the record, beside the banner.
    expect(screen.getByRole('heading', { name: 'Line-1-Entrance' })).toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent(/could not refresh/i);

    // Strike 2 — a genuine click of the real Retry button. Waiting on
    // `callCount === 3` alone would pass as soon as the third fetch is
    // *dispatched*, not once it has *settled* — `isFetching` only goes back
    // to `false` after RTK Query has processed the response (N1, phase 6).
    const user = userEvent.setup();
    await user.click(retryAfterFirstStrike);
    await waitFor(() => {
      expect(callCount).toBe(3);
      expect(isCameraQueryPending(store)).toBe(false);
    });

    expect(screen.getByRole('heading', { name: 'Line-1-Entrance' })).toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent(/could not refresh/i);

    // Strike 3 — the threshold. The record must leave the screen now, not
    // merely show another banner. Same reasoning as strike 2's wait above:
    // `callCount === 4` alone is true the instant the fourth fetch is
    // *dispatched*, not once RTK Query has processed the response and the
    // strike is actually counted — a suspected contributor to this test's
    // CI-only flake (#2762; not reproduced in 25/25 local runs in isolation,
    // so this closes a real gap without being confirmed as the sole cause).
    await user.click(screen.getByRole('button', { name: /retry/i }));
    await waitFor(() => {
      expect(callCount).toBe(4);
      expect(isCameraQueryPending(store)).toBe(false);
    });

    // Widened from the 1000ms default (#2762): this assertion has flaked in
    // CI roughly every other run regardless of PR content, including on a
    // PR that touched nothing but this file and a prior attempt at the same
    // fix (strike 3's settlement wait, above) — never reproduced in 25+
    // local runs in isolation. The remaining render work after the strike
    // settles (useRevocationFallback's state update, the page's re-render,
    // masking the record) is real but should be sub-100ms; a loaded
    // 2-core GitHub runner occasionally needing more than 1000ms for it is
    // the simplest remaining explanation that fits every data point so far.
    expect(await screen.findByRole('heading', { name: /no such camera/i }, { timeout: 5000 })).toBeInTheDocument();
    expect(screen.queryByText('Line-1-Entrance')).toBeNull();
    expect(screen.queryByText('rtsp://10.0.5.12/h264')).toBeNull();
    expect(screen.queryByTestId('camera-viewer')).toBeNull();
    expect(screen.queryByRole('button', { name: /^rename$/i })).toBeNull();
    expect(screen.queryByRole('button', { name: /correct the address/i })).toBeNull();
    expect(screen.queryByRole('button', { name: /retire camera/i })).toBeNull();
    expect(screen.queryByRole('alert')).toBeNull();
  });
});
