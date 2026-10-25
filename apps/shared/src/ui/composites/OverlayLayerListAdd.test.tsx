// @vitest-environment jsdom
import { useState } from 'react';
import { cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { OverlayElement } from '../../api/overlays.api.js';
import { OverlayLayerList } from './OverlayLayerList.js';

/**
 * Spec 457 (issue #3107) C3. New behaviour, RED (ADR-0139): the layer list's "Add"
 * menu replaces the single "Add label" button. Owner ruling 2026-10-23: a menu of
 * Text / Box / Ellipse, and a decorative kind icon in every row.
 */

afterEach(cleanup);

function text(value: string): OverlayElement {
  return {
    kind: 'Text',
    color: '#FFFFFFD9',
    text: value,
    normalizedX: 0.1,
    normalizedY: 0.1,
    normalizedWidth: 0.3,
    normalizedHeight: 0.1,
    fontSizePx: 32,
  };
}

function shape(kind: 'Box' | 'Ellipse'): OverlayElement {
  return {
    kind,
    color: '#FF0000FF',
    normalizedX: 0.5,
    normalizedY: 0.5,
    normalizedWidth: 0.2,
    normalizedHeight: 0.2,
  };
}

function elementOf(kind: OverlayElement['kind']): OverlayElement {
  return kind === 'Text' ? text('New label') : shape(kind);
}

function renderStatic(
  elements: OverlayElement[],
  options: { canAdd?: boolean; readOnly?: boolean; onAdd?: (kind: OverlayElement['kind']) => void } = {},
) {
  const onAdd = options.onAdd ?? vi.fn();
  render(
    <OverlayLayerList
      elements={elements}
      rowIds={elements.map((_, index) => `row-${index}`)}
      selectedId="row-0"
      canAdd={options.canAdd ?? true}
      canRemove
      readOnly={options.readOnly}
      onSelect={vi.fn()}
      onMove={vi.fn()}
      onRemove={vi.fn()}
      onAdd={onAdd}
    />,
  );
  return onAdd;
}

/** A host that adds the chosen kind at the front and selects it, as the form does. */
function Host({ initial }: { initial: string[] }) {
  const [rows, setRows] = useState(() => initial.map((value, index) => ({ id: `row-${index}`, element: text(value) })));
  const [selectedId, setSelectedId] = useState('row-0');
  const [serial, setSerial] = useState(0);
  return (
    <OverlayLayerList
      elements={rows.map((row) => row.element)}
      rowIds={rows.map((row) => row.id)}
      selectedId={selectedId}
      canAdd
      canRemove={rows.length > 1}
      onSelect={setSelectedId}
      onMove={vi.fn()}
      onRemove={vi.fn()}
      onAdd={(kind) => {
        const id = `added-${serial}`;
        setSerial(serial + 1);
        setRows((current) => [...current, { id, element: elementOf(kind) }]);
        setSelectedId(id);
      }}
    />
  );
}

async function openAddMenu(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole('button', { name: 'Add' }));
  return screen.findByRole('menu');
}

function liveRegion(): HTMLElement {
  return screen.getByTestId('overlay-layer-list-live-region');
}

describe('OverlayLayerList Add menu', () => {
  it('Offers exactly Text, Box and Ellipse, in that order', async () => {
    const user = userEvent.setup();
    renderStatic([text('A')]);
    const menu = await openAddMenu(user);
    expect(
      within(menu)
        .getAllByRole('menuitem')
        .map((item) => item.textContent),
    ).toEqual(['Text', 'Box', 'Ellipse']);
  });

  it('Has no "Add label" button any more', () => {
    renderStatic([text('A')]);
    expect(screen.queryByRole('button', { name: 'Add label' })).not.toBeInTheDocument();
  });

  it.each(['Text', 'Box', 'Ellipse'] as const)('Choosing %s calls onAdd with that kind exactly once', async (kind) => {
    const user = userEvent.setup();
    const onAdd = renderStatic([text('A')]);
    const menu = await openAddMenu(user);
    await user.click(within(menu).getByRole('menuitem', { name: kind }));
    expect(onAdd).toHaveBeenCalledExactlyOnceWith(kind);
  });

  it('Opens no menu and adds nothing from an unavailable trigger at the cap, saying why', async () => {
    const user = userEvent.setup();
    const onAdd = renderStatic([text('A')], { canAdd: false });
    const add = screen.getByRole('button', { name: 'Add' });
    expect(add).toHaveAttribute('aria-disabled', 'true');
    expect(screen.getByText('Up to 8 elements')).toBeInTheDocument();

    await user.click(add);
    add.focus();
    await user.keyboard('{Enter}');
    await user.keyboard('{ArrowDown}');

    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
    expect(onAdd).not.toHaveBeenCalled();
  });

  it('Opens no menu and adds nothing from an unavailable trigger while read-only', async () => {
    const user = userEvent.setup();
    const onAdd = renderStatic([text('A')], { readOnly: true });
    const add = screen.getByRole('button', { name: 'Add' });
    expect(add).toHaveAttribute('aria-disabled', 'true');

    await user.click(add);
    add.focus();
    await user.keyboard('{Enter}');

    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
    expect(onAdd).not.toHaveBeenCalled();
  });

  it('Does not show the limit text while more elements may be added', () => {
    renderStatic([text('A')]);
    expect(screen.queryByText('Up to 8 elements')).not.toBeInTheDocument();
  });

  it.each([
    ['Text', 'New label', 'Label added, position 1 of 3'],
    ['Box', 'Box 1', 'Box added, position 1 of 3'],
    ['Ellipse', 'Ellipse 1', 'Ellipse added, position 1 of 3'],
  ] as const)('Adding a %s focuses its chip and announces it', async (kind, chipName, sentence) => {
    const user = userEvent.setup();
    render(<Host initial={['A', 'B']} />);
    const menu = await openAddMenu(user);
    await user.click(within(menu).getByRole('menuitem', { name: kind }));

    expect(liveRegion()).toHaveTextContent(sentence);
    expect(document.activeElement).toBe(screen.getByRole('button', { name: chipName, pressed: true }));
  });
});

describe('OverlayLayerList kind icons', () => {
  it('Shows one decorative icon per row, of that row kind, inside the chip', () => {
    renderStatic([text('A'), shape('Box'), shape('Ellipse')]);
    for (const [name, kind] of [
      ['A', 'Text'],
      ['Box 1', 'Box'],
      ['Ellipse 1', 'Ellipse'],
    ] as const) {
      const chip = screen.getByRole('button', { name });
      const icons = chip.querySelectorAll('svg');
      expect(icons, `${name} carries exactly one icon`).toHaveLength(1);
      expect(icons[0]).toHaveAttribute('aria-hidden', 'true');
      expect(icons[0]).toHaveAttribute('data-kind', kind);
    }
  });

  it('Leaves every chip named by its layer name alone', () => {
    renderStatic([text('A'), shape('Box'), shape('Ellipse')]);
    const names = within(screen.getByRole('list', { name: 'Layers' }))
      .getAllByRole('listitem')
      .map((item) => within(item).getAllByRole('button')[0]!.textContent);
    expect(names).toEqual(['Ellipse 1', 'Box 1', 'A']);
    expect(screen.getByRole('button', { name: 'Remove Box 1' })).toBeInTheDocument();
  });
});
