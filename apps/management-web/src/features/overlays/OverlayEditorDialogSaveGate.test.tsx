import { act, cleanup, render, screen, waitFor } from '@testing-library/react';
import { useSyncExternalStore } from 'react';
import { Provider } from 'react-redux';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { store } from '../../app/store.js';
import type { OverlayEditTarget } from './OverlayEditorDialog.js';

/**
 * Spec 160 (issue #2387) US1 — new behaviour, RED (ADR-0139).
 *
 * New file, not an append to `OverlayEditorDialogChainRecovery.test.tsx`
 * (plan.md §5.1): T003 rewrites that file's own native-`disabled` assertions,
 * and the phase-4a red here must be attributable to the new behaviour alone.
 *
 * The architect's correction to #2387's framing: focus is destroyed at
 * Save-click time by the pre-existing `isLoading` term (the mutation's
 * pending flag), before any chain conflict exists — `chainFetching` only
 * extends the window during which focus cannot return. So this harness must
 * drive the mutation's own pending state, not only the chain query. Neither
 * the existing `OverlayEditorDialog.test.tsx` mock (`isLoading: false`,
 * hard-coded) nor `OverlayEditorDialogChainRecovery.test.tsx`'s mock
 * (same) can do that — both are extended here with a second
 * `useSyncExternalStore` source for the edit mutation's `isLoading`, plus a
 * deferred promise for its trigger, so the pending window can be held open
 * and settled on command from the test body.
 *
 * **No `document.activeElement` assertion in this file.** jsdom does not run
 * the browser's disable-blur ("focus fixup") algorithm — confirmed against a
 * bare, unmocked React `<button disabled>` with no app code involved, and
 * against jsdom 30.0.1's own source (`HTMLOrSVGElement-impl.js`'s `blur()`
 * only runs on an explicit call or a focus move elsewhere; `Document-impl.js`'s
 * focus-fixup rule fires only on node removal, never on an attribute change).
 * So `document.activeElement` cannot be made to leave the Save button here,
 * on `develop` or after the fix — the assertion would be unfalsifiable, not
 * merely weak. This repo already answered the identical question for a
 * structurally identical control: `e2e/overlays.spec.ts:213-220` (spec 154's
 * Undo button) states the same finding and moves the claim to Playwright.
 * The focus claim for *this* control is carried by
 * `e2e/overlays.spec.ts`'s "a stale-version conflict does not cost the
 * keyboard operator their place at Save" — a real browser, where disabling
 * the focused element genuinely does blur it.
 *
 * What this file asserts instead (spec §7, tasks.md rule 2): never
 * `aria-disabled` alone. The gate-open ending of the main scenario is paired
 * with a click that actually calls the mutation with the expected version;
 * the Enter-key scenario is itself the gate-closed / call-count pairing.
 */

const createDraftMock = vi.fn(async () => ({ data: 'noop' }));

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

/**
 * The steppable mutation source T001 needs and neither existing harness
 * has (plan.md §5.2). `isLoading` starts false; `editDraftMock` flips it
 * true synchronously on trigger and returns a promise that stays pending
 * until the test settles it via `refuseEdit` / `resolveEdit` — modelling
 * the real window between "operator clicked Save" and "the PATCH answered".
 */
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

/** Settles the pending trigger with a refusal and drops `isLoading`. */
function refuseEdit(error: unknown) {
  setMutationState({ isLoading: false, error });
  const resolve = deferredEditResolve;
  deferredEditResolve = null;
  resolve?.({ error });
}

