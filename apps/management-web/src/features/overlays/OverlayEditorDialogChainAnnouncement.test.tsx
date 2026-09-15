import { act, cleanup, render, screen } from '@testing-library/react';
import { useSyncExternalStore } from 'react';
import { Provider } from 'react-redux';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { store } from '../../app/store.js';
import type { OverlayEditTarget } from './OverlayEditorDialog.js';

/**
 * Spec 160 (issue #2387) US3 — new behaviour, RED (ADR-0139).
 *
 * New file (plan.md §5.1): `OverlayEditorDialogChainRecovery.test.tsx` is
 * where T003's assertion migrations land, and mixing a new-behaviour red
 * with a rewrite would make the phase-4a evidence unreadable.
 *
 * `ChainRecoveryNotice` writes its announcement in exactly two places —
 * `activate()` (a Retry/Reload click) and the settle effect's `origin ===
 * null` bail-out (spec.md §1.3). Neither is reached by a chain re-read that
 * nothing in this notice started: the dominant trigger in production is the
 * rejected edit mutation's own `invalidatesTags`
 * (`LayoutEditorDialog.tsx:280-294` has the evidence for the sibling
 * dialog), which never touches `ChainRecoveryNotice`'s `origin` state at
 * all. These tests step the chain query directly — no Retry/Reload click
 * anywhere in them — to isolate exactly that path, without needing the
 * mutation-pending harness `OverlayEditorDialogSaveGate.test.tsx` built for
 * US1/US2: US3 is about what the notice announces given a `reReading`
 * transition, regardless of what caused it.
 *
 * **No focus-destination assertion.** FR-009 says this path must not move
 * focus at all, so the only honest jsdom claim is "unchanged from a
 * reference captured before the transition" — never
 * `document.activeElement).not.toBe(document.body)` (tasks.md rule 1), and
 * never a claim that depends on the browser's disable-blur algorithm jsdom
 * does not implement (`OverlayEditorDialogSaveGate.test.tsx`'s doc comment
 * has the citation).
 */

const createDraftMock = vi.fn(async () => ({ data: 'noop' }));
const editDraftMock = vi.fn(async (_body: unknown) => ({ data: 1 }));

interface ChainQueryState {
  data?: { overlayIdentifier: string; version: number };
  isError: boolean;
  isFetching: boolean;
}

let chainQueryState: ChainQueryState = { data: undefined, isError: false, isFetching: false };
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

const refetchChainMock = vi.fn();

vi.mock('@smart-sentinel-eye/shared/api/overlays.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/overlays.api')>();
  return {
    ...actual,
    useCreateOverlayDraftMutation: () => [createDraftMock, { isLoading: false, error: undefined, reset: vi.fn() }],
    useEditDraftOverlayRevisionMutation: () => [editDraftMock, { isLoading: false, error: undefined, reset: vi.fn() }],
    useGetOverlayQuery: () => {
      const state = useSyncExternalStore(subscribeChainQueryState, () => chainQueryState);
      return {
        data: state.data,
        currentData: state.data,
        isLoading: false,
        isError: state.isError,
        isFetching: state.isFetching,
        refetch: refetchChainMock,
      };
    },
  };
});

/** `OverlayEditor` reads these even when nothing in a test ever mentions a camera. */
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
});

const { OverlayEditorDialog } = await import('./OverlayEditorDialog.js');

const EDIT_TARGET: OverlayEditTarget = {
  overlayIdentifier: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
  revisionNumber: 1,
  name: 'Line-1 Title',
  label: {
    text: 'Line 1',
    normalizedX: 0.1,
    normalizedY: 0.1,
    normalizedWidth: 0.3,
    normalizedHeight: 0.08,
    fontSizePx: 32,
  },
};

function renderDialog() {
  return render(
    <Provider store={store}>
      <OverlayEditorDialog open={true} onOpenChange={() => {}} editTarget={EDIT_TARGET} />
    </Provider>,
  );
}

function statusRegion() {
  return screen.getByTestId('overlay-chain-recovery-status');
}

describe('OverlayEditorDialog — an unrequested re-read announces itself (spec 160 US3)', () => {
  beforeEach(() => {
    createDraftMock.mockClear();
    editDraftMock.mockClear();
    refetchChainMock.mockClear();
  });

  afterEach(() => {
    cleanup();
  });

  /**
   * Spec §5.5. Nothing here ever clicks Retry or Reload — the chain query is
   * stepped directly, the way the rejected mutation's own `invalidatesTags`
   * would drive it in production. The expected red (tasks.md): the status
   * region is EMPTY where `Re-reading the overlay…` is expected — the
   * empty-vs-expected diff is itself the proof the path is silent today.
   */
  it('announces an invalidation-driven re-read on the way in and the way out, without moving focus (FR-009)', async () => {
    chainQueryState = {
      data: { overlayIdentifier: EDIT_TARGET.overlayIdentifier, version: 7 },
      isError: false,
      isFetching: false,
    };
    renderDialog();

    const focusBefore = document.activeElement;

    await act(async () => {
      setChainQueryState({ ...chainQueryState, isError: false, isFetching: true });
    });

    // The red: nothing wrote to the region, because `activate()` was never
    // called (no click) and the settle effect bails out at `origin === null`.
    expect(statusRegion()).toHaveTextContent(/re-reading the overlay/i);
    expect(document.activeElement).toBe(focusBefore);

    await act(async () => {
      setChainQueryState({
        data: { overlayIdentifier: EDIT_TARGET.overlayIdentifier, version: 8 },
        isError: false,
        isFetching: false,
      });
    });

    expect(statusRegion()).toHaveTextContent(/the overlay was read\. save is available\./i);
    expect(document.activeElement).toBe(focusBefore);
  });

  /**
   * Spec §5.6, the trap (FR-010). The negative half, and it is expected to
   * PASS on unmodified `develop` — nothing writes to the region on a first
   * read either, today, so this is green by accident rather than by design
   * (tasks.md T010 says so explicitly). Kept anyway: the obvious
   * implementation of the `it` above — announce on every `reReading` rising
   * edge, full stop — would announce "Re-reading the overlay…" on every
   * dialog open too, and this is the only test that would catch that.
   */
  it('stays silent through the dialog’s first read (FR-010, passes on develop by accident)', async () => {
    chainQueryState = { data: undefined, isError: false, isFetching: true };
    renderDialog();

    expect(statusRegion().textContent).toBe('');

    await act(async () => {
      setChainQueryState({
        data: { overlayIdentifier: EDIT_TARGET.overlayIdentifier, version: 7 },
        isError: false,
        isFetching: false,
      });
    });

    expect(statusRegion().textContent).toBe('');
  });
});
