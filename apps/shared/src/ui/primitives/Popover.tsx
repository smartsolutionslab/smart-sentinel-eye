import * as RadixPopover from '@radix-ui/react-popover';
import type { ReactNode } from 'react';

export interface PopoverProps {
  /** `Trigger asChild` — must be focusable. */
  trigger: ReactNode;
  children: ReactNode;
  /** `aria-label` for the dialog-role content. */
  label: string;
  /** Default `'bottom'`. */
  side?: 'top' | 'right' | 'bottom' | 'left';
}

/**
 * Design-system disclosure on `@radix-ui/react-popover` (spec 266 / issue
 * #2335 US3, plan.md §3.3). Read-only content: no close button, Escape and
 * outside-click dismiss.
 */
export function Popover({ trigger, children, label, side = 'bottom' }: PopoverProps) {
  return (
    <RadixPopover.Root>
      <RadixPopover.Trigger asChild>{trigger}</RadixPopover.Trigger>
      <RadixPopover.Portal>
        <RadixPopover.Content
          side={side}
          sideOffset={4}
          aria-label={label}
          className="z-popover rounded-md border border-border-subtle bg-bg-raised px-3 py-2 text-xs text-fg-primary shadow-popover"
        >
          {children}
          <RadixPopover.Arrow className="fill-bg-raised" />
        </RadixPopover.Content>
      </RadixPopover.Portal>
    </RadixPopover.Root>
  );
}
