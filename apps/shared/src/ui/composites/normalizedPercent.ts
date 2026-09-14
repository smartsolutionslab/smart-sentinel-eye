/**
 * The percent ⇄ normalized unit boundary (spec 151 FR-016, issue #2346).
 * `QUANTUM` is the same `1e-4` grid spec 149 already defined for the keyboard
 * path (`OverlayEditor.tsx`) — `1e-4` normalized **is** `0.01%`, so two
 * decimal places of percent and the programme's quantum are a bijection.
 *
 * <p><b>Phase 4a scaffold (spec 151, T001).</b> The three functions below are
 * deliberately left unimplemented — `OverlayGeometryFields.test.tsx` observes
 * each of them red on a wrong return value, never on a missing export or a
 * type error, per ADR-0139/ADR-0144. T003 fills in the real bodies per
 * plan.md §1, including the one trap this module exists to avoid: the parse
 * must not divide by 100 (`24.87 / 100 === 0.24870000000000003`, off-grid at
 * seventeen decimals) but instead `Math.round(percent * 100) / QUANTUM`.
 * `formatPercent`'s real body is `OverlayEditor.tsx`'s existing function,
 * moved here byte-identical by T005 — scaffolding it correctly here would
 * pre-empt that move and hide whether it actually happened.</p>
 */

export const QUANTUM = 10_000;

/** Normalized [0,1] → percent text at grid resolution, trailing zeros trimmed. */
export function toPercentText(normalized: number): string {
  void normalized;
  return '';
}

/** Percent text → normalized [0,1], quantized to `QUANTUM`; `null` if unparseable. */
export function parsePercent(text: string): number | null {
  void text;
  return null;
}

/** Normalized [0,1] → a percent string with a trailing `%` (spec 149's announcement format). */
export function formatPercent(normalized: number): string {
  void normalized;
  return '';
}
