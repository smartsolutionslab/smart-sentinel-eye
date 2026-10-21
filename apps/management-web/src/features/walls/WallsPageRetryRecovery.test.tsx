import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';

// gateway.ts resolves the API origin at module load; stub it before the store
// (and with it every api slice) is imported.
vi.stubEnv('VITE_API_GATEWAY_URL', 'http://gateway.test');

const { store, resetApiCaches } = await import('../../app/store.js');
const { WallsPage } = await import('./WallsPage.js');

/**
 * Spec 429 (#3073) R11, phase 4a RED. The app's real store, `fetch` stubbed and
 * routed by URL: the first walls read is refused, the retried one succeeds, and
 * the page heading must take focus (today it stays on `document.body`).
 */

function wall() {
  return {
    wallIdentifier: '11111111-1111-1111-1111-111111111111',
    version: 0,
    fab: 'munich',
    name: 'Line 3 rotation',
    scenes: [],
    showing: '22222222-2222-2222-2222-222222222222',
    sceneVersion: 0,
    showingSince: '2026-01-01T00:00:00Z',
  };
}

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

describe('WallsPage retry recovery (spec 429)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    // Unmount first: a mounted page would re-subscribe the moment the cache is reset.
    cleanup();
    resetApiCaches(store.dispatch);
  });

  it('Focuses the page heading when a retried read succeeds', async () => {
    const user = userEvent.setup();
    let wallsCalls = 0;
    vi.stubGlobal(
      'fetch',
      vi.fn((request: Request) => {
        if (request.url.includes('layout-composition/layouts')) {
          return Promise.resolve(jsonResponse({ chains: [], published: [] }));
        }
        wallsCalls += 1;
        return Promise.resolve(
          wallsCalls === 1 ? jsonResponse({ title: 'PROBLEM', status: 500 }, 500) : jsonResponse([wall()]),
        );
      }),
    );
    render(
      <Provider store={store}>
        <MemoryRouter>
          <WallsPage />
        </MemoryRouter>
      </Provider>,
    );

    const retry = await screen.findByRole('button', { name: 'Retry' });
    retry.focus();
    await user.click(retry);
    await screen.findByText('Line 3 rotation');

    expect(screen.getByRole('heading', { level: 1, name: 'Walls' })).toHaveFocus();
  });
});
