import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useSyncExternalStore } from 'react';
import { Provider } from 'react-redux';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { skipToken } from '@reduxjs/toolkit/query/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { store } from '../../app/store.js';
import type { Overlay, OverlayRevision } from '@smart-sentinel-eye/shared/api/overlays.api';

/**
 * Spec 305 (#2350), plan.md "Edit page — seeding invariants". `OverlayEditPage`
 * does not exist yet (T009), so every test below fails on a missing module —
 * a real, if blunt, RED (ADR-0139/0144) — not on a mistaken assertion against
 * code that happens to compile. `OverlayDraftForm`/`useCanvasFit` are the
 * engineer's to build next (T008-T009); this file pins only the page's own
 * contract: seeding, the four notice states, navigation, and the
 * `ResizeObserver`-driven canvas (FR-003, FR-006, FR-007).
 */

const OVERLAY_ID = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';

const editDraftMock = vi.fn(async () => ({ data: 1 }));
let editError: unknown = undefined;

/**
 * Phase-6 review (should-fix 1): mutable per-test state, mirroring
 * `OverlayEditorDialog.test.tsx`'s own `chainQueryState` — a `vi.mock`
 * factory is hoisted above every test, so the mock itself cannot vary by
 * call, and the test sets this object in place.
 *
 * Unlike that older harness, this one is a REAL subscription
 * (`useSyncExternalStore`), the same pattern `OverlayDraftFormSaveGate.test.tsx`
 * already uses for its own mutation state: a plain mutable object read
 * straight from the mock's closure is NOT a subscription, so
 * `rerender(<Provider>...<RouterProvider/></Provider>)` on an unchanged route
 * element is a no-op — React bails out before re-rendering `OverlayEditPage`
 * at all, and the mocked hook is never called again to pick up the new
 * value. A naive page that re-derives its seed on every render passed the
 * old "does not reseed" test for exactly this reason (phase-6 review,
 * proved by counterfactual). `setChainQueryState` below notifies every
 * subscriber, so a change pushed through it inside `act()` genuinely
 * reaches a mounted `OverlayEditPage` and is re-evaluated.
 */
interface ChainQueryState {
  currentData?: Overlay;
  isFetching: boolean;
  isError: boolean;
  error?: unknown;
  refetch: () => void;
}

let chainQueryState: ChainQueryState = { currentData: undefined, isFetching: false, isError: false, refetch: vi.fn() };
let chainQueryListeners: Array<() => void> = [];

function setChainQueryState(next: ChainQueryState) {
  chainQueryState = next;
  for (const listener of chainQueryListeners) listener();
}

function subscribeChainQueryState(listener: () => void) {
  chainQueryListeners = [...chainQueryListeners, listener];
  return () => {
    chainQueryListeners = chainQueryListeners.filter((entry) => entry !== listener);
  };
}

const useGetOverlayQueryMock = vi.fn((...args: unknown[]) => {
  void args;
  return useSyncExternalStore(subscribeChainQueryState, () => chainQueryState);
});

vi.mock('@smart-sentinel-eye/shared/api/overlays.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/overlays.api')>();
  return {
    ...actual,
    useGetOverlayQuery: (...args: unknown[]) => useGetOverlayQueryMock(...args),
    useEditDraftOverlayRevisionMutation: () => [
      editDraftMock,
      { isLoading: false, error: editError, reset: vi.fn(() => (editError = undefined)) },
    ],
    // The page's own form still calls this hook unconditionally (rules of
    // hooks), even though create mode is `OverlayCreatePage`'s to use.
    useCreateOverlayDraftMutation: () => [
      vi.fn(async () => ({ data: 'noop' })),
      { isLoading: false, error: undefined, reset: vi.fn() },
    ],
  };
});

const useListAllCameraChoicesQueryMock = vi.fn();
vi.mock('@smart-sentinel-eye/shared/api/cameras.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/cameras.api')>();
  return {
    ...actual,
    useListAllCameraChoicesQuery: (...args: unknown[]) => useListAllCameraChoicesQueryMock(...args),
  };
});

const useGetStreamQueryMock = vi.fn();
vi.mock('@smart-sentinel-eye/shared/api/streams.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/streams.api')>();
  return {
    ...actual,
    useGetStreamQuery: (...args: unknown[]) => useGetStreamQueryMock(...args),
  };
});

