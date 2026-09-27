import * as RadixDialog from '@radix-ui/react-dialog';
import { useId, useLayoutEffect, useRef, useState } from 'react';

export interface CommandPaletteItem {
  /** Unique; handed back to `onSelect`. */
  value: string;
  /** Shown, and matched by the filter. */
  label: string;
}

export interface CommandPaletteProps {
  /** Controlled — the consumer owns the chord and the trigger. */
  open: boolean;
  onOpenChange: (open: boolean) => void;
  items: readonly CommandPaletteItem[];
  /** Called after the dialog has closed. */
  onSelect: (value: string) => void;
  /** Dialog title (sr-only) and the listbox's accessible name. */
  label: string;
  /** The search field's placeholder. */
  placeholder: string;
  /** Shown when the filter matches nothing. */
  emptyText: string;
}

/**
 * A filtered command palette on `@radix-ui/react-dialog` (spec 266 / issue
 * #2335 US6, plan.md §3.5). The modal layer (focus trap, Escape,
 * outside-click, focus restoration, portal) is Radix's; the listbox
 * (`role="combobox"` input + `aria-activedescendant`) is hand-written —
 * Radix has no combobox primitive.
 */
export function CommandPalette({
  open,
  onOpenChange,
  items,
  onSelect,
  label,
  placeholder,
  emptyText,
}: CommandPaletteProps) {
  assertUniqueValues(items);

  // Which value (if any) Enter/click chose, read back by `onCloseAutoFocus`
  // once the dialog has actually closed (plan.md §3.5) — never called
  // inline, or `onSelect` would fire while the palette is still mid-close.
  const pendingValueRef = useRef<string | null>(null);

  // This component has no `RadixDialog.Trigger` (the consumer owns the
  // chord and its own trigger), so `context.triggerRef` — what Radix's own
  // `onCloseAutoFocus` falls back to — is always null and its default
  // restoration is a silent no-op, dropping focus to `<body>` on
  // Escape/outside-dismiss. A layout effect captures the pre-open element
  // instead: React runs every layout effect, however deep, before any
  // component's passive effect — so this always beats Radix's own
  // mount-autofocus moving focus into the search field.
  const previouslyFocusedRef = useRef<HTMLElement | null>(null);
  useLayoutEffect(() => {
    if (open) {
      previouslyFocusedRef.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    }
  }, [open]);

  return (
    <RadixDialog.Root open={open} onOpenChange={onOpenChange}>
      <RadixDialog.Portal>
        <RadixDialog.Overlay className="fixed inset-0 z-overlay bg-scrim" />
        <RadixDialog.Content
          aria-describedby={undefined}
          className={
            'fixed left-1/2 top-[15vh] z-overlay w-full max-w-lg -translate-x-1/2 rounded-lg border ' +
            'border-border-subtle bg-bg-raised text-fg-primary shadow-overlay'
          }
          onCloseAutoFocus={(event) => {
            // Always ours: Radix's own default falls back to a
            // `RadixDialog.Trigger` this component never renders (see
            // `previouslyFocusedRef` above), so it is always prevented here.
            event.preventDefault();
            const value = pendingValueRef.current;
            if (value !== null) {
              // A value was chosen: focus is the caller's to decide (plan.md
              // §4.4 focuses the destination link), not restored here.
              pendingValueRef.current = null;
              onSelect(value);
            } else {
              // Nothing pending (Escape, outside click): restore focus to
              // whatever opened the palette.
              previouslyFocusedRef.current?.focus();
            }
          }}
        >
          <RadixDialog.Title className="sr-only">{label}</RadixDialog.Title>
          {/*
            No reset effect: Radix unmounts Content on close (no
            forceMount), so PaletteBody is a fresh instance — query and
            highlight — on every open (spec US6 "reopening shows an empty
            query").
          */}
          <PaletteBody
            items={items}
            label={label}
            placeholder={placeholder}
            emptyText={emptyText}
            onActivate={(value) => {
              pendingValueRef.current = value;
              onOpenChange(false);
            }}
          />
        </RadixDialog.Content>
      </RadixDialog.Portal>
    </RadixDialog.Root>
  );
}

function assertUniqueValues(items: readonly CommandPaletteItem[]): void {
  const seen = new Set<string>();
  for (const item of items) {
    if (seen.has(item.value)) {
      throw new Error(`Duplicate command palette item value "${item.value}"`);
    }
    seen.add(item.value);
  }
}

interface PaletteBodyProps {
  items: readonly CommandPaletteItem[];
  label: string;
  placeholder: string;
  emptyText: string;
  onActivate: (value: string) => void;
}

function PaletteBody({ items, label, placeholder, emptyText, onActivate }: PaletteBodyProps) {
  const [query, setQuery] = useState('');
  const [highlightedIndex, setHighlightedIndex] = useState(0);
  const listboxId = useId();

  const matches = items.filter((item) => item.label.toLocaleLowerCase().includes(query.trim().toLocaleLowerCase()));
  const highlighted = matches[highlightedIndex];

  return (
    <>
      <input
        role="combobox"
        aria-expanded="true"
        aria-controls={listboxId}
        aria-autocomplete="list"
        aria-activedescendant={highlighted === undefined ? undefined : optionId(listboxId, highlighted.value)}
        value={query}
        placeholder={placeholder}
        className={
          'w-full border-b border-border-subtle bg-transparent px-4 py-3 text-sm text-fg-primary ' +
          'placeholder:text-fg-muted outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-focus-ring'
        }
        onChange={(event) => {
          setQuery(event.target.value);
          setHighlightedIndex(0);
        }}
        onKeyDown={(event) => {
          if (event.key === 'ArrowDown') {
            event.preventDefault();
            setHighlightedIndex((index) => Math.min(index + 1, Math.max(matches.length - 1, 0)));
          } else if (event.key === 'ArrowUp') {
            event.preventDefault();
            setHighlightedIndex((index) => Math.max(index - 1, 0));
          } else if (event.key === 'Enter') {
            event.preventDefault();
            if (highlighted !== undefined) {
              onActivate(highlighted.value);
            }
          }
        }}
      />
      {matches.length === 0 ? (
        <p role="status" className="px-4 py-3 text-sm text-fg-muted">
          {emptyText}
        </p>
      ) : (
        <ul id={listboxId} role="listbox" aria-label={label} className="max-h-80 overflow-y-auto p-1">
          {matches.map((item, index) => {
            const isHighlighted = index === highlightedIndex;
            return (
              <li
                key={item.value}
                id={optionId(listboxId, item.value)}
                role="option"
                aria-selected={isHighlighted}
                data-highlighted={isHighlighted ? '' : undefined}
                onPointerMove={() => setHighlightedIndex(index)}
                onClick={() => onActivate(item.value)}
                className="rounded-sm px-3 py-2 text-sm data-[highlighted]:bg-accent-subtle"
              >
                {item.label}
              </li>
            );
          })}
        </ul>
      )}
    </>
  );
}

function optionId(listboxId: string, value: string): string {
  return `${listboxId}-${value.replace(/[^a-zA-Z0-9_-]/g, '-')}`;
}
