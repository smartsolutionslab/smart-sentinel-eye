import clsx from 'clsx';
import type { ReactNode } from 'react';

export interface FormFieldProps {
  label: string;
  htmlFor: string;
  error?: string;
  children: ReactNode;
  className?: string;
}

// Form-field composite per ADR-0079: pairs a Radix-friendly Label, the
// input slot (children), and an error message. React Hook Form drives
// validation; this component only renders the result.
export function FormField({ label, htmlFor, error, children, className }: FormFieldProps) {
  return (
    <div className={clsx('flex flex-col gap-1', className)}>
      <label htmlFor={htmlFor} className="text-sm font-medium text-fg-primary">
        {label}
      </label>
      {children}
      {error !== undefined && (
        <span role="alert" className="text-sm text-accent-fault">
          {error}
        </span>
      )}
    </div>
  );
}
