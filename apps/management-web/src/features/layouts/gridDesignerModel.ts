import type { FieldError, FieldErrors, Resolver } from 'react-hook-form';
import {
  createLayoutDraftSchema,
  editDraftRevisionSchema,
  MAX_CELLS,
  spansOverlap,
  type LayoutTileInput,
} from '@smart-sentinel-eye/shared/api/layouts.schema';
import type { LayoutTile } from '@smart-sentinel-eye/shared/api/layouts.api';

/**
 * One cell of the designer grid. The grid is rendered *dense* — one cell per
 * `(row, col)` position — but the wire shape (`tiles`) is *sparse*: a cell
 * with no camera is an empty cell and is dropped before POST/PATCH (ADR-0112
 * §2 — sparse grids allowed). `overlayIdentifier` is `''` for "(none)".
 *
 * `rowSpan`/`colSpan` default to 1 and claim a rectangle from this cell's
 * origin (spec 262, ADR-0156); a cell another populated cell's span covers
 * is not rendered (see `coveredBy`) but stays in this dense array so the
 * resolver's `tiles[i]` ↔ cell-index mapping stays stable.
 */
export interface DesignerCell {
  cameraIdentifier: string;
  overlayIdentifier: string;
  row: number;
  col: number;
  rowSpan: number;
  colSpan: number;
}

/**
 * The designer's form value. `cells` is the dense grid (length `rows*cols`);
 * `name` is only used by the create flow (edit-after-publish keeps the chain
 * name). Submitting filters `cells` into the sparse `tiles` the frozen Zod
 * schema validates.
 */
export interface GridDesignerValue {
  name: string;
  grid: { rows: number; cols: number };
  cells: DesignerCell[];
}

/** A grid-size preset offered as a UI convenience (ADR-0112 §2 / Alt B). */
export interface GridPreset {
  rows: number;
  cols: number;
  label: string;
}

/** Shared by every `<select>` in the designer (`GridDesigner.tsx`, `TileSpanFields.tsx`). */
export const SELECT_CLASS = 'w-full rounded-md border border-fg-muted/40 bg-bg-base px-3 py-2 text-fg-primary';

// Derived from MAX_CELLS / the schema's 1..3 row-col bounds: every rows×cols
// with rows,cols ∈ {1,2,3} and rows*cols ≤ MAX_CELLS (ADR-0156 §2) — nine
// presets. One source of truth.
export const GRID_PRESETS: ReadonlyArray<GridPreset> = (() => {
  const presets: GridPreset[] = [];
  for (let rows = 1; rows <= 3; rows += 1) {
    for (let cols = 1; cols <= 3; cols += 1) {
      if (rows * cols <= MAX_CELLS) {
        presets.push({ rows, cols, label: `${rows}×${cols}` });
      }
    }
  }
  return presets;
})();

/**
 * Build a dense `rows×cols` cell grid, carrying over any existing cell.
 * A carried cell's span is clamped to the new grid from its own origin
 * (spec 262 US3 "a smaller preset clamps spans") — shrinking a grid cannot
 * create an overlap, since a valid wall's spans never intersected before the
 * shrink either.
 */
export function buildCells(rows: number, cols: number, existing: ReadonlyArray<DesignerCell> = []): DesignerCell[] {
  const byPosition = new Map<string, DesignerCell>();
  for (const cell of existing) {
    byPosition.set(`${cell.row},${cell.col}`, cell);
  }
  const cells: DesignerCell[] = [];
  for (let row = 0; row < rows; row += 1) {
    for (let col = 0; col < cols; col += 1) {
      const carried = byPosition.get(`${row},${col}`);
      if (carried === undefined) {
        cells.push({ cameraIdentifier: '', overlayIdentifier: '', row, col, rowSpan: 1, colSpan: 1 });
        continue;
      }
      cells.push({
        ...carried,
        row,
        col,
        rowSpan: Math.max(1, Math.min(carried.rowSpan, rows - row)),
        colSpan: Math.max(1, Math.min(carried.colSpan, cols - col)),
      });
    }
  }
  return cells;
}

/** Map persisted tiles (sparse) onto a dense cell grid for the edit flow. */
export function cellsFromTiles(rows: number, cols: number, tiles: ReadonlyArray<LayoutTile>): DesignerCell[] {
  const existing = tiles.map((tile) => ({
    cameraIdentifier: tile.cameraIdentifier,
    overlayIdentifier: tile.overlayIdentifier ?? '',
    row: tile.row,
    col: tile.col,
    rowSpan: tile.rowSpan,
    colSpan: tile.colSpan,
  }));
  return buildCells(rows, cols, existing);
}

/** Filter the dense cells into the sparse `tiles` wire shape (drops empties). */
export function tilesFromCells(cells: ReadonlyArray<DesignerCell>): LayoutTileInput[] {
  return cells
    .filter((cell) => cell.cameraIdentifier !== '')
    .map((cell) => ({
      cameraIdentifier: cell.cameraIdentifier,
      overlayIdentifier: cell.overlayIdentifier === '' ? null : cell.overlayIdentifier,
      row: cell.row,
      col: cell.col,
      rowSpan: cell.rowSpan,
      colSpan: cell.colSpan,
    }));
}

/**
 * Cells another populated cell's span covers, keyed by the covered cell's
 * own index and mapping to the covering cell's index (spec 262 US3). Only a
 * *populated* cell's span covers anything — an empty cell's span is always
 * 1×1 (the camera select's `onChange` in `GridDesigner.tsx` resets it on
 * clear), so this never needs to consider one.
 */
