import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { useSyncExternalStore } from 'react';
import { Provider } from 'react-redux';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { store } from '../../app/store.js';
import type { LayoutEditTarget } from './LayoutEditorDialog.js';

/**
 * Spec 156 (issue #2372) — new behaviour, RED (ADR-0139). Mirrors
 * `OverlayEditorDialogChainRecovery.test.tsx` deliberately (FR-012): the
 * drift between these two dialogs is the whole reason the issue exists, so
 * parity is expressed as a test rather than left to review.
 *
 * New file, not an addition to `LayoutEditorDialog.test.tsx` (plan.md §5
 * rule 2) — PR #2377 (issue #2371) appends to the tail of
 * `describe('LayoutEditorDialog — edit')` in that exact file, which is
 * where a naive addition here would land.
 */

const createDraftMock = vi.fn(async () => ({ data: 'noop' }));
const editDraftMock = vi.fn(async (_body: unknown) => ({ data: 2 }));
let editError: unknown = undefined;

interface ChainQueryState {
  data?: { layoutIdentifier: string; version: number };
  isError: boolean;
  isFetching: boolean;
}

/**
 * See the overlay sibling file for why this needs a subscription rather
 * than a bare mutable object: nothing else would re-render the mounted
 * dialog when `chainQueryState` changes, and `LayoutEditorDialog.test.tsx`'s
 * own `useGetLayoutQuery` mock is arg-independent and returns a fixed
 * object, which cannot drive `failed -> in flight -> resolved` either.
 */
let chainQueryState: ChainQueryState = { data: undefined, isError: true, isFetching: false };
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

