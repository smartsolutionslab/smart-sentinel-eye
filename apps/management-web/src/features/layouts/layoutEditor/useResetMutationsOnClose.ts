import { useEffect } from 'react';

interface Resettable {
  reset: () => void;
}

// Drop any prior backend error when the dialog closes so a stale banner
// doesn't greet the operator on the next open.
//
// Resets BOTH mutation states, not the one `isEdit` names (phase-6 review):
// `LayoutsPage.tsx` drives `open={editTarget !== undefined}`, so `open`
// and `isEdit` are the same boolean, and by the time this effect fires on
// close (`!open`), `isEdit` has already gone false — a mode-selected reset
// would always clear create's state, leaving a refused edit's error to
// survive into the next open, on a different, never-refused draft.
export function useResetMutationsOnClose(open: boolean, createState: Resettable, editState: Resettable): void {
  useEffect(() => {
    if (!open) {
      createState.reset();
      editState.reset();
    }
  }, [open, createState, editState]);
}
