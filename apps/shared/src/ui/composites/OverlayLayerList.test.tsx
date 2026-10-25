// @vitest-environment jsdom
import { useState } from 'react';
import { cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { OverlayElement } from '../../api/overlays.api.js';
import { OverlayLayerList } from './OverlayLayerList.js';

/**
 * Spec 441 (issue #3072) C2. New behaviour, RED (ADR-0139): the layer list.
 * The stub renders nothing, so every query below fails on a missing list.
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

function box(): OverlayElement {
  return {
    kind: 'Box',
    color: '#FF0000FF',
    normalizedX: 0.5,
    normalizedY: 0.5,
    normalizedWidth: 0.2,
    normalizedHeight: 0.2,
  };
}

interface Handlers {
  onSelect: (id: string) => void;
  onMove: (ordinal: number, delta: 1 | -1) => void;
  onRemove: (ordinal: number) => void;
  onAdd: (kind: OverlayElement['kind']) => void;
}

function handlers(): Handlers {
  return { onSelect: vi.fn(), onMove: vi.fn(), onRemove: vi.fn(), onAdd: vi.fn() };
}

function renderStatic(
  elements: OverlayElement[],
  options: { selectedId?: string; canAdd?: boolean; canRemove?: boolean; handlers?: Handlers } = {},
) {
  const h = options.handlers ?? handlers();
  const rowIds = elements.map((_, index) => `row-${index}`);
  render(
    <OverlayLayerList
      elements={elements}
      rowIds={rowIds}
      selectedId={options.selectedId ?? 'row-0'}
      canAdd={options.canAdd ?? true}
      canRemove={options.canRemove ?? true}
      {...h}
    />,
  );
  return h;
}

/** A stateful host so a move re-renders the list the way the form does, keeping each row's key. */
function MovingHost({ initial }: { initial: string[] }) {
  const [rows, setRows] = useState(() => initial.map((value, index) => ({ id: `row-${index}`, element: text(value) })));
  const [selectedId, setSelectedId] = useState('row-0');
  return (
    <OverlayLayerList
      elements={rows.map((row) => row.element)}
      rowIds={rows.map((row) => row.id)}
      selectedId={selectedId}
      canAdd
      canRemove={rows.length > 1}
      onSelect={setSelectedId}
      onMove={(ordinal, delta) =>
        setRows((current) => {
          const next = [...current];
          const [moved] = next.splice(ordinal, 1);
          next.splice(ordinal + delta, 0, moved!);
          return next;
        })
      }
      onRemove={(ordinal) => setRows((current) => current.filter((_, index) => index !== ordinal))}
      onAdd={() => undefined}
    />
  );
}

function rowNames(): string[] {
  const list = screen.getByRole('list', { name: 'Layers' });
  return within(list)
    .getAllByRole('listitem')
    .map((item) => within(item).getAllByRole('button')[0]!.textContent ?? '');
}

describe('OverlayLayerList', () => {
  it('Is a native list named "Layers"', () => {
    renderStatic([text('A')]);
    expect(screen.getByRole('list', { name: 'Layers' })).toBeInTheDocument();
  });

  it('Shows the front-most (highest ordinal) element at the top', () => {
    renderStatic([text('A'), text('B'), text('C')]);
    expect(rowNames()).toEqual(['C', 'B', 'A']);
  });

  it('Marks only the selected row pressed', () => {
    renderStatic([text('A'), text('B')], { selectedId: 'row-1' });
    expect(screen.getByRole('button', { name: 'B', pressed: true })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'A', pressed: false })).toBeInTheDocument();
  });

  it('Names shapes "Box n" and an empty label "Empty label"', () => {
    renderStatic([box(), text('  ')]);
    expect(rowNames()).toEqual(['Empty label', 'Box 1']);
  });

  it('Reports the selected row id when a row is chosen', async () => {
    const user = userEvent.setup();
    const h = renderStatic([text('A'), text('B')]);
    await user.click(screen.getByRole('button', { name: 'B' }));
    expect(h.onSelect).toHaveBeenCalledWith('row-1');
  });

  it('Gives each row accessible Move up, Move down and Remove names that include the element name', () => {
    renderStatic([text('A'), text('B'), text('C')]);
    expect(screen.getByRole('button', { name: 'Move B up' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Move B down' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Remove B' })).toBeInTheDocument();
  });

  it('Moves up as +1 in ordinal space (bring forward)', async () => {
    const user = userEvent.setup();
    const h = renderStatic([text('A'), text('B'), text('C')]);
    await user.click(screen.getByRole('button', { name: 'Move A up' }));
    expect(h.onMove).toHaveBeenCalledExactlyOnceWith(0, 1);
  });

  it('Moves down as -1 in ordinal space (send back)', async () => {
    const user = userEvent.setup();
    const h = renderStatic([text('A'), text('B'), text('C')]);
    await user.click(screen.getByRole('button', { name: 'Move C down' }));
    expect(h.onMove).toHaveBeenCalledExactlyOnceWith(2, -1);
  });

  it('Makes Move up unavailable on the top row and inert', async () => {
    const user = userEvent.setup();
    const h = renderStatic([text('A'), text('B')]);
    const up = screen.getByRole('button', { name: 'Move B up' });
    expect(up).toHaveAttribute('aria-disabled', 'true');
    await user.click(up);
    expect(h.onMove).not.toHaveBeenCalled();
  });

  it('Makes Move down unavailable on the bottom row and inert', async () => {
    const user = userEvent.setup();
    const h = renderStatic([text('A'), text('B')]);
    const down = screen.getByRole('button', { name: 'Move A down' });
    expect(down).toHaveAttribute('aria-disabled', 'true');
    await user.click(down);
    expect(h.onMove).not.toHaveBeenCalled();
  });

  it('Removes by ordinal', async () => {
    const user = userEvent.setup();
    const h = renderStatic([text('A'), text('B')]);
    await user.click(screen.getByRole('button', { name: 'Remove A' }));
    expect(h.onRemove).toHaveBeenCalledExactlyOnceWith(0);
  });

  it('Makes Remove unavailable and inert when canRemove is false', async () => {
    const user = userEvent.setup();
    const h = renderStatic([text('A')], { canRemove: false });
    const remove = screen.getByRole('button', { name: 'Remove A' });
    expect(remove).toHaveAttribute('aria-disabled', 'true');
    await user.click(remove);
    expect(h.onRemove).not.toHaveBeenCalled();
  });

  it('Adds a label from the Add menu', async () => {
    const user = userEvent.setup();
    const h = renderStatic([text('A')]);
    await user.click(screen.getByRole('button', { name: 'Add' }));
    await user.click(await screen.findByRole('menuitem', { name: 'Text' }));
    expect(h.onAdd).toHaveBeenCalledTimes(1);
  });

  it('Makes Add unavailable and inert at the limit, saying so', async () => {
    const user = userEvent.setup();
    const h = renderStatic([text('A')], { canAdd: false });
    const add = screen.getByRole('button', { name: 'Add' });
    expect(add).toHaveAttribute('aria-disabled', 'true');
    expect(screen.getByText('Up to 8 elements')).toBeInTheDocument();
    await user.click(add);
    expect(h.onAdd).not.toHaveBeenCalled();
  });

  it('Does not show the limit text while more labels may be added', () => {
    renderStatic([text('A')]);
    expect(screen.queryByText('Up to 8 elements')).not.toBeInTheDocument();
  });

  it('Reorders the visible rows after a move, keeping the same button focused', async () => {
    const user = userEvent.setup();
    render(<MovingHost initial={['A', 'B', 'C']} />);
    const up = screen.getByRole('button', { name: 'Move A up' });
    up.focus();
    await user.click(up);
    expect(rowNames()).toEqual(['C', 'A', 'B']);
    expect(screen.getByRole('button', { name: 'Move A up' })).toHaveFocus();
  });

  it('Keeps focus on the control when a move reaches the top', async () => {
    const user = userEvent.setup();
    render(<MovingHost initial={['A', 'B']} />);
    const up = screen.getByRole('button', { name: 'Move A up' });
    up.focus();
    await user.click(up);
    expect(screen.getByRole('button', { name: 'Move A up' })).toHaveFocus();
    expect(screen.getByRole('button', { name: 'Move A up' })).toHaveAttribute('aria-disabled', 'true');
  });

  it('Announces the new position in list order through one polite live region', async () => {
    const user = userEvent.setup();
    render(<MovingHost initial={['A', 'B', 'C']} />);
    await user.click(screen.getByRole('button', { name: 'Move A up' }));
    const live = screen.getByTestId('overlay-layer-list-live-region');
    expect(live).toHaveAttribute('aria-live', 'polite');
    expect(live).toHaveTextContent('A moved to position 2 of 3');
  });

  it('Mutates the live region again when the same announcement repeats', async () => {
    const user = userEvent.setup();
    render(<MovingHost initial={['A', 'B', 'C']} />);
    const live = screen.getByTestId('overlay-layer-list-live-region');
    await user.click(screen.getByRole('button', { name: 'Move A up' }));
    const first = live.firstElementChild;
    await user.click(screen.getByRole('button', { name: 'Move A down' }));
    await user.click(screen.getByRole('button', { name: 'Move A up' }));
    expect(live).toHaveTextContent('A moved to position 2 of 3');
    expect(live.firstElementChild).not.toBe(first);
  });
});
