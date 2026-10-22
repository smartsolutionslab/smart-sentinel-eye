export type WallTileStatus = 'live' | 'connecting' | 'offline' | 'unauthorized' | 'forbidden';

export interface WallTile {
  key: string;
  status: WallTileStatus;
  /** When the refusal arrived, in milliseconds; meaningful only for a refused tile. */
  refusedAt?: number;
}

export const WALL_CREDENTIAL_WINDOW_MILLISECONDS = 10_000;

export const isWallCredentialRefusal = (_tiles: readonly WallTile[], _windowMilliseconds: number): boolean => false;
