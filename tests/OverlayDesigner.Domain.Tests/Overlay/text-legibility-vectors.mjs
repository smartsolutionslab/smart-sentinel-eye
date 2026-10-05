#!/usr/bin/env node
// Independent reference implementation of the text-legibility rule
// (plan.md §"TextLegibility", ADR-0165 §2, spec 300 FR-020).
//
// This script is NEITHER the C# (`TextLegibility.cs`) NOR the TypeScript
// (`textLegibility.ts`) implementation. It is a third, from-scratch coding
// of the same WCAG-derived formula, written only to produce the fixture
// `text-legibility-vectors.json` that both of the real implementations are
// checked against (`TextLegibilityVectorTests` in C#, `textLegibility.test.ts`
// in TS). If any of the three drift from the other two, its own vector test
// fails — that is the whole point of a *third*, independently-written
// generator: two implementations agreeing proves nothing when they could
// share the same mistake.
//
// Formula (plan.md §"TextLegibility"):
//  1. Parse R, G, B, A from the eight hex digits into [0, 1]. Linearise
//     R, G, B (c <= 0.04045 ? c/12.92 : ((c+0.055)/1.055)^2.4). `Ls` is the
//     WCAG relative luminance of the opaque colour.
//  2. Lighter composite over white video =
//       max(lum(A*rgb + (1-A)), A*Ls + (1-A))
//     — the first term composites in sRGB-encoded space then measures
//     luminance; the second composites directly in linear light.
//     Darker composite over black video = min(lum(A*rgb), A*Ls).
//  3. contrastLight = 1.05 / (lighter + 0.05); contrastDark = (darker + 0.05) / 0.05.
//  4. Legible iff max(contrastLight, contrastDark) >= 4.5.
//
// Run: `node text-legibility-vectors.mjs` — rewrites
// `text-legibility-vectors.json` beside this script.

import { writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const MINIMUM_CONTRAST = 4.5;

function hexByte(two) {
  return parseInt(two, 16) / 255;
}

/** Parses "#RRGGBBAA" into {r,g,b,a} in [0,1]. */
function parseColor(color) {
  if (!/^#[0-9A-Fa-f]{8}$/.test(color)) {
    throw new Error(`not an eight-digit colour: ${color}`);
  }
  return {
    r: hexByte(color.slice(1, 3)),
    g: hexByte(color.slice(3, 5)),
    b: hexByte(color.slice(5, 7)),
    a: hexByte(color.slice(7, 9)),
  };
}

function linearise(c) {
  return c <= 0.04045 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4);
}

/** WCAG relative luminance of an sRGB-encoded [0,1] triple. */
function relativeLuminance(r, g, b) {
  return 0.2126 * linearise(r) + 0.7152 * linearise(g) + 0.0722 * linearise(b);
}

function worstCaseContrasts(color) {
  const { r, g, b, a } = parseColor(color);
  const ls = relativeLuminance(r, g, b);

  const lumEncodedOverWhite = relativeLuminance(a * r + (1 - a), a * g + (1 - a), a * b + (1 - a));
  const lumLinearOverWhite = a * ls + (1 - a);
  const lighter = Math.max(lumEncodedOverWhite, lumLinearOverWhite);

  const lumEncodedOverBlack = relativeLuminance(a * r, a * g, a * b);
  const lumLinearOverBlack = a * ls;
  const darker = Math.min(lumEncodedOverBlack, lumLinearOverBlack);

  const contrastLight = 1.05 / (lighter + 0.05);
  const contrastDark = (darker + 0.05) / 0.05;
  return { contrastLight, contrastDark };
}

function isLegible(color) {
  const { contrastLight, contrastDark } = worstCaseContrasts(color);
  return Math.max(contrastLight, contrastDark) >= MINIMUM_CONTRAST;
}

function inkFor(color) {
  const { contrastLight, contrastDark } = worstCaseContrasts(color);
  return contrastDark >= contrastLight ? 'dark' : 'light';
}

function toHexByte(value) {
  return value.toString(16).toUpperCase().padStart(2, '0');
}

/** Scans alpha 00..FF and returns the first (lowest) legible two-digit hex alpha for this RGB. */
function minimumAlphaHex(rgbSixDigits) {
  for (let alpha = 0; alpha <= 255; alpha++) {
    const candidate = `#${rgbSixDigits}${toHexByte(alpha)}`;
    if (isLegible(candidate)) {
      return toHexByte(alpha);
    }
  }
  throw new Error(`no legible alpha found for ${rgbSixDigits} — FF should always be legible`);
}

function vectorFor(color) {
  return {
    color,
    legible: isLegible(color),
    ink: inkFor(color),
    minimumAlpha: minimumAlphaHex(color.slice(1, 7)),
  };
}

function withAlpha(rgbSixDigits, alphaHex) {
  return `#${rgbSixDigits}${alphaHex}`;
}

function oneStepBelow(alphaHex) {
  const value = parseInt(alphaHex, 16);
  if (value <= 0) {
    throw new Error(`cannot step below alpha ${alphaHex}`);
  }
  return toHexByte(value - 1);
}

