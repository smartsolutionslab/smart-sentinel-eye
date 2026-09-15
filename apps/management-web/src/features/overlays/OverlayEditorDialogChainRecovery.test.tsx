import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { useSyncExternalStore } from 'react';
import { Provider } from 'react-redux';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { store } from '../../app/store.js';
import type { OverlayEditTarget } from './OverlayEditorDialog.js';

/**
 * Spec 156 (issue #2372) — new behaviour, RED (ADR-0139).
 *
 * New file, not an addition to `OverlayEditorDialog.test.tsx` (plan.md §5
 * rule 2): PR #2377 appends to a `describe` block in the sibling
 * `LayoutEditorDialog.test.tsx`, and this spec's own plan draws the same
 * line for the overlay side — new files avoid the conflict entirely, and it
 * is already house convention (five `OverlayEditorDialog*.test.tsx` files).
 *
 * The issue reads as "the control is destroyed when the refetch succeeds".
 * It is destroyed the instant Retry is *clicked*: `chainFailed` is RTK
 * Query's `isError`, and `refetch()` sets `status = 'pending'` unconditionally,
 * so `isError` goes false while the request is still in flight (spec.md §1).
 * Every test below drives that in-flight window explicitly — none of them
 * can be satisfied by moving focus "on success".
 */

const createDraftMock = vi.fn(async () => ({ data: 'noop' }));
const editDraftMock = vi.fn(async (_body: unknown) => ({ data: 1 }));
let createError: unknown = undefined;
let editError: unknown = undefined;

interface ChainQueryState {
  data?: { overlayIdentifier: string; version: number };
  isError: boolean;
  isFetching: boolean;
}

/**
 * Neither `OverlayEditorDialog.test.tsx`'s own `chainQueryState` (no
 * `isFetching`) nor its `refetchChainMock` (a bare `vi.fn()` that records a
 * call and changes nothing) can drive `failed -> in flight -> resolved`
 * (plan.md §6) — this harness is what T002 asks this file to build.
 *
 * `useSyncExternalStore` rather than a plain object read: mutating
 * `chainQueryState` alone would not re-render the already-mounted dialog —
 * nothing subscribes to a bare module-level `let`. This mirrors what a real
 * RTK Query `refetch()` does (a store dispatch that reaches subscribed
 * components) closely enough that a test can step the state machine by
 * calling `setChainQueryState` and trust the dialog re-renders, rather than
 * calling `rerender()` by hand after every step — the gap plan.md §6 warns
 * produces a test that cannot make `isFetching` true and so cannot fail
 * against an implementation that never renders the in-flight state at all.
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

vi.mock('@smart-sentinel-eye/shared/api/overlays.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/overlays.api')>();
  return {
    ...actual,
    useCreateOverlayDraftMutation: () => [
      createDraftMock,
      { isLoading: false, error: createError, reset: vi.fn(() => (createError = undefined)) },
    ],
    useEditDraftOverlayRevisionMutation: () => [
      editDraftMock,
      { isLoading: false, error: editError, reset: vi.fn(() => (editError = undefined)) },
    ],
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

/**
 * What the mocked `refetch()` does by default — the same transition a real
 * refetch's dispatch produces. `...chainQueryState` (not `data: undefined`,
 * phase-6 review): RTK Query keeps `currentData` defined through a
 * same-argument refetch, clearing it only on a `skipToken` step or an arg
 * change (neither happens here) — the spec itself says so
 * (`OverlayEditorDialog.tsx:72-81`). Overwriting it to `undefined` was only
 * ever accidentally correct for Retry, whose precondition already has no
 * data; it was wrong for Reload, whose precondition is a *successful* prior
 * read.
 */
function beginReRead() {
  setChainQueryState({ ...chainQueryState, isError: false, isFetching: true });
}

function statusRegion() {
  return screen.getByTestId('overlay-chain-recovery-status');
}

