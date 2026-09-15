import { act, cleanup, render, screen, waitFor } from '@testing-library/react';
import { useSyncExternalStore } from 'react';
import { Provider } from 'react-redux';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { store } from '../../app/store.js';
import type { LayoutEditTarget } from './LayoutEditorDialog.js';

/**
 * Spec 160 (issue #2387) US1 — new behaviour, RED (ADR-0139). Mirrors
 * `OverlayEditorDialogSaveGate.test.tsx` deliberately (spec §5.2/FR-012):
 * the drift between the two dialogs is the whole reason the issue exists.
 *
 * New file, not an append to `LayoutEditorDialogChainRecovery.test.tsx`
 * (plan.md §5.1) — T004 rewrites that file's native-`disabled` assertions,
 * and the phase-4a red here must be attributable to the new behaviour alone.
 *
 * At least one camera is present in this harness's `useListAllCameraChoicesQuery`
 * mock, so `knownCameras.size === 0` is never what closes the gate here — a
 * test that could pass on the pre-existing camera term alone would be
 * passing for the wrong reason (tasks.md T002).
 *
 * See `OverlayEditorDialogSaveGate.test.tsx`'s file doc comment for the
 * harness rationale (steppable mutation `isLoading` + deferred trigger).
 *
 * **No `document.activeElement` assertion in this file**, for the identical
 * reason given there: jsdom does not run the browser's disable-blur
 * algorithm, so the assertion would be unfalsifiable rather than merely
 * weak (confirmed against a bare React `<button disabled>`, no app code
 * involved, and against jsdom 30.0.1's own source). The focus claim for
 * this control is carried by `e2e/overlays.spec.ts`'s "a stale-version
 * conflict does not cost the keyboard operator their place at Save" — a
 * real browser, mirroring `e2e/overlays.spec.ts:213-220` (spec 154's Undo
 * button), which answered this exact question first.
 */

const createDraftMock = vi.fn(async () => ({ data: 'noop' }));

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

interface MutationState {
  isLoading: boolean;
  error: unknown;
}

let mutationState: MutationState = { isLoading: false, error: undefined };
let mutationListeners: Array<() => void> = [];

function setMutationState(next: MutationState) {
  mutationState = next;
  for (const listener of mutationListeners) listener();
}

function subscribeMutationState(listener: () => void) {
  mutationListeners = [...mutationListeners, listener];
  return () => {
    mutationListeners = mutationListeners.filter((entry) => entry !== listener);
  };
}

type EditResult = { data: number } | { error: unknown };
let deferredEditResolve: ((value: EditResult) => void) | null = null;

const editDraftMock = vi.fn((_body: unknown) => {
  setMutationState({ isLoading: true, error: undefined });
  return new Promise<EditResult>((resolve) => {
    deferredEditResolve = resolve;
  });
});

function refuseEdit(error: unknown) {
  setMutationState({ isLoading: false, error });
  const resolve = deferredEditResolve;
  deferredEditResolve = null;
  resolve?.({ error });
}

