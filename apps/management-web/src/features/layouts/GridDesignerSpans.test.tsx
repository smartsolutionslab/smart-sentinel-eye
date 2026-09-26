import { describe, it, expect } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useForm } from 'react-hook-form';
import type { CameraSummary } from '@smart-sentinel-eye/shared/api/cameras.api';
import type { LayoutTile } from '@smart-sentinel-eye/shared/api/layouts.api';
import { GridDesigner } from './GridDesigner.js';
import { buildCells, cellsFromTiles, type DesignerCell, type GridDesignerValue } from './gridDesignerModel.js';

/**
 * Spec 258 (#2607), ADR-0156, US3 — the two native `Row span` / `Column
 * span` selects per populated tile (plan.md §4.3). `GridDesigner.tsx` does
 * not render them yet, so every case below fails on `getAllByLabelText`
 * ("missing control", ADR-0139 red first) until `TileSpanFields` is wired
 * in.
 */

const CAM_A = '11111111-1111-1111-1111-111111111111';
const CAM_B = '22222222-2222-2222-2222-222222222222';
const CAM_C = '33333333-3333-3333-3333-333333333333';
const CAM_D = '44444444-4444-4444-4444-444444444444';
const CAM_E = '55555555-5555-5555-5555-555555555555';
const CAM_F = '66666666-6666-6666-6666-666666666666';

function camera(identifier: string, name: string): CameraSummary {
  return {
    cameraIdentifier: identifier,
    version: 1,
    fab: 'munich',
    name,
    rtspUrl: 'rtsp://10.0.5.1/h264',
    registeredAt: '2026-05-24T10:00:00Z',
    status: 'Registered',
  };
}

const HERO_WALL_CAMERAS = [
  camera(CAM_A, 'Hero'),
  camera(CAM_B, 'Corner top-right'),
  camera(CAM_C, 'Mid right'),
  camera(CAM_D, 'Bottom left'),
  camera(CAM_E, 'Bottom mid'),
  camera(CAM_F, 'Bottom right'),
];

/** A cell fixture, cast because `DesignerCell` does not carry spans yet. */
function cellAt(
  row: number,
  col: number,
  overrides: { cameraIdentifier?: string; rowSpan?: number; colSpan?: number } = {},
): DesignerCell {
  return {
    cameraIdentifier: overrides.cameraIdentifier ?? '',
    overlayIdentifier: '',
    row,
    col,
    rowSpan: overrides.rowSpan ?? 1,
    colSpan: overrides.colSpan ?? 1,
  } as DesignerCell;
}

function tileAt(row: number, col: number, overrides: { rowSpan?: number; colSpan?: number } = {}): LayoutTile {
  return {
    cameraIdentifier: CAM_A,
    overlayIdentifier: null,
    row,
    col,
    rowSpan: overrides.rowSpan ?? 1,
    colSpan: overrides.colSpan ?? 1,
  } as LayoutTile;
}

/** The spec's hero-and-thumbnails 3x3 wall: (0,0) is the hero, unspanned so far. */
function heroWallCells(): DesignerCell[] {
  return buildCells(3, 3, [
    cellAt(0, 0, { cameraIdentifier: CAM_A }),
    cellAt(0, 2, { cameraIdentifier: CAM_B }),
    cellAt(1, 2, { cameraIdentifier: CAM_C }),
    cellAt(2, 0, { cameraIdentifier: CAM_D }),
    cellAt(2, 1, { cameraIdentifier: CAM_E }),
    cellAt(2, 2, { cameraIdentifier: CAM_F }),
  ]);
}

function Harness({
  grid = { rows: 3, cols: 3 },
  cells = heroWallCells(),
  cameras = HERO_WALL_CAMERAS,
}: {
  grid?: { rows: number; cols: number };
  cells?: DesignerCell[];
  cameras?: ReadonlyArray<CameraSummary>;
}) {
  const form = useForm<GridDesignerValue>({ defaultValues: { name: 'wall', grid, cells } });

  return (
    <GridDesigner form={form} cameras={cameras} overlays={[]} camerasLoading={false} overlaysLoading={false} />
  );
}

describe('GridDesigner — Row span / Column span controls (spec 258 US3)', () => {
  it('Hides the three cells a 2x2 hero span covers', async () => {
    const user = userEvent.setup();
    render(<Harness />);

    expect(screen.getByText('Tile 1,2')).toBeInTheDocument();
    expect(screen.getByText('Tile 2,1')).toBeInTheDocument();
    expect(screen.getByText('Tile 2,2')).toBeInTheDocument();

    await user.selectOptions(screen.getAllByLabelText('Row span')[0]!, '2');
    await user.selectOptions(screen.getAllByLabelText('Column span')[0]!, '2');

    expect(screen.queryByText('Tile 1,2')).not.toBeInTheDocument();
    expect(screen.queryByText('Tile 2,1')).not.toBeInTheDocument();
    expect(screen.queryByText('Tile 2,2')).not.toBeInTheDocument();
    // The hero's own tile, and the other three populated ones, stay.
    expect(screen.getByText('Tile 1,1')).toBeInTheDocument();
    expect(screen.getByText('Tile 1,3')).toBeInTheDocument();
  });

  it('Brings a covered cell back empty when the span shrinks', async () => {
    const user = userEvent.setup();
    render(<Harness />);

    await user.selectOptions(screen.getAllByLabelText('Row span')[0]!, '2');
    await user.selectOptions(screen.getAllByLabelText('Column span')[0]!, '2');
    expect(screen.queryByText('Tile 1,2')).not.toBeInTheDocument();

    await user.selectOptions(screen.getAllByLabelText('Row span')[0]!, '1');
    await user.selectOptions(screen.getAllByLabelText('Column span')[0]!, '1');

    const reappeared = screen.getByText('Tile 1,2').closest('div');
    expect(reappeared).not.toBeNull();
    const cameraSelect = within(reappeared as HTMLElement).getByLabelText('Camera');
    expect(cameraSelect).toHaveValue('');
  });

  it('Offers Column span options 1 and 2 only, with a camera on (0,2)', () => {
    render(<Harness />);

    const columnSelect = screen.getAllByLabelText('Column span')[0]!;
    const optionValues = within(columnSelect).getAllByRole('option').map((option) => (option as HTMLOptionElement).value);

    expect(optionValues).toEqual(['1', '2']);
  });

  it('Sets a span through the keyboard alone, no click required', async () => {
    const user = userEvent.setup();
    render(<Harness />);

    const rowSelect = screen.getAllByLabelText('Row span')[0]!;
    expect(rowSelect.tagName).toBe('SELECT');

    rowSelect.focus();
    expect(rowSelect).toHaveFocus();
    await user.selectOptions(rowSelect, '2');

    expect(rowSelect).toHaveValue('2');
  });

  it('Shows the stored spans when editing a published hero wall', () => {
    const editCells = cellsFromTiles(3, 3, [
      tileAt(0, 0, { rowSpan: 2, colSpan: 2 }),
      tileAt(0, 2),
      tileAt(1, 2),
      tileAt(2, 0),
      tileAt(2, 1),
      tileAt(2, 2),
    ]);

    render(<Harness cells={editCells} />);

    const rowSelect = screen.getAllByLabelText('Row span')[0]!;
    const colSelect = screen.getAllByLabelText('Column span')[0]!;
    expect(rowSelect).toHaveValue('2');
    expect(colSelect).toHaveValue('2');
    expect(screen.queryByText('Tile 1,2')).not.toBeInTheDocument();
  });
});
