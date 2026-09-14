/**
 * The percent ⇄ normalized unit boundary (spec 151 FR-016, issue #2346).
 * `QUANTUM` is the same `1e-4` grid spec 149 already defined for the keyboard
 * path (`OverlayEditor.tsx`) — `1e-4` normalized **is** `0.01%`, so two
 * decimal places of percent and the programme's quantum are a bijection.
 */

export const QUANTUM = 10_000;

/** A plain decimal shape only — rejects `0x10`, `1e2`, `--5`, `NaN`, `Infinity`, etc. */
const DECIMAL_SHAPE = /^-?\d+(\.\d+)?$/;

/** Normalized [0,1] → percent text at grid resolution, trailing zeros trimmed. */
export function toPercentText(normalized: number): string {
  return String(Number((normalized * 100).toFixed(2)));
}

/**
 * Percent text → normalized [0,1], quantized to `QUANTUM`; `null` if
 * unparseable. Quantizes on the normalized side
 * (`Math.round(percent * 100) / QUANTUM`), never by dividing by 100 — see
 * spec.md §Precision: `24.87 / 100 === 0.24870000000000003`, off-grid at
 * seventeen decimals.
 */
export function parsePercent(text: string): number | null {
  const trimmed = text.trim();
  if (trimmed === '' || !DECIMAL_SHAPE.test(trimmed)) return null;

  const percent = Number(trimmed);
  if (!Number.isFinite(percent)) return null;

  return Math.round(percent * 100) / QUANTUM;
}

/** Normalized [0,1] → a percent string with a trailing `%` (spec 149's announcement format). */
export function formatPercent(normalized: number): string {
  return `${toPercentText(normalized)}%`;
}
