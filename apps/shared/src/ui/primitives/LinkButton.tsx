import type { ComponentPropsWithRef } from 'react';

// Spec 427 / issue #2668, phase 4a red: declaration-only stub (final exported type, no behaviour)
// so the tests reach their assertions. The real primitive replaces this body.
export type LinkButtonProps = ComponentPropsWithRef<'button'>;

export function LinkButton({ ...rest }: LinkButtonProps) {
  return <button {...rest} />;
}
