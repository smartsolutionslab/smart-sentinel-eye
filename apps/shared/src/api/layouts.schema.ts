import { z } from 'zod';

/**
 * Max tiles / cells on a wall — mirrors the backend
 * `GridDimensions.MaxTiles` / `MaxCells` (ADR-0112 §4). The one justified
 * cross-tier duplication: the browser validates before POST for inline
 * feedback, the aggregate validates authoritatively.
 */
export const MAX_TILES = 4;
export const MAX_CELLS = 4;

const GUID = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;

// One grid tile. Mirrors the backend TileDto / the FE LayoutTile shape:
// a required camera, an optional overlay (`null`/omitted == unbound), at
// zero-indexed (row, col).
const tileSchema = z.object({
  cameraIdentifier: z.string().regex(GUID, 'cameraIdentifier must be a Guid'),
  overlayIdentifier: z.string().regex(GUID, 'overlayIdentifier must be a Guid').nullable().optional(),
  row: z.number().int().min(0),
  col: z.number().int().min(0),
});

const gridSchema = z.object({
  rows: z.number().int().min(1).max(2),
  cols: z.number().int().min(1).max(2),
});

// The four grid invariants the backend enforces (ADR-0112 §2): ≥ 1 tile,
// in-bounds, no duplicate position, ≤ MAX_TILES populated / ≤ MAX_CELLS
// grid. Surfaced inline so the designer can flag a bad wall before POST.
const refineGrid = (
  value: { grid: { rows: number; cols: number }; tiles: Array<{ row: number; col: number }> },
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

  const seen = new Set<string>();
  tiles.forEach((tile, index) => {
    if (tile.row >= grid.rows || tile.col >= grid.cols) {
      ctx.addIssue({ code: z.ZodIssueCode.custom, path: ['tiles', index], message: 'Tile is out of grid bounds' });
    }
    const key = `${tile.row},${tile.col}`;
    if (seen.has(key)) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['tiles', index],
        message: 'Two tiles occupy the same position',
      });
    }
    seen.add(key);
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
