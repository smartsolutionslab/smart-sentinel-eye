import { useState } from 'react';

// The subject a confirmation dialog is open for, or null when none is. The shape of `T` stays
// with the page: the pages' targets differ in the fields their mutations need.
export function useConfirmTarget<T>() {
  const [target, setTarget] = useState<T | null>(null);
  return {
    target,
    ask: setTarget,
    cancel: () => setTarget(null),
    // Run, then clear: the order every page had inline. A null target does nothing.
    confirm: (run: (target: T) => void) => () => {
      if (target === null) {
        return;
      }
      run(target);
      setTarget(null);
    },
  };
}