beforeEach(() => {
  useListAllCameraChoicesQueryMock.mockReturnValue({
    data: { items: [], count: 0, complete: true },
    isLoading: false,
    isFetching: false,
    isError: false,
  });
  useGetStreamQueryMock.mockReturnValue({ data: undefined, isLoading: false, error: undefined });
  editDraftMock.mockClear();
  useGetOverlayQueryMock.mockClear();
  editError = undefined;
  chainQueryState = { currentData: undefined, isFetching: false, isError: false, refetch: vi.fn() };
  // Defensive: `@testing-library/react`'s auto-cleanup already unmounts the
  // previous test's tree (unsubscribing it) before this runs, but resetting
  // the listener list here too means a leaked subscription from a test that
  // throws before unmount can never bleed a stale `setState` into the next.
  chainQueryListeners = [];
});

afterEach(() => {
  vi.unstubAllGlobals();
});

const { OverlayEditPage } = await import('./OverlayEditPage.js');

function draftRevision(revisionNumber: number, text: string): OverlayRevision {
  return {
    revisionIdentifier: `rev-${revisionNumber}`,
    revisionNumber,
    state: 'Draft',
    elements: [
      {
        kind: 'Text',
        color: '#FFFFFFD9',
        text,
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
  };
}

function publishedRevision(revisionNumber: number, text: string): OverlayRevision {
  return { ...draftRevision(revisionNumber, text), state: 'Published', publishedAt: '2026-05-28T10:00:00Z' };
}

function chainOf(overlayIdentifier: string, version: number, name: string, revisions: OverlayRevision[]): Overlay {
  return {
    overlayIdentifier,
    version,
    name,
    createdAt: '2026-05-27T10:00:00Z',
    createdBy: '22222222-2222-2222-2222-222222222222',
    revisions,
  };
}

function renderEditPage(overlayIdentifier: string, revisionNumber: string) {
  const router = createMemoryRouter(
    [
      { path: '/overlays/:overlayIdentifier/revisions/:revisionNumber/edit', element: <OverlayEditPage /> },
      { path: '/overlays', element: <h1>Overlays list (stub)</h1> },
    ],
    { initialEntries: [`/overlays/${overlayIdentifier}/revisions/${revisionNumber}/edit`] },
  );
  const result = render(
    <Provider store={store}>
      <RouterProvider router={router} />
    </Provider>,
  );
  return { router, ...result };
}

describe('OverlayEditPage — seeding and the four notice states (spec 305, #2350)', () => {
  it("Seeds the form from the named revision's elements and the chain's name", () => {
    chainQueryState = {
      currentData: chainOf(OVERLAY_ID, 5, 'Line 3', [draftRevision(2, 'Loaded label')]),
      isFetching: false,
      isError: false,
      refetch: vi.fn(),
    };

    renderEditPage(OVERLAY_ID, '2');

    expect(screen.getByTestId('overlay-editor-text')).toHaveValue('Loaded label');
    expect(screen.getByText(/draft v2/i)).toBeInTheDocument();
    expect(screen.getByText(/line 3/i)).toBeInTheDocument();
  });

  it('Renders no element with role "dialog" anywhere on the page', () => {
    chainQueryState = {
      currentData: chainOf(OVERLAY_ID, 5, 'Line 3', [draftRevision(2, 'Loaded label')]),
      isFetching: false,
      isError: false,
      refetch: vi.fn(),
    };

    renderEditPage(OVERLAY_ID, '2');

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('Shows a notice and no Save control when the named revision is no longer a draft', () => {
    chainQueryState = {
      currentData: chainOf(OVERLAY_ID, 5, 'Line 3', [publishedRevision(1, 'Live label')]),
      isFetching: false,
      isError: false,
      refetch: vi.fn(),
    };

    renderEditPage(OVERLAY_ID, '1');

    expect(screen.getByText(/no longer a draft/i)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /save/i })).not.toBeInTheDocument();
    expect(screen.queryByTestId('overlay-editor-text')).not.toBeInTheDocument();
  });

  /**
   * The spec's own not-found scenarios (a 404 and a malformed revision
   * segment) both resolve to the same sentence — "I see the not-found
   * notice" is stated identically for both (spec.md's malformed-segment
   * scenario) — so this file asserts the one wording spec.md gives verbatim
   * ("This overlay does not exist.") for every not-found case.
   */
  it('Shows a not-found notice when the overlay does not exist (404)', () => {
    chainQueryState = {
      currentData: undefined,
      isFetching: false,
      isError: true,
      error: { status: 404, data: { title: 'OVERLAY_NOT_FOUND' } },
      refetch: vi.fn(),
    };

    renderEditPage(OVERLAY_ID, '1');

    expect(screen.getByText(/does not exist/i)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /save/i })).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: /overlays/i })).toBeInTheDocument();
  });

  it('Shows a not-found notice and sends no GET for a revision segment that is not a positive integer', () => {
    renderEditPage(OVERLAY_ID, 'abc');

    expect(screen.getByText(/does not exist/i)).toBeInTheDocument();
    expect(useGetOverlayQueryMock).toHaveBeenCalled();
    expect(useGetOverlayQueryMock.mock.calls[0]![0]).toBe(skipToken);
  });

  it('Shows a RetryBanner on a failed read, and Retry re-issues it', async () => {
    const user = userEvent.setup();
    const refetchChainMock = vi.fn();
    chainQueryState = { currentData: undefined, isFetching: false, isError: true, error: { status: 500 }, refetch: refetchChainMock };

    renderEditPage(OVERLAY_ID, '1');

    await user.click(screen.getByRole('button', { name: /retry/i }));

    expect(refetchChainMock).toHaveBeenCalledTimes(1);
  });
});

