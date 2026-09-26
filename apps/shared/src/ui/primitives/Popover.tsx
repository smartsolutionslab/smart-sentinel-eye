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
 * Spec 266 (issue #2335) phase 4a signature-only stub — plan.md §3.3 and §6.
 *
 * Renders nothing and imports no Radix package, so `Popover.test.tsx` fails
 * on content, not on a missing module (ADR-0139/0144), and the US5
 * dependency guard stays red until the real component lands.
 */
export function Popover(_props: PopoverProps) {
  return null;
}
