import { describe, expect, it } from 'vitest';
import type { DesignerCell } from '../gridDesignerModel';
import { DEFAULT_DISPLAY_SHAPE, DISPLAY_SHAPES, cellSizePx, displaySizePx, viewerBoxPx } from './displayShapes';

// The wall's own spacing: `gap-1 p-1` on the CSS grid (LayoutGrid.tsx), i.e. 4px.
const WALL_SPACING_PX = 4;

function cell(overrides: Partial<DesignerCell> = {}): DesignerCell {
  return {
    cameraIdentifier: 'camera-1',
    overlayIdentifier: '',
    row: 0,
    col: 0,
    rowSpan: 1,
    colSpan: 1,
    ...overrides,
  };
}

function shape(key: string) {
  const found = DISPLAY_SHAPES.find((s) => s.key === key);
  if (!found) throw new Error(`no shape ${key}`);
  return found;
}

describe('display shapes', () => {
  it('offers exactly the five shapes the spec names, and defaults to 16:9', () => {
    expect(DISPLAY_SHAPES.map((s) => s.key)).toEqual(['16:9', '16:10', '21:9', '4:3', '9:16']);
    expect(DEFAULT_DISPLAY_SHAPE.key).toBe('16:9');
  });

  it('fits the largest display of the shape into a wider preview area', () => {
    // 1600x400 area, 16:9: height-bound -> 400 high, 711.11 wide.
    const size = displaySizePx(shape('16:9'), { width: 1600, height: 400 });
    expect(size.height).toBeCloseTo(400, 5);
    expect(size.width).toBeCloseTo((400 * 16) / 9, 5);
  });

  it('fits the largest display of the shape into a taller preview area', () => {
    // 400x1600 area, 9:16 portrait: width-bound -> 400 wide, 711.11 high.
    const size = displaySizePx(shape('9:16'), { width: 400, height: 1600 });
    expect(size.width).toBeCloseTo(400, 5);
    expect(size.height).toBeCloseTo((400 * 16) / 9, 5);
  });

  it('keeps the shape: width over height equals the shape ratio for every shape', () => {
    for (const s of DISPLAY_SHAPES) {
      const size = displaySizePx(s, { width: 1000, height: 700 });
      expect(size.width / size.height).toBeCloseTo(s.widthUnits / s.heightUnits, 5);
      expect(size.width).toBeLessThanOrEqual(1000 + 1e-9);
      expect(size.height).toBeLessThanOrEqual(700 + 1e-9);
    }
  });
});

describe('cell size on a display', () => {
  const area = { width: 1600, height: 900 }; // exactly a 16:9 display

  it('takes the wall padding and gaps out of the cell size', () => {
    // 3x3 grid, one cell: (1600 - 2*4 padding - 2*4 gaps) / 3 = 528
    //                     (900  - 2*4 padding - 2*4 gaps) / 3 = 294.666...
    const size = cellSizePx(shape('16:9'), { rows: 3, cols: 3 }, cell(), area);
    expect(size.width).toBeCloseTo((1600 - 2 * WALL_SPACING_PX - 2 * WALL_SPACING_PX) / 3, 5);
    expect(size.height).toBeCloseTo((900 - 2 * WALL_SPACING_PX - 2 * WALL_SPACING_PX) / 3, 5);
  });

  it('gives a spanning tile its cells plus the gaps it absorbs', () => {
    const one = cellSizePx(shape('16:9'), { rows: 3, cols: 3 }, cell(), area);
    const wide = cellSizePx(shape('16:9'), { rows: 3, cols: 3 }, cell({ colSpan: 2 }), area);
    expect(wide.width).toBeCloseTo(2 * one.width + WALL_SPACING_PX, 5);
    expect(wide.height).toBeCloseTo(one.height, 5);
  });

  it('never lets a span leave the grid', () => {
    const atEdge = cellSizePx(
      shape('16:9'),
      { rows: 3, cols: 3 },
      cell({ row: 2, col: 2, rowSpan: 3, colSpan: 3 }),
      area,
    );
    const single = cellSizePx(shape('16:9'), { rows: 3, cols: 3 }, cell({ row: 2, col: 2 }), area);
    expect(atEdge).toEqual(single);
  });

  it('follows the shape: the same grid on a portrait display gives a taller cell than wide', () => {
    const size = cellSizePx(shape('9:16'), { rows: 1, cols: 1 }, cell(), area);
    expect(size.height).toBeGreaterThan(size.width);
  });

  it('is the whole display minus padding for a one-by-one grid', () => {
    const display = displaySizePx(shape('21:9'), area);
    const size = cellSizePx(shape('21:9'), { rows: 1, cols: 1 }, cell(), area);
    expect(size.width).toBeCloseTo(display.width - 2 * WALL_SPACING_PX, 5);
    expect(size.height).toBeCloseTo(display.height - 2 * WALL_SPACING_PX, 5);
  });
});

// T005, observed on the seeded nine-tile wall (research.md R1): the 16:9 viewer box
// is as wide as its cell, vertically centred, and the cell clips (overflow: hidden).
describe('the 16:9 viewer box inside a cell', () => {
  it('fills a 16:9 cell exactly', () => {
    const box = viewerBoxPx({ width: 640, height: 360 });
    expect(box.width).toBeCloseTo(640, 5);
    expect(box.height).toBeCloseTo(360, 5);
    expect(box.top).toBeCloseTo(0, 5);
  });

  it('is clipped, not shrunk, in a cell flatter than 16:9 (observed 635x195 -> 635x357, top -81)', () => {
    const box = viewerBoxPx({ width: 635, height: 195 });
    expect(box.width).toBeCloseTo(635, 5);
    expect(box.height).toBeCloseTo((635 * 9) / 16, 5);
    expect(box.top).toBeCloseTo((195 - box.height) / 2, 5);
    expect(box.top).toBeLessThan(0);
  });

  it('is letterboxed, centred, in a cell taller than 16:9 (observed 261x328 -> 261x147, top +91)', () => {
    const box = viewerBoxPx({ width: 261, height: 328 });
    expect(box.width).toBeCloseTo(261, 5);
    expect(box.height).toBeCloseTo((261 * 9) / 16, 5);
    expect(box.top).toBeCloseTo((328 - box.height) / 2, 5);
    expect(box.top).toBeGreaterThan(0);
  });
});
