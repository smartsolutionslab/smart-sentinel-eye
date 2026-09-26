import { describe, it, expect } from 'vitest';
import type { LayoutTile } from '@smart-sentinel-eye/shared/api/layouts.api';
import { buildGridItems } from './wallGrid.js';

const CAMERA_A = 'cam-a';
const CAMERA_B = 'cam-b';

/** A tile fixture carrying `rowSpan`/`colSpan` (spec 258 FR-002). */
function tileAt(
  row: number,
  col: number,
  overrides: { rowSpan?: number; colSpan?: number; cameraIdentifier?: string } = {},
): LayoutTile {
  return {
    cameraIdentifier: overrides.cameraIdentifier ?? CAMERA_A,
    overlayIdentifier: null,
    row,
    col,
    rowSpan: overrides.rowSpan ?? 1,
    colSpan: overrides.colSpan ?? 1,
  };
}

describe('buildGridItems — explicit placement for a wall of spanning tiles (spec 258 US2)', () => {
  it('places a 2x2 hero and five 1x1 tiles on a 3x3 wall with no placeholders', () => {
    const tiles: LayoutTile[] = [
      tileAt(0, 0, { rowSpan: 2, colSpan: 2 }),
      tileAt(0, 2),
      tileAt(1, 2),
      tileAt(2, 0),
      tileAt(2, 1),
      tileAt(2, 2),
    ];

    const items = buildGridItems(3, 3, tiles);

    expect(items.filter((item) => item.tile !== null)).toHaveLength(6);
    expect(items.filter((item) => item.tile === null)).toHaveLength(0);
    // Row-major by origin, so DOM order matches a 1x1 wall's (plan.md §4.2).
    expect(items.map((item) => item.key)).toEqual(['0:0', '0:2', '1:2', '2:0', '2:1', '2:2']);
    expect(items[0]).toMatchObject({ key: '0:0', row: 0, col: 0, rowSpan: 2, colSpan: 2 });
  });

  it('shows the uncovered cells of a sparse 3x3 wall as placeholders, none under the hero', () => {
    const hero = tileAt(0, 0, { rowSpan: 2, colSpan: 2 });

    const items = buildGridItems(3, 3, [hero]);

    const tileItems = items.filter((item) => item.tile !== null);
    const placeholders = items.filter((item) => item.tile === null);
    expect(tileItems).toHaveLength(1);
    expect(placeholders).toHaveLength(5);
    // The four cells the hero's span covers — (0,1),(1,0),(1,1) plus its own
    // origin — must not also appear as placeholders.
    expect(placeholders.map((item) => item.key).sort()).toEqual(['0:2', '1:2', '2:0', '2:1', '2:2'].sort());
  });

  /**
   * CHARACTERISATION (spec 258 §8 declared pin). An all-1x1 wall must place
   * every tile and placeholder in the same row-major-by-origin order the
   * pre-feature auto-placement produced: one item per cell.
   *
   * The literal green proof for "a 1x1 wall renders exactly as today" is
   * `CellPage.test.tsx`'s `'Renders every tile of an all-1x1 wall in
   * row-major order, unchanged by this feature'`, which exercises the real
   * `CellPage.tsx` wired to `buildGridItems` — see that file for the actual
   * pin.
   */
  it('lays out an all-1x1 wall in the same row-major order as today (characterisation)', () => {
    const tiles: LayoutTile[] = [
      tileAt(0, 0),
      tileAt(0, 1),
      tileAt(0, 2),
      tileAt(1, 0),
      tileAt(1, 1),
      tileAt(1, 2),
    ];

    const items = buildGridItems(2, 3, tiles);

    expect(items.map((item) => item.key)).toEqual(['0:0', '0:1', '0:2', '1:0', '1:1', '1:2']);
    expect(items.every((item) => item.rowSpan === 1 && item.colSpan === 1)).toBe(true);
    expect(items.every((item) => item.tile !== null)).toBe(true);
  });

  it('clamps a span that runs off the grid rather than throwing (defensive, from-the-wire)', () => {
    // The aggregate already refuses this (spec 258 FR-003); the renderer
    // stays total in case a stale or malformed payload reaches it anyway
    // (plan.md §4.2).
    const badTile = tileAt(1, 1, { rowSpan: 5, colSpan: 5 });

    expect(() => buildGridItems(2, 2, [badTile])).not.toThrow();

    const items = buildGridItems(2, 2, [badTile]);
    const placed = items.find((item) => item.tile !== null);
    expect(placed).toMatchObject({ key: '1:1', rowSpan: 1, colSpan: 1 });
    expect(items.filter((item) => item.tile === null)).toHaveLength(3);
  });

  it('drops a tile whose origin sits at or past the grid’s row/col count, rather than clamping into a dead 1x1 entry (defensive, from-the-wire)', () => {
    // row === rows (or col === cols) makes `rows - tile.row` (or the col
    // equivalent) 0, so a lower-bound-only guard lets it through and the
    // span clamp forces it back up to a 1x1 that renders nothing but still
    // occupies a cell — this asserts it is dropped before that clamp runs.
    const atRowBound = tileAt(2, 0, { cameraIdentifier: CAMERA_A });
    const atColBound = tileAt(0, 2, { cameraIdentifier: CAMERA_B });

    const items = buildGridItems(2, 2, [atRowBound, atColBound]);

    expect(items).toHaveLength(4);
    expect(items.every((item) => item.tile === null)).toBe(true);
  });

  it('drops a tile with a non-finite span or a negative origin, rather than clamping to NaN (defensive, from-the-wire)', () => {
    const nanSpan = tileAt(0, 0, { rowSpan: Number.NaN, cameraIdentifier: CAMERA_A });
    const negativeOrigin = tileAt(-1, 0, { cameraIdentifier: CAMERA_B });

    const items = buildGridItems(2, 2, [nanSpan, negativeOrigin]);

    expect(items).toHaveLength(4);
    expect(items.every((item) => item.tile === null)).toBe(true);
  });

  it('drops a later tile that overlaps an earlier one, keeping the first (defensive, impossible from a valid layout)', () => {
    const first = tileAt(0, 0, { cameraIdentifier: CAMERA_A });
    const second = tileAt(0, 0, { cameraIdentifier: CAMERA_B });

    const items = buildGridItems(2, 2, [first, second]);

    expect(items).toHaveLength(4);
    const tileItems = items.filter((item) => item.tile !== null);
    expect(tileItems).toHaveLength(1);
    expect(tileItems[0]?.tile?.cameraIdentifier).toBe(CAMERA_A);
  });
});
