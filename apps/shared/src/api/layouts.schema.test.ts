import { describe, it, expect } from 'vitest';
import { editDraftRevisionSchema } from './layouts.schema.js';

/**
 * Spec 258 (#2607), ADR-0156. States the grid/tile contract `layouts.schema.ts`
 * enforces: grid dimensions 1..3, `MAX_TILES = MAX_CELLS = 9`, full-span
 * in-bounds, cell-overlap (plan.md §4.1).
 */

const CAMERA_A = '11111111-1111-1111-1111-111111111111';

interface TileOverrides {
  rowSpan?: number;
  colSpan?: number;
  cameraIdentifier?: string;
}

function tileAt(row: number, col: number, overrides: TileOverrides = {}) {
  return {
    cameraIdentifier: overrides.cameraIdentifier ?? CAMERA_A,
    overlayIdentifier: null,
    row,
    col,
    rowSpan: overrides.rowSpan ?? 1,
    colSpan: overrides.colSpan ?? 1,
  };
}

describe('layouts.schema — the 3x3 grid with spanning tiles (ADR-0156)', () => {
  it('accepts a 3x3 grid with a 2x2 hero tile and five 1x1 tiles', () => {
    const result = editDraftRevisionSchema.safeParse({
      grid: { rows: 3, cols: 3 },
      tiles: [
        tileAt(0, 0, { rowSpan: 2, colSpan: 2 }),
        tileAt(0, 2),
        tileAt(1, 2),
        tileAt(2, 0),
        tileAt(2, 1),
        tileAt(2, 2),
      ],
    });

    expect(result.success).toBe(true);
  });

  it('refuses a grid taller than the new three-row cap', () => {
    const result = editDraftRevisionSchema.safeParse({
      grid: { rows: 4, cols: 1 },
      tiles: [tileAt(0, 0)],
    });

    expect(result.success).toBe(false);
    if (result.success) return;
    const rowsIssue = result.error.issues.find((issue) => issue.path.join('.') === 'grid.rows');
    expect(rowsIssue).toBeDefined();
    // `too_big` issues carry a `maximum` field.
    expect((rowsIssue as unknown as { maximum?: number }).maximum).toBe(3);
  });

  it('flags a tile whose span runs past the grid edge, on that tile’s own index', () => {
    const result = editDraftRevisionSchema.safeParse({
      grid: { rows: 2, cols: 2 },
      tiles: [
        tileAt(0, 0),
        // Origin (0,1) is in-bounds; the span's last column (2) is not.
        tileAt(0, 1, { colSpan: 2 }),
      ],
    });

    expect(result.success).toBe(false);
    if (result.success) return;
    const issue = result.error.issues.find((candidate) => candidate.path.join('.') === 'tiles.1');
    expect(issue?.message).toBe('Tile span is out of grid bounds');
  });

  it('flags the later of two overlapping tiles, not the earlier one', () => {
    const result = editDraftRevisionSchema.safeParse({
      grid: { rows: 2, cols: 2 },
      tiles: [
        // Covers the whole 2x2 grid.
        tileAt(0, 0, { rowSpan: 2, colSpan: 2 }),
        // Its origin cell (0,1) is already claimed by the tile above.
        tileAt(0, 1),
      ],
    });

    expect(result.success).toBe(false);
    if (result.success) return;
    const laterIssue = result.error.issues.find((issue) => issue.path.join('.') === 'tiles.1');
    expect(laterIssue?.message).toBe('Two tiles overlap');
    expect(result.error.issues.some((issue) => issue.path.join('.') === 'tiles.0')).toBe(false);
  });

  it('refuses a tile with a span of zero', () => {
    const result = editDraftRevisionSchema.safeParse({
      grid: { rows: 1, cols: 1 },
      tiles: [tileAt(0, 0, { rowSpan: 0 })],
    });

    expect(result.success).toBe(false);
  });

  it('refuses ten tiles as exceeding the new nine-tile cap', () => {
    const tiles = Array.from({ length: 10 }, () => tileAt(0, 0));
    const result = editDraftRevisionSchema.safeParse({
      grid: { rows: 2, cols: 2 },
      tiles,
    });

    expect(result.success).toBe(false);
    if (result.success) return;
    const countIssue = result.error.issues.find(
      (issue) => issue.path.join('.') === 'tiles' && issue.message.includes('at most'),
    );
    expect(countIssue?.message).toBe('A grid may contain at most 9 tiles');
  });
});
