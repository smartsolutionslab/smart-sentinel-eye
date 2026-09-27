import * as RadixDropdownMenu from '@radix-ui/react-dropdown-menu';
import clsx from 'clsx';
import { useRef, type ComponentRef, type ReactNode } from 'react';

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
  const triggerRef = useRef<ComponentRef<typeof RadixDropdownMenu.Trigger>>(null);

  return (
    // `modal={false}` is load-bearing (plan.md §3.2): a modal menu that closes
    // while a Radix dialog opens from its `onSelect` leaves
    // `pointer-events: none` on `<body>` and races the two focus scopes.
    <RadixDropdownMenu.Root modal={false}>
      <RadixDropdownMenu.Trigger ref={triggerRef} asChild>
        {trigger}
      </RadixDropdownMenu.Trigger>
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
                  // Focus the trigger BEFORE calling the caller's handler,
                  // synchronously — not deferred. A dialog the handler opens
                  // (e.g. a ConfirmDialog) captures "whatever had focus" the
                  // moment it mounts; without this, that is transiently the
                  // menu item itself (Presence unmounts the closing menu's
                  // content on a LATER render pass, and React batches this
                  // item's own state update with the menu's close into the
                  // SAME commit), which the dialog then tries to refocus
                  // after it closes — a detached node a browser silently
                  // refuses to focus, dropping focus to `<body>` (plan.md
                  // §3.2's whole reason for existing). Forcing it here makes
                  // the *trigger* the pre-open element instead, with no
                  // reliance on how many ticks anything else takes.
                  triggerRef.current?.focus();
                  entry.onSelect();
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
