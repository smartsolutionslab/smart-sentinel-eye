import { configureStore } from '@reduxjs/toolkit';
import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';

// Same stubbing as CameraDetailRevocation.test.tsx — fetchBaseQuery needs an
// absolute URL and gateway.ts resolves it at module load.
vi.stubEnv('VITE_API_GATEWAY_URL', 'http://gateway.test');

vi.mock('react-oidc-context', () => ({
  useAuth: () => ({ user: { access_token: 'operator-access-token' } }),
}));

vi.mock('@smart-sentinel-eye/shared/ui/composites/CameraViewer', () => ({
  CameraViewer: () => <div data-testid="camera-viewer">viewer</div>,
}));

const { camerasApi } = await import('@smart-sentinel-eye/shared/api/cameras.api');
const { CameraDetailPage } = await import('./CameraDetailPage.js');

const CAMERA_IDENTIFIER = '11111111-1111-1111-1111-111111111111';
const NEVER_LOADED_IDENTIFIER = '22222222-2222-2222-2222-222222222222';

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

function notFoundResponse(): Response {
  return jsonResponse({ title: 'CAMERA_NOT_FOUND', status: 404, detail: 'No such camera.' }, 404);
}

/**
 * Store-level "settled" signal, same reasoning as
 * CameraDetailRevocation.test.tsx's `isCameraQueryPending`: the DOM alone
 * cannot distinguish a strike's refetch still in flight from one already
 * resolved, and the two render identically either way (N1, phase 6).
 */
function isCameraQueryPending(store: ReturnType<typeof createStore>, identifier: string): boolean {
  return camerasApi.endpoints.getCamera.select({ cameraIdentifier: identifier })(store.getState()).isLoading;
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
 * Spec 313 (#2750) T003, plan.md §5 row 3. Real `camerasApi` against a real
 * store, `fetch` stubbed at the network boundary — same mechanism as
 * `CameraDetailRevocation.test.tsx`, pinning RTK Query 2.12's `currentData`
 * retention against a rejected **404** refetch (not just a 403) as spec.md
 * §1 claims.
 *
 * Expected RED today (tasks.md): the three-404 case, because
 * `CameraDetailPage` does not yet pass `notFoundRevokes` to the hook, so a
 * 404 resets the strike count and the heading stays "Line-1-Entrance".
 * The two-404 and first-load cases are expected GREEN already — a 404
 * resetting the count (today's behaviour) and a first-load 404 rendering
 * "No such camera" (spec 029, unaffected by this change either way) are
 * both what happens with or without the fix.
 */
describe('CameraDetailPage against the real store and a stubbed network (spec 313 #2750)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('shows "No such camera" after three consecutive 404 refreshes following a successful load', async () => {
    let callCount = 0;
    const fetchMock = vi.fn((request: Request) => {
      callCount += 1;
      expect(request.method).toBe('GET');
      return Promise.resolve(callCount === 1 ? jsonResponse(cameraDetail()) : notFoundResponse());
    });
    vi.stubGlobal('fetch', fetchMock);

    const store = createStore();
    renderDetailPage(store, CAMERA_IDENTIFIER);

    expect(await screen.findByRole('heading', { name: 'Line-1-Entrance' })).toBeInTheDocument();
    expect(callCount).toBe(1);

    // Strike 1 — same invalidate-while-the-only-subscriber mechanism as
    // CameraDetailRevocation.test.tsx, triggering a real refetch that
    // settles 404.
    store.dispatch(camerasApi.util.invalidateTags([{ type: 'Camera', id: CAMERA_IDENTIFIER }]));
    await act(async () => {
      await Promise.resolve();
    });

    const retryAfterFirstStrike = await screen.findByRole('button', { name: /retry/i });
    expect(callCount).toBe(2);
    expect(screen.getByRole('heading', { name: 'Line-1-Entrance' })).toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent(/could not refresh/i);

    // Strike 2 — a genuine click of the real Retry button.
    const user = userEvent.setup();
    await user.click(retryAfterFirstStrike);
    await waitFor(() => {
      expect(callCount).toBe(3);
      expect(isCameraQueryPending(store, CAMERA_IDENTIFIER)).toBe(false);
    });

    expect(screen.getByRole('heading', { name: 'Line-1-Entrance' })).toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent(/could not refresh/i);

    // Strike 3 — the threshold. The record, viewer and controls must leave
    // the screen, exactly as three 403s already do.
    await user.click(screen.getByRole('button', { name: /retry/i }));
    await waitFor(() => {
      expect(callCount).toBe(4);
      expect(isCameraQueryPending(store, CAMERA_IDENTIFIER)).toBe(false);
    });

    expect(await screen.findByRole('heading', { name: /no such camera/i }, { timeout: 5000 })).toBeInTheDocument();
    expect(screen.queryByText('Line-1-Entrance')).toBeNull();
    expect(screen.queryByText('rtsp://10.0.5.12/h264')).toBeNull();
    expect(screen.queryByTestId('camera-viewer')).toBeNull();
    expect(screen.queryByRole('button', { name: /^rename$/i })).toBeNull();
    expect(screen.queryByRole('button', { name: /correct the address/i })).toBeNull();
    expect(screen.queryByRole('button', { name: /retire camera/i })).toBeNull();
    expect(screen.queryByRole('alert')).toBeNull();
  });

  it('keeps the record and banner after only two consecutive 404 refreshes', async () => {
    let callCount = 0;
    const fetchMock = vi.fn((request: Request) => {
      callCount += 1;
      expect(request.method).toBe('GET');
      return Promise.resolve(callCount === 1 ? jsonResponse(cameraDetail()) : notFoundResponse());
    });
    vi.stubGlobal('fetch', fetchMock);

    const store = createStore();
    renderDetailPage(store, CAMERA_IDENTIFIER);

    expect(await screen.findByRole('heading', { name: 'Line-1-Entrance' })).toBeInTheDocument();
    expect(callCount).toBe(1);

    // Strike 1.
    store.dispatch(camerasApi.util.invalidateTags([{ type: 'Camera', id: CAMERA_IDENTIFIER }]));
    await act(async () => {
      await Promise.resolve();
    });

    const retryAfterFirstStrike = await screen.findByRole('button', { name: /retry/i });
    expect(callCount).toBe(2);

    // Strike 2 — still below the threshold of 3.
    const user = userEvent.setup();
    await user.click(retryAfterFirstStrike);
    await waitFor(() => {
      expect(callCount).toBe(3);
      expect(isCameraQueryPending(store, CAMERA_IDENTIFIER)).toBe(false);
    });

    expect(screen.getByRole('heading', { name: 'Line-1-Entrance' })).toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent(/could not refresh/i);
    expect(screen.queryByRole('heading', { name: /no such camera/i })).toBeNull();
  });

  // Fence: a 404 with no prior successful load for this identifier is
  // spec 029's ordinary refusal, unaffected by this change — the record was
  // never shown, so there is no strike to count either before or after.
  it('renders "No such camera" on the very first load of a never-registered identifier, unchanged', async () => {
    const fetchMock = vi.fn((request: Request) => {
      expect(request.method).toBe('GET');
      return Promise.resolve(notFoundResponse());
    });
    vi.stubGlobal('fetch', fetchMock);

    const store = createStore();
    renderDetailPage(store, NEVER_LOADED_IDENTIFIER);

    expect(await screen.findByRole('heading', { name: /no such camera/i })).toBeInTheDocument();
    expect(screen.queryByTestId('camera-viewer')).toBeNull();
  });
});