vi.mock('@smart-sentinel-eye/shared/api/overlays.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/overlays.api')>();
  return {
    ...actual,
    useCreateOverlayDraftMutation: () => [createDraftMock, { isLoading: false, error: undefined, reset: vi.fn() }],
    useEditDraftOverlayRevisionMutation: () => {
      const state = useSyncExternalStore(subscribeMutationState, () => mutationState);
      return [editDraftMock, { isLoading: state.isLoading, error: state.error, reset: vi.fn() }];
    },
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

describe('OverlayEditorDialog — Save keeps its focus while it is unavailable (spec 160 US1)', () => {
  beforeEach(() => {
    createDraftMock.mockClear();
    editDraftMock.mockClear();
    refetchChainMock.mockClear();
    deferredEditResolve = null;
    mutationState = { isLoading: false, error: undefined };
    chainQueryState = {
      data: { overlayIdentifier: EDIT_TARGET.overlayIdentifier, version: 7 },
      isError: false,
      isFetching: false,
    };
  });

  afterEach(() => {
    cleanup();
  });

  /**
   * Spec §5.1, verbatim. The closing two lines are load-bearing (spec's own
   * words): a gate stuck closed forever would satisfy every assertion above
   * them, which is why the scenario ends with a click that must actually
   * reach the mutation, carrying the re-read version.
   */
  it('keeps focus on Save from the click through the conflict to the recovered version', async () => {
    const user = userEvent.setup();
    renderDialog();

    const saveButton = screen.getByRole('button', { name: /^save draft$/i });

    await user.click(saveButton);
    await waitFor(() => expect(editDraftMock).toHaveBeenCalledTimes(1));

    // The mutation is now pending (isLoading true). This is the assertion
    // that must be RED: FR-001 says `aria-disabled`; unmodified `develop`
    // renders native `disabled` here instead. (The focus claim that goes
    // with this moment — a real browser blurs the focused element the
    // instant it natively disables, and Radix cannot rescue that blur — is
    // not assertable in jsdom at all; see this file's doc comment and
    // `e2e/overlays.spec.ts`'s "a stale-version conflict does not cost the
    // keyboard operator their place at Save".)
    expect(saveButton).toHaveAttribute('aria-disabled', 'true');

    await act(async () => {
      refuseEdit({
        status: 409,
        data: { title: 'OVERLAY_REVISION_STALE', detail: 'Overlay has changed since version 7 (now 8).' },
      });
    });

    // The conflict's own `invalidatesTags` starts the chain re-read.
    await act(async () => {
      setChainQueryState({ ...chainQueryState, isError: false, isFetching: true });
    });

    expect(saveButton).toHaveAttribute('aria-disabled', 'true');

    await act(async () => {
      setChainQueryState({
        data: { overlayIdentifier: EDIT_TARGET.overlayIdentifier, version: 8 },
        isError: false,
        isFetching: false,
      });
    });

    expect(saveButton).not.toHaveAttribute('aria-disabled', 'true');

    // Gate-open pairing (rule 2): the re-enabled Save actually submits, and
    // carries the recovered version — not the stale one the click before it
    // was refused for.
    await user.click(saveButton);
    await waitFor(() => expect(editDraftMock).toHaveBeenCalledTimes(2));
    expect(editDraftMock).toHaveBeenLastCalledWith(
      expect.objectContaining({ overlayIdentifier: EDIT_TARGET.overlayIdentifier, version: 8 }),
    );
  });

  /**
   * Spec §5.3. This path exists only after FR-001/FR-003 land: a natively
   * disabled default button already suppresses implicit Enter-to-submit, so
   * on unmodified `develop` this passes vacuously — the gate-closed /
   * call-count pairing (rule 2) it establishes only becomes load-bearing
   * once Save is `aria-disabled` (which restores implicit submission) and
   * the click handler is the only remaining defence. Kept anyway: it is the
   * assertion that would catch this change becoming worse than the bug it
   * fixes (a blocked Enter silently submitting).
   */
  it('does not submit when Enter is pressed in a field while the gate is closed (passes vacuously pre-fix)', async () => {
    const user = userEvent.setup();
    chainQueryState = {
      data: { overlayIdentifier: EDIT_TARGET.overlayIdentifier, version: 7 },
      isError: false,
      isFetching: true,
    };
    renderDialog();

    const labelField = screen.getByTestId('overlay-editor-text');
    await user.click(labelField);
    await user.keyboard('{Enter}');

    expect(editDraftMock).not.toHaveBeenCalled();
  });
});
