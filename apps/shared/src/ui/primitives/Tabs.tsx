import * as RadixTabs from '@radix-ui/react-tabs';
import clsx from 'clsx';
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
 * Design-system tabs on `@radix-ui/react-tabs` (spec 266 / issue #2335 US4,
 * plan.md §3.4). Library-only today — no consumer yet (Q3 default).
 */
export function Tabs({ tabs, label, value, defaultValue, onValueChange, activationMode = 'manual' }: TabsProps) {
  // Guard at the boundary: a duplicate value is a programming error, in
  // every build.
  const seen = new Set<string>();
  for (const tab of tabs) {
    if (seen.has(tab.value)) {
      throw new Error(`Duplicate tab value "${tab.value}"`);
    }
    seen.add(tab.value);
  }

  const firstEnabled = tabs.find((tab) => !tab.disabled)?.value;

  return (
    <RadixTabs.Root
      value={value}
      defaultValue={defaultValue ?? firstEnabled}
      onValueChange={onValueChange}
      activationMode={activationMode}
    >
      <RadixTabs.List aria-label={label} className="flex gap-1 border-b border-border-subtle">
        {tabs.map((tab) => (
          <RadixTabs.Trigger
            key={tab.value}
            value={tab.value}
            disabled={tab.disabled}
            className={clsx(
              'px-3 py-2 text-sm text-fg-muted border-b-2 border-transparent',
              'data-[state=active]:text-fg-primary data-[state=active]:border-accent',
              'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-focus-ring',
              'data-[disabled]:text-fg-disabled',
            )}
          >
            {tab.label}
          </RadixTabs.Trigger>
        ))}
      </RadixTabs.List>
      {tabs.map((tab) => (
        <RadixTabs.Content key={tab.value} value={tab.value}>
          {tab.content}
        </RadixTabs.Content>
      ))}
    </RadixTabs.Root>
  );
}
