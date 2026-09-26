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
 * Spec 266 (issue #2335) phase 4a signature-only stub — plan.md §3.2 and §6.
 *
 * Renders nothing and imports no Radix package, so `DropdownMenu.test.tsx`
 * fails on content, not on a missing module (ADR-0139/0144), and the US5
 * dependency guard stays red until the real component lands.
 */
export function DropdownMenu(_props: DropdownMenuProps) {
  return null;
}