describe('OverlayEditorDialog — a recovery control that survives its own activation (spec 156)', () => {
  beforeEach(() => {
    createDraftMock.mockClear();
    editDraftMock.mockClear();
    refetchChainMock.mockClear();
    refetchChainMock.mockImplementation(() => {
      beginReRead();
      return Promise.resolve();
    });
    createError = undefined;
    editError = undefined;
    chainQueryState = { data: undefined, isError: true, isFetching: false };
  });

  afterEach(() => {
    cleanup();
  });

  /**
   * The discriminating test (tasks.md "The discriminating test"). Assertion
   * #2 — `document.activeElement` IS the Retry button while the refetch is
   * in flight — is the one a plausible wrong fix still fails: moving focus
   * "on success" leaves the button already gone by request time (spec.md
   * §1), and natively `disable`-ing it blurs it the instant it disables
   * (`OverlayEditor.tsx:649-657`).
   */
  it('Retry keeps focus while the re-read is in flight, and hands it to Save when it succeeds', async () => {
    const user = userEvent.setup();
    renderDialog();

    const retryButton = screen.getByRole('button', { name: /retry/i });
    expect(retryButton.closest('[role="alert"]')).not.toBeNull();

    await user.click(retryButton);

    // 1. Retry is still in the document.
    expect(screen.getByRole('button', { name: /retry/i })).toBeInTheDocument();
    // 2. The discriminating assertion.
    expect(document.activeElement).toBe(retryButton);
    // 3. `aria-disabled`, never natively disabled (a native disable would
    //    already have blurred it, failing assertion 2).
    expect(retryButton).toHaveAttribute('aria-disabled', 'true');
    // 4. The always-mounted status region carries the in-flight announcement.
    expect(statusRegion()).toHaveTextContent(/re-reading the overlay/i);
    // 5. `role="alert"` is gone — but this proves only that the
    //    *attribute* is absent right now, not that the underlying element
    //    unmounted. It is in fact the same `<p>`, reused unchanged across
    //    the whole click -> fail -> click cycle (`chainArmActive` spans
    //    both states); only its `role` toggles off and back on. That reuse
    //    is a real regression (blocker 1, phase-6 review) — see "Announces
    //    a second refusal" below.
    expect(screen.queryByRole('alert')).toBeNull();

    await act(async () => {
      setChainQueryState({
        data: { overlayIdentifier: EDIT_TARGET.overlayIdentifier, version: 7 },
        isError: false,
        isFetching: false,
      });
    });

    // 6. Nothing left to retry.
    expect(screen.queryByRole('button', { name: /retry/i })).toBeNull();
    // 7. Focus lands on Save, the control the operator was trying to reach.
    const saveButton = screen.getByRole('button', { name: /^save draft$/i });
    expect(document.activeElement).toBe(saveButton);
    // 8. The success is exactly what enables it (spec 160 FR-001: `aria-disabled`,
    //    not native `disabled`). Paired with the behavioural half (tasks.md rule
    //    2) — the attribute alone cannot tell "gate open" from "gate cosmetic".
    expect(saveButton).not.toHaveAttribute('aria-disabled', 'true');
    // 9. The announcement changes to reflect the outcome.
    expect(statusRegion()).toHaveTextContent(/was read/i);

    await user.click(saveButton);
    expect(editDraftMock).toHaveBeenCalledTimes(1);
    expect(editDraftMock).toHaveBeenLastCalledWith(
      expect.objectContaining({ overlayIdentifier: EDIT_TARGET.overlayIdentifier, version: 7 }),
    );
  });

  it('Leaves focus on Retry and shows the alert again when the re-read is refused a second time (FR-006)', async () => {
    const user = userEvent.setup();
    renderDialog();

    const retryButton = screen.getByRole('button', { name: /retry/i });
    await user.click(retryButton);
    expect(statusRegion()).toHaveTextContent(/re-reading the overlay/i);

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
    // Cleared, not merely unchanged — the failure's own insertion is what
    // announces it now, so the status region has nothing left to say.
    expect(statusRegion().textContent).toBe('');
  });

  /**
   * Regression, blocker 1 (phase-6 review). `chainArmActive` (the mount
   * condition for the chain-read `<p>`) spans BOTH the in-flight window and
   * the failed state, so React reconciles one persistent element across a
   * whole click -> fail -> click -> fail cycle rather than unmounting and
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
   * ever fire, so the two "Re-reading the overlay…" writes this test
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
    expect(statusRegion()).toHaveTextContent(/re-reading the overlay/i);
  });

  /**
   * FR-008. Reload's alert is derived from the mutation's error, and
   * `refetchChain()` touches only the query — nothing clears it, so Reload
   * must stay mounted and stay focused throughout, unlike Retry.
   */
  it('Reload keeps its own focus through the re-read it starts, and never hands focus to Save (FR-008)', async () => {
    const user = userEvent.setup();
    editError = {
      status: 409,
      data: { title: 'OVERLAY_REVISION_STALE', detail: 'Overlay has changed since version 7 (now 8).' },
    };
    chainQueryState = {
      data: { overlayIdentifier: EDIT_TARGET.overlayIdentifier, version: 7 },
      isError: false,
      isFetching: false,
    };
    renderDialog();

    const reloadButton = await screen.findByRole('button', { name: /reload/i });
    await user.click(reloadButton);

    expect(reloadButton).toBeInTheDocument();
    expect(document.activeElement).toBe(reloadButton);
    expect(statusRegion()).toHaveTextContent(/re-reading the overlay/i);

    await act(async () => {
      setChainQueryState({
        data: { overlayIdentifier: EDIT_TARGET.overlayIdentifier, version: 8 },
        isError: false,
        isFetching: false,
      });
    });

    expect(document.activeElement).toBe(reloadButton);
    expect(document.activeElement).not.toBe(screen.getByRole('button', { name: /^save draft$/i }));
  });

  /**
   * Regression, blocker 2 (phase-6 review). FR-008 enumerates only Reload's
   * *success* path; a Reload-originated re-read can itself be refused
   * (the other writer having deleted or archived the record since, or any
   * backend blip), and that path is reachable in production on the exact
   * screen Reload exists for — a stale-version conflict.
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
      data: { title: 'OVERLAY_REVISION_STALE', detail: 'Overlay has changed since version 7 (now 8).' },
    };
    chainQueryState = {
      data: { overlayIdentifier: EDIT_TARGET.overlayIdentifier, version: 7 },
      isError: false,
      isFetching: false,
    };
    renderDialog();

    const reloadButton = await screen.findByRole('button', { name: /reload/i });
    await user.click(reloadButton);
    expect(document.activeElement).toBe(reloadButton);

    await act(async () => {
      setChainQueryState({
        data: { overlayIdentifier: EDIT_TARGET.overlayIdentifier, version: 7 },
        isError: true,
        isFetching: false,
      });
    });

    // The discriminating assertion: today `getByRole('reload')` throws —
    // the chain-read arm has taken over and Reload is gone.
    const reloadAfterRefusal = screen.getByRole('button', { name: /reload/i });
    expect(document.activeElement).toBe(reloadAfterRefusal);
  });

  /**
   * FR-007. Every normal dialog open goes `false -> false` on `chainFailed`
   * with `reReading` flickering in between — a naive effect on `chainFailed`
   * alone would steal focus to Save here, which is a worse defect than the
   * one being fixed.
   */
  it('Moves no focus and announces nothing on a normal successful open (FR-007)', () => {
    chainQueryState = {
      data: { overlayIdentifier: EDIT_TARGET.overlayIdentifier, version: 7 },
      isError: false,
      isFetching: false,
    };
    renderDialog();

    expect(document.activeElement).not.toBe(screen.getByRole('button', { name: /^save draft$/i }));
    expect(statusRegion().textContent).toBe('');
  });

  /**
   * Spec 158 (issue #2379) — new behaviour, RED (ADR-0139).
   *
   * `currentData` survives a same-argument refetch (the comment block above
   * `useGetOverlayQuery` at `OverlayEditorDialog.tsx:72-82` states this, and
   * `beginReRead()` above deliberately spreads `...chainQueryState` rather
   * than clearing `data` to model it). So while a re-read of the same
   * overlay is in flight, `currentChain` stays defined at the pre-re-read
   * version, and the shipped predicate
   * (`isLoading || (isEdit && currentChain === undefined)`) was the gate at
   * `OverlayEditorDialog.tsx:311`, until `36a27d01` added `chainFetching` —
   * it was false, so Save stayed enabled and would have submitted
   * `version: 7`, the exact version the re-read exists to correct.
   *
   * Steps 4-5 below cannot be satisfied by a cosmetic fix: hard-wiring Save
   * to `disabled` forever would pass an attribute-only assertion but fail
   * the complement leg, and a fix that only prevents the click handler but
   * leaves the attribute enabled would fail step 3.
   */
  it('Save is unavailable while the re-read Reload started is in flight, and resumes once it answers with the new version (FR-001, FR-004)', async () => {
    const user = userEvent.setup();
    editError = {
      status: 409,
      data: { title: 'OVERLAY_REVISION_STALE', detail: 'Overlay has changed since version 7 (now 8).' },
    };
    chainQueryState = {
      data: { overlayIdentifier: EDIT_TARGET.overlayIdentifier, version: 7 },
      isError: false,
      isFetching: false,
    };
    renderDialog();

    const reloadButton = await screen.findByRole('button', { name: /reload/i });
    const saveButton = screen.getByRole('button', { name: /^save draft$/i });
    const editDraftCallsBeforeReload = editDraftMock.mock.calls.length;

    await user.click(reloadButton);

    // 1. `data` is retained through the refetch (harness contract above).
    expect(chainQueryState.data).toStrictEqual({ overlayIdentifier: EDIT_TARGET.overlayIdentifier, version: 7 });
    expect(chainQueryState.isFetching).toBe(true);

    // 2. The failing assertion: Save must be unavailable while the re-read
    //    is in flight, even though `currentChain` (v7) is still defined.
    //    `aria-disabled`, not native `disabled` (spec 160 FR-001).
    expect(saveButton).toHaveAttribute('aria-disabled', 'true');

    // 3. The harm, not only the attribute: clicking must not submit v7 a
    //    second time while the re-read is still on the wire.
    await user.click(saveButton);
    expect(editDraftMock).toHaveBeenCalledTimes(editDraftCallsBeforeReload);

    // 4. The complement: once the re-read answers with the corrected
    //    version, Save re-enables and a save now carries that version, not
    //    the stale one. Without this leg a predicate hard-wired to
    //    `disabled` would still satisfy assertions 2-3.
    await act(async () => {
      setChainQueryState({
        data: { overlayIdentifier: EDIT_TARGET.overlayIdentifier, version: 8 },
        isError: false,
        isFetching: false,
      });
    });

    expect(saveButton).not.toHaveAttribute('aria-disabled', 'true');

    await user.click(saveButton);

    expect(editDraftMock).toHaveBeenCalledTimes(editDraftCallsBeforeReload + 1);
    expect(editDraftMock).toHaveBeenLastCalledWith(
      expect.objectContaining({ overlayIdentifier: EDIT_TARGET.overlayIdentifier, version: 8 }),
    );
  });
});
