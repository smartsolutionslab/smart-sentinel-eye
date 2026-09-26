// @vitest-environment jsdom
import { useState } from 'react';
import { cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { CommandPalette, type CommandPaletteItem } from './CommandPalette.js';

/**
 * Spec 266 (issue #2335) US6 — phase 4a is BEHAVIOUR-CHANGING (ADR-0139/0144):
 * every case below must be observed RED before `CommandPalette.tsx` is a real
 * implementation.
 *
 * Committed against the signature-only stub (renders `null`, no Radix
 * import — plan.md §6), so every case fails on content ("unable to find role
 * dialog" / "unable to find role combobox"), never on a missing module. This
 * stub's absence of a `@radix-ui/react-dialog` import does not affect the
 * US5 dependency guard either way (`Dialog.tsx` already imports it).
 */
afterEach(cleanup);

const ITEMS: readonly CommandPaletteItem[] = [
  { value: '/cameras', label: 'Cameras' },
  { value: '/layouts', label: 'Layouts' },
  { value: '/walls', label: 'Walls' },
  { value: '/overlays', label: 'Overlays' },
  { value: '/rules', label: 'Rules' },
  { value: '/system-variables', label: 'System variables' },
  { value: '/audit', label: 'Audit' },
];

function Harness({
  items = ITEMS,
  onSelect = vi.fn(),
}: {
  items?: readonly CommandPaletteItem[];
  onSelect?: (value: string) => void;
}) {
  const [open, setOpen] = useState(false);
  return (
    <>
      <button type="button" onClick={() => setOpen(true)}>
        Go to…
      </button>
      <CommandPalette
        open={open}
        onOpenChange={setOpen}
        items={items}
        onSelect={onSelect}
        label="Go to"
        placeholder="Go to a surface…"
        emptyText="No matching surfaces"
      />
    </>
  );
}

async function openPalette(user: ReturnType<typeof userEvent.setup>) {
  const trigger = screen.getByRole('button', { name: /go to…/i });
  await user.click(trigger);
  await screen.findByRole('dialog');
  return trigger;
}

describe('CommandPalette', () => {
  it('Renders a dialog named by label with focus on the search combobox', async () => {
    const user = userEvent.setup();
    render(<Harness />);

    await openPalette(user);

    const dialog = screen.getByRole('dialog', { name: 'Go to' });
    expect(within(dialog).getByRole('combobox')).toHaveFocus();
  });

  it('Lists every item, with the first highlighted and referenced by aria-activedescendant', async () => {
    const user = userEvent.setup();
    render(<Harness />);

    await openPalette(user);

    const listbox = screen.getByRole('listbox');
    const options = within(listbox).getAllByRole('option');
    expect(options.map((option) => option.textContent)).toEqual(ITEMS.map((item) => item.label));

    const first = options[0]!;
    expect(first).toHaveAttribute('aria-selected', 'true');
    expect(first).toHaveAttribute('data-highlighted');

    const combobox = screen.getByRole('combobox');
    expect(combobox).toHaveAttribute('aria-activedescendant', first.id);
  });

  it('Typing filters case-insensitively and highlights the first match', async () => {
    const user = userEvent.setup();
    render(<Harness />);

    await openPalette(user);
    await user.type(screen.getByRole('combobox'), 'RUL');

    const options = within(screen.getByRole('listbox')).getAllByRole('option');
    expect(options).toHaveLength(1);
    expect(options[0]).toHaveTextContent('Rules');
    expect(options[0]).toHaveAttribute('aria-selected', 'true');
  });

  it('ArrowDown/ArrowUp move the highlight and clamp at both ends', async () => {
    const user = userEvent.setup();
    render(<Harness />);

    await openPalette(user);
    screen.getByRole('combobox').focus();

    await user.keyboard('{ArrowDown}{ArrowDown}');
    expect(screen.getAllByRole('option')[2]).toHaveAttribute('aria-selected', 'true'); // Walls

    await user.keyboard('{ArrowUp}');
    expect(screen.getAllByRole('option')[1]).toHaveAttribute('aria-selected', 'true'); // Layouts

    await user.keyboard('{ArrowUp}{ArrowUp}{ArrowUp}{ArrowUp}{ArrowUp}');
    expect(screen.getAllByRole('option')[0]).toHaveAttribute('aria-selected', 'true'); // clamped at top

    await user.keyboard('{ArrowDown}'.repeat(ITEMS.length + 3));
    expect(screen.getAllByRole('option')[ITEMS.length - 1]).toHaveAttribute('aria-selected', 'true'); // clamped at bottom
  });

  it('Enter calls onOpenChange(false) and then onSelect with the highlighted value, exactly once', async () => {
    const user = userEvent.setup();
    const onSelect = vi.fn();
    const openChanges: boolean[] = [];

    function Wrapped() {
      const [open, setOpen] = useState(false);
      return (
        <>
          <button
            type="button"
            onClick={() => {
              openChanges.push(true);
              setOpen(true);
            }}
          >
            Go to…
          </button>
          <CommandPalette
            open={open}
            onOpenChange={(next) => {
              openChanges.push(next);
              setOpen(next);
            }}
            items={ITEMS}
            onSelect={onSelect}
            label="Go to"
            placeholder="Go to a surface…"
            emptyText="No matching surfaces"
          />
        </>
      );
    }

    render(<Wrapped />);
    await openPalette(user);
    await user.type(screen.getByRole('combobox'), 'rul');
    await user.keyboard('{Enter}');

    expect(onSelect).toHaveBeenCalledTimes(1);
    expect(onSelect).toHaveBeenCalledWith('/rules');
    expect(openChanges.at(-1)).toBe(false);
  });

  it('Enter with no matches calls neither callback, and shows emptyText as a status', async () => {
    const user = userEvent.setup();
    const onSelect = vi.fn();
    render(<Harness onSelect={onSelect} />);

    await openPalette(user);
    await user.type(screen.getByRole('combobox'), 'zzz');

    expect(screen.queryAllByRole('option')).toHaveLength(0);
    expect(screen.getByRole('status')).toHaveTextContent('No matching surfaces');

    await user.keyboard('{Enter}');

    expect(onSelect).not.toHaveBeenCalled();
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });

  it('Clicking an option calls onSelect with its value', async () => {
    const user = userEvent.setup();
    const onSelect = vi.fn();
    render(<Harness onSelect={onSelect} />);

    await openPalette(user);
    await user.click(screen.getByRole('option', { name: 'Audit' }));

    expect(onSelect).toHaveBeenCalledWith('/audit');
  });

  it('Escape closes without calling onSelect, and returns focus to the element focused before opening', async () => {
    const user = userEvent.setup();
    const onSelect = vi.fn();
    render(<Harness onSelect={onSelect} />);

    const trigger = screen.getByRole('button', { name: /go to…/i });
    trigger.focus();
    const openedTrigger = await openPalette(user);

    await user.keyboard('{Escape}');

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(onSelect).not.toHaveBeenCalled();
    expect(openedTrigger).toHaveFocus();
  });

  it('Reopening shows an empty query with the first option highlighted', async () => {
    const user = userEvent.setup();
    render(<Harness />);

    const trigger = await openPalette(user);
    await user.type(screen.getByRole('combobox'), 'rul');
    await user.keyboard('{Escape}');

    await user.click(trigger);

    const combobox = await screen.findByRole('combobox');
    expect(combobox).toHaveValue('');
    expect(screen.getAllByRole('option')).toHaveLength(ITEMS.length);
    expect(screen.getAllByRole('option')[0]).toHaveAttribute('aria-selected', 'true');
  });

  it('Throws naming a duplicate item value', () => {
    const duplicate: readonly CommandPaletteItem[] = [
      { value: '/rules', label: 'Rules' },
      { value: '/rules', label: 'Rules again' },
    ];

    expect(() =>
      render(
        <CommandPalette
          open
          onOpenChange={vi.fn()}
          items={duplicate}
          onSelect={vi.fn()}
          label="Go to"
          placeholder="Go to a surface…"
          emptyText="No matching surfaces"
        />,
      ),
    ).toThrow(/\/rules/);
  });
});