describe('OverlayEditPage — Save and Cancel both leave the page (spec 305, #2350)', () => {
  it('Save success navigates to /overlays', async () => {
    const user = userEvent.setup();
    chainQueryState = {
      currentData: chainOf(OVERLAY_ID, 5, 'Line 3', [draftRevision(2, 'Loaded label')]),
      isFetching: false,
      isError: false,
      refetch: vi.fn(),
    };
    const { router } = renderEditPage(OVERLAY_ID, '2');

    await user.click(screen.getByRole('button', { name: /^save draft$/i }));

    await waitFor(() => expect(router.state.location.pathname).toBe('/overlays'));
  });

  it('Cancel navigates to /overlays and sends no PATCH', async () => {
    const user = userEvent.setup();
    chainQueryState = {
      currentData: chainOf(OVERLAY_ID, 5, 'Line 3', [draftRevision(2, 'Loaded label')]),
      isFetching: false,
      isError: false,
      refetch: vi.fn(),
    };
    const { router } = renderEditPage(OVERLAY_ID, '2');

    await user.click(screen.getByRole('button', { name: /cancel/i }));

    expect(editDraftMock).not.toHaveBeenCalled();
    await waitFor(() => expect(router.state.location.pathname).toBe('/overlays'));
  });
});

describe('OverlayEditPage — the form seeds once (spec 305 FR-003, plan.md "Seed once")', () => {
  it('Does not reseed the text field from a background refetch once an edit is in progress', async () => {
    const user = userEvent.setup();
    setChainQueryState({
      currentData: chainOf(OVERLAY_ID, 5, 'Line 3', [draftRevision(2, 'Original label')]),
      isFetching: false,
      isError: false,
      refetch: vi.fn(),
    });
    renderEditPage(OVERLAY_ID, '2');

    const textInput = screen.getByTestId('overlay-editor-text');
    await user.clear(textInput);
    await user.type(textInput, 'Edited by operator');

    // Simulate a background refetch — e.g. the conflict-recovery refetch
    // FR-003 names — that answers with different content for the SAME
    // revision. Only the If-Match version may move; the seed must not.
    //
    // Pushed through the real subscription (`act` + `setChainQueryState`),
    // not a `rerender` on an unchanged route element — a `rerender` here
    // would be a no-op (phase-6 review, should-fix 1: proved by
    // counterfactual, see the harness's own doc comment above) and this
    // test would pass even against a page that reseeds on every render.
    act(() => {
      setChainQueryState({
        ...chainQueryState,
        currentData: chainOf(OVERLAY_ID, 6, 'Line 3', [draftRevision(2, 'Refetched from server')]),
      });
    });

    expect(screen.getByTestId('overlay-editor-text')).toHaveValue('Edited by operator');
  });

  /**
   * Phase-6 review, blocker 1 (#2350): in RTK Query 2.12, a rejected refetch
   * keeps `currentData` but sets `isError` — so this fires on the form's own
   * "Retry" action OR on a conflict's tag-invalidation refetch exactly as
   * the background-refetch case above does, and the page's own
   * `if (chainFailed)` branch used to fire regardless of whether a seed
   * already existed, unmounting `OverlayDraftForm` (and everything typed
   * into it) for the `RetryBanner`. Reproduced live: seed, type, push
   * `isError: true`, and the typed text used to vanish.
   */
  it('Keeps the typed edit on screen when a later read fails once an edit is in progress (phase-6 blocker 1)', async () => {
    const user = userEvent.setup();
    setChainQueryState({
      currentData: chainOf(OVERLAY_ID, 5, 'Line 3', [draftRevision(2, 'Original label')]),
      isFetching: false,
      isError: false,
      refetch: vi.fn(),
    });
    renderEditPage(OVERLAY_ID, '2');

    const textInput = screen.getByTestId('overlay-editor-text');
    await user.clear(textInput);
    await user.type(textInput, 'Edited by operator');

    act(() => {
      setChainQueryState({ ...chainQueryState, isError: true, error: { status: 503 } });
    });

    expect(screen.getByTestId('overlay-editor-text')).toHaveValue('Edited by operator');
    // The point of the fix: the page-level `RetryBanner` branch must not
    // have taken over — that branch unmounts `OverlayDraftForm` entirely.
    expect(screen.queryByText(/could not load this overlay/i)).not.toBeInTheDocument();
  });

  /**
   * Phase-6 review, blocker 2 (#2350): plan.md step 5 ("After the form
   * seeds, a later read that shows the revision left Draft... does NOT swap
   * the editor for the notice; the form's existing
   * `OVERLAY_REVISION_NOT_DRAFT` handling covers that case") was
   * unimplemented — the page's `revision.state !== 'Draft'` check ran
   * regardless of whether a seed already existed, replacing the whole
   * editor with `NotDraftNotice` on a later conflict refetch. Reproduced
   * live: seed, type, push a chain where the revision is Published, and the
   * editor used to disappear entirely.
   */
  it('Keeps the typed edit on screen when a later read shows the revision left Draft (phase-6 blocker 2)', async () => {
    const user = userEvent.setup();
    setChainQueryState({
      currentData: chainOf(OVERLAY_ID, 5, 'Line 3', [draftRevision(2, 'Original label')]),
      isFetching: false,
      isError: false,
      refetch: vi.fn(),
    });
    renderEditPage(OVERLAY_ID, '2');

    const textInput = screen.getByTestId('overlay-editor-text');
    await user.clear(textInput);
    await user.type(textInput, 'Edited by operator');

    act(() => {
      setChainQueryState({
        ...chainQueryState,
        currentData: chainOf(OVERLAY_ID, 6, 'Line 3', [publishedRevision(2, 'Refetched from server')]),
      });
    });

    expect(screen.getByTestId('overlay-editor-text')).toHaveValue('Edited by operator');
    // The point of the fix: the page-level `NotDraftNotice` must not have
    // taken over — that notice replaces the whole editor.
    expect(screen.queryByText(/no longer a draft/i)).not.toBeInTheDocument();
  });
});

