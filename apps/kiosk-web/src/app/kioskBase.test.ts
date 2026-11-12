import { describe, it, expect } from 'vitest';
import { kioskBase } from './kioskBase';

describe('The kiosk serves from a path chosen by its mode flag', () => {
  it.each([undefined, '', 'kiosk', 'operator'])('Serves the ordinary kiosk from /kiosk/ for mode %s', (mode) => {
    expect(kioskBase(mode)).toBe('/kiosk/');
  });

  it.each(['wall', 'WALL', 'Wall'])('Serves the wall display from /wall/ for mode %s', (mode) => {
    expect(kioskBase(mode)).toBe('/wall/');
  });
});