vi.mock('@smart-sentinel-eye/shared/api/layouts.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/layouts.api')>();
  return {
    ...actual,
    useCreateLayoutDraftMutation: () => [createDraftMock, { isLoading: false, error: undefined, reset: vi.fn() }],
    useEditDraftRevisionMutation: () => {
      const state = useSyncExternalStore(subscribeMutationState, () => mutationState);
      return [editDraftMock, { isLoading: state.isLoading, error: state.error, reset: vi.fn() }];
    },
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

describe('LayoutEditorDialog — Save keeps its focus while it is unavailable (spec 160 US1)', () => {
  beforeEach(() => {
    createDraftMock.mockClear();
    editDraftMock.mockClear();
    refetchChainMock.mockClear();
    deferredEditResolve = null;
    mutationState = { isLoading: false, error: undefined };
    chainQueryState = {
      data: { layoutIdentifier: EDIT_TARGET.layoutIdentifier, version: 7 },
      isError: false,
      isFetching: false,
    };
  });

  afterEach(() => {
    cleanup();
  });

  /** Spec §5.1, verbatim, mirrored for the layout dialog. */
  it('keeps focus on Save from the click through the conflict to the recovered version', async () => {
    const user = userEvent.setup();
    renderDialog();

    const saveButton = screen.getByRole('button', { name: /^save draft$/i });

    await user.click(saveButton);
    await waitFor(() => expect(editDraftMock).toHaveBeenCalledTimes(1));

    // The mutation is now pending (isLoading true). This is the assertion
    // that must be RED: FR-001 says `aria-disabled`; unmodified `develop`
    // renders native `disabled` here instead. (The focus claim that goes
    // with this moment is carried by `e2e/overlays.spec.ts`'s real-browser
    // test — see this file's doc comment.)
    expect(saveButton).toHaveAttribute('aria-disabled', 'true');

    await act(async () => {
      refuseEdit({
        status: 409,
        data: { title: 'LAYOUT_REVISION_STALE', detail: 'Layout has changed since version 7 (now 8).' },
      });
    });

    await act(async () => {
      setChainQueryState({ ...chainQueryState, isError: false, isFetching: true });
    });

    expect(saveButton).toHaveAttribute('aria-disabled', 'true');

    await act(async () => {
      setChainQueryState({
        data: { layoutIdentifier: EDIT_TARGET.layoutIdentifier, version: 8 },
        isError: false,
        isFetching: false,
      });
    });

    expect(saveButton).not.toHaveAttribute('aria-disabled', 'true');

    await user.click(saveButton);
    await waitFor(() => expect(editDraftMock).toHaveBeenCalledTimes(2));
    expect(editDraftMock).toHaveBeenLastCalledWith(
      expect.objectContaining({ layoutIdentifier: EDIT_TARGET.layoutIdentifier, version: 8 }),
    );
  });

  /** Spec §5.3, mirrored — see the overlay file's doc comment for why this passes vacuously pre-fix. */
  it('does not submit when Enter is pressed in a field while the gate is closed (passes vacuously pre-fix)', async () => {
    const user = userEvent.setup();
    chainQueryState = {
      data: { layoutIdentifier: EDIT_TARGET.layoutIdentifier, version: 7 },
      isError: false,
      isFetching: true,
    };
    renderDialog();

    const cameraFilterField = screen.getByLabelText(/find a camera/i);
    await user.click(cameraFilterField);
    await user.keyboard('{Enter}');

    expect(editDraftMock).not.toHaveBeenCalled();
  });

  /**
   * Spec §5.2 (US2, issue #2387 finding 2, FR-007/FR-008) — new behaviour,
   * RED (ADR-0139), mirrored from the overlay sibling file (tasks.md T007).
   * See that file's doc comment for the full reasoning: starts from an
   * already-refused mutation and an already-*settled*, refused re-read set
   * directly (not a live click cycle), and pairs the gate-closed assertion
   * with a call-count assertion via `expect.soft` so both halves of the
   * architect's stated red land in one run's output.
   */
  it('keeps Save unavailable when the layout re-read itself is refused, with Retry as the way out (US2 FR-007/FR-008)', async () => {
    const user = userEvent.setup();
    mutationState = {
      isLoading: false,
      error: {
        status: 409,
        data: { title: 'LAYOUT_REVISION_STALE', detail: 'Layout has changed since version 7 (now 8).' },
      },
    };
    chainQueryState = {
      data: { layoutIdentifier: EDIT_TARGET.layoutIdentifier, version: 7 },
      isError: true,
      isFetching: false,
    };
    renderDialog();

    const saveButton = screen.getByRole('button', { name: /^save draft$/i });
    const retryButton = screen.getByRole('button', { name: /retry/i });
    expect(retryButton).toBeInTheDocument();

    expect.soft(saveButton).toHaveAttribute('aria-disabled', 'true');

    await user.click(saveButton);
    expect.soft(editDraftMock).not.toHaveBeenCalled();

    // On unmodified code the blocked click above was NOT actually blocked —
    // see the overlay sibling file's doc comment for why this reset is here
    // rather than being evidence of anything about the retry recovery below.
    await act(async () => {
      setMutationState({ isLoading: false, error: mutationState.error });
    });
    const editCallsBeforeRetry = editDraftMock.mock.calls.length;

    await user.click(retryButton);
    await act(async () => {
      setChainQueryState({ ...chainQueryState, isError: false, isFetching: true });
    });
    await act(async () => {
      setChainQueryState({
        data: { layoutIdentifier: EDIT_TARGET.layoutIdentifier, version: 8 },
        isError: false,
        isFetching: false,
      });
    });

    expect(saveButton).not.toHaveAttribute('aria-disabled', 'true');
    expect(document.activeElement).toBe(saveButton);

    await user.click(saveButton);
    await waitFor(() => expect(editDraftMock).toHaveBeenCalledTimes(editCallsBeforeRetry + 1));
    expect(editDraftMock).toHaveBeenLastCalledWith(
      expect.objectContaining({ layoutIdentifier: EDIT_TARGET.layoutIdentifier, version: 8 }),
    );
  });
});
