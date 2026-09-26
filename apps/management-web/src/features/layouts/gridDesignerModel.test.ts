import { describe, it, expect } from 'vitest';
import type { LayoutTile } from '@smart-sentinel-eye/shared/api/layouts.api';
import {
  buildCells,
  cellsFromTiles,
  tilesFromCells,
  GRID_PRESETS,
  // Spec 258 (#2607), ADR-0156, plan.md §4.3 — none of these three exist on
  // today's `gridDesignerModel.ts`. The import itself fails to resolve until
  // they are added, which fails every test in this file at once (the same
  // "missing export" RED every other new case in this phase relies on).
  coveredBy,
  spanOptions,
  clearCameraAt,
  type DesignerCell,
} from './gridDesignerModel.js';

const HERO = 'cam-hero';
const CORNER = 'cam-corner';

/**
 * A cell fixture wide enough to carry `rowSpan`/`colSpan` (plan.md §4.3:
 * `DesignerCell` gains them, default 1). Cast because today's `DesignerCell`
 * does not declare those fields yet.
 */
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
    cameraIdentifier: HERO,
    overlayIdentifier: null,
    row,
    col,
    rowSpan: overrides.rowSpan ?? 1,
    colSpan: overrides.colSpan ?? 1,
  } as LayoutTile;
}

function indexAt(cells: ReadonlyArray<DesignerCell>, row: number, col: number): number {
  const index = cells.findIndex((cell) => cell.row === row && cell.col === col);
  expect(index, `no cell at (${row},${col})`).toBeGreaterThanOrEqual(0);
  return index;
}

describe('gridDesignerModel — spans up to a 3x3 grid (spec 258 US3)', () => {
  it('offers nine presets, rows and cols each 1..3, up to the nine-cell cap', () => {
    expect(GRID_PRESETS).toHaveLength(9);
    expect(GRID_PRESETS.map((preset) => preset.label).sort()).toEqual(
      ['1×1', '1×2', '1×3', '2×1', '2×2', '2×3', '3×1', '3×2', '3×3'].sort(),
    );
  });

  it('clamps a cell’s span to the smaller grid when a preset shrinks', () => {
    const onABigGrid = [cellAt(0, 0, { cameraIdentifier: HERO, rowSpan: 3, colSpan: 3 })];

    const shrunk = buildCells(2, 2, onABigGrid);

    const hero = shrunk[indexAt(shrunk, 0, 0)]!;
    expect(hero.rowSpan).toBe(2);
    expect(hero.colSpan).toBe(2);
  });

  it('offers only the column spans that stay in-bounds and clear a populated cell', () => {
    const cells = buildCells(3, 3, [cellAt(0, 0, { cameraIdentifier: HERO }), cellAt(0, 2, { cameraIdentifier: CORNER })]);

    const options = spanOptions(cells, indexAt(cells, 0, 0), { rows: 3, cols: 3 });

    // A colSpan of 3 from (0,0) would cover (0,2), which is populated.
    expect(options.cols).toEqual([1, 2]);
  });

  it('offers every row span up to the grid edge when nothing populated blocks it', () => {
    const cells = buildCells(3, 3, [cellAt(0, 0, { cameraIdentifier: HERO })]);

    const options = spanOptions(cells, indexAt(cells, 0, 0), { rows: 3, cols: 3 });

    expect(options.rows).toEqual([1, 2, 3]);
  });

  it('maps every cell a hero’s span covers back to the hero, via coveredBy', () => {
    const cells = buildCells(3, 3, [cellAt(0, 0, { cameraIdentifier: HERO, rowSpan: 2, colSpan: 2 })]);
    const heroIndex = indexAt(cells, 0, 0);

    const covered = coveredBy(cells);

    const coveredPositions = [...covered.keys()].map((index) => `${cells[index]!.row},${cells[index]!.col}`);
    expect(coveredPositions.sort()).toEqual(['0,1', '1,0', '1,1'].sort());
    for (const coveringIndex of covered.values()) {
      expect(coveringIndex).toBe(heroIndex);
    }
  });

  it('round-trips rowSpan/colSpan through cellsFromTiles and tilesFromCells', () => {
    const tiles: LayoutTile[] = [tileAt(0, 0, { rowSpan: 2, colSpan: 2 }), tileAt(0, 2)];

    const cells = cellsFromTiles(3, 3, tiles);
    const roundTripped = tilesFromCells(cells);

    expect(roundTripped.find((tile) => tile.row === 0 && tile.col === 0)).toMatchObject({ rowSpan: 2, colSpan: 2 });
    expect(roundTripped.find((tile) => tile.row === 0 && tile.col === 2)).toMatchObject({ rowSpan: 1, colSpan: 1 });
  });

  it('resets a cell’s span to 1x1 when its camera is cleared', () => {
    const cells = buildCells(3, 3, [cellAt(0, 0, { cameraIdentifier: HERO, rowSpan: 2, colSpan: 2 })]);
    const heroIndex = indexAt(cells, 0, 0);

    const cleared = clearCameraAt(cells, heroIndex);

    expect(cleared[heroIndex]).toMatchObject({ cameraIdentifier: '', rowSpan: 1, colSpan: 1 });
  });
});
