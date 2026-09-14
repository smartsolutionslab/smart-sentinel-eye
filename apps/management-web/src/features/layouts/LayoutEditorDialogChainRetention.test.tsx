import { configureStore } from '@reduxjs/toolkit';
import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { afterEach, describe, expect, it, vi } from 'vitest';

// gateway.ts resolves the API origin at module load, the same way
// rules.api.test.ts stubs it (apps/shared/src/api/rules.api.test.ts): stub the
// env before any dynamic import touches layouts.api, directly or via
// LayoutEditorDialog, so fetchBaseQuery builds absolute URLs — Node's
// `Request` rejects a relative one.
vi.stubEnv('VITE_API_GATEWAY_URL', 'http://gateway.test');

/**
 * Spec 153 (#2368), new behaviour, RED. `useGetLayoutQuery` is REAL in this
 * file — that is the whole point. `LayoutEditorDialog.test.tsx` and
 * `LayoutsPage.test.tsx` both mock the hook, and a mocked hook returns the
 * same value regardless of its argument — it cannot express RTK Query's own
 * distinction between `data` (the last successful result for *any* argument
 * the hook has ever been called with) and `currentData` (resets to
 * `undefined` on a `skipToken` or an argument change — but, as the second
 * `it` below establishes empirically, NOT on an invalidation-driven refetch
 * of the *same* argument). That gap is exactly how a mocked-everything suite
 * proved the wiring while missing the caching bug.
 *
 * `LayoutsPage.tsx:333-340` keeps the edit `LayoutEditorDialog` permanently
 * mounted and drives `open`/`editTarget` together
 * (`open={editTarget !== undefined}`), so closing on one layout and reopening
 * on a different one is exactly `editTarget: A -> undefined -> B` on this one
 * component instance — not an unmount and a fresh mount.
 * `LayoutEditorDialog.tsx:65` destructures `data` from the hook; because
 * `data` survives both the `skipToken` step and the argument change, layout
 * B's dialog can read layout A's chain — including A's version — before B's
 * own GET has ever answered.
 *
 * Not an extension of `LayoutEditorDialogRetention.test.tsx` — that file
 * covers *camera* retention across a close/reopen, a different concern.
 */
vi.mock('@smart-sentinel-eye/shared/api/cameras.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/cameras.api')>();
  return {
    ...actual,
    useListAllCameraChoicesQuery: () => ({
      data: {
        items: [
          {
            cameraIdentifier: '11111111-1111-1111-1111-111111111111',
            name: 'Line-1-Entrance',
            rtspUrl: 'rtsp://10.0.5.12/h264',
            registeredAt: '2026-05-25T10:00:00Z',
          },
        ],
        count: 1,
        complete: true,
      },
      currentData: {
        items: [
          {
            cameraIdentifier: '11111111-1111-1111-1111-111111111111',
            name: 'Line-1-Entrance',
            rtspUrl: 'rtsp://10.0.5.12/h264',
            registeredAt: '2026-05-25T10:00:00Z',
          },
        ],
        count: 1,
        complete: true,
      },
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
    useListOverlaysQuery: () => ({
      data: { chains: [], published: [] },
      currentData: { chains: [], published: [] },
      isLoading: false,
    }),
  };
});

const { layoutsApi } = await import('@smart-sentinel-eye/shared/api/layouts.api');
const { LayoutEditorDialog } = await import('./LayoutEditorDialog.js');

const LAYOUT_A = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const LAYOUT_B = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
const CAMERA_ID = '11111111-1111-1111-1111-111111111111';

function chainOf(layoutIdentifier: string, version: number) {
  return {
    layoutIdentifier,
    version,
    fab: 'munich',
    name: 'Layout',
    createdAt: '2026-05-27T10:00:00Z',
    createdBy: '22222222-2222-2222-2222-222222222222',
    revisions: [],
  };
}

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

function createStore() {
  return configureStore({
    reducer: { [layoutsApi.reducerPath]: layoutsApi.reducer },
    middleware: (getDefault) => getDefault().concat(layoutsApi.middleware),
  });
}

function targetFor(layoutIdentifier: string) {
  return {
    layoutIdentifier,
    revisionNumber: 1,
    name: 'Layout',
    grid: { rows: 1, cols: 1 },
    tiles: [{ cameraIdentifier: CAMERA_ID, overlayIdentifier: null, row: 0, col: 0 }],
  };
}

