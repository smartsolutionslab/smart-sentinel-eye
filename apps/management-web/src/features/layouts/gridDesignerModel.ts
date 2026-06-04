import type { FieldError, FieldErrors, Resolver } from 'react-hook-form';
import {
  createLayoutDraftSchema,
  editDraftRevisionSchema,
  MAX_TILES,
  type LayoutTileInput,
} from '@smart-sentinel-eye/shared/api/layouts.schema';
import type { LayoutTile } from '@smart-sentinel-eye/shared/api/layouts.api';

/**
 * One cell of the designer grid. The grid is rendered *dense* — one cell per
 * `(row, col)` position — but the wire shape (`tiles`) is *sparse*: a cell
 * with no camera is an empty cell and is dropped before POST/PATCH (ADR-0112
 * §2 — sparse grids allowed). `overlayIdentifier` is `''` for "(none)".
 */
export interface DesignerCell {
  cameraIdentifier: string;
  overlayIdentifier: string;
  row: number;
  col: number;
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

// Derived from MAX_TILES / the schema's 1..2 row-col bounds: every rows×cols
// with rows,cols ∈ {1,2} and rows*cols ≤ MAX_TILES. One source of truth.
export const GRID_PRESETS: ReadonlyArray<GridPreset> = (() => {
  const presets: GridPreset[] = [];
  for (let rows = 1; rows <= 2; rows += 1) {
    for (let cols = 1; cols <= 2; cols += 1) {
      if (rows * cols <= MAX_TILES) {
        presets.push({ rows, cols, label: `${rows}×${cols}` });
      }
    }
  }
  return presets;
})();

/** Build a dense `rows×cols` cell grid, carrying over any existing cell. */
export function buildCells(
  rows: number,
  cols: number,
  existing: ReadonlyArray<DesignerCell> = [],
): DesignerCell[] {
  const byPosition = new Map<string, DesignerCell>();
  for (const cell of existing) {
    byPosition.set(`${cell.row},${cell.col}`, cell);
  }
  const cells: DesignerCell[] = [];
  for (let row = 0; row < rows; row += 1) {
    for (let col = 0; col < cols; col += 1) {
      const carried = byPosition.get(`${row},${col}`);
      cells.push(
        carried ?? { cameraIdentifier: '', overlayIdentifier: '', row, col },
      );
    }
  }
  return cells;
}

/** Map persisted tiles (sparse) onto a dense cell grid for the edit flow. */
export function cellsFromTiles(
  rows: number,
  cols: number,
  tiles: ReadonlyArray<LayoutTile>,
): DesignerCell[] {
  const existing = tiles.map((tile) => ({
    cameraIdentifier: tile.cameraIdentifier,
    overlayIdentifier: tile.overlayIdentifier ?? '',
    row: tile.row,
    col: tile.col,
  }));
  return buildCells(rows, cols, existing);
}

/** Filter the dense cells into the sparse `tiles` wire shape (drops empties). */
export function tilesFromCells(
  cells: ReadonlyArray<DesignerCell>,
): LayoutTileInput[] {
  return cells
    .filter((cell) => cell.cameraIdentifier !== '')
    .map((cell) => ({
      cameraIdentifier: cell.cameraIdentifier,
      overlayIdentifier:
        cell.overlayIdentifier === '' ? null : cell.overlayIdentifier,
      row: cell.row,
      col: cell.col,
    }));
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
export function createGridDesignerResolver(
  mode: GridDesignerMode,
): Resolver<GridDesignerValue> {
  return async (values) => {
    const cells = values.cells;
    const tiles = tilesFromCells(cells);
    const candidate =
      mode === 'create'
        ? { name: values.name, grid: values.grid, tiles }
        : { grid: values.grid, tiles };
    const schema =
      mode === 'create' ? createLayoutDraftSchema : editDraftRevisionSchema;

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
function setOnce(
  existing: FieldError | undefined,
  message: string,
): FieldError {
  return existing ?? { type: 'manual', message };
}
