import { describe, expect, it } from 'vitest';
import { boxSchema, textSchema } from '../../api/overlays.schema.js';
import { isLegibleTextColor } from './textLegibility.js';
import { OVERLAY_SWATCHES } from './overlayPalette.js';

/**
 * Spec 463 (issue #3108) T040, plan C10. New behaviour, RED (ADR-0139): the editor's swatch
 * palette is thirteen ADR-0148 primitives under the brightness cap (ADR-0190), status hues
 * excluded (ADR-0146 item 4). Names are plain strings, values are `#RRGGBBFF` primitives.
 */

const EXPECTED_NAMES = [
  'gray-500',
  'gray-600',
  'gray-700',
  'gray-800',
  'gray-850',
  'gray-900',
  'gray-950',
  'cyan-600',
  'cyan-700',
  'cyan-800',
  'cyan-900',
  'cyan-950',
  'black',
];

function swatch(name: string) {
  return OVERLAY_SWATCHES.find((entry) => entry.name === name);
}

describe('OVERLAY_SWATCHES', () => {
  it('Offers exactly gray-500..950, cyan-600..950 and black, thirteen in all', () => {
    expect(OVERLAY_SWATCHES.map((entry) => entry.name)).toEqual(EXPECTED_NAMES);
    expect(OVERLAY_SWATCHES).toHaveLength(13);
  });

  it('Writes every value as an upper-case eight-digit #RRGGBBFF hex', () => {
    for (const { name, value } of OVERLAY_SWATCHES) {
      expect(value, name).toMatch(/^#[0-9A-F]{6}FF$/);
    }
  });

  it('Has no duplicate value', () => {
    expect(new Set(OVERLAY_SWATCHES.map((entry) => entry.value)).size).toBe(OVERLAY_SWATCHES.length);
  });

  it('Names cyan-700 #0F707DFF and gray-500 #818A98FF, as the acceptance scenarios state', () => {
    expect(swatch('cyan-700')?.value).toBe('#0F707DFF');
    expect(swatch('gray-500')?.value).toBe('#818A98FF');
  });

  it('Passes the Box schema for every swatch, so none is over the brightness cap', () => {
    for (const { name, value } of OVERLAY_SWATCHES) {
      const result = boxSchema.safeParse({
        kind: 'Box',
        color: value,
        normalizedX: 0,
        normalizedY: 0,
        normalizedWidth: 0.5,
        normalizedHeight: 0.5,
      });
      expect(result.success, name).toBe(true);
    }
  });

  it('Passes the Text schema and the legibility rule at full alpha for every swatch', () => {
    for (const { name, value } of OVERLAY_SWATCHES) {
      expect(isLegibleTextColor(value), name).toBe(true);
      const result = textSchema.safeParse({
        kind: 'Text',
        color: value,
        text: 'T1',
        fontSizePx: 32,
        normalizedX: 0,
        normalizedY: 0,
        normalizedWidth: 0.5,
        normalizedHeight: 0.5,
      });
      expect(result.success, name).toBe(true);
    }
  });

  it('Offers no green, red or amber primitive', () => {
    for (const { name } of OVERLAY_SWATCHES) {
      expect(name).not.toMatch(/^(green|red|amber)-/);
    }
  });

  it('Names a swatch with a plain string, never a var(--...) token reference', () => {
    for (const { name, value } of OVERLAY_SWATCHES) {
      expect(name).not.toContain('var(');
      expect(value).not.toContain('var(');
    }
  });
});
