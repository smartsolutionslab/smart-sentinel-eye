/**
 * The frontend's half of the compositor-independent worst-case text
 * legibility rule (spec 300, #2349, ADR-0165 §2). Same formula as the
 * domain's `TextLegibility`
 * (`src/OverlayDesigner/Domain/Overlay/TextLegibility.cs`) and the
 * independent reference generator `text-legibility-vectors.mjs` — this file
 * is a close transliteration of the generator, not a clean-room
 * reimplementation (ADR-0165 requires independence only for the generator
 * itself). It is pinned to the generator's committed vectors
 * (`text-legibility-vectors.json`, shared with the domain's own vector test)
 * so a transcription slip between the three shows up as a failing vector.
 * That catches drift, not a shared conceptual error in the formula — a
 * mistake all three implementations agree on would still pass every vector.
 *
 * A translucent text surface sits over moving video with no fixed
 * luminance, so the only defensible guarantee is a worst case over every
 * possible video pixel, checked under both sRGB-encoded and linear-light
 * alpha compositing so the guarantee does not depend on a kiosk's GPU path.
 *
 * Every function here is pure, allocation-only and DOM-free. Per paint,
 * `inkFor` is three luminance evaluations (nine `pow`), with no loop and no
 * `getComputedStyle` — cheap enough for the composite + render leg
 * (constitution §IV, ≤ 50 ms). `minimumTextAlpha` scans 0..255 and is used
 * only by the form error and PR-B's slider minimum, never per paint.
 */

const MINIMUM_CONTRAST = 4.5;

export interface ParsedColor {
  r: number;
  g: number;
  b: number;
  a: number;
}

/** Parses an eight-digit `#RRGGBBAA` colour into 0-1 components. */
export function parseColor(hex: string): ParsedColor {
  return {
    r: parseInt(hex.slice(1, 3), 16) / 255,
    g: parseInt(hex.slice(3, 5), 16) / 255,
    b: parseInt(hex.slice(5, 7), 16) / 255,
    a: parseInt(hex.slice(7, 9), 16) / 255,
  };
}

function linearise(c: number): number {
  return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
}

function relativeLuminance(r: number, g: number, b: number): number {
  return 0.2126 * linearise(r) + 0.7152 * linearise(g) + 0.0722 * linearise(b);
}

export interface WorstCaseContrasts {
  light: number;
  dark: number;
}

/**
 * The lighter composite over white video and the darker composite over
 * black video, each taken across both sRGB-encoded and linear-light
 * compositing (ADR-0165 §2). `color` is an eight-digit `#RRGGBBAA` hex
 * string.
 */
export function worstCaseContrasts(color: string): WorstCaseContrasts {
  const { r, g, b, a } = parseColor(color);
  const ls = relativeLuminance(r, g, b);

  const lumEncodedOverWhite = relativeLuminance(a * r + (1 - a), a * g + (1 - a), a * b + (1 - a));
  const lumLinearOverWhite = a * ls + (1 - a);
  const lighter = Math.max(lumEncodedOverWhite, lumLinearOverWhite);

  const lumEncodedOverBlack = relativeLuminance(a * r, a * g, a * b);
  const lumLinearOverBlack = a * ls;
  const darker = Math.min(lumEncodedOverBlack, lumLinearOverBlack);

  const light = 1.05 / (lighter + 0.05);
  const dark = (darker + 0.05) / 0.05;
  return { light, dark };
}

/**
 * Which ink paints best on `color` — the larger of the two worst-case
 * contrasts, ties going to dark (ADR-0165 §2).
 */
export function inkFor(color: string): 'dark' | 'light' {
  const { light, dark } = worstCaseContrasts(color);
  return dark >= light ? 'dark' : 'light';
}

/** True when `color`, at its own alpha, clears 4.5:1 under either compositing model. */
export function isLegibleTextColor(color: string): boolean {
  const { light, dark } = worstCaseContrasts(color);
  return Math.max(light, dark) >= MINIMUM_CONTRAST;
}

/**
 * The lowest two-digit alpha at `rgbHex` (six hex digits, no `#`/alpha)
 * that would be legible. The worst case rises monotonically with alpha
 * (ADR-0165 §2, checked over the cube), so a linear scan from 0 finds the
 * floor.
 */
export function minimumTextAlpha(rgbHex: string): string {
  for (let alpha = 0; alpha <= 255; alpha++) {
    const alphaHex = alpha.toString(16).toUpperCase().padStart(2, '0');
    if (isLegibleTextColor(`#${rgbHex}${alphaHex}`)) {
      return alphaHex;
    }
  }
  // Unreachable: alpha 255 is always legible (ADR-0165 §2, exhaustive
  // minimum 4.5826:1).
  return 'FF';
}
