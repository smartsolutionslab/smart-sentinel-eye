import type { DesignerCell } from '../gridDesignerModel';

export interface DisplayShape {
  key: string;
  widthUnits: number;
  heightUnits: number;
}

export interface SizePx {
  width: number;
  height: number;
}

export const DISPLAY_SHAPES: ReadonlyArray<DisplayShape> = [
  { key: '16:9', widthUnits: 16, heightUnits: 9 },
  { key: '16:10', widthUnits: 16, heightUnits: 10 },
  { key: '21:9', widthUnits: 21, heightUnits: 9 },
  { key: '4:3', widthUnits: 4, heightUnits: 3 },
  { key: '9:16', widthUnits: 9, heightUnits: 16 },
];

export const DEFAULT_DISPLAY_SHAPE: DisplayShape = DISPLAY_SHAPES[0];

/** The wall's `gap-1 p-1` (LayoutGrid.tsx): 4px between cells and around the grid. */
const WALL_SPACING_PX = 4;

/** The largest display of `shape` that fits inside `area`. */
export function displaySizePx(shape: DisplayShape, area: SizePx): SizePx {
  const ratio = shape.widthUnits / shape.heightUnits;
  return area.width / area.height > ratio
    ? { width: area.height * ratio, height: area.height }
    : { width: area.width, height: area.width / ratio };
}

function extentPx(total: number, tracks: number, span: number, start: number): number {
  const clamped = Math.max(1, Math.min(span, tracks - start));
  const track = (total - 2 * WALL_SPACING_PX - (tracks - 1) * WALL_SPACING_PX) / tracks;
  return clamped * track + (clamped - 1) * WALL_SPACING_PX;
}

/** One cell's size on a display of `shape` shown inside `area`; a span never leaves the grid. */
export function cellSizePx(
  shape: DisplayShape,
  grid: { rows: number; cols: number },
  cell: DesignerCell,
  area: SizePx,
): SizePx {
  const display = displaySizePx(shape, area);
  return {
    width: extentPx(display.width, grid.cols, cell.colSpan, cell.col),
    height: extentPx(display.height, grid.rows, cell.rowSpan, cell.row),
  };
}

export interface ViewerBox extends SizePx {
  top: number;
}

/**
 * The 16:9 `CameraViewer` root inside a wall cell, as observed on the nine-tile
 * wall: as wide as the cell, centred vertically, clipped by the cell when the
 * cell is flatter than 16:9 (negative `top`) and letterboxed when taller.
 */
export function viewerBoxPx(cell: SizePx): ViewerBox {
  const height = (cell.width * 9) / 16;
  return { width: cell.width, height, top: (cell.height - height) / 2 };
}
