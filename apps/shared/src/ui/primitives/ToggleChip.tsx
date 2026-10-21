import type { ComponentPropsWithRef } from 'react';

// Spec 427 / issue #2668, phase 4a red: declaration-only stub (final exported type, no behaviour)
// so the tests reach their assertions. The real primitive replaces this body.
export interface ToggleChipProps extends Omit<ComponentPropsWithRef<'button'>, 'aria-pressed'> {
  pressed: boolean;
}

export function ToggleChip({ pressed, ...rest }: ToggleChipProps) {
  void pressed;
  return <button {...rest} />;
}
