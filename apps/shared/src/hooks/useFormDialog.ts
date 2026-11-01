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
 * The obvious rewrite is wrong here. Moving this into the Dialog's <c>onOpenChange</c> handler would catch only
 * Radix-initiated closes (Esc, overlay click): Cancel and the submit-success path call the <i>parent's</i>
 * <c>onOpenChange</c> and close by flipping the <c>open</c> prop, which that handler never sees. Watching
 * <c>open</c> catches every close path. The cost is one extra render of an already-closed dialog.
 * </p>
 *
 * <p>
 * <c>clear</c> always sees the latest <c>reset</c>/<c>resetMutationState</c> via <c>useEffectEvent</c>; the effect
 * itself re-fires only when <c>open</c> changes. Listing RTK Query's <c>reset</c> in a dependency array instead
 * churns: see <see cref="useResetOnOpen"/>.
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
 * <c>reset</c> always sees the latest <c>reset</c>/<c>resetMutationState</c> via <c>useEffectEvent</c>; the effect
 * itself re-fires only on <c>open</c>/<c>resetKey</c>. Listing RTK Query's <c>reset</c> in the deps array instead
 * churns: triggering the mutation gives it a new identity (its <c>useCallback</c> closes over the pending promise),
 * which re-ran this effect mid-submit and called <c>resetMutationState()</c> a second time, discarding the very
 * "pending" state it had just recorded while the request was still in flight. <c>unavailable</c> read that
 * discarded state, so Save reported "not loading" seconds into a held request (issue #2624 CI, spec 267).
 * </p>
 *
 * <p>
 * <c>resetKey</c> is one fixed slot, not a dependency array: a dialog whose reset also depends on a prop (rename
 * resets when <c>currentName</c> changes while open) passes it here; a dialog that does not passes nothing, and the
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
 * focusable (<c>unavailable</c>) and no longer suppresses implicit submission (Enter in a field); this guard on the
 * form's <c>onSubmit</c> is what refuses the second one.
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
