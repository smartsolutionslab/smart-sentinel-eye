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

/**
 * What the mocked `refetch()` does by default — the same transition a real
 * refetch's dispatch produces. `...chainQueryState` (not `data: undefined`,
 * phase-6 review): RTK Query keeps `currentData` defined through a
 * same-argument refetch, clearing it only on a `skipToken` step or an arg
 * change (neither happens here) — `LayoutEditorDialog.tsx:66-74` says so.
 * Overwriting it to `undefined` was only ever accidentally correct for
 * Retry, whose precondition already has no data; it was wrong for Reload,
 * whose precondition is a *successful* prior read.
 */
function beginReRead() {
  setChainQueryState({ ...chainQueryState, isError: false, isFetching: true });
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
    // `role="alert"` is gone — but this proves only that the *attribute*
    // is absent right now, not that the underlying element unmounted. It
    // is in fact the same `<p>`, reused unchanged across the whole click
    // -> fail -> click cycle (`chainArmActive` spans both states); only
    // its `role` toggles off and back on. That reuse is a real regression
    // (blocker 1, phase-6 review) — see "Announces a second refusal"
    // below.
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
    // `aria-disabled`, not native `disabled` (spec 160 FR-001), paired with
    // the behavioural half (tasks.md rule 2) — the attribute alone cannot
    // tell "gate open" from "gate cosmetic".
    expect(saveButton).not.toHaveAttribute('aria-disabled', 'true');
    expect(statusRegion()).toHaveTextContent(/was read/i);

    await user.click(saveButton);
    expect(editDraftMock).toHaveBeenCalledTimes(1);
    expect(editDraftMock).toHaveBeenLastCalledWith(
      expect.objectContaining({ layoutIdentifier: EDIT_TARGET.layoutIdentifier, version: 7 }),
    );
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
    // Spec 160 FR-001/rule 2 — `aria-disabled`, paired with a call-count check.
    const saveButton = screen.getByRole('button', { name: /^save draft$/i });
    expect(saveButton).toHaveAttribute('aria-disabled', 'true');
    await user.click(saveButton);
    expect(editDraftMock).not.toHaveBeenCalled();
    expect(statusRegion().textContent).toBe('');
  });

  /**
   * Regression, blocker 1 (phase-6 review), mirrored from the overlay
   * dialog (FR-012). `chainArmActive` (the mount condition for the
   * chain-read `<p>`) spans BOTH the in-flight window and the failed
   * state, so React reconciles one persistent element across a whole
   * click -> fail -> click -> fail cycle rather than unmounting and
   * remounting it — only `role` toggles between `undefined` and `'alert'`.
   * `queryByRole('alert')` cannot see that difference (it reads the current
   * accessibility tree, not DOM history), so this compares node identity
   * directly, the way the discriminating test's own assertion 5 comment
   * now points here.
   *
   * This is a regression against the ternary this component replaced: that
   * rendered two structurally separate `<p role="alert">` arms, so a
   * genuine unmount/remount happened on every `chainFailed` transition,
   * earning a real insertion-announcement each time (#2346's requirement).
   * Meanwhile FR-006 clears the status region to `''` on failure, so on a
   * second-and-later refusal the operator today gets nothing at all — the
   * common case when a backend is down, not a rare one.
   */
  it('Announces a second refusal — the alert node is re-inserted, not merely re-labelled (regression, blocker 1)', async () => {
    const user = userEvent.setup();
    renderDialog();

    const firstAlert = screen.getByRole('alert');

    const retryButton = screen.getByRole('button', { name: /retry/i });
    await user.click(retryButton);
    await act(async () => {
      setChainQueryState({ data: undefined, isError: true, isFetching: false });
    });

    // The discriminating assertion: today this is the SAME node (only its
    // `role` came back), so it fails — proving the operator earns no fresh
    // insertion-announcement on a repeat failure.
    const secondAlert = screen.getByRole('alert');
    expect(secondAlert).not.toBe(firstAlert);
  });

  /**
   * FR-009. This does NOT prove a same-string write survives React's
   * same-value bail-out (#2344's defect) — it can't, from here. FR-006
   * clears the status region to `''` on failure before a second Retry can
   * ever fire, so the two "Re-reading the layout…" writes this test
   * drives are never adjacent in React's render history (asserted below:
   * the region reads `''` right before the second click). The bail-out
   * only fires on two *consecutive* identical writes, so it never gets a
   * chance to trigger here, key-token remount or not — confirmed by
   * counterfactual (phase-6 review): dropping `ChainRecoveryNotice.tsx`'s
   * `key={announcement.token}` left this test green.
   * `OverlayEditorUndo.test.tsx:915-931` is where that collision is real:
   * two consecutive successful undos both write 'Undone' with nothing
   * between them, so its key-remount is load-bearing in a way this one
   * isn't.
   *
   * What this test does prove: the second announcement actually reaches
   * the DOM — the region is not left stuck on `''` or silently frozen once
   * a second recovery cycle starts.
   */
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

    // Same message text as the first re-read — but not adjacent to it: the
    // clear asserted above already intervened, so this does not exercise
    // #2344's collision (see the docblock above).
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

  /**
   * Regression, blocker 2 (phase-6 review), mirrored from the overlay
   * dialog (FR-012). FR-008 enumerates only Reload's *success* path; a
   * Reload-originated re-read can itself be refused (the other writer
   * having deleted or archived the record since, or any backend blip),
   * and that path is reachable in production on the exact screen Reload
   * exists for — a stale-version conflict.
   *
   * `chainArmActive = readFailed || (reReading && origin === 'retry')`
   * takes no account of `origin` in its first disjunct: `readFailed` alone
   * flips it true regardless of which control started the fetch, so a
   * Reload-originated refusal hands the chain-read arm priority and
   * unmounts the `backendError` arm — Reload — anyway, taking the focused
   * button down with it. Focus falls to the dialog container, the exact
   * defect this component exists to fix, on a path FR-008 never covers.
   */
  it('Reload stays mounted and focused when the re-read it starts is itself refused (regression, blocker 2)', async () => {
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
    expect(document.activeElement).toBe(reloadButton);

    await act(async () => {
      setChainQueryState({
        data: { layoutIdentifier: EDIT_TARGET.layoutIdentifier, version: 7 },
        isError: true,
        isFetching: false,
      });
    });

    // The discriminating assertion: today `getByRole('reload')` throws —
    // the chain-read arm has taken over and Reload is gone.
    const reloadAfterRefusal = screen.getByRole('button', { name: /reload/i });
    expect(document.activeElement).toBe(reloadAfterRefusal);
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

  /**
   * Spec 158 (issue #2379) — GREEN characterisation (ADR-0139/ADR-0144).
   * Its own assertion mechanism moved with spec 160 (`aria-disabled`, not
   * native `disabled`); the behaviour it pins did not.
   *
   * Unlike the overlay dialog, `LayoutEditorDialog.tsx`'s `saveBlocked`
   * local already ORs `chainFetching` in — but nothing in this repo pinned
   * it: the comment above that predicate said so in words ("Neither is
   * pinned by a test in this repo…") until this test landed and T005
   * corrected it, and every `it` in `LayoutEditorDialogChainRetention.test.tsx`
   * whose Save-unavailable assertion runs with `currentChain === undefined`
   * (`:207`, `:332` — an in-flight read, so `chainFetching: true`; `:409` —
   * a settled, failed read, so `chainFailed: true` instead) is closed by the
   * `currentChain === undefined` half of the predicate alone — `chainFetching`
   * could be deleted and that file would stay green. This file's own Reload
   * test (FR-008, above) drives `isFetching:
   * true` with `data` retained but asserts focus only, never Save's
   * availability.
   *
   * This test exists to close that gap: it is the mirror of the overlay
   * dialog's new RED test (`OverlayEditorDialogChainRecovery.test.tsx`),
   * captured GREEN here because `chainFetching` is already present — no
   * production edit follows on this side. Proved by counterfactual in T004
   * (deleting ` || chainFetching` from `saveBlocked` must fail this exact
   * test) rather than trusted on the strength of this comment.
   */
  it('Save is unavailable while the re-read Reload started is in flight, and resumes once it answers with the new version (FR-005)', async () => {
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
    const saveButton = screen.getByRole('button', { name: /^save draft$/i });
    const editDraftCallsBeforeReload = editDraftMock.mock.calls.length;

    await user.click(reloadButton);

    // 1. `data` is retained through the refetch (harness contract above).
    expect(chainQueryState.data).toStrictEqual({ layoutIdentifier: EDIT_TARGET.layoutIdentifier, version: 7 });
    expect(chainQueryState.isFetching).toBe(true);

    // 2. Save must be unavailable while the re-read is in flight, even
    //    though `currentChain` (v7) is still defined. `aria-disabled`, not
    //    native `disabled` (spec 160 FR-001).
    expect(saveButton).toHaveAttribute('aria-disabled', 'true');

    // 3. The harm, not only the attribute: clicking must not submit v7 a
    //    second time while the re-read is still on the wire.
    await user.click(saveButton);
    expect(editDraftMock).toHaveBeenCalledTimes(editDraftCallsBeforeReload);

    // 4. The complement: once the re-read answers with the corrected
    //    version, Save re-enables and a save now carries that version, not
    //    the stale one.
    await act(async () => {
      setChainQueryState({
        data: { layoutIdentifier: EDIT_TARGET.layoutIdentifier, version: 8 },
        isError: false,
        isFetching: false,
      });
    });

    expect(saveButton).not.toHaveAttribute('aria-disabled', 'true');

    await user.click(saveButton);

    expect(editDraftMock).toHaveBeenCalledTimes(editDraftCallsBeforeReload + 1);
    expect(editDraftMock).toHaveBeenLastCalledWith(
      expect.objectContaining({ layoutIdentifier: EDIT_TARGET.layoutIdentifier, version: 8 }),
    );
  });
});
