import clsx from 'clsx';
import { forwardRef, type InputHTMLAttributes } from 'react';

export type InputProps = InputHTMLAttributes<HTMLInputElement>;

export const Input = forwardRef<HTMLInputElement, InputProps>(function Input({ className, ...rest }, ref) {
  return (
    <input
      ref={ref}
      className={clsx(
        'block w-full rounded-md border border-fg-muted bg-bg-elevated px-3 py-2 text-sm text-fg-primary ' +
          'placeholder:text-fg-muted focus-visible:outline-none focus-visible:ring-2 ' +
          'focus-visible:ring-accent-active disabled:opacity-50',
        className,
      )}
      {...rest}
    />
  );
});
