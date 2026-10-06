import { configureStore } from '@reduxjs/toolkit';
import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';

// gateway.ts resolves the API origin at module load — stub the env before any
// dynamic import touches overlays.api, directly or via OverlayEditPage, so
// fetchBaseQuery builds absolute URLs (Node's `Request` rejects a relative
// one). Same stub `OverlayEditorDialogChainRetention.test.tsx` uses.
vi.stubEnv('VITE_API_GATEWAY_URL', 'http://gateway.test');

/**
 * Spec 305 (#2350), plan.md §Phase 4a: this is the route's re-pin of
 * `OverlayEditorDialogChainRetention.test.tsx`'s A→B case, which plan.md
 * retires along with the dialog because a route is navigated between two
 * URLs on one mounted `OverlayEditPage` instance exactly the way the dialog
 * used to be driven `editTarget: A -> undefined -> B` on one mounted
 * instance — not an unmount and a fresh mount.
 *
 * `useGetOverlayQuery` is REAL in this file, for the same reason it is real
 * in `CameraDetailPageNavigation.test.tsx`: a mocked hook returns the same
 * value regardless of its argument, so it cannot express RTK Query's own
 * `data`/`currentData` distinction (`data` survives an argument change;
 * `currentData` resets on one). `OverlayEditPage` does not exist yet
 * (T009) — every test below fails on a missing module, not a wrong
 * assertion (ADR-0139/0144).
 */
vi.mock('@smart-sentinel-eye/shared/api/cameras.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/cameras.api')>();
  return {
    ...actual,
    useListAllCameraChoicesQuery: () => ({
      data: { items: [], count: 0, complete: true },
      isLoading: false,
      isFetching: false,
      isError: false,
    }),
  };
});

vi.mock('@smart-sentinel-eye/shared/api/streams.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/streams.api')>();
  return {
    ...actual,
    useGetStreamQuery: () => ({ data: undefined, isLoading: false, error: undefined }),
  };
});

vi.mock('@smart-sentinel-eye/shared/api/systemVariables.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/systemVariables.api')>();
  return {
    ...actual,
    useResolveOverlayTextQuery: () => ({ currentData: undefined, isFetching: false, isError: false }),
  };
});

const { overlaysApi } = await import('@smart-sentinel-eye/shared/api/overlays.api');
const { OverlayEditPage } = await import('./OverlayEditPage.js');

const OVERLAY_A = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const OVERLAY_B = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';

function labelElement(text: string) {
  return {
    kind: 'Text' as const,
    color: '#FFFFFFD9',
    text,
    normalizedX: 0.1,
    normalizedY: 0.1,
    normalizedWidth: 0.3,
    normalizedHeight: 0.08,
    fontSizePx: 32,
  };
}

function chainOf(overlayIdentifier: string, version: number, name: string, text: string) {
  return {
    overlayIdentifier,
    version,
    name,
    createdAt: '2026-05-27T10:00:00Z',
    createdBy: '22222222-2222-2222-2222-222222222222',
    revisions: [
      {
        revisionIdentifier: 'rev-1',
        revisionNumber: 1,
        state: 'Draft' as const,
        elements: [labelElement(text)],
        createdAt: '2026-05-27T10:00:00Z',
        createdBy: '22222222-2222-2222-2222-222222222222',
        publishedAt: null,
        archivedAt: null,
      },
    ],
  };
}

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

function createStore() {
  return configureStore({
    reducer: { [overlaysApi.reducerPath]: overlaysApi.reducer },
    middleware: (getDefault) => getDefault().concat(overlaysApi.middleware),
  });
}

function renderAt(store: ReturnType<typeof createStore>, initial: string) {
  const router = createMemoryRouter(
    [{ path: '/overlays/:overlayIdentifier/revisions/:revisionNumber/edit', element: <OverlayEditPage /> }],
    { initialEntries: [initial] },
  );
  render(
    <Provider store={store}>
      <RouterProvider router={router} />
    </Provider>,
  );
  return router;
}

describe('OverlayEditPage navigation — B never inherits A (spec 305, re-pins OverlayEditorDialogChainRetention A→B)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("Never shows A's label text or submits A's version while B's own read is still in flight", async () => {
    let resolveB: ((response: Response) => void) | undefined;
    const fetchMock = vi.fn((request: Request) => {
      if (request.method === 'GET' && request.url.includes(OVERLAY_A)) {
        return Promise.resolve(jsonResponse(chainOf(OVERLAY_A, 7, 'Overlay A', 'Line A')));
      }
      if (request.method === 'GET' && request.url.includes(OVERLAY_B)) {
        // Held open for the whole test — B's own version is never answered,
        // so nothing has told the page what it is.
        return new Promise<Response>((resolve) => {
          resolveB = resolve;
        });
      }
      if (request.method === 'PATCH') {
        return Promise.resolve(jsonResponse(1));
      }
      throw new Error(`unexpected fetch: ${request.method} ${request.url}`);
    });
    vi.stubGlobal('fetch', fetchMock);

    const store = createStore();
    const router = renderAt(store, `/overlays/${OVERLAY_A}/revisions/1/edit`);

    await waitFor(() =>
      expect(screen.getByRole('button', { name: /^save draft$/i })).toHaveAttribute('aria-disabled', 'false'),
    );
    expect(screen.getByTestId('overlay-editor-text')).toHaveValue('Line A');

    // Navigate the SAME mounted page instance from A's URL to B's — not an
    // unmount and a fresh mount.
    await act(async () => {
      await router.navigate(`/overlays/${OVERLAY_B}/revisions/1/edit`);
    });

    expect(screen.queryByDisplayValue('Line A')).not.toBeInTheDocument();
    // The point of the test: Save must stay unavailable until B's own GET
    // answers, not merely until some GET for some overlay has, once.
    expect(screen.getByRole('button', { name: /^save draft$/i })).toHaveAttribute('aria-disabled', 'true');

    // Defence in depth: no PATCH may ever carry A's version (7) once the
    // route has moved on to B.
    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: /^save draft$/i }));
    const patchedWithAsVersion = fetchMock.mock.calls.some(
      ([request]) => (request as Request).method === 'PATCH' && (request as Request).headers.get('If-Match') === '"7"',
    );
    expect(patchedWithAsVersion).toBe(false);

    resolveB?.(jsonResponse(chainOf(OVERLAY_B, 3, 'Overlay B', 'Line B')));
  });
});
