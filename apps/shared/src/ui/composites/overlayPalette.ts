/**
 * The editor's swatch palette (spec 463, ADR-0190 D7): thirteen ADR-0148 primitives at full
 * alpha, every one under the brightness cap and legible as label ink, none of them a status
 * hue (ADR-0146 item 4). Values are the primitives' sRGB resolution from `tokens.css`, pinned
 * equal to it by `OverlayColourPaletteTests`. Names are plain strings, not `var(--...)` references.
 */
export interface OverlaySwatch {
  readonly name: string;
  readonly value: string;
}

export const OVERLAY_SWATCHES: readonly OverlaySwatch[] = [
  { name: 'gray-500', value: '#818A98FF' },
  { name: 'gray-600', value: '#676F7CFF' },
  { name: 'gray-700', value: '#454B54FF' },
  { name: 'gray-800', value: '#272C32FF' },
  { name: 'gray-850', value: '#1D2126FF' },
  { name: 'gray-900', value: '#14171CFF' },
  { name: 'gray-950', value: '#0B0D10FF' },
  { name: 'cyan-600', value: '#3CA2B2FF' },
  { name: 'cyan-700', value: '#0F707DFF' },
  { name: 'cyan-800', value: '#09545FFF' },
  { name: 'cyan-900', value: '#043A42FF' },
  { name: 'cyan-950', value: '#02242AFF' },
  { name: 'black', value: '#000000FF' },
];
