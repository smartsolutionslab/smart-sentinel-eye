import * as RadixDialog from '@radix-ui/react-dialog';
import clsx from 'clsx';
import type { ReactNode } from 'react';

export interface DialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: ReactNode;
  description?: ReactNode;
  children: ReactNode;
}

export function Dialog({ open, onOpenChange, title, description, children }: DialogProps) {
  return (
    <RadixDialog.Root open={open} onOpenChange={onOpenChange}>
      <RadixDialog.Portal>
        <RadixDialog.Overlay className="fixed inset-0 z-overlay bg-scrim backdrop-blur-sm" />
        <RadixDialog.Content
          className={clsx(
            'fixed left-1/2 top-1/2 z-overlay w-full max-w-md -translate-x-1/2 -translate-y-1/2 ' +
              // Cap to the viewport and scroll: tall content (e.g. the overlay
              // editor canvas) must not push the action buttons off-screen.
              'max-h-[90vh] overflow-y-auto ' +
              'rounded-lg bg-bg-raised p-6 shadow-overlay border border-border-subtle text-fg-primary',
          )}
        >
          <RadixDialog.Title className="text-lg font-semibold">{title}</RadixDialog.Title>
          {description !== undefined && (
            <RadixDialog.Description className="mt-1 text-sm text-fg-muted">{description}</RadixDialog.Description>
          )}
          <div className="mt-4">{children}</div>
        </RadixDialog.Content>
      </RadixDialog.Portal>
    </RadixDialog.Root>
  );
}
