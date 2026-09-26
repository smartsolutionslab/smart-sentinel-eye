import { z } from 'zod';

/**
 * Max tiles / cells on a wall — mirrors the backend
 * `GridDimensions.MaxTiles` / `MaxCells` (ADR-0156 §2, raising the cap from
 * 4 to 9). The one justified cross-tier duplication: the browser validates
 * before POST for inline feedback, the aggregate validates authoritatively.
 */
export const MAX_TILES = 9;
export const MAX_CELLS = 9;

const GUID = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;

/** A tile's claimed rectangle: origin `(row, col)` plus its `rowSpan × colSpan` extent. */
export interface TileSpan {
  row: number;
  col: number;
  rowSpan: number;
  colSpan: number;
}

/**
 * Whether two tile spans claim any cell in common (ADR-0156 §2's "no two
 * tiles overlap" invariant, same-tier shared so `gridDesignerModel.ts`'s
 * `spanOptions` doesn't reimplement the rectangle-intersection test
 * independently. `refineGrid` below still enumerates cells itself — it
 * needs per-cell attribution (which tile a cell belongs to), which a
 * pairwise boolean can't give it — but the boolean test itself is this one.
 */
export function spansOverlap(a: TileSpan, b: TileSpan): boolean {
  return (
    a.row < b.row + b.rowSpan && b.row < a.row + a.rowSpan && a.col < b.col + b.colSpan && b.col < a.col + a.colSpan
  );
}

// One grid tile. Mirrors the backend TileDto / the FE LayoutTile shape: a
// required camera, an optional overlay (`null`/omitted == unbound), at
// zero-indexed (row, col), claiming a `rowSpan × colSpan` rectangle whose
// origin is that cell (ADR-0156 §2). Omitted spans default to 1×1 on the
// backend; the browser schema requires them explicitly because the designer
// (US3) and every other caller always states them.
const tileSchema = z.object({
  cameraIdentifier: z.string().regex(GUID, 'cameraIdentifier must be a Guid'),
  overlayIdentifier: z.string().regex(GUID, 'overlayIdentifier must be a Guid').nullable().optional(),
  row: z.number().int().min(0),
  col: z.number().int().min(0),
  rowSpan: z.number().int().min(1),
  colSpan: z.number().int().min(1),
});

const gridSchema = z.object({
  rows: z.number().int().min(1).max(3),
  cols: z.number().int().min(1).max(3),
});

// The grid invariants the backend enforces (ADR-0156 §2): ≥ 1 tile, every
// tile's full span in-bounds, no two spans overlapping a cell, ≤ MAX_TILES
// populated / ≤ MAX_CELLS grid. Surfaced inline so the designer can flag a
// bad wall before POST.
const refineGrid = (
  value: {
    grid: { rows: number; cols: number };
    tiles: Array<{ row: number; col: number; rowSpan: number; colSpan: number }>;
  },
  ctx: z.RefinementCtx,
): void => {
  const { grid, tiles } = value;

  if (grid.rows * grid.cols > MAX_CELLS) {
    ctx.addIssue({ code: z.ZodIssueCode.custom, path: ['grid'], message: `A grid may not exceed ${MAX_CELLS} cells` });
  }
  if (tiles.length > MAX_TILES) {
    ctx.addIssue({
      code: z.ZodIssueCode.custom,
      path: ['tiles'],
      message: `A grid may contain at most ${MAX_TILES} tiles`,
    });
  }

  // First tile to claim a cell owns it; a later claimant whose span shares
  // an already-claimed cell is flagged on its OWN index, not the earlier
  // tile's — mirrors the backend's pairwise `Overlaps` check (ADR-0156 §2),
  // which likewise reports the violation once per offending tile.
  const claimedBy = new Map<string, number>();
  tiles.forEach((tile, index) => {
    const outOfBounds = tile.row + tile.rowSpan > grid.rows || tile.col + tile.colSpan > grid.cols;
    if (outOfBounds) {
      ctx.addIssue({ code: z.ZodIssueCode.custom, path: ['tiles', index], message: 'Tile span is out of grid bounds' });
    }

    // An out-of-bounds tile's in-grid portion is still real territory a
    // later tile can collide with, so it must still be claimed here — only
    // cells that actually exist on the grid are eligible keys (clamping,
    // not skipping the claim entirely, is what kept the earlier bug alive).
    const rowEnd = Math.min(tile.row + tile.rowSpan, grid.rows);
    const colEnd = Math.min(tile.col + tile.colSpan, grid.cols);
    const cells: string[] = [];
    let overlaps = false;
    for (let row = tile.row; row < rowEnd; row += 1) {
      for (let col = tile.col; col < colEnd; col += 1) {
        const key = `${row},${col}`;
        cells.push(key);
        if (claimedBy.has(key)) {
          overlaps = true;
        }
      }
    }

    if (!outOfBounds && overlaps) {
      ctx.addIssue({ code: z.ZodIssueCode.custom, path: ['tiles', index], message: 'Two tiles overlap' });
    }

    // An in-bounds, overlapping tile doesn't get to claim the cell it lost
    // the race for — the earlier claimant keeps it, same as before this fix.
    if (outOfBounds || !overlaps) {
      for (const key of cells) {
        claimedBy.set(key, index);
      }
    }
  });
};

export const createLayoutDraftSchema = z
  .object({
    name: z
      .string()
      .trim()
      .min(1, 'Name is required')
      .max(80, 'Name must be 80 characters or fewer')
      .refine((s) => !/[\r\n]/.test(s), 'Name must not contain a line break'),
    grid: gridSchema,
    tiles: z.array(tileSchema).min(1, 'A layout must contain at least one tile'),
  })
  .superRefine(refineGrid);

export const editDraftRevisionSchema = z
  .object({
    grid: gridSchema,
    tiles: z.array(tileSchema).min(1, 'A layout must contain at least one tile'),
  })
  .superRefine(refineGrid);

export type LayoutTileInput = z.infer<typeof tileSchema>;
export type GridInput = z.infer<typeof gridSchema>;
export type CreateLayoutDraftInput = z.infer<typeof createLayoutDraftSchema>;
export type EditDraftRevisionInput = z.infer<typeof editDraftRevisionSchema>;
