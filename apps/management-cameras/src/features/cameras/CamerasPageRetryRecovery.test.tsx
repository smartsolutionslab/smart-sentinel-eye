import { configureStore, createListenerMiddleware } from '@reduxjs/toolkit';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { jsonResponse } from '@smart-sentinel-eye/shared/test/responses';

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

/**
 * Spec 429 (#3073) R13, phase 4a RED. Real `camerasApi`/`streamsApi`, `fetch`
 * stubbed and routed by URL: the first camera-list read is refused, the retried
 * one succeeds, and the page heading must take focus (today it stays on
 * `document.body`).
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

function createStore() {
  const listenerMiddleware = createListenerMiddleware();
  return configureStore({
    reducer: { [camerasApi.reducerPath]: camerasApi.reducer, [streamsApi.reducerPath]: streamsApi.reducer },
    middleware: (getDefault) =>
      getDefault().prepend(listenerMiddleware.middleware).concat(camerasApi.middleware, streamsApi.middleware),
  });
}

describe('CamerasPage retry recovery (spec 429)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('Focuses the page heading when a retried read succeeds', async () => {
    const user = userEvent.setup();
    let cameraCalls = 0;
    vi.stubGlobal(
      'fetch',
      vi.fn((request: Request) => {
        if (request.url.includes('stream-distribution/streams')) {
          return Promise.resolve(jsonResponse([]));
        }
        cameraCalls += 1;
        return Promise.resolve(
          cameraCalls === 1 ? jsonResponse({ title: 'PROBLEM', status: 500 }, 500) : jsonResponse(populatedPage()),
        );
      }),
    );
    render(
      <Provider store={createStore()}>
        <MemoryRouter>
          <CamerasPage />
        </MemoryRouter>
      </Provider>,
    );

    const retry = await screen.findByRole('button', { name: 'Retry' });
    retry.focus();
    await user.click(retry);
    await screen.findByText('Line-1-Entrance');

    expect(screen.getByRole('heading', { level: 1, name: 'Cameras' })).toHaveFocus();
  });
});
