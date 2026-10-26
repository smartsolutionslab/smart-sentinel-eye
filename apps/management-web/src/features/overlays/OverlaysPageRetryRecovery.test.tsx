import { configureStore, createListenerMiddleware } from '@reduxjs/toolkit';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';

// gateway.ts resolves the API origin at module load; stub it before any
// dynamic import touches overlays.api.
vi.stubEnv('VITE_API_GATEWAY_URL', 'http://gateway.test');

const { overlaysApi } = await import('@smart-sentinel-eye/shared/api/overlays.api');
const { OverlaysPage } = await import('./OverlaysPage.js');

/**
 * Spec 429 (#3073) R10, phase 4a RED. Real `overlaysApi`, `fetch` stubbed: the
 * first list read is refused, the retried one succeeds, and the page heading
 * must take focus (today it stays on `document.body`).
 */

function listResponse() {
  return {
    chains: [
      {
        overlayIdentifier: '11111111-1111-1111-1111-111111111111',
        version: 0,
        name: 'Line-1 Title',
        createdAt: '2026-05-27T10:00:00Z',
        createdBy: '22222222-2222-2222-2222-222222222222',
        revisions: [
          {
            revisionIdentifier: '33333333-3333-3333-3333-333333333333',
            revisionNumber: 1,
            state: 'Draft',
            elements: [
              {
                kind: 'Text',
                color: '#9F9F9FD9',
                text: 'Production Line 1',
                normalizedX: 0.1,
                normalizedY: 0.1,
                normalizedWidth: 0.3,
                normalizedHeight: 0.08,
                fontSizePx: 32,
              },
            ],
            createdAt: '2026-05-27T10:00:00Z',
            createdBy: '22222222-2222-2222-2222-222222222222',
            publishedAt: null,
            archivedAt: null,
          },
        ],
      },
    ],
    published: [],
  };
}

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

function createStore() {
  // useRevocationFallback observes settlements via a dispatched listener and
  // throws without this middleware in the store.
  const listenerMiddleware = createListenerMiddleware();
  return configureStore({
    reducer: { [overlaysApi.reducerPath]: overlaysApi.reducer },
    middleware: (getDefault) => getDefault().prepend(listenerMiddleware.middleware).concat(overlaysApi.middleware),
  });
}

describe('OverlaysPage retry recovery (spec 429)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('Focuses the page heading when a retried read succeeds', async () => {
    const user = userEvent.setup();
    let callCount = 0;
    vi.stubGlobal(
      'fetch',
      vi.fn(() => {
        callCount += 1;
        return Promise.resolve(
          callCount === 1 ? jsonResponse({ title: 'PROBLEM', status: 500 }, 500) : jsonResponse(listResponse()),
        );
      }),
    );
    render(
      <Provider store={createStore()}>
        <MemoryRouter>
          <OverlaysPage />
        </MemoryRouter>
      </Provider>,
    );

    const retry = await screen.findByRole('button', { name: 'Retry' });
    retry.focus();
    await user.click(retry);
    await screen.findByText('Line-1 Title');

    expect(screen.getByRole('heading', { level: 1, name: 'Overlays' })).toHaveFocus();
  });
});