describe('OverlayEditPage — the canvas uses the page width (spec 305 FR-006)', () => {
  // Untyped against the DOM lib's `ResizeObserver*` names deliberately — this
  // app's eslint config has no per-tag DOM lib globals (the same reasoning
  // `OverlayDraftForm.tsx`'s own `ComponentRef<'button'>` comment gives),
  // and widening it is the gate-weakening ADR-0144 rules out.
  class FakeResizeObserver {
    static instances: FakeResizeObserver[] = [];
    callback: (entries: unknown[]) => void;
    constructor(callback: (entries: unknown[]) => void) {
      this.callback = callback;
      FakeResizeObserver.instances.push(this);
    }
    observe(target: unknown) {
      // jsdom gives every element a 0x0 layout box, so a real observer would
      // never report a measured width here — the callback contract itself is
      // what this test pins, not jsdom's (nonexistent) layout engine.
      this.callback([{ target, contentRect: { width: 1600, height: 900 } }]);
    }
    unobserve() {}
    disconnect() {}
  }

  it('Sizes the canvas 1600x900 (16:9) from a stubbed 1600px ResizeObserver width', async () => {
    vi.stubGlobal('ResizeObserver', FakeResizeObserver);
    chainQueryState = {
      currentData: chainOf(OVERLAY_ID, 5, 'Line 3', [draftRevision(2, 'Loaded label')]),
      isFetching: false,
      isError: false,
      refetch: vi.fn(),
    };

    renderEditPage(OVERLAY_ID, '2');

    const canvas = await screen.findByTestId('overlay-editor-canvas');
    await waitFor(() => expect(canvas.style.width).toBe('1600px'));
    expect(canvas.style.height).toBe('900px');
  });

  it('Falls back to the 800x450 default when no ResizeObserver is available', () => {
    vi.stubGlobal('ResizeObserver', undefined);
    chainQueryState = {
      currentData: chainOf(OVERLAY_ID, 5, 'Line 3', [draftRevision(2, 'Loaded label')]),
      isFetching: false,
      isError: false,
      refetch: vi.fn(),
    };

    renderEditPage(OVERLAY_ID, '2');

    const canvas = screen.getByTestId('overlay-editor-canvas');
    expect(canvas.style.width).toBe('800px');
    expect(canvas.style.height).toBe('450px');
  });
});
