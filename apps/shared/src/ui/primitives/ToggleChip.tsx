import clsx from 'clsx';
import type { ComponentPropsWithRef } from 'react';

export interface ToggleChipProps extends Omit<ComponentPropsWithRef<'button'>, 'aria-pressed'> {
  pressed: boolean;
}

// A filter chip that announces whether it is on. `pressed` is the only source of
// `aria-pressed`. The pressed fill carries no hover or active background: a hover
// background would override `bg-accent-subtle` and hide the selection under the pointer.
export function ToggleChip({ pressed, className, type, ...rest }: ToggleChipProps) {
  const base =
    'rounded-md border px-3 py-1 text-sm ' +
    'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus-ring';

  const pressedFill = 'border-accent bg-accent-subtle text-accent';

  const restFill = 'border-border-subtle text-fg-muted hover:bg-bg-hover active:bg-bg-pressed';

  return (
    <button
      type={type ?? 'button'}
      aria-pressed={pressed}
      className={clsx(base, pressed ? pressedFill : restFill, className)}
      {...rest}
    />
  );
}
