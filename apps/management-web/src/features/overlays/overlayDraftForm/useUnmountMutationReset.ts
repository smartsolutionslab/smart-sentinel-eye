import { useEffect } from 'react';

/**
 * Spec 305 (#2350). Resets BOTH mutation states on unmount — not on an
 * `open` prop, which this form does not have. The dialog that used to host
 * this body kept it permanently mounted and reset explicitly on close; this
 * form is instead always given a fresh mount per target (the dialog's own
 * `Dialog` unmounts its children on close, and `OverlayEditPage`/
 * `OverlayCreatePage` unmount this form entirely on navigating away), so a
 * cleanup-on-unmount reset is the equivalent guarantee: a refused save's
 * banner must never survive into a later mount of this form for a
 * different target.
 *
 * Resets BOTH, not the one `isEdit` names (phase-6 review, carried over
 * from the dialog this form replaces): a mode-selected reset would always
 * clear create's state, leaving a refused edit's error free to leak into
 * whatever mounts next.
 *
 * Deliberately mount-once: the cleanup must run exactly once, on unmount,
 * not on every render the mutation hooks happen to return a new object
 * from.
 */
export function useUnmountMutationReset(createState: { reset: () => void }, editState: { reset: () => void }): void {
  useEffect(() => {
    return () => {
      createState.reset();
      editState.reset();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps -- see above
  }, []);
}
