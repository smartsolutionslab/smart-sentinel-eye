export type WallTileStatus = 'live' | 'connecting' | 'offline' | 'unauthorized' | 'forbidden';

export interface WallTile {
  key: string;
  status: WallTileStatus;
  /** When the refusal arrived, in milliseconds; meaningful only for a refused tile. */
  refusedAt?: number;
}

export const WALL_CREDENTIAL_WINDOW_MILLISECONDS = 10_000;

const MINIMUM_TILES = 2;

/**
 * Whether the whole wall has lost its credential: at least two tiles, every
 * one refused 401, the first and last refusal no further apart than the
 * window (inclusive). A 403 is a policy answer for one camera, never this.
 */
export const isWallCredentialRefusal = (tiles: readonly WallTile[], windowMilliseconds: number): boolean => {
  if (tiles.length < MINIMUM_TILES) return false;
  const times: number[] = [];
  for (const tile of tiles) {
    if (tile.status !== 'unauthorized' || tile.refusedAt === undefined) return false;
    times.push(tile.refusedAt);
  }
  return Math.max(...times) - Math.min(...times) <= windowMilliseconds;
};
