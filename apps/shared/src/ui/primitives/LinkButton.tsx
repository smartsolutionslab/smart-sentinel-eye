import clsx from 'clsx';
import type { ComponentPropsWithRef } from 'react';

export type LinkButtonProps = ComponentPropsWithRef<'button'>;

// An underlined text action. It chooses no colour or size: both are inherited from the
// surrounding alert, so the action reads in the alert's own foreground (spec 287 D2).
// Unavailability is announced with `aria-disabled` and the cursor stays a call-site class,
// as on `Button` (ADR-0151).
export function LinkButton({ className, type, ...rest }: LinkButtonProps) {
  const base =
    'underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus-ring ' +
    'aria-disabled:text-fg-disabled';

  return <button type={type ?? 'button'} className={clsx(base, className)} {...rest} />;
}
