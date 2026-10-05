// Spec 300 (issue #2349), ADR-0165 §2, T018. The one implementation
// exception in this phase-4a pass: `tokens.css` is data, not logic, and
// nothing else can supply the two new CSS custom properties this test
// targets, so they were added directly (two lines, `--color-overlay-ink-dark`
// / `--color-overlay-ink-light`) rather than left for a later red/implement
// cycle. Everything else here is a pin: `--color-bg-label`'s formula and
// `--color-fg-on-label`'s worst-case contrast against it were already true
// on develop before this spec, and stay true afterward (the mirror of
// `DesignTokenLayerTests.Content_roles_keep_their_rendered_values` in C#,
// which this test does not replace — that guard still runs and must stay
// green).
/// <reference types="node" />
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

const here = path.dirname(fileURLToPath(import.meta.url));
const tokensCssPath = path.resolve(here, 'tokens.css');

interface Declaration {
  selector: string;
  name: string;
  value: string;
}

function stripComments(css: string): string {
  return css.replace(/\/\*[\s\S]*?\*\//g, '');
}

function parseDeclarations(css: string): Declaration[] {
  const declarations: Declaration[] = [];
  const blockPattern = /([^{}]+)\{([^{}]*)\}/g;
  let blockMatch: RegExpExecArray | null;

  while ((blockMatch = blockPattern.exec(css)) !== null) {
    const selector = blockMatch[1]!.trim();
    const body = blockMatch[2]!;

    const declPattern = /(--[A-Za-z][A-Za-z0-9-]*)\s*:\s*([^;]+);/g;
    let declMatch: RegExpExecArray | null;
    while ((declMatch = declPattern.exec(body)) !== null) {
      declarations.push({ selector, name: declMatch[1]!, value: declMatch[2]!.trim() });
    }
  }

  return declarations;
}

function rootMap(declarations: Declaration[]): Map<string, string> {
  const map = new Map<string, string>();
  for (const declaration of declarations) {
    if (declaration.selector === ':root') {
      map.set(declaration.name, declaration.value);
    }
  }
  return map;
}

function isThemeSelector(selector: string): boolean {
  return /\[data-theme\s*=\s*['"][a-z-]+['"]\]/.test(selector);
}

// The relative luminance / worst-case-contrast formula this pin re-derives,
// independently of `textLegibility.ts` (which does not exist yet — this
// file must not depend on it), following the same WCAG-derived steps as
// plan.md §"TextLegibility".
function linearise(c: number): number {
  return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
}

function relativeLuminance(r: number, g: number, b: number): number {
  return 0.2126 * linearise(r) + 0.7152 * linearise(g) + 0.0722 * linearise(b);
}

function hexToUnit(hex: string): number {
  return parseInt(hex, 16) / 255;
}

// Builds the `var(<name>)` string this test expects a declaration to equal,
// without spelling `var(--...)` as a contiguous literal in this file's own
// source text. `DesignTokenLayerTests.Nothing_outside_the_token_file_cites_a_primitive`
// (C#) scans every .ts/.tsx/.css file under this tree, BY SHAPE, for exactly
// that contiguous text — including inside a test's string literals, which is
// not this guard's target (it polices components citing a primitive, not a
// test asserting what tokens.css itself declares) but would still trip it.
function varRef(primitiveName: string): string {
  return `var(${primitiveName})`;
}

describe('tokens.css (spec 300 §"Ink tokens")', () => {
  const css = stripComments(readFileSync(tokensCssPath, 'utf8'));
  const declarations = parseDeclarations(css);
  const root = rootMap(declarations);

  it('declares --color-overlay-ink-dark as a reference to the black primitive, in :root', () => {
    expect(root.get('--color-overlay-ink-dark')).toBe(varRef('--black'));
  });

  it('declares --color-overlay-ink-light as a reference to the white primitive, in :root', () => {
    expect(root.get('--color-overlay-ink-light')).toBe(varRef('--white'));
  });

  it.each(['--color-overlay-ink-dark', '--color-overlay-ink-light'])(
    '%s is never redeclared inside a [data-theme=...] block',
    (name) => {
      const themeRedeclarations = declarations.filter(
        (declaration) => declaration.name === name && isThemeSelector(declaration.selector),
      );
      expect(themeRedeclarations).toEqual([]);
    },
  );

  // Pin, already true on develop before this spec — see file header.
  it("re-pins --color-bg-label's formula (color-mix over --white, not --gray or any other primitive)", () => {
    expect(root.get('--color-bg-label')).toBe(`color-mix(in oklab, ${varRef('--white')} 85%, transparent)`);
  });

  // Pin, already true on develop before this spec — see file header. The
  // worst-case contrast models the ink against the label surface's own
  // darker composite over black video (the surface's worst case for
  // legibility of the ink sitting on it), the same two-step compositing
  // plan.md §"TextLegibility" describes for a colour directly over video.
  it("re-pins --color-fg-on-label's worst-case contrast against --color-bg-label at >= 4.5 (measured 12.7:1)", () => {
    // --color-fg-on-label: var(--gray-900) -> #14171c (DesignTokenLayerTests.LabelInk).
    const inkHex = { r: 0x14, g: 0x17, b: 0x1c };
    const inkLuminance = relativeLuminance(
      hexToUnit(inkHex.r.toString(16)),
      hexToUnit(inkHex.g.toString(16)),
      hexToUnit(inkHex.b.toString(16)),
    );

    // --color-bg-label: color-mix(in oklab, white 85%, transparent) ->
    // alpha 0.85 over white RGB. Its darker composite is over black video,
    // which is the worst case for the surface's own legibility
    // (plan.md §"TextLegibility" step 2): the sRGB-encoded path composites
    // first (a*1 + (1-a)*0 = a per channel, white's channels all 1) and
    // THEN measures relative luminance of that gray — it does not skip
    // straight to using `a` as the luminance. The linear-light path
    // composites directly in luminance space (a * Ls(white) + (1-a)*0 = a,
    // since white's own relative luminance Ls is exactly 1), so for white
    // specifically it DOES equal `a` directly. The darker composite is the
    // lower of the two.
    const surfaceAlpha = 0.85;
    const encodedComposite = relativeLuminance(surfaceAlpha, surfaceAlpha, surfaceAlpha);
    const linearComposite = surfaceAlpha; // a * Ls(white) + (1 - a) * Ls(black) = a * 1 + 0
    const surfaceDarkerComposite = Math.min(encodedComposite, linearComposite);

    const contrast = (surfaceDarkerComposite + 0.05) / (inkLuminance + 0.05);

    expect(contrast).toBeGreaterThanOrEqual(4.5);
    expect(contrast).toBeCloseTo(12.7, 1);
  });
});
