// @vitest-environment jsdom
import { useState } from 'react';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { OverlayElement } from '../../api/overlays.api.js';
import { OverlayLayerList } from './OverlayLayerList.js';

/**
 * Spec 441 (issue #3072) review fixes: focus and announcements after Remove and
 * Add, one labelled group per row, and a read-only list that stays silent.
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

function Host({ initial, readOnly = false }: { initial: string[]; readOnly?: boolean }) {
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
      readOnly={readOnly}
      onSelect={setSelectedId}
      onMove={vi.fn()}
      onRemove={(ordinal) => {
        const remaining = rows.filter((_, index) => index !== ordinal);
        setRows(remaining);
        setSelectedId(remaining[remaining.length - 1]!.id);
      }}
      onAddLabel={() => {
        const id = `added-${serial}`;
        setSerial(serial + 1);
        setRows((current) => [...current, { id, element: text('New label') }]);
        setSelectedId(id);
      }}
    />
  );
}

function liveRegion(): HTMLElement {
  return screen.getByTestId('overlay-layer-list-live-region');
}

describe('OverlayLayerList review fixes', () => {
  it('Moves focus to the newly selected chip after Remove, and announces the removal', async () => {
    const user = userEvent.setup();
    render(<Host initial={['A', 'B']} />);
    await user.click(screen.getByRole('button', { name: 'Remove A' }));

    expect(document.activeElement).not.toBe(document.body);
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'B', pressed: true }));
    expect(liveRegion()).toHaveTextContent('A removed');
  });

  it('Announces the position of an added label and focuses its chip', async () => {
    const user = userEvent.setup();
    render(<Host initial={['A', 'B']} />);
    await user.click(screen.getByRole('button', { name: 'Add label' }));

    expect(liveRegion()).toHaveTextContent('Label added, position 1 of 3');
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'New label', pressed: true }));
  });

  it('Wraps each row in one group named after the layer', () => {
    render(<Host initial={['A', 'B']} />);
    const group = screen.getByRole('group', { name: 'Layer A' });
    expect(group).toContainElement(screen.getByRole('button', { name: 'Move A up' }));
    expect(group).toContainElement(screen.getByRole('button', { name: 'Remove A' }));
    expect(screen.getAllByRole('group')).toHaveLength(2);
  });

  it('Stays silent and marks the controls unavailable while read-only', async () => {
    const user = userEvent.setup();
    render(<Host initial={['A', 'B']} readOnly />);
    const up = screen.getByRole('button', { name: 'Move A up' });
    expect(up).toHaveAttribute('aria-disabled', 'true');
    expect(screen.getByRole('button', { name: 'Remove A' })).toHaveAttribute('aria-disabled', 'true');
    expect(screen.getByRole('button', { name: 'Add label' })).toHaveAttribute('aria-disabled', 'true');
    await user.click(up);
    await user.click(screen.getByRole('button', { name: 'Remove A' }));
    await user.click(screen.getByRole('button', { name: 'Add label' }));
    expect(liveRegion()).toHaveTextContent('');
  });
});
