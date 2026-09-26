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
 * Spec 266 (issue #2335) phase 4a signature-only stub — plan.md §3.1 and §6.
 *
 * This is deliberately **not** an implementation: it renders nothing and
 * imports no Radix package, so `Select.test.tsx` fails on content ("unable to
 * find role combobox") rather than on a missing module, and the US5
 * dependency guard (`SharedUiDependencyUsageTests`) stays red until the real
 * component lands. `tsc --noEmit` stays green against this exact prop shape
 * so the phase-4a commit still builds on its own (ADR-0087).
 */
export function Select(_props: SelectProps) {
  return null;
}
