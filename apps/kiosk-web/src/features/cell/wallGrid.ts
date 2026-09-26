import type { LayoutTile } from '@smart-sentinel-eye/shared/api/layouts.api';

/**
 * One cell of the rendered grid (spec 258 US2, ADR-0156). Either a tile's
 * origin — carrying its (clamped) span — or an uncovered cell rendered as a
 * placeholder (`tile: null`, span 1×1). A cell a tile's span covers but does
 * not originate emits no item at all: it is neither a tile nor a
 * placeholder, because the spanning tile already draws over it.
 */
export interface GridItem {
  key: string;
  tile: LayoutTile | null;
  row: number;
  col: number;
  rowSpan: number;
  colSpan: number;
}

/**
 * Lay out every grid coordinate in row-major order for explicit CSS grid
 * placement (FR-008). Each tile is placed at its origin, spanning
 * `rowSpan × colSpan` clamped to the grid so a malformed payload from the
 * wire cannot make this throw (the aggregate already refuses an
 * out-of-bounds span — this is defensive only). Cells the span covers are
 * marked occupied and emit nothing; every other uncovered cell becomes a
 * 1×1 placeholder. A later tile that would claim an already-occupied cell
 * is dropped entirely (first wins) — impossible from a valid layout, kept
 * only so the renderer stays total.
 *
 * Row-major-by-origin keeps an all-1×1 wall's order identical to today's
 * `buildGridCells` (spec 258 §8 characterisation), and tile keys stay the
 * origin `row:col` so `useWallAlignment`'s keys do not move.
 */
export function buildGridItems(rows: number, cols: number, tiles: LayoutTile[]): GridItem[] {
  const occupied = new Set<string>();
  const origins = new Map<string, { tile: LayoutTile; rowSpan: number; colSpan: number }>();

  for (const tile of tiles) {
    const rowSpan = Math.max(1, Math.min(tile.rowSpan, rows - tile.row));
    const colSpan = Math.max(1, Math.min(tile.colSpan, cols - tile.col));

    const cells: string[] = [];
    let overlaps = false;
    for (let row = tile.row; row < tile.row + rowSpan; row += 1) {
      for (let col = tile.col; col < tile.col + colSpan; col += 1) {
        const key = cellKey(row, col);
        cells.push(key);
        if (occupied.has(key)) {
          overlaps = true;
        }
      }
    }
    if (overlaps) {
      continue;
    }

    for (const key of cells) {
      occupied.add(key);
    }
    origins.set(cellKey(tile.row, tile.col), { tile, rowSpan, colSpan });
  }

  const items: GridItem[] = [];
  for (let row = 0; row < rows; row += 1) {
    for (let col = 0; col < cols; col += 1) {
      const key = cellKey(row, col);
      const origin = origins.get(key);
      if (origin !== undefined) {
        items.push({ key, tile: origin.tile, row, col, rowSpan: origin.rowSpan, colSpan: origin.colSpan });
        continue;
      }
      if (occupied.has(key)) {
        continue;
      }
      items.push({ key, tile: null, row, col, rowSpan: 1, colSpan: 1 });
    }
  }
  return items;
}

function cellKey(row: number, col: number): string {
  return `${row}:${col}`;
}
