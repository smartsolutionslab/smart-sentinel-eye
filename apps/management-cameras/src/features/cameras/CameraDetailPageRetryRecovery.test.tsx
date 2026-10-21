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
// dynamic import touches cameras.api, mirroring CameraDetailFocusRefetch.test.tsx.
vi.stubEnv('VITE_API_GATEWAY_URL', 'http://gateway.test');

vi.mock('react-oidc-context', () => ({
  useAuth: () => ({ user: { access_token: 'operator-access-token' } }),
}));

vi.mock('@smart-sentinel-eye/shared/api/streams.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/streams.api')>();
  return {
    ...actual,
    useGetStreamQuery: () => ({ data: undefined, currentData: undefined, isLoading: false, error: undefined }),
  };
});

vi.mock('@smart-sentinel-eye/shared/ui/composites/CameraViewer', () => ({
  CameraViewer: () => <div data-testid="camera-viewer">viewer</div>,
}));

const { camerasApi } = await import('@smart-sentinel-eye/shared/api/cameras.api');
const { CameraDetailPage } = await import('./CameraDetailPage.js');

const CAMERA_IDENTIFIER = '11111111-1111-1111-1111-111111111111';

/**
 * Spec 429 (#3073) R14, phase 4a RED. The detail page only shows its Retry
 * banner over a record already on screen, so the failure here is a refused
 * focus refresh: the record loads, a window focus refreshes it, the refresh is
 * refused (banner), and the operator's Retry succeeds. The page heading (the
 * camera name) must then take focus (today it stays on `document.body`).
 */

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

function createStore() {
  const listenerMiddleware = createListenerMiddleware();
  return configureStore({
    reducer: { [camerasApi.reducerPath]: camerasApi.reducer },
    middleware: (getDefault) => getDefault().prepend(listenerMiddleware.middleware).concat(camerasApi.middleware),
  });
}

describe('CameraDetailPage retry recovery (spec 429)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('Focuses the page heading when a retried refresh succeeds', async () => {
    const user = userEvent.setup();
    let callCount = 0;
    vi.stubGlobal(
      'fetch',
      vi.fn(() => {
        callCount += 1;
        return Promise.resolve(
          callCount === 2 ? jsonResponse({ title: 'PROBLEM', status: 503 }, 503) : jsonResponse(cameraDetail()),
        );
      }),
    );
    const store = createStore();
    installFocusListeners(store);
    render(
      <Provider store={store}>
        <MemoryRouter initialEntries={[`/cameras/${CAMERA_IDENTIFIER}`]}>
          <Routes>
            <Route path="/cameras/:cameraIdentifier" element={<CameraDetailPage />} />
          </Routes>
        </MemoryRouter>
      </Provider>,
    );
    await screen.findByRole('heading', { name: 'Line-1-Entrance' });

    fireWindowFocus();
    await settleRunningQueries(store, camerasApi);
    const retry = await screen.findByRole('button', { name: 'Retry' });
    retry.focus();
    await user.click(retry);
    await settleRunningQueries(store, camerasApi);
    await screen.findByRole('heading', { name: 'Line-1-Entrance' });

    expect(screen.getByRole('heading', { level: 1, name: 'Line-1-Entrance' })).toHaveFocus();
  });
});
