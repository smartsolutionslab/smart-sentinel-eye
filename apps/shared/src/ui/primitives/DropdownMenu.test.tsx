// @vitest-environment jsdom
import { useState } from 'react';
import { cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { DropdownMenu, type MenuEntry } from './DropdownMenu.js';
import { ConfirmDialog } from './ConfirmDialog.js';

/**
 * Spec 266 (issue #2335) US2 — phase 4a is BEHAVIOUR-CHANGING (ADR-0139/0144):
 * every case below must be observed RED before `DropdownMenu.tsx` is a real
 * Radix wrapper.
 *
 * Committed against the signature-only stub (renders `null`, no Radix
 * import — plan.md §6), so every case fails on content ("unable to find role
 * menu"), never on a missing module.
 */
afterEach(cleanup);

function renderMenu(entries: readonly MenuEntry[]) {
  render(<DropdownMenu trigger={<button type="button">More actions</button>} entries={entries} />);
  return screen.getByRole('button', { name: /more actions/i });
}

describe('DropdownMenu', () => {
  it('Enter on the trigger opens the menu and focuses the first enabled item', async () => {
    const user = userEvent.setup();
    const trigger = renderMenu([
      { kind: 'item', label: 'Publish', onSelect: vi.fn() },
      { kind: 'item', label: 'Archive', onSelect: vi.fn() },
    ]);

    trigger.focus();
    await user.keyboard('{Enter}');

    const menu = await screen.findByRole('menu');
    expect(within(menu).getByRole('menuitem', { name: 'Publish' })).toHaveFocus();
  });

  it('ArrowDown moves focus to the next item', async () => {
    const user = userEvent.setup();
    const trigger = renderMenu([
      { kind: 'item', label: 'Publish', onSelect: vi.fn() },
      { kind: 'item', label: 'Archive', onSelect: vi.fn() },
    ]);

    trigger.focus();
    await user.keyboard('{Enter}');
    await screen.findByRole('menu');
    await user.keyboard('{ArrowDown}');

    expect(screen.getByRole('menuitem', { name: 'Archive' })).toHaveFocus();
  });

  it('Escape closes the menu and returns focus to the trigger', async () => {
    const user = userEvent.setup();
    const trigger = renderMenu([{ kind: 'item', label: 'Publish', onSelect: vi.fn() }]);

    trigger.focus();
    await user.keyboard('{Enter}');
    await screen.findByRole('menu');
    await user.keyboard('{Escape}');

    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
    expect(trigger).toHaveFocus();
  });

  it('Choosing an item calls its onSelect exactly once', async () => {
    const user = userEvent.setup();
    const onSelect = vi.fn();
    const trigger = renderMenu([{ kind: 'item', label: 'Publish', onSelect }]);

    await user.click(trigger);
    await user.click(await screen.findByRole('menuitem', { name: 'Publish' }));

    expect(onSelect).toHaveBeenCalledTimes(1);
  });

  it('A disabled item carries aria-disabled and does not fire onSelect', async () => {
    const user = userEvent.setup();
    const onSelect = vi.fn();
    const trigger = renderMenu([{ kind: 'item', label: 'Archive', onSelect, disabled: true }]);

    await user.click(trigger);
    const item = await screen.findByRole('menuitem', { name: 'Archive' });

    expect(item).toHaveAttribute('aria-disabled', 'true');

    await user.click(item);
    expect(onSelect).not.toHaveBeenCalled();
  });

  it('Renders a separator entry with role separator', async () => {
    const user = userEvent.setup();
    const trigger = renderMenu([
      { kind: 'item', label: 'Publish', onSelect: vi.fn() },
      { kind: 'separator' },
      { kind: 'item', label: 'Archive', onSelect: vi.fn() },
    ]);

    await user.click(trigger);

    expect(await screen.findByRole('separator')).toBeInTheDocument();
  });

  /**
   * The load-bearing case (plan.md §3.2, spec US2 scenario 2): a menu item
   * opening a ConfirmDialog must not leave `pointer-events: none` on
   * `<body>` or race the two focus scopes. `Root modal={false}` is what this
   * holds — a modal Radix menu closing while the dialog opens from its
   * `onSelect` is exactly the defect this pins against.
   */
  function ArchiveMenuHarness() {
    const [confirmOpen, setConfirmOpen] = useState(false);
    return (
      <>
        <DropdownMenu
          trigger={<button type="button">More actions</button>}
          entries={[{ kind: 'item', label: 'Archive', onSelect: () => setConfirmOpen(true), variant: 'danger' }]}
        />
        <ConfirmDialog
          open={confirmOpen}
          onOpenChange={setConfirmOpen}
          title="Archive this layout?"
          confirmLabel="Archive"
          onConfirm={() => setConfirmOpen(false)}
        >
          <p>This takes the layout out of service.</p>
        </ConfirmDialog>
      </>
    );
  }

  it('An item that opens a ConfirmDialog focuses Cancel, and Escape returns focus to the trigger with pointer events restored', async () => {
    const user = userEvent.setup();
    render(<ArchiveMenuHarness />);

    const trigger = screen.getByRole('button', { name: /more actions/i });
    await user.click(trigger);
    await user.click(await screen.findByRole('menuitem', { name: 'Archive' }));

    const dialog = await screen.findByRole('alertdialog');
    expect(within(dialog).getByRole('button', { name: /cancel/i })).toHaveFocus();

    await user.keyboard('{Escape}');

    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(trigger).toHaveFocus();
    expect(document.body.style.pointerEvents).not.toBe('none');
  });
});
