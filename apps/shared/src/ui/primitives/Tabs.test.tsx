// @vitest-environment jsdom
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it } from 'vitest';
import { Tabs, type TabDefinition } from './Tabs.js';

/**
 * Spec 266 (issue #2335) US4 — phase 4a is BEHAVIOUR-CHANGING (ADR-0139/0144):
 * every case below must be observed RED before `Tabs.tsx` is a real Radix
 * wrapper. No consumer exists yet (Q3 default: library-only).
 *
 * Committed against the signature-only stub (renders `null`, no Radix
 * import — plan.md §6), so every case fails on content ("unable to find role
 * tablist"), never on a missing module.
 */
afterEach(cleanup);

const TABS: readonly TabDefinition[] = [
  { value: 'details', label: 'Details', content: <p>Details panel</p> },
  { value: 'history', label: 'History', content: <p>History panel</p> },
];

describe('Tabs', () => {
  it('Names the tablist with the label prop', () => {
    render(<Tabs tabs={TABS} label="Camera detail" />);

    expect(screen.getByRole('tablist', { name: 'Camera detail' })).toBeInTheDocument();
  });

  it('ArrowRight moves focus without activating the tab, manual activation being the default', async () => {
    const user = userEvent.setup();
    render(<Tabs tabs={TABS} label="Camera detail" />);

    screen.getByRole('tab', { name: 'Details' }).focus();
    await user.keyboard('{ArrowRight}');

    expect(screen.getByRole('tab', { name: 'History' })).toHaveFocus();
    expect(screen.getByText('Details panel')).toBeInTheDocument();
    expect(screen.queryByText('History panel')).not.toBeInTheDocument();
  });

  it('Enter activates the focused tab and shows its panel', async () => {
    const user = userEvent.setup();
    render(<Tabs tabs={TABS} label="Camera detail" />);

    screen.getByRole('tab', { name: 'Details' }).focus();
    await user.keyboard('{ArrowRight}{Enter}');

    expect(screen.getByRole('tab', { name: 'History' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByText('History panel')).toBeInTheDocument();
  });

  it('activationMode="automatic" activates on the arrow key alone', async () => {
    const user = userEvent.setup();
    render(<Tabs tabs={TABS} label="Camera detail" activationMode="automatic" />);

    screen.getByRole('tab', { name: 'Details' }).focus();
    await user.keyboard('{ArrowRight}');

    expect(screen.getByRole('tab', { name: 'History' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByText('History panel')).toBeInTheDocument();
  });

  it('Skips a disabled tab when moving focus', async () => {
    const user = userEvent.setup();
    const tabs: readonly TabDefinition[] = [
      { value: 'details', label: 'Details', content: <p>Details panel</p> },
      { value: 'history', label: 'History', content: <p>History panel</p>, disabled: true },
      { value: 'raw', label: 'Raw', content: <p>Raw panel</p> },
    ];
    render(<Tabs tabs={tabs} label="Camera detail" />);

    screen.getByRole('tab', { name: 'Details' }).focus();
    await user.keyboard('{ArrowRight}');

    expect(screen.getByRole('tab', { name: 'Raw' })).toHaveFocus();
  });

  it('Throws naming a duplicate tab value', () => {
    const duplicate: readonly TabDefinition[] = [
      { value: 'details', label: 'Details', content: <p>A</p> },
      { value: 'details', label: 'Details again', content: <p>B</p> },
    ];

    expect(() => render(<Tabs tabs={duplicate} label="Camera detail" />)).toThrow(/details/);
  });
});