describe('LayoutEditorDialog — the chain is re-read for the layout actually being edited (spec 153)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("Never lets Save read or submit the previous layout's version while the new one is still loading (cross-layout)", async () => {
    let resolveB: ((response: Response) => void) | undefined;
    const fetchMock = vi.fn((request: Request) => {
      if (request.method === 'GET' && request.url.includes(LAYOUT_A)) {
        return Promise.resolve(jsonResponse(chainOf(LAYOUT_A, 7)));
      }
      if (request.method === 'GET' && request.url.includes(LAYOUT_B)) {
        // Held open for the whole test — B's own version is never answered,
        // so nothing has told the dialog what it is.
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
    const targetA = targetFor(LAYOUT_A);
    const targetB = targetFor(LAYOUT_B);

    const { rerender } = render(
      <Provider store={store}>
        <LayoutEditorDialog open={true} onOpenChange={() => {}} editTarget={targetA} />
      </Provider>,
    );

    // Let A's GET actually resolve before doing anything else. Today's Save
    // button is gated on `knownCameras.size`, not on the chain read (that gate
    // is exactly FR-002, which does not exist yet) — so waiting on the button
    // would prove nothing about whether A's chain has landed. The cache entry
    // is the one thing that is true regardless of what the dialog does with
    // it.
    await waitFor(() => {
      const state = layoutsApi.endpoints.getLayout.select(LAYOUT_A)(store.getState());
      expect(state.data?.version).toBe(7);
    });

    // Close — LayoutsPage.tsx always drives `open` and `editTarget` together
    // (`open={editTarget !== undefined}`), so this is the real transition,
    // not a test-only shortcut.
    rerender(
      <Provider store={store}>
        <LayoutEditorDialog open={false} onOpenChange={() => {}} editTarget={undefined} />
      </Provider>,
    );

    // Reopen on a DIFFERENT layout. Its GET is in flight and held.
    rerender(
      <Provider store={store}>
        <LayoutEditorDialog open={true} onOpenChange={() => {}} editTarget={targetB} />
      </Provider>,
    );
    await act(async () => {
      await Promise.resolve();
    });

    // The point of the test: `data` (RTK Query's last successful result for
    // ANY argument) still holds A's chain here; only `currentData` resets on
    // the argument change to B (`skipToken` in between, then B's id). Save
    // must stay disabled until B's own GET answers — not merely until some
    // GET, for some layout, once has.
    expect(screen.getByRole('button', { name: /^save draft$/i })).toBeDisabled();

    // Defence in depth, in case Save is wrongly reachable: no PATCH may ever
    // carry A's version (7) once the target has moved on to B.
    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: /^save draft$/i }));
    const patchedWithAsVersion = fetchMock.mock.calls.some(
      ([request]) => request.method === 'PATCH' && request.headers.get('If-Match') === '"7"',
    );
    expect(patchedWithAsVersion).toBe(false);

    resolveB?.(jsonResponse(chainOf(LAYOUT_B, 3)));
  });

  it('Does not submit the version it already read when reopening the SAME layout while a branch-invalidated refetch is in flight (window 3)', async () => {
    let layoutACalls = 0;
    let resolveSecondRead: ((response: Response) => void) | undefined;
    const fetchMock = vi.fn((request: Request) => {
      if (request.method === 'GET' && request.url.includes(LAYOUT_A)) {
        layoutACalls += 1;
        if (layoutACalls === 1) {
          return Promise.resolve(jsonResponse(chainOf(LAYOUT_A, 7)));
        }
        // The second read — provoked below by invalidating the tag while the
        // dialog is unsubscribed, exactly what `branchDraftRevision` does
        // (`layouts.api.ts:135`) — is held open for the whole test.
        return new Promise<Response>((resolve) => {
          resolveSecondRead = resolve;
        });
      }
      if (request.method === 'PATCH') {
        return Promise.resolve(jsonResponse(1));
      }
      throw new Error(`unexpected fetch: ${request.method} ${request.url}`);
    });
    vi.stubGlobal('fetch', fetchMock);

    const store = createStore();
    const targetA = targetFor(LAYOUT_A);

    const { rerender } = render(
      <Provider store={store}>
        <LayoutEditorDialog open={true} onOpenChange={() => {}} editTarget={targetA} />
      </Provider>,
    );

    await waitFor(() => {
      const state = layoutsApi.endpoints.getLayout.select(LAYOUT_A)(store.getState());
      expect(state.data?.version).toBe(7);
    });

    // Close. The dialog unsubscribes from layout A's chain, but the cache
    // entry survives the 60s `keepUnusedDataFor` window.
    rerender(
      <Provider store={store}>
        <LayoutEditorDialog open={false} onOpenChange={() => {}} editTarget={undefined} />
      </Provider>,
    );

    // Mirror what `LayoutsPage.onEdit` does on a second Edit click:
    // `branchDraftRevision` invalidates `{type:'Layout', id}` — while nobody
    // is subscribed, since `editTarget` is still `undefined` at that instant
    // (the branch is awaited *before* `setEditTarget` runs). RTK Query marks
    // the cached entry invalid rather than refetching it immediately, because
    // there is no active subscriber to refetch.
    store.dispatch(layoutsApi.util.invalidateTags([{ type: 'Layout', id: LAYOUT_A }]));

    // Reopen on the SAME layout. Resubscribing to an entry RTK Query has
    // marked invalid forces a fresh GET — independent of
    // `refetchOnMountOrArgChange` — and that GET is the one held above.
    rerender(
      <Provider store={store}>
        <LayoutEditorDialog open={true} onOpenChange={() => {}} editTarget={targetA} />
      </Provider>,
    );
    await act(async () => {
      await Promise.resolve();
    });

    // The point of this second case: reopening the identical layout is not
    // the cross-layout case above, and `currentData` alone does not close
    // it — the stale value (7) is still what RTK Query reports as
    // `currentData` while this second, invalidation-driven fetch for the
    // very same argument is in flight. Only a gate on fetch state (not on
    // "is there a value yet") can keep Save disabled here.
    expect(screen.getByRole('button', { name: /^save draft$/i })).toBeDisabled();

    // Defence in depth: no PATCH may carry the version (7) this dialog
    // already knows to be superseded by whatever the in-flight re-read will
    // answer.
    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: /^save draft$/i }));
    const patchedWithStaleVersion = fetchMock.mock.calls.some(
      ([request]) => request.method === 'PATCH' && request.headers.get('If-Match') === '"7"',
    );
    expect(patchedWithStaleVersion).toBe(false);

    resolveSecondRead?.(jsonResponse(chainOf(LAYOUT_A, 8)));
  });
});
