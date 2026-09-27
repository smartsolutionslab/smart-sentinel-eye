import * as RadixDropdownMenu from '@radix-ui/react-dropdown-menu';
import clsx from 'clsx';
import type { ReactNode } from 'react';

export type MenuEntry =
  | { kind: 'item'; label: string; onSelect: () => void; disabled?: boolean; variant?: 'default' | 'danger' }
  | { kind: 'separator' };

export interface DropdownMenuProps {
  /** Rendered via `Trigger asChild` — must be a focusable element (a Button). */
  trigger: ReactNode;
  entries: readonly MenuEntry[];
  /** Default `'end'` (row actions sit at the right edge). */
  align?: 'start' | 'end';
}

/**
 * Design-system row-actions menu on `@radix-ui/react-dropdown-menu` (spec 266
 * / issue #2335 US2, plan.md §3.2).
 */
export function DropdownMenu({ trigger, entries, align = 'end' }: DropdownMenuProps) {
  return (
    // `modal={false}` is load-bearing (plan.md §3.2): a modal menu that closes
    // while a Radix dialog opens from its `onSelect` leaves
    // `pointer-events: none` on `<body>` and races the two focus scopes.
    <RadixDropdownMenu.Root modal={false}>
      <RadixDropdownMenu.Trigger asChild>{trigger}</RadixDropdownMenu.Trigger>
      <RadixDropdownMenu.Portal>
        <RadixDropdownMenu.Content
          align={align}
          sideOffset={4}
          className="z-popover min-w-40 rounded-md border border-border-subtle bg-bg-raised p-1 text-fg-primary shadow-popover"
        >
          {entries.map((entry, index) =>
            entry.kind === 'separator' ? (
              <RadixDropdownMenu.Separator key={`separator-${index}`} className="my-1 h-px bg-border-subtle" />
            ) : (
              <RadixDropdownMenu.Item
                key={entry.label}
                disabled={entry.disabled}
                onSelect={() => {
                  // Deferred, not called inline: Radix's own `onClose()` runs
                  // synchronously right after this callback returns (plan.md
                  // §3.2), so calling the caller's handler here would open a
                  // dialog while the menu is still mid-close. A single
                  // `setTimeout` fires before the closing menu's own
                  // FocusScope has unmounted (its focus-restoration teardown
                  // is itself scheduled via a `setTimeout` once React commits
                  // the close) — nesting a second one lets that teardown run
                  // first, so the dialog that opens here is never the thing a
                  // stale, already-removed menu item tries to refocus later.
                  // Proven by the ConfirmDialog case's observable outcome
                  // (focus ends on the trigger, not the document body), not
                  // by counting ticks (ADR-0150).
                  const onSelect = entry.onSelect;
                  setTimeout(() => setTimeout(() => onSelect(), 0), 0);
                }}
                className={clsx(
                  'cursor-default select-none px-3 py-2 text-sm rounded-sm outline-none',
                  'data-[highlighted]:bg-accent-subtle',
                  'data-[disabled]:text-fg-disabled',
                  entry.variant === 'danger' && 'text-accent-fault',
                )}
              >
                {entry.label}
              </RadixDropdownMenu.Item>
            ),
          )}
        </RadixDropdownMenu.Content>
      </RadixDropdownMenu.Portal>
    </RadixDropdownMenu.Root>
  );
}
