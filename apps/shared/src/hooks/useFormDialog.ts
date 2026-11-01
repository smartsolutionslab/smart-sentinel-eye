import { type FormEvent, useEffect, useEffectEvent } from 'react';

/**
 * Runs `clear` whenever the dialog is closed (and when it mounts closed).
 *
 * <p>
 * Drop any prior backend error and typed input when the dialog closes, so a stale banner or value doesn't greet the
 * operator on the next open: the mutation result and the form values both live outside the unmounted dialog's DOM,
 * because the parent renders the dialog unconditionally.
 * </p>
 *
 * <p>
 * The obvious rewrite is wrong here. Moving this into the Dialog's `onOpenChange` handler would catch only
 * Radix-initiated closes (Esc, overlay click): Cancel and the submit-success path call the <i>parent's</i>
 * `onOpenChange` and close by flipping the `open` prop, which that handler never sees. Watching
 * `open` catches every close path. The cost is one extra render of an already-closed dialog.
 * </p>
 *
 * <p>
 * `clear` also runs once on mount while the dialog is closed, as the dialogs always did, so it must be idempotent and
 * free of side effects such as toasts or telemetry.
 * </p>
 *
 * <p>
 * `clear` always sees the latest `reset`/`resetMutationState` via `useEffectEvent`; the effect
 * itself re-fires only when `open` changes. Listing RTK Query's `reset` in a dependency array instead
 * churns: see {@link useResetOnOpen}.
 * </p>
 */
export function useClearOnClose(open: boolean, clear: () => void): void {
  const clearOnClose = useEffectEvent(clear);

  useEffect(() => {
    if (!open) clearOnClose();
  }, [open]);
}

/**
 * Runs `reset` when the dialog opens, and again whenever `resetKey` changes while it is open.
 *
 * <p>
 * Reopening starts from what is stored now, not from what a previous attempt left behind, and a stale refusal banner
 * must not greet the next open.
 * </p>
 *
 * <p>
 * `reset` always sees the latest `reset`/`resetMutationState` via `useEffectEvent`; the effect
 * itself re-fires only on `open`/`resetKey`. Listing RTK Query's `reset` in the deps array instead
 * churns: triggering the mutation gives it a new identity (its `useCallback` closes over the pending promise),
 * which re-ran this effect mid-submit and called `resetMutationState()` a second time, discarding the very
 * "pending" state it had just recorded while the request was still in flight. `unavailable` read that
 * discarded state, so Save reported "not loading" seconds into a held request (issue #2624 CI, spec 267).
 * </p>
 *
 * <p>
 * `resetKey` is one fixed slot, not a dependency array: a dialog whose reset also depends on a prop (rename
 * resets when `currentName` changes while open) passes it here; a dialog that does not passes nothing, and the
 * constant slot never changes.
 * </p>
 */
export function useResetOnOpen(open: boolean, reset: () => void, resetKey?: unknown): void {
  const resetForOpen = useEffectEvent(reset);

  useEffect(() => {
    if (open) resetForOpen();
  }, [open, resetKey]);
}

/**
 * ADR-0151: refuses a second submit while one is in flight. A disabled button would drop focus, so the button stays
 * focusable (`unavailable`) and no longer suppresses implicit submission (Enter in a field); this guard on the
 * form's `onSubmit` is what refuses the second one.
 */
export function useSubmitGuard(
  isLoading: boolean,
  onSubmit: (event: FormEvent) => unknown,
): (event: FormEvent) => void {
  return function handleFormSubmit(event: FormEvent) {
    if (isLoading) {
      event.preventDefault();
      return;
    }
    void onSubmit(event);
  };
}
