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
 * Spec 153 (#2368). `useGetLayoutQuery` is REAL in this file — that is the
 * whole point. `LayoutEditorDialog.test.tsx` and `LayoutsPage.test.tsx` both
 * mock the hook, and a mocked hook returns the same value regardless of its
 * argument — it cannot express RTK Query's own distinction between `data`
 * (the last successful result for *any* argument the hook has ever been
 * called with) and `currentData` (resets to `undefined` on a `skipToken` or
 * an argument change).
 *
 * `LayoutsPage.tsx:333-340` keeps the edit `LayoutEditorDialog` permanently
 * mounted and drives `open`/`editTarget` together
 * (`open={editTarget !== undefined}`), so closing on one layout and reopening
 * on a different one is exactly `editTarget: A -> undefined -> B` on this one
 * component instance — not an unmount and a fresh mount.
 *
 * **Phase-6 finding.** The shipped Save gate is
 * `isEdit && (currentChain === undefined || chainFetching)`. While a held-open
 * GET is in flight, `chainFetching` is true regardless of which field
 * `currentChain` reads from — so the first two `it`s below, on their own,
 * disable Save (and block the PATCH) whether the component reads `data` or
 * `currentData`. Reverting `currentData` back to `data` in
 * `LayoutEditorDialog.tsx` and running this file plus the whole mocked
 * `LayoutEditorDialog.test.tsx` against it stayed green — proved by that
 * counterfactual, not assumed. The third `it` closes the gap: once B's GET
 * *settles* (rather than staying held), `chainFetching` drops to `false` and
 * `currentChain === undefined` is the only thing left standing between the
 * operator and A's version going out under B's identity — that is the case
 * that actually discriminates `data` from `currentData`. The first `it`
 * additionally pins the **positive** leg (Save enables and submits exactly
 * B's own version once it lands), so a gate change that left `chainFetching`
 * permanently true could not pass silently either.
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
    tiles: [{ cameraIdentifier: CAMERA_ID, overlayIdentifier: null, row: 0, col: 0, rowSpan: 1, colSpan: 1 }],
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
    // must stay unavailable until B's own GET answers — not merely until some
    // GET, for some layout, once has. `aria-disabled`, not native `disabled`
    // (spec 160 FR-001) — paired below with the click/PATCH check.
    expect(screen.getByRole('button', { name: /^save draft$/i })).toHaveAttribute('aria-disabled', 'true');

    // Defence in depth, in case Save is wrongly reachable: no PATCH may ever
    // carry A's version (7) once the target has moved on to B.
    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: /^save draft$/i }));
    const patchedWithAsVersion = fetchMock.mock.calls.some(
      ([request]) => request.method === 'PATCH' && request.headers.get('If-Match') === '"7"',
    );
    expect(patchedWithAsVersion).toBe(false);

    // The positive leg. Everything above would also pass a gate stuck on
    // `chainFetching` alone, with `currentChain` never actually read — so
    // this closes the complement: once B's own version lands, Save must
    // enable and submit *exactly* that version, not merely "some" version.
    resolveB?.(jsonResponse(chainOf(LAYOUT_B, 3)));
    await waitFor(() => {
      const state = layoutsApi.endpoints.getLayout.select(LAYOUT_B)(store.getState());
      expect(state.data?.version).toBe(3);
    });
    await user.click(screen.getByRole('button', { name: /^save draft$/i }));
    const patchedWithBsVersion = fetchMock.mock.calls.some(
      ([request]) => request.method === 'PATCH' && request.headers.get('If-Match') === '"3"',
    );
    expect(patchedWithBsVersion).toBe(true);
  });

  /**
   * Window 3 (branch path): reopening the SAME layout after
   * `branchDraftRevision` has invalidated `{type:'Layout', id}` while the
   * dialog was closed (unsubscribed).
   *
   * **Not a fetch-state test.** `spec.md` originally claimed this window
   * needed a gate on `chainFetching`, reasoning that `currentData` would
   * stay stale during the invalidation-driven refetch. Phase 4a disproved
   * that empirically — `c4c71dab` corrected `spec.md`/`tasks.md` — and
   * phase 6 re-confirmed it independently, reading the raw store slice
   * directly: invalidating a tag with **zero active subscribers evicts the
   * cache entry outright** (`status: "uninitialized"`, both `data` and
   * `currentData` gone) rather than marking it stale-but-cached. The
   * resubscribe below is therefore a genuinely fresh, empty fetch — closed
   * by the exact same `currentChain === undefined` half of the gate as the
   * cross-layout case above, not by `chainFetching`. Kept as its own test
   * rather than folded into the cross-layout case because it pins a
   * different production trigger (`LayoutsPage.onEdit`'s branch-then-open,
   * spec.md's "Branch path" scenario) and a different RTK Query mechanism
   * (store-level eviction on invalidation, not the hook's per-instance
   * `lastResult` carry-forward) — a regression that made an invalidated,
   * unsubscribed entry survive instead of being evicted would slip past the
   * cross-layout case entirely.
   */
  it('Reads the CURRENT version, not the one it held before the branch, when reopening the same layout after branchDraftRevision has invalidated it', async () => {
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
        // (`layouts.api.ts:135`) — is held open for the whole test. It is
        // reached at all because the invalidated entry was evicted, not
        // because of `refetchOnMountOrArgChange`.
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

    // The invalidated entry was evicted (see the block comment above), so
    // `currentChain` is `undefined` here — the same half of the gate that
    // covers a layout's first-ever read. This assertion does not, on its
    // own, distinguish `data` from `currentData`: `chainFetching` is also
    // true while this second GET is held, so it alone would disable Save
    // too (see the file-level counterfactual note). It stays as a
    // regression pin on the eviction behaviour itself. `aria-disabled`, not
    // native `disabled` (spec 160 FR-001) — paired below with the click/PATCH
    // check.
    expect(screen.getByRole('button', { name: /^save draft$/i })).toHaveAttribute('aria-disabled', 'true');

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

  /**
   * The case that actually distinguishes `data` from `currentData`.
   *
   * The two cases above hold B's (or the re-read's) GET open for the whole
   * test, so `chainFetching` is true throughout and disables Save on its
   * own — a fix that read `data` instead of `currentData` would pass both
   * unnoticed (proved by counterfactual: reverting `currentData` back to
   * `data` while leaving `chainFetching` in place left this file, and the
   * whole mocked `LayoutEditorDialog.test.tsx`, green). Here B's GET
   * *settles* — with a 500 — so `chainFetching` drops back to `false` once
   * the response lands, and `currentChain === undefined` is the only thing
   * left standing between the operator and A's version going out under B's
   * identity. `data` would still hold A's chain here (it survives the
   * argument change); `currentData` does not.
   */
  it("Never submits the previous layout's version when the new layout's own chain read fails outright", async () => {
    const fetchMock = vi.fn((request: Request) => {
      if (request.method === 'GET' && request.url.includes(LAYOUT_A)) {
        return Promise.resolve(jsonResponse(chainOf(LAYOUT_A, 7)));
      }
      if (request.method === 'GET' && request.url.includes(LAYOUT_B)) {
        return Promise.resolve(jsonResponse({ title: 'Internal Server Error' }, 500));
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

    await waitFor(() => {
      const state = layoutsApi.endpoints.getLayout.select(LAYOUT_A)(store.getState());
      expect(state.data?.version).toBe(7);
    });

    rerender(
      <Provider store={store}>
        <LayoutEditorDialog open={false} onOpenChange={() => {}} editTarget={undefined} />
      </Provider>,
    );
    rerender(
      <Provider store={store}>
        <LayoutEditorDialog open={true} onOpenChange={() => {}} editTarget={targetB} />
      </Provider>,
    );

    // Let B's read actually settle (fail), rather than stopping at the first
    // microtask flush the way the held-open cases do — the whole point is
    // that `chainFetching` has gone back to `false` by the time this
    // assertion runs. FR-004's alert is the observable signal that it has.
    await screen.findByRole('alert');

    // `aria-disabled`, not native `disabled` (spec 160 FR-001) — paired below
    // with the click/PATCH check.
    expect(screen.getByRole('button', { name: /^save draft$/i })).toHaveAttribute('aria-disabled', 'true');

    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: /^save draft$/i }));
    const patchedWithAsVersion = fetchMock.mock.calls.some(
      ([request]) => request.method === 'PATCH' && request.headers.get('If-Match') === '"7"',
    );
    expect(patchedWithAsVersion).toBe(false);
  });
});
