// Spec 463 (#3108), ADR-0190, T007. The console's schema refuses the same colours the domain refuses
// (relative luminance above 0.35, alpha ignored), and the cap constant is pinned to the backend source
// (FR-020). These live in a file of their own so the pre-existing schema test is not edited.
//
// `OVERLAY_COLOR_MAX_LUMINANCE` and `relativeLuminance` do not exist yet; they are read off the module
// namespace so a missing export is an assertion failure here, not a silently-undefined named import.
/// <reference types="node" />
import { existsSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import * as schemaModule from './overlays.schema.js';
import * as legibilityModule from '../ui/composites/textLegibility.js';
import { DEFAULT_OVERLAY_COLOR, overlayElementSchema } from './overlays.schema.js';

const here = path.dirname(fileURLToPath(import.meta.url));
const colorSource = path.resolve(here, '..', '..', '..', '..', 'src/OverlayDesigner/Domain/Overlay/OverlayColor.cs');

function geometry() {
  return { normalizedX: 0.1, normalizedY: 0.1, normalizedWidth: 0.3, normalizedHeight: 0.08 };
}

function box(color: string) {
  return { kind: 'Box', color, ...geometry() };
}

function text(color: string) {
  return { kind: 'Text', color, text: 'Line 1', fontSizePx: 32, ...geometry() };
}

describe('OVERLAY_COLOR_MAX_LUMINANCE (ADR-0190, FR-020)', () => {
  it('is 0.35 and equals OverlayColor.MaximumRelativeLuminance read straight from the backend source', () => {
    expect(existsSync(colorSource), `expected ${colorSource} to exist`).toBe(true);
    const match = readFileSync(colorSource, 'utf8').match(/public const double MaximumRelativeLuminance = ([\d.]+);/);

    expect(
      match,
      'OverlayColor.cs no longer declares `public const double MaximumRelativeLuminance = <n>;` in the form this test parses',
    ).not.toBeNull();

    const exported = (schemaModule as Record<string, unknown>)['OVERLAY_COLOR_MAX_LUMINANCE'];
    expect(exported).toBe(0.35);
    expect(exported).toBe(Number(match![1]));
  });

  it('DEFAULT_OVERLAY_COLOR is the nearest allowed colour of the old white default', () => {
    expect(DEFAULT_OVERLAY_COLOR).toBe('#9F9F9FD9');
  });
});

describe('relativeLuminance (textLegibility.ts)', () => {
  it('is exported and returns WCAG relative luminance, alpha ignored', () => {
    const relativeLuminance = (legibilityModule as Record<string, unknown>)['relativeLuminance'] as
      ((hex: string) => number) | undefined;

    expect(typeof relativeLuminance).toBe('function');
    expect(relativeLuminance!('#9F9F9FFF')).toBeCloseTo(0.3467, 4);
    expect(relativeLuminance!('#9F9F9F10')).toBeCloseTo(0.3467, 4);
    expect(relativeLuminance!('#A0A0A0FF')).toBeCloseTo(0.3515, 4);
    expect(relativeLuminance!('#FFFFFFFF')).toBeCloseTo(1, 6);
  });
});

describe('overlayElementSchema: luminance cap (ADR-0190)', () => {
  it('accepts a Box exactly at the cap (#9F9F9F)', () => {
    expect(overlayElementSchema.safeParse(box('#9F9F9F')).success).toBe(true);
  });

  it.each(['#A0A0A0', '#FFFFFF', '#FFFFFF10', '#FFEB3BFF'])(
    'rejects the Box colour %s on path color naming 0.35',
    (color) => {
      const result = overlayElementSchema.safeParse(box(color));

      expect(result.success).toBe(false);
      if (!result.success) {
        const issue = result.error.issues.find((candidate) => candidate.path.join('.') === 'color');
        expect(issue, 'expected an issue on path color').toBeDefined();
        expect(issue!.message).toContain('0.35');
      }
    },
  );

  it('rejects an over-the-cap Text colour at full opacity too', () => {
    const result = overlayElementSchema.safeParse(text('#FFFFFFFF'));

    expect(result.success).toBe(false);
    if (!result.success) {
      expect(
        result.error.issues.some((issue) => issue.path.join('.') === 'color' && issue.message.includes('0.35')),
      ).toBe(true);
    }
  });

  it('accepts the new default as a Text colour', () => {
    expect(overlayElementSchema.safeParse(text(DEFAULT_OVERLAY_COLOR)).success).toBe(true);
  });
});
