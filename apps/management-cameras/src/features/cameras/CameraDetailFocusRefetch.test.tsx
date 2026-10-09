import { configureStore, createListenerMiddleware } from '@reduxjs/toolkit';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  fireWindowFocus,
  installFocusListeners,
  settleRunningQueries,
} from '@smart-sentinel-eye/shared/test/focusRefetch';

// gateway.ts resolves the API origin at module load; stub it before any
// dynamic import touches cameras.api, mirroring CameraDetailRevocation.test.tsx.
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

/**
 * Spec 317 (#2751) T005, plan.md §4 row 4. Real `camerasApi` against a real
 * store, `fetch` stubbed at the network boundary, a real `window` focus
 * event (FR-006) — mirrors `CameraDetailRevocation.test.tsx`'s pattern,
 * extended with `installFocusListeners`/`fireWindowFocus` (T002) in place
 * of a Retry click or `invalidateTags`.
 *
 * RED today for every case except the two the anti-tautology note calls
 * out: `CameraDetailPage` never calls `setupListeners` and its
 * `useGetCameraQuery` passes no `refetchOnFocus`, so a focus event changes
 * nothing. "No request while a dialog is open" and the 503/200 outline rows
 * are green before the fix too — vacuously, since nothing refetches on
 * focus at all yet; "exactly one request after the dialog closes" is the
 * row beside them that proves the mechanism actually engaged once it does.
 */

function cameraDetail(overrides: Partial<ReturnType<typeof baseCameraDetail>> = {}) {
  return { ...baseCameraDetail(), ...overrides };
}

function baseCameraDetail() {
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

function problemResponse(status: number): Response {
  return jsonResponse({ title: 'PROBLEM', status }, status);
}

function createStore() {
  // Spec 314 (#2762): useRevocationFallback observes settlements via a
  // dispatched listener and throws without this middleware in the store.
  const listenerMiddleware = createListenerMiddleware();
  return configureStore({
    reducer: { [camerasApi.reducerPath]: camerasApi.reducer },
    middleware: (getDefault) => getDefault().prepend(listenerMiddleware.middleware).concat(camerasApi.middleware),
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

describe('CameraDetailPage refetches on window focus (spec 317, #2751)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('A focus sends exactly one new request for the camera on screen', async () => {
    let callCount = 0;
    const fetchMock = vi.fn(() => {
      callCount += 1;
      return Promise.resolve(jsonResponse(cameraDetail()));
    });
    vi.stubGlobal('fetch', fetchMock);

    const store = createStore();
    installFocusListeners(store);
    renderDetailPage(store, CAMERA_IDENTIFIER);

    await screen.findByRole('heading', { name: 'Line-1-Entrance' });
    expect(callCount).toBe(1);

    fireWindowFocus();
    await settleRunningQueries(store, camerasApi);

    expect(callCount).toBe(2);
  });

  it('Shows "No such camera" after three consecutive 403 focus refreshes', async () => {
    let callCount = 0;
    const fetchMock = vi.fn(() => {
      callCount += 1;
      return Promise.resolve(callCount === 1 ? jsonResponse(cameraDetail()) : problemResponse(403));
    });
    vi.stubGlobal('fetch', fetchMock);

    const store = createStore();
    installFocusListeners(store);
    renderDetailPage(store, CAMERA_IDENTIFIER);

    await screen.findByRole('heading', { name: 'Line-1-Entrance' });

    for (let strike = 0; strike < 3; strike += 1) {
      fireWindowFocus();
      await settleRunningQueries(store, camerasApi);
    }

    expect(await screen.findByRole('heading', { name: /no such camera/i })).toBeInTheDocument();
    expect(screen.queryByText('Line-1-Entrance')).toBeNull();
    expect(screen.queryByRole('alert')).toBeNull();
  });

  it.each([
    { status: 403, refused: true },
    { status: 404, refused: true },
    { status: 503, refused: false },
    { status: 200, refused: false },
  ])('Three focus refreshes answered $status leave the page refused=$refused', async ({ status, refused }) => {
    let callCount = 0;
    const fetchMock = vi.fn(() => {
      callCount += 1;
      if (callCount === 1) {
        return Promise.resolve(jsonResponse(cameraDetail()));
      }
      return Promise.resolve(status === 200 ? jsonResponse(cameraDetail()) : problemResponse(status));
    });
    vi.stubGlobal('fetch', fetchMock);

    const store = createStore();
    installFocusListeners(store);
    renderDetailPage(store, CAMERA_IDENTIFIER);

    await screen.findByRole('heading', { name: 'Line-1-Entrance' });

    for (let strike = 0; strike < 3; strike += 1) {
      fireWindowFocus();
      await settleRunningQueries(store, camerasApi);
    }

    if (refused) {
      expect(await screen.findByRole('heading', { name: /no such camera/i })).toBeInTheDocument();
    } else {
      expect(screen.queryByRole('heading', { name: /no such camera/i })).toBeNull();
    }
  });

  describe.each([
    { dialog: 'Rename', openButton: /^rename$/i },
    { dialog: 'Edit camera address', openButton: /correct the address/i },
    { dialog: 'Retire', openButton: /retire camera/i },
  ])('while the $dialog dialog is open (D2)', ({ openButton }) => {
    it('sends no request on focus while open, then exactly one once it is closed', async () => {
      let callCount = 0;
      const fetchMock = vi.fn(() => {
        callCount += 1;
        return Promise.resolve(jsonResponse(cameraDetail()));
      });
      vi.stubGlobal('fetch', fetchMock);

      const store = createStore();
      installFocusListeners(store);
      renderDetailPage(store, CAMERA_IDENTIFIER);

      await screen.findByRole('heading', { name: 'Line-1-Entrance' });
      expect(callCount).toBe(1);

      const user = userEvent.setup();
      await user.click(screen.getByRole('button', { name: openButton }));

      fireWindowFocus();
      await settleRunningQueries(store, camerasApi);

      // While open: the dialog quotes `record.version`/`record.name` live
      // from the query result (D2) — no request may move it underneath the
      // operator.
      expect(callCount).toBe(1);

      await user.click(screen.getByRole('button', { name: /cancel/i }));

      fireWindowFocus();
      await settleRunningQueries(store, camerasApi);

      // After close: the critical row — proves the suspend isn't just focus
      // refetch being disabled outright.
      expect(callCount).toBe(2);
    });
  });
});
