import type { ReactNode } from 'react';

export interface TabDefinition {
  value: string;
  label: string;
  content: ReactNode;
  disabled?: boolean;
}

export interface TabsProps {
  tabs: readonly TabDefinition[];
  /** `aria-label` on the tab list. */
  label: string;
  /** Controlled. */
  value?: string;
  /** Uncontrolled; defaults to the first enabled tab. */
  defaultValue?: string;
  onValueChange?: (value: string) => void;
  /** Default `'manual'` (spec 266 US4). */
  activationMode?: 'manual' | 'automatic';
}

/**
 * Spec 266 (issue #2335) phase 4a signature-only stub — plan.md §3.4 and §6.
 *
 * Renders nothing and imports no Radix package, so `Tabs.test.tsx` fails on
 * content, not on a missing module (ADR-0139/0144), and the US5 dependency
 * guard stays red until the real component lands.
 */
export function Tabs(_props: TabsProps) {
  return null;
}