vi.mock('@smart-sentinel-eye/shared/api/layouts.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/layouts.api')>();
  return {
    ...actual,
    useCreateLayoutDraftMutation: () => [createDraftMock, { isLoading: false, error: editError, reset: vi.fn() }],
    useEditDraftRevisionMutation: () => [editDraftMock, { isLoading: false, error: editError, reset: vi.fn() }],
    useGetLayoutQuery: () => {
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

const CAMERA_A = '11111111-1111-1111-1111-111111111111';
const CAMERA_B = '22222222-2222-2222-2222-222222222222';

vi.mock('@smart-sentinel-eye/shared/api/cameras.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/cameras.api')>();
  return {
    ...actual,
    useListAllCameraChoicesQuery: () => ({
      data: {
        items: [
          {
            cameraIdentifier: CAMERA_A,
            name: 'Line-1-Entrance',
            rtspUrl: 'rtsp://10.0.5.12/h264',
            registeredAt: '2026-05-25T10:00:00Z',
          },
          {
            cameraIdentifier: CAMERA_B,
            name: 'Line-2-Exit',
            rtspUrl: 'rtsp://10.0.5.13/h264',
            registeredAt: '2026-05-25T10:00:00Z',
          },
        ],
        count: 2,
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
    useListOverlaysQuery: () => ({ data: { chains: [], published: [] }, isLoading: false }),
  };
});

const { LayoutEditorDialog } = await import('./LayoutEditorDialog.js');

const EDIT_TARGET: LayoutEditTarget = {
  layoutIdentifier: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
  revisionNumber: 2,
  name: 'Rolling Mill',
  grid: { rows: 1, cols: 2 },
  tiles: [
    { cameraIdentifier: CAMERA_A, overlayIdentifier: null, row: 0, col: 0 },
    { cameraIdentifier: CAMERA_B, overlayIdentifier: null, row: 0, col: 1 },
  ],
};

function renderDialog() {
  return render(
    <Provider store={store}>
      <LayoutEditorDialog open={true} onOpenChange={() => {}} editTarget={EDIT_TARGET} />
    </Provider>,
  );
}

/** What the mocked `refetch()` does by default — the same transition a real refetch's dispatch produces. */
function beginReRead() {
  setChainQueryState({ data: undefined, isError: false, isFetching: true });
}

function statusRegion() {
  return screen.getByTestId('layout-chain-recovery-status');
}

describe('LayoutEditorDialog — a recovery control that survives its own activation (spec 156)', () => {
  beforeEach(() => {
    createDraftMock.mockClear();
    editDraftMock.mockClear();
    refetchChainMock.mockClear();
    refetchChainMock.mockImplementation(() => {
      beginReRead();
      return Promise.resolve();
    });
    editError = undefined;
    chainQueryState = { data: undefined, isError: true, isFetching: false };
  });

  afterEach(() => {
    cleanup();
  });

  /**
   * The discriminating test (tasks.md "The discriminating test"), mirrored
   * from the overlay dialog (FR-012). Assertion #2 —
   * `document.activeElement` IS the Retry button while the refetch is in
   * flight — is the one a plausible wrong fix still fails.
   */
  it('Retry keeps focus while the re-read is in flight, and hands it to Save when it succeeds', async () => {
    const user = userEvent.setup();
    renderDialog();

    const retryButton = screen.getByRole('button', { name: /retry/i });
    expect(retryButton.closest('[role="alert"]')).not.toBeNull();

    await user.click(retryButton);

    expect(screen.getByRole('button', { name: /retry/i })).toBeInTheDocument();
    expect(document.activeElement).toBe(retryButton);
    expect(retryButton).toHaveAttribute('aria-disabled', 'true');
    expect(statusRegion()).toHaveTextContent(/re-reading the layout/i);
    expect(screen.queryByRole('alert')).toBeNull();

    await act(async () => {
      setChainQueryState({
        data: { layoutIdentifier: EDIT_TARGET.layoutIdentifier, version: 7 },
        isError: false,
        isFetching: false,
      });
    });

    expect(screen.queryByRole('button', { name: /retry/i })).toBeNull();
    const saveButton = screen.getByRole('button', { name: /^save draft$/i });
    expect(document.activeElement).toBe(saveButton);
    expect(saveButton).not.toBeDisabled();
    expect(statusRegion()).toHaveTextContent(/was read/i);
  });

  it('Leaves focus on Retry and shows the alert again when the re-read is refused a second time (FR-006)', async () => {
    const user = userEvent.setup();
    renderDialog();

    const retryButton = screen.getByRole('button', { name: /retry/i });
    await user.click(retryButton);
    expect(statusRegion()).toHaveTextContent(/re-reading the layout/i);

    await act(async () => {
      setChainQueryState({ data: undefined, isError: true, isFetching: false });
    });

    const alert = screen.getByRole('alert');
    expect(alert).toHaveTextContent(/could not be read/i);
    const retryAgain = screen.getByRole('button', { name: /retry/i });
    expect(retryAgain).not.toHaveAttribute('aria-disabled', 'true');
    expect(document.activeElement).toBe(retryAgain);
    expect(screen.getByRole('button', { name: /^save draft$/i })).toBeDisabled();
    expect(statusRegion().textContent).toBe('');
  });

  it('Mutates the status region a second time even though the recovery message repeats itself (FR-009)', async () => {
    renderDialog();

    // `fireEvent.click`, not `user.click` (phase-6 review finding): jsdom's
    // `MutationObserver` delivers queued records via a microtask scheduled
    // the instant the DOM mutates, and `user-event`'s internal flush cycles
    // yield the microtask queue after the click's synchronous work — which
    // lets that notify microtask run first (FIFO) and drains the observer
    // before control returns here, so `takeRecords()` always reads empty.
    // `OverlayEditorUndo.test.tsx:915-931` hits the identical shape (same
    // key-token remount, same `takeRecords()`) and stays on `fireEvent.click`
    // for exactly this reason. The other tests in this file keep
    // `user.click` deliberately — they assert focus, where its realism is
    // the point.
    fireEvent.click(screen.getByRole('button', { name: /retry/i }));
    await act(async () => {
      setChainQueryState({ data: undefined, isError: true, isFetching: false });
    });
    expect(statusRegion().textContent).toBe('');

    const observer = new globalThis.MutationObserver(() => {});
    observer.observe(statusRegion(), { childList: true, subtree: true, characterData: true });

    fireEvent.click(screen.getByRole('button', { name: /retry/i }));

    const mutations = observer.takeRecords();
    observer.disconnect();
    expect(mutations.length).toBeGreaterThan(0);
    expect(statusRegion()).toHaveTextContent(/re-reading the layout/i);
  });

  /**
   * FR-008. Layout's Reload is gated on `staleConflict` alone (unlike
   * Overlay's `staleConflict || notDraft`) — `LayoutEditorDialog.tsx` has no
   * not-a-draft conflict — but the mechanism under test is identical.
   */
  it('Reload keeps its own focus through the re-read it starts, and never hands focus to Save (FR-008)', async () => {
    const user = userEvent.setup();
    editError = {
      status: 409,
      data: { title: 'LAYOUT_REVISION_STALE', detail: 'Layout has changed since version 7 (now 8).' },
    };
    chainQueryState = {
      data: { layoutIdentifier: EDIT_TARGET.layoutIdentifier, version: 7 },
      isError: false,
      isFetching: false,
    };
    renderDialog();

    const reloadButton = await screen.findByRole('button', { name: /reload/i });
    await user.click(reloadButton);

    expect(reloadButton).toBeInTheDocument();
    expect(document.activeElement).toBe(reloadButton);
    expect(statusRegion()).toHaveTextContent(/re-reading the layout/i);

    await act(async () => {
      setChainQueryState({
        data: { layoutIdentifier: EDIT_TARGET.layoutIdentifier, version: 8 },
        isError: false,
        isFetching: false,
      });
    });

    expect(document.activeElement).toBe(reloadButton);
    expect(document.activeElement).not.toBe(screen.getByRole('button', { name: /^save draft$/i }));
  });

  it('Moves no focus and announces nothing on a normal successful open (FR-007)', () => {
    chainQueryState = {
      data: { layoutIdentifier: EDIT_TARGET.layoutIdentifier, version: 7 },
      isError: false,
      isFetching: false,
    };
    renderDialog();

    expect(document.activeElement).not.toBe(screen.getByRole('button', { name: /^save draft$/i }));
    expect(statusRegion().textContent).toBe('');
  });
});
