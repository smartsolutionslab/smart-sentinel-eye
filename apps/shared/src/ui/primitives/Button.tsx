import { Slot } from '@radix-ui/react-slot';
import clsx from 'clsx';
import type { ComponentPropsWithRef } from 'react';

export type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger';

// `ComponentPropsWithRef<'button'>`, not `ButtonHTMLAttributes<HTMLButtonElement>`
// (spec 156, plan.md §3b): a strict superset that also carries `ref` — React
// 19.2.8 already passes `ref` through `{...rest}` onto the native element at
// runtime, so this widening is TypeScript-only and no call site changes.
export interface ButtonProps extends ComponentPropsWithRef<'button'> {
  variant?: ButtonVariant;
  asChild?: boolean;
  unavailable?: boolean;
}

// Custom design-system button (ADR-0077). Built on Radix Slot so it can wrap
// arbitrary children when asChild is set. Tailwind tokens via CSS custom
// properties (ADR-0078).
export function Button({ variant = 'primary', asChild, className, type, ...rest }: ButtonProps) {
  const Component = asChild ? Slot : 'button';
  const base =
    'inline-flex items-center justify-center rounded-md px-4 py-2 text-sm font-medium ' +
    'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-offset-2 ' +
    'disabled:pointer-events-none disabled:opacity-50 transition-colors';
  const variants: Record<ButtonVariant, string> = {
    primary: 'bg-accent-active text-bg-base hover:opacity-90',
    secondary: 'border border-fg-muted text-fg-primary hover:bg-bg-elevated',
    ghost: 'text-fg-primary hover:bg-bg-elevated',
    // Reuses the fault token rather than adding one: it is already the
    // product's red, on error banners and the Offline health badge. A
    // destructive action reading as the same red an operator already knows
    // means trouble is the point.
    danger: 'bg-accent-fault text-bg-base hover:opacity-90',
  };
  return (
    <Component
      type={asChild ? undefined : (type ?? 'button')}
      className={clsx(base, variants[variant], className)}
      {...rest}
    />
  );
}