export function coveredBy(cells: ReadonlyArray<DesignerCell>): Map<number, number> {
  const indexByPosition = new Map<string, number>();
  cells.forEach((cell, index) => indexByPosition.set(`${cell.row},${cell.col}`, index));

  const covered = new Map<number, number>();
  cells.forEach((cell, index) => {
    if (cell.cameraIdentifier === '') return;
    for (let row = cell.row; row < cell.row + cell.rowSpan; row += 1) {
      for (let col = cell.col; col < cell.col + cell.colSpan; col += 1) {
        if (row === cell.row && col === cell.col) continue;
        const coveredIndex = indexByPosition.get(`${row},${col}`);
        if (coveredIndex !== undefined) {
          covered.set(coveredIndex, index);
        }
      }
    }
  });
  return covered;
}

/**
 * The row/column span values the cell at `index` may take without running
 * off the grid or covering another *populated* cell — an empty (or covered)
 * cell never blocks, since it disappears under the span (plan.md §4.3). Each
 * axis is checked holding the other axis's current span fixed, symmetric
 * with the sibling axis.
 */
export function spanOptions(
  cells: ReadonlyArray<DesignerCell>,
  index: number,
  grid: { rows: number; cols: number },
): { rows: number[]; cols: number[] } {
  const cell = cells[index];
  if (cell === undefined) return { rows: [], cols: [] };

  // Full rectangle intersection against every OTHER populated cell's own
  // claimed span — not just its origin. A candidate that only checked the
  // other cell's (row, col) missed a spanning neighbour whose origin sits
  // outside the candidate rectangle but whose span reaches into it (e.g. a
  // neighbour spanning down from a row above). Same predicate as the
  // backend's `Tile.Overlaps`, and the shared `spansOverlap` the schema uses.
  const blocksOther = (rowSpan: number, colSpan: number): boolean =>
    cells.some(
      (other, otherIndex) =>
        otherIndex !== index &&
        other.cameraIdentifier !== '' &&
        spansOverlap({ row: cell.row, col: cell.col, rowSpan, colSpan }, other),
    );

  const rows: number[] = [];
  for (let span = 1; span <= grid.rows - cell.row; span += 1) {
    if (blocksOther(span, cell.colSpan)) break;
    rows.push(span);
  }

  const cols: number[] = [];
  for (let span = 1; span <= grid.cols - cell.col; span += 1) {
    if (blocksOther(cell.rowSpan, span)) break;
    cols.push(span);
  }

  return { rows, cols };
}

/**
 * The schema variant the resolver validates against. Create authors a name;
 * edit-after-publish keeps the chain name and validates the name-less
 * `editDraftRevisionSchema`.
 */
export type GridDesignerMode = 'create' | 'edit';

// The dense cell index each sparse tile maps back to, so a `tiles[i]` Zod
// issue lands on the right grid cell. `tilesFromCells` keeps cell order, so
// the i-th populated cell is the i-th tile.
function populatedCellIndices(cells: ReadonlyArray<DesignerCell>): number[] {
  const indices: number[] = [];
  cells.forEach((cell, index) => {
    if (cell.cameraIdentifier !== '') {
      indices.push(index);
    }
  });
  return indices;
}

/**
 * A React Hook Form resolver that validates the *dense* designer value against
 * the *frozen* multi-tile Zod schema (the single source of grid invariants,
 * ADR-0112 §2). It filters empty cells into `tiles`, runs the schema, then
 * maps each Zod issue path back onto the dense form so errors surface inline:
 * a `tiles[i]` issue lands on its grid cell, a `grid`/`tiles` issue surfaces
 * grid-level.
 */
export function createGridDesignerResolver(mode: GridDesignerMode): Resolver<GridDesignerValue> {
  return async (values) => {
    const cells = values.cells;
    const tiles = tilesFromCells(cells);
    const candidate =
      mode === 'create' ? { name: values.name, grid: values.grid, tiles } : { grid: values.grid, tiles };
    const schema = mode === 'create' ? createLayoutDraftSchema : editDraftRevisionSchema;

    const parsed = schema.safeParse(candidate);
    if (parsed.success) {
      return { values, errors: {} };
    }

    const cellIndices = populatedCellIndices(cells);
    const errors: FieldErrors<GridDesignerValue> = {};
    const cellErrors: Record<number, FieldErrors<DesignerCell>> = {};
    const gridMessages: string[] = [];

    for (const issue of parsed.error.issues) {
      const [head, second, third] = issue.path;
      if (head === 'name') {
        errors.name = setOnce(errors.name, issue.message);
      } else if (head === 'grid') {
        gridMessages.push(issue.message);
      } else if (head === 'tiles' && typeof second === 'number') {
        const cellIndex = cellIndices[second];
        if (cellIndex !== undefined) {
          const field: 'cameraIdentifier' | 'overlayIdentifier' =
            third === 'overlayIdentifier' ? 'overlayIdentifier' : 'cameraIdentifier';
          const existing = cellErrors[cellIndex] ?? {};
          existing[field] = setOnce(existing[field], issue.message);
          cellErrors[cellIndex] = existing;
        } else {
          gridMessages.push(issue.message);
        }
      } else {
        // `tiles` (whole array — min/max) and any unmapped issue are grid-level.
        gridMessages.push(issue.message);
      }
    }

    if (Object.keys(cellErrors).length > 0) {
      errors.cells = cellErrors as FieldErrors<GridDesignerValue>['cells'];
    }
    if (gridMessages.length > 0) {
      errors.grid = {
        type: 'manual',
        message: gridMessages[0],
      } as FieldErrors<GridDesignerValue>['grid'];
    }

    return { values: {}, errors };
  };
}

// Keep the first message for a field so the most specific issue wins.
function setOnce(existing: FieldError | undefined, message: string): FieldError {
  return existing ?? { type: 'manual', message };
}
