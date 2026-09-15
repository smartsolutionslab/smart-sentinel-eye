import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { useSyncExternalStore } from 'react';
import { Provider } from 'react-redux';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { store } from '../../app/store.js';
import type { LayoutEditTarget } from './LayoutEditorDialog.js';

/**
 * Spec 160 (issue #2387) US3 — new behaviour, RED (ADR-0139). Mirrors
 * `OverlayEditorDialogChainAnnouncement.test.tsx` deliberately — see that
 * file's doc comment for the full reasoning.
 *
 * Not redundant with the overlay file (tasks.md T011): `ChainRecoveryNotice`
 * is shared, but the FR-010 `previouslyRead` discriminator is computed
 * *per dialog* (`currentChain !== undefined` at each call site), so only a
 * layout-specific test catches the layout dialog being left unwired for
 * US3 while the overlay one is wired.
 *
 * New file, not an addition to `LayoutEditorDialogChainRecovery.test.tsx` —
 * T004 rewrites that file's native-`disabled` assertions, and the
 * phase-4a red here must be attributable to the new behaviour alone.
 */

const createDraftMock = vi.fn(async () => ({ data: 'noop' }));
const editDraftMock = vi.fn(async (_body: unknown) => ({ data: 2 }));

interface ChainQueryState {
  data?: { layoutIdentifier: string; version: number };
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

vi.mock('@smart-sentinel-eye/shared/api/layouts.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/layouts.api')>();
  return {
    ...actual,
    useCreateLayoutDraftMutation: () => [createDraftMock, { isLoading: false, error: undefined, reset: vi.fn() }],
    useEditDraftRevisionMutation: () => [editDraftMock, { isLoading: false, error: undefined, reset: vi.fn() }],
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

function statusRegion() {
  return screen.getByTestId('layout-chain-recovery-status');
}

describe('LayoutEditorDialog — an unrequested re-read announces itself (spec 160 US3)', () => {
  beforeEach(() => {
    createDraftMock.mockClear();
    editDraftMock.mockClear();
    refetchChainMock.mockClear();
  });

  afterEach(() => {
    cleanup();
  });

  /** Spec §5.5, mirrored — see the overlay file's doc comment for the reasoning. */
  it('announces an invalidation-driven re-read on the way in and the way out, without moving focus (FR-009)', async () => {
    chainQueryState = { data: undefined, isError: false, isFetching: true };
    renderDialog();

    // The dialog's own first read, driven to a genuine settle before the
    // re-read starts (phase-6 finding 1, mirrored from the overlay file):
    // FR-010's discriminator is now per-mount, latched only once a read has
    // settled while this dialog is mounted, so this test must produce a
    // real settle rather than start from an already-warm cache — a warm
    // start is covered by its own negative test below.
    await act(async () => {
      setChainQueryState({
        data: { layoutIdentifier: EDIT_TARGET.layoutIdentifier, version: 7 },
        isError: false,
        isFetching: false,
      });
    });

    const focusBefore = document.activeElement;

    await act(async () => {
      setChainQueryState({ ...chainQueryState, isError: false, isFetching: true });
    });

    expect(statusRegion()).toHaveTextContent(/re-reading the layout/i);
    expect(document.activeElement).toBe(focusBefore);

    await act(async () => {
      setChainQueryState({
        data: { layoutIdentifier: EDIT_TARGET.layoutIdentifier, version: 8 },
        isError: false,
        isFetching: false,
      });
    });

    expect(statusRegion()).toHaveTextContent(/the layout was read\. save is available\./i);
    expect(document.activeElement).toBe(focusBefore);
  });

  /** Spec §5.6, the trap, mirrored — see the overlay file's doc comment for why this passes on develop by accident. */
  it('stays silent through the dialog’s first read (FR-010, passes on develop by accident)', async () => {
    chainQueryState = { data: undefined, isError: false, isFetching: true };
    renderDialog();

    expect(statusRegion().textContent).toBe('');

    await act(async () => {
      setChainQueryState({
        data: { layoutIdentifier: EDIT_TARGET.layoutIdentifier, version: 7 },
        isError: false,
        isFetching: false,
      });
    });

    expect(statusRegion().textContent).toBe('');
  });

  /**
   * Phase-6 finding 1 (issue #2387), mirrored from the overlay file — see
   * its doc comment for the full reasoning. Not redundant with it: the
   * layout dialog wires its own `previouslyRead`-turned-`hadPriorReadRef`
   * discriminator independently, so only a layout-specific test catches the
   * layout dialog regressing while the overlay one stays fixed.
   */
  it('stays silent on a warm reopen, before this mount has seen its own read settle (FR-010)', async () => {
    chainQueryState = {
      data: { layoutIdentifier: EDIT_TARGET.layoutIdentifier, version: 7 },
      isError: false,
      isFetching: false,
    };
    renderDialog();

    const focusBefore = document.activeElement;

    expect(statusRegion().textContent).toBe('');

    await act(async () => {
      setChainQueryState({ ...chainQueryState, isFetching: true });
    });

    expect(statusRegion().textContent).toBe('');

    await act(async () => {
      setChainQueryState({
        data: { layoutIdentifier: EDIT_TARGET.layoutIdentifier, version: 7 },
        isError: false,
        isFetching: false,
      });
    });

    expect(statusRegion().textContent).toBe('');
    expect(document.activeElement).toBe(focusBefore);
  });

  /**
   * Phase-6 finding 1 (issue #2387, third review round), mirrored from the
   * overlay file — see its doc comment for the full reasoning.
   */
  it('after a refused Retry, a later unrequested re-read that SUCCEEDS still announces and never hands focus to Save (phase-6 finding 1)', async () => {
    chainQueryState = { data: undefined, isError: false, isFetching: true };
    renderDialog();

    await act(async () => {
      setChainQueryState({ data: undefined, isError: true, isFetching: false });
    });
    const retryButton = screen.getByRole('button', { name: /retry/i });

    fireEvent.click(retryButton);
    await act(async () => {
      setChainQueryState({ ...chainQueryState, isError: false, isFetching: true });
    });
    await act(async () => {
      setChainQueryState({ ...chainQueryState, isError: true, isFetching: false });
    });
    expect(document.activeElement).toBe(screen.getByRole('button', { name: /retry/i }));

    await act(async () => {
      setChainQueryState({ ...chainQueryState, isError: false, isFetching: true });
    });
    expect(statusRegion()).toHaveTextContent(/re-reading the layout/i);

    await act(async () => {
      setChainQueryState({
        data: { layoutIdentifier: EDIT_TARGET.layoutIdentifier, version: 8 },
        isError: false,
        isFetching: false,
      });
    });

    expect(document.activeElement).not.toBe(screen.getByRole('button', { name: /^save draft$/i }));
  });

  /**
   * Phase-6 finding 1 (issue #2387, third review round), the failure
   * variant, mirrored from the overlay file — see its doc comment for the
   * full reasoning.
   */
  it('after a refused Retry, a later unrequested re-read that FAILS does not remount the Retry control (phase-6 finding 1)', async () => {
    chainQueryState = { data: undefined, isError: false, isFetching: true };
    renderDialog();

    await act(async () => {
      setChainQueryState({ data: undefined, isError: true, isFetching: false });
    });
    const retryButton = screen.getByRole('button', { name: /retry/i });

    fireEvent.click(retryButton);
    await act(async () => {
      setChainQueryState({ ...chainQueryState, isError: false, isFetching: true });
    });
    await act(async () => {
      setChainQueryState({ ...chainQueryState, isError: true, isFetching: false });
    });
    const retryButtonAfterRefusal = screen.getByRole('button', { name: /retry/i });
    expect(document.activeElement).toBe(retryButtonAfterRefusal);

    await act(async () => {
      setChainQueryState({ ...chainQueryState, isError: false, isFetching: true });
    });
    expect(statusRegion()).toHaveTextContent(/re-reading the layout/i);

    await act(async () => {
      setChainQueryState({ ...chainQueryState, isError: true, isFetching: false });
    });

    expect(screen.getByRole('button', { name: /retry/i })).toBe(retryButtonAfterRefusal);
    expect(document.activeElement).toBe(retryButtonAfterRefusal);
  });
});
