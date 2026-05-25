import { Slot } from '@radix-ui/react-slot';
import clsx from 'clsx';
import type { ButtonHTMLAttributes } from 'react';

export type ButtonVariant = 'primary' | 'secondary' | 'ghost';

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
  asChild?: boolean;
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
  };
  return (
    <Component
      type={asChild ? undefined : (type ?? 'button')}
      className={clsx(base, variants[variant], className)}
      {...rest}
    />
  );
}
