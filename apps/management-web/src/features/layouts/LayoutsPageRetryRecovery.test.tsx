import { configureStore, createListenerMiddleware } from '@reduxjs/toolkit';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { afterEach, describe, expect, it, vi } from 'vitest';

// gateway.ts resolves the API origin at module load; stub it before any
// dynamic import touches layouts.api/cameras.api.
vi.stubEnv('VITE_API_GATEWAY_URL', 'http://gateway.test');

// LayoutsPage mounts LayoutEditorDialog (closed), which reads camera choices and
// overlays through their own queries; stubbed as LayoutsPageFocusRefetch does so
// this file is about the list query alone.
vi.mock('@smart-sentinel-eye/shared/api/cameras.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/cameras.api')>();
  return {
    ...actual,
    useListAllCameraChoicesQuery: () => ({
      data: { items: [], count: 0, complete: true },
      currentData: { items: [], count: 0, complete: true },
      isLoading: false,
      isFetching: false,
      isError: false,
    }),
  };
});

vi.mock('@smart-sentinel-eye/shared/api/overlays.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/overlays.api')>();
  return {
    ...actual,
    useListOverlaysQuery: () => ({ data: { chains: [], published: [] }, isLoading: false }),
  };
});

const { layoutsApi } = await import('@smart-sentinel-eye/shared/api/layouts.api');
const { LayoutsPage } = await import('./LayoutsPage.js');

/**
 * Spec 429 (#3073) R9, phase 4a RED. Real `layoutsApi`, `fetch` stubbed: the
 * first list read is refused, the retried one succeeds, and the page heading
 * must take focus (today it stays on `document.body`).
 */

function listResponse() {
  return {
    chains: [
      {
        layoutIdentifier: '11111111-1111-1111-1111-111111111111',
        version: 0,
        fab: 'munich',
        name: 'Line-1',
        createdAt: '2026-05-26T10:00:00Z',
        createdBy: '22222222-2222-2222-2222-222222222222',
        revisions: [
          {
            revisionIdentifier: '33333333-3333-3333-3333-333333333333',
            revisionNumber: 1,
            state: 'Draft',
            gridRows: 1,
            gridCols: 1,
            tiles: [
              {
                cameraIdentifier: '44444444-4444-4444-4444-444444444444',
                overlayIdentifier: null,
                row: 0,
                col: 0,
                rowSpan: 1,
                colSpan: 1,
              },
            ],
            createdAt: '2026-05-26T10:00:00Z',
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
    reducer: { [layoutsApi.reducerPath]: layoutsApi.reducer },
    middleware: (getDefault) => getDefault().prepend(listenerMiddleware.middleware).concat(layoutsApi.middleware),
  });
}

describe('LayoutsPage retry recovery (spec 429)', () => {
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
        <LayoutsPage />
      </Provider>,
    );

    const retry = await screen.findByRole('button', { name: 'Retry' });
    retry.focus();
    await user.click(retry);
    await screen.findByText('Line-1');

    expect(screen.getByRole('heading', { level: 1, name: 'Layouts' })).toHaveFocus();
  });
});
