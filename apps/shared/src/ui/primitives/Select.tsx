import * as RadixSelect from '@radix-ui/react-select';
import clsx from 'clsx';
import type { Ref } from 'react';

export interface SelectOption {
  /** Non-empty; "" is the placeholder's (spec 266 §1 finding 3). */
  value: string;
  label: string;
  disabled?: boolean;
}

export interface SelectProps {
  /** FormField's `htmlFor` targets the trigger. */
  id: string;
  /** `undefined` or `""` shows the placeholder. */
  value: string | undefined;
  onValueChange: (value: string) => void;
  options: readonly SelectOption[];
  placeholder?: string;
  disabled?: boolean;
  /** Renders Radix's hidden native select for form posts. */
  name?: string;
  /** RHF Controller's `field.onBlur`. */
  onBlur?: () => void;
  'aria-invalid'?: boolean;
  'aria-describedby'?: string;
  /** RHF focuses the trigger on a validation error. */
  ref?: Ref<HTMLButtonElement>;
}

/**
 * Design-system listbox on `@radix-ui/react-select` (spec 266 / issue #2335
 * US1, plan.md §3.1). Props-object style — a caller never imports
 * `@radix-ui/react-select` directly.
 */
export function Select({
  id,
  value,
  onValueChange,
  options,
  placeholder,
  disabled,
  name,
  onBlur,
  'aria-invalid': ariaInvalid,
  'aria-describedby': ariaDescribedby,
  ref,
}: SelectProps) {
  // Guard at the boundary (spec US1 "bad request"): a programming error, not
  // input to tolerate. "Nothing chosen" is the placeholder's job, never an
  // option's — Radix's own `shouldShowPlaceholder` already treats "" as
  // "no value" (spec 266 §1 finding 3), so an option with that value could
  // never be selected anyway.
  for (const option of options) {
    if (option.value === '') {
      throw new Error(`Select option "${option.label}" has an empty value; use placeholder for "nothing chosen"`);
    }
  }

  return (
    <RadixSelect.Root value={value} onValueChange={onValueChange} disabled={disabled} name={name}>
      <RadixSelect.Trigger
        id={id}
        ref={ref}
        onBlur={onBlur}
        aria-invalid={ariaInvalid}
        aria-describedby={ariaDescribedby}
        className={clsx(
          'inline-flex w-full items-center justify-between gap-2 rounded-md border border-border-strong',
          'bg-bg-elevated px-3 py-2 text-sm text-fg-primary',
          'data-[placeholder]:text-fg-muted',
          'data-[state=open]:border-accent',
          'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-focus-ring',
          'disabled:text-fg-disabled disabled:border-border-subtle',
        )}
      >
        <RadixSelect.Value placeholder={placeholder} />
        {/* Plain ASCII, not a triangle glyph (▾ U+25BE): outside IBM Plex
            Sans's covered range (spec 261's font-coverage guard) — #2336
            owns visual polish for every primitive here, this one included. */}
        <span aria-hidden="true">v</span>
      </RadixSelect.Trigger>
      <RadixSelect.Portal>
        <RadixSelect.Content
          position="popper"
          sideOffset={4}
          className="z-popover rounded-md border border-border-subtle bg-bg-raised text-fg-primary shadow-popover"
        >
          <RadixSelect.Viewport>
            {options.map((option) => (
              <RadixSelect.Item
                key={option.value}
                value={option.value}
                disabled={option.disabled}
                className={clsx(
                  'px-3 py-2 text-sm rounded-sm outline-none',
                  'data-[highlighted]:bg-accent-subtle',
                  'data-[disabled]:text-fg-disabled',
                )}
              >
                <RadixSelect.ItemText>{option.label}</RadixSelect.ItemText>
                <RadixSelect.ItemIndicator className="text-accent" aria-hidden="true">
                  {' '}
                  ✓
                </RadixSelect.ItemIndicator>
              </RadixSelect.Item>
            ))}
          </RadixSelect.Viewport>
        </RadixSelect.Content>
      </RadixSelect.Portal>
    </RadixSelect.Root>
  );
}