const vectors = [];
const seen = new Set();

function add(color) {
  if (seen.has(color)) {
    return;
  }
  seen.add(color);
  vectors.push(vectorFor(color));
}

// 1. Every colour appearing in spec.md's scenarios (scenario outlines,
// the happy path, the ink table, the end-to-end procedure).
const specScenarioColors = [
  '#D32F2FFF', // #d32f2f canonicalised — box + caption, happy path
  '#FFEB3BFF', // "Caution" caption / ink table
  '#FFFFFF80', // "Line 1" translucent caption, above white's floor
  '#FFA00080', // half-transparent ellipse stroke, e2e procedure
  '#D32F2F00', // Box, invisible stroke accepted
  '#FFA00040', // Ellipse, translucent stroke accepted
  '#FFFFFF75', // white's minimum alpha — accepted
  '#FFFFFF74', // one step below white's floor — refused
  '#D32F2FF9', // red's minimum alpha — accepted
  '#D32F2FF8', // one step below red's floor — refused
  '#D32F2F80', // far below red's floor — refused, message carries F9
  '#1565C0FF', // ink table: blue, white ink
  '#757575FF', // ink table / opaque threshold pair: mid-grey, white ink
  '#767676FF', // ink table / opaque threshold pair: mid-grey, black ink
  '#FFA000CC', // ellipse-only scenario stroke colour
  '#FFFFFFD9', // the default colour — pre-migration backfill
];
specScenarioColors.forEach(add);

// 2. Boundary pairs, named explicitly (already covered above by name, kept
// as an explicit step so the list reads as a checklist against plan.md).
['#FFFFFF74', '#FFFFFF75', '#D32F2FF8', '#D32F2FF9'].forEach(add);

// 3. The opaque threshold pair.
['#757575FF', '#767676FF'].forEach(add);

// 4. The 8 cube corners, at FF and at their own minimum alpha.
const cubeCorners = [
  '000000', '0000FF', '00FF00', '00FFFF',
  'FF0000', 'FF00FF', 'FFFF00', 'FFFFFF',
];
for (const rgb of cubeCorners) {
  add(withAlpha(rgb, 'FF'));
  const floor = minimumAlphaHex(rgb);
  add(withAlpha(rgb, floor));
}

// 5. 64 lattice colours (4x4x4 over {00,55,AA,FF} per channel), at their
// minimum alpha and one step below it.
const latticeLevels = ['00', '55', 'AA', 'FF'];
for (const r of latticeLevels) {
  for (const g of latticeLevels) {
    for (const b of latticeLevels) {
      const rgb = `${r}${g}${b}`;
      const floor = minimumAlphaHex(rgb);
      add(withAlpha(rgb, floor));
      add(withAlpha(rgb, oneStepBelow(floor)));
    }
  }
}

const outputPath = join(dirname(fileURLToPath(import.meta.url)), 'text-legibility-vectors.json');
writeFileSync(outputPath, `${JSON.stringify(vectors, null, 2)}\n`, 'utf8');
console.log(`wrote ${vectors.length} vectors to ${outputPath}`);

// Sanity check against the exact figures spec.md's reference tables name,
// so a formula bug is caught here rather than discovered later by the C#/TS
// tests disagreeing with the spec in the same way.
const expectations = [
  ['FFFFFF', '75'],
  ['FFA000', 'A4'],
  ['FFEB3B', '80'],
  ['1565C0', 'F1'],
  ['000000', 'D1'],
  ['D32F2F', 'F9'],
  ['767676', 'FB'],
  ['757575', 'FE'],
];
let mismatches = 0;
for (const [rgb, expected] of expectations) {
  const actual = minimumAlphaHex(rgb);
  const status = actual === expected ? 'OK' : 'MISMATCH';
  if (actual !== expected) mismatches++;
  console.log(`${status} minimumAlpha(${rgb}) = ${actual} (spec.md: ${expected})`);
}
// spec.md names the ink by colour ("black"/"white"); inkFor (plan.md) names
// it by role ("dark"/"light") because that is what the CSS token is keyed
// on (--color-overlay-ink-dark / -light). dark ink is painted black, light
// ink is painted white (tokens.css), so the two vocabularies translate
// one-for-one: dark <-> black, light <-> white.
const inkExpectations = [
  ['#FFEB3BFF', 'black', 'dark'],
  ['#1565C0FF', 'white', 'light'],
  ['#757575FF', 'white', 'light'],
  ['#767676FF', 'black', 'dark'],
  ['#FFFFFF75', 'black', 'dark'],
  ['#D32F2FF9', 'white', 'light'],
];
for (const [color, specLabel, expected] of inkExpectations) {
  const actual = inkFor(color);
  const status = actual === expected ? 'OK' : 'MISMATCH';
  if (actual !== expected) mismatches++;
  console.log(`${status} inkFor(${color}) = ${actual} (spec.md: ${specLabel} ink => ${expected})`);
}
if (mismatches > 0) {
  console.error(`${mismatches} mismatch(es) against spec.md's reference tables.`);
  process.exitCode = 1;
}
