import { describe, it, expect } from 'vitest';
import {
  WALL_CREDENTIAL_WINDOW_MILLISECONDS,
  isWallCredentialRefusal,
  type WallTile,
  type WallTileStatus,
} from './wallCredential.js';

/**
 * Spec 439 (issue #2781) T003 — RED. The wall-level credential rule: every
 * mounted tile refused 401, at least two tiles, latest within 10 000 ms of the
 * earliest (inclusive). 403 never counts.
 */

const refused = (key: string, at: number, status: WallTileStatus = 'unauthorized'): WallTile => ({
  key,
  status,
  refusedAt: at,
});
const running = (key: string, status: WallTileStatus = 'live'): WallTile => ({ key, status });
const windowMs = WALL_CREDENTIAL_WINDOW_MILLISECONDS;

describe('isWallCredentialRefusal', () => {
  it('Uses a ten second window', () => {
    expect(WALL_CREDENTIAL_WINDOW_MILLISECONDS).toBe(10_000);
  });

  it('Does not enter for a wall with no tiles', () => {
    expect(isWallCredentialRefusal([], windowMs)).toBe(false);
  });

  it('Does not enter for a single tile refused 401', () => {
    expect(isWallCredentialRefusal([refused('a', 0)], windowMs)).toBe(false);
  });

  it('Enters when two tiles are refused 401 at the same instant', () => {
    expect(isWallCredentialRefusal([refused('a', 0), refused('b', 0)], windowMs)).toBe(true);
  });

  it('Enters when the two refusals are exactly 10 000 ms apart', () => {
    expect(isWallCredentialRefusal([refused('a', 0), refused('b', 10_000)], windowMs)).toBe(true);
  });

  it('Does not enter when the two refusals are 10 001 ms apart', () => {
    expect(isWallCredentialRefusal([refused('a', 0), refused('b', 10_001)], windowMs)).toBe(false);
  });

  it('Measures the window from the earliest to the latest regardless of order', () => {
    expect(isWallCredentialRefusal([refused('b', 10_000), refused('a', 0)], windowMs)).toBe(true);
    expect(isWallCredentialRefusal([refused('b', 10_001), refused('a', 0)], windowMs)).toBe(false);
  });

  it('Does not enter when one tile is 401 and the other 403', () => {
    expect(isWallCredentialRefusal([refused('a', 0), refused('b', 0, 'forbidden')], windowMs)).toBe(false);
  });

  it('Does not enter when both tiles are 403', () => {
    expect(isWallCredentialRefusal([refused('a', 0, 'forbidden'), refused('b', 0, 'forbidden')], windowMs)).toBe(false);
  });

  it.each<WallTileStatus>(['live', 'connecting', 'offline'])('Does not enter while a third tile is %s', (status) => {
    expect(isWallCredentialRefusal([refused('a', 0), refused('b', 100), running('c', status)], windowMs)).toBe(false);
  });

  it('Enters once the one non-refused tile is unmounted', () => {
    const tiles = [refused('a', 0), refused('b', 100), running('c')];
    expect(isWallCredentialRefusal(tiles, windowMs)).toBe(false);
    expect(isWallCredentialRefusal(tiles.slice(0, 2), windowMs)).toBe(true);
  });

  it('Enters when the third tile later refuses 401 within the window of the first', () => {
    expect(isWallCredentialRefusal([refused('a', 0), refused('b', 100), refused('c', 9_000)], windowMs)).toBe(true);
  });
});
