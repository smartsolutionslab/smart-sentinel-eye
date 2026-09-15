import { configureStore } from '@reduxjs/toolkit';
import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { afterEach, describe, expect, it, vi } from 'vitest';

// gateway.ts resolves the API origin at module load, the same way
// rules.api.test.ts stubs it (apps/shared/src/api/rules.api.test.ts): stub the
// env before any dynamic import touches overlays.api, directly or via
// OverlayEditorDialog, so fetchBaseQuery builds absolute URLs — Node's
// `Request` rejects a relative one.
vi.stubEnv('VITE_API_GATEWAY_URL', 'http://gateway.test');

/**
 * Phase-6 review finding, new behaviour, RED. `useGetOverlayQuery` is REAL in
 * this file — that is the whole point. `OverlayEditorDialog.test.tsx` and
 * `OverlaysPage.test.tsx` both mock the hook, and a mocked hook returns the
 * same value regardless of its argument — it cannot express RTK Query's own
 * distinction between `data` (the last successful result for *any* argument
 * the hook has ever been called with) and `currentData` (resets to
 * `undefined` on a `skipToken` or an argument change). That gap is exactly how
 * a mocked-everything suite proved the wiring while missing the caching bug —
 * the same failure mode this spec's own e2e comment names for the mutation
 * that stayed exported and uncalled for four specs.
 *
 * `OverlaysPage.tsx` keeps the edit `OverlayEditorDialog` permanently mounted
 * and drives `open`/`editTarget` together (`open={editTarget !== undefined}`),
 * so closing on one overlay and reopening on a different one is exactly
 * `editTarget: A → undefined → B` on this one component — not an unmount and
 * a fresh mount. `OverlayEditorDialog.tsx:71-75` destructures `data` from the
 * hook; because `data` survives both the `skipToken` step and the argument
 * change, overlay B's dialog can read overlay A's chain — including A's
 * version — before B's own GET has ever answered.
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

// Not this file's concern (spec 148); keep this suite's only live network
// surface the overlays gateway itself.
vi.mock('@smart-sentinel-eye/shared/api/systemVariables.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/systemVariables.api')>();
  return {
    ...actual,
    useResolveOverlayTextQuery: () => ({ currentData: undefined, isFetching: false, isError: false }),
  };
});

const { overlaysApi } = await import('@smart-sentinel-eye/shared/api/overlays.api');
const { OverlayEditorDialog } = await import('./OverlayEditorDialog.js');

const OVERLAY_A = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const OVERLAY_B = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';

const LABEL = {
  text: 'Overlay text',
  normalizedX: 0.1,
  normalizedY: 0.1,
  normalizedWidth: 0.3,
  normalizedHeight: 0.08,
  fontSizePx: 32,
};

function chainOf(overlayIdentifier: string, version: number) {
  return {
    overlayIdentifier,
    version,
    name: 'Overlay',
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
    reducer: { [overlaysApi.reducerPath]: overlaysApi.reducer },
    middleware: (getDefault) => getDefault().concat(overlaysApi.middleware),
  });
}

describe('OverlayEditorDialog — the chain is re-read per overlay, not carried over (phase-6 review)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("Never lets Save read or submit the previous overlay's version while the new one is still loading", async () => {
    let resolveB: ((response: Response) => void) | undefined;
    const fetchMock = vi.fn((request: Request) => {
      if (request.method === 'GET' && request.url.includes(OVERLAY_A)) {
        return Promise.resolve(jsonResponse(chainOf(OVERLAY_A, 7)));
      }
      if (request.method === 'GET' && request.url.includes(OVERLAY_B)) {
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
    const targetA = { overlayIdentifier: OVERLAY_A, revisionNumber: 1, name: 'Overlay A', label: LABEL };
    const targetB = { overlayIdentifier: OVERLAY_B, revisionNumber: 1, name: 'Overlay B', label: LABEL };

    const { rerender } = render(
      <Provider store={store}>
        <OverlayEditorDialog open={true} onOpenChange={() => {}} editTarget={targetA} />
      </Provider>,
    );

    // Let A's GET resolve and the dialog settle on version 7. The button
    // exists on the very first render regardless of the fetch outcome, so
    // `findByRole` alone would not wait for anything — `waitFor` polls the
    // save-gate attribute itself as a synchronization primitive, not as a
    // UX claim in its own right (spec 160 FR-001: `aria-disabled`, not
    // native `disabled` — the same claim, moved to the new attribute; this
    // line makes no independent availability assertion for a click to pair
    // with, unlike the disabled-gate assertion below at the actual point of
    // the test).
    await waitFor(() =>
      expect(screen.getByRole('button', { name: /^save draft$/i })).not.toHaveAttribute('aria-disabled', 'true'),
    );

    // Close — OverlaysPage.tsx always drives `open` and `editTarget` together
    // (`open={editTarget !== undefined}`), so this is the real transition, not
    // a test-only shortcut.
    rerender(
      <Provider store={store}>
        <OverlayEditorDialog open={false} onOpenChange={() => {}} editTarget={undefined} />
      </Provider>,
    );

    // Reopen on a DIFFERENT overlay. Its GET is in flight and held.
    rerender(
      <Provider store={store}>
        <OverlayEditorDialog open={true} onOpenChange={() => {}} editTarget={targetB} />
      </Provider>,
    );
    await act(async () => {
      await Promise.resolve();
    });

    // The point of the test: `data` (RTK Query's last successful result for
    // ANY argument) still holds A's chain here; only `currentData` resets on
    // the argument change to B (`skipToken` in between, then B's id). Save
    // must stay unavailable until B's own GET answers — not merely until some
    // GET, for some overlay, once has. `aria-disabled`, not native `disabled`
    // (spec 160 FR-001) — paired below with the click/PATCH check that is
    // this test's own behavioural half (tasks.md rule 2).
    expect(screen.getByRole('button', { name: /^save draft$/i })).toHaveAttribute('aria-disabled', 'true');

    // Defence in depth, in case Save is wrongly reachable: no PATCH may ever
    // carry A's version (7) once the target has moved on to B.
    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: /^save draft$/i }));
    const patchedWithAsVersion = fetchMock.mock.calls.some(
      ([request]) => request.method === 'PATCH' && request.headers.get('If-Match') === '"7"',
    );
    expect(patchedWithAsVersion).toBe(false);

    resolveB?.(jsonResponse(chainOf(OVERLAY_B, 3)));
  });
});
