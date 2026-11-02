import { QUANTUM } from '../normalizedPercent.js';

const MIN_NORMALIZED = 0;
const MAX_NORMALIZED = 1;

export function clamp01(value: number): number {
  if (Number.isNaN(value)) return 0;
  if (value < MIN_NORMALIZED) return MIN_NORMALIZED;
  if (value > MAX_NORMALIZED) return MAX_NORMALIZED;
  return value;
}

// Spec 149 §The step grid / §The keyboard map: the two step sizes and the
// server's size floor. `QUANTUM` — the grid
// that keeps repeated presses from drifting in IEEE-754 — is imported from
// `normalizedPercent.js` (phase 6 nit 6): spec 151 defined the same constant
// a second time there rather than importing it. Reasoning lives in spec.md,
// not restated here.
export const FINE_STEP = 0.005;
export const COARSE_STEP = 0.05;
const MIN_NORMALIZED_SIZE = 0.005;

/**
 * #2361: the size clamp for `width`/`height`, separate from `clamp01`'s
 * position clamp. `[0, 1]` is the correct domain for `x`/`y` — exactly `0`
 * is a valid position — but a size of `0` is not a valid size, so a
 * non-positive or `NaN` pixel-derived width/height (a drag past the canvas
 * edge, or `NaN` from a zero-size ref) must floor to `MIN_NORMALIZED_SIZE`
 * rather than pass through as `clamp01` would. Deliberately **not**
 * `Math.max(value, MIN_NORMALIZED_SIZE)` — that would also floor a
 * small-but-domain-valid size (e.g. `0.003`), which must pass through
 * unchanged; only a genuinely invalid value (`<= 0` or `NaN`) gets floored.
 */
export function clampSize(value: number): number {
  // !(value > 0) — not value <= 0 — so this also catches NaN (NaN > 0 is false)
  if (!(value > 0)) return MIN_NORMALIZED_SIZE;
  if (value > MAX_NORMALIZED) return MAX_NORMALIZED;
  return value;
}

function quantize(value: number): number {
  return Math.round(value * QUANTUM) / QUANTUM;
}

/**
 * The reachable-region bound (`1 - size` / `1 - origin`), floored to the
 * quantum grid rather than plain-quantized (finding 1). `1 - size` is
 * arithmetic on a quantized `size`, but IEEE-754 still lands it off-grid for
 * a large fraction of grid-valid sizes (`1 - 0.8 === 0.19999999999999996`),
 * and the bound is applied *last*, so an unquantized bound silently defeats
 * the quantum at exactly the boundary the quantum exists for. A plain floor
 * would then round a mathematically-exact bound like that one *inward* by a
 * whole quantum step (`0.1999`, not `0.2`) — the epsilon nudge absorbs the
 * float noise (~1e-13 here) without being anywhere near large enough to
 * round a genuinely off-grid bound (an origin/size from a drag, e.g.
 * `0.248714`) *outward* past the true edge.
 */
function quantizeBoundFloor(value: number): number {
  return Math.floor(value * QUANTUM + 1e-9) / QUANTUM;
}

/**
 * FR-007, per plan.md §2 (corrected post-review — finding 2): quantize, then
 * `clamp01` (the same clamp the drag path uses), then clamp the *motion*
 * against the reachable-region bound rather than clamping the resulting
 * *value* to it. `NormalizedPosition` bounds `x`/`y` to `[0, 1]` each but not
 * relative to size, so the domain permits a label already off the canvas's
 * high edge (`x + width > 1`); clamping the value would snap such a label
 * onto the bound on the very first keypress, including one that moved it
 * *away* from that edge. Clamping the motion instead lets an off-region
 * value move freely toward the region (`delta` pointing inward) and refuses
 * only the press that would carry it further out. `size` is the axis's
 * *other* dimension (width for an x move, height for a y move).
 */
export function nudgePosition(current: number, delta: number, size: number): number {
  const stepped = clamp01(quantize(current + delta));
  if (delta <= 0) return stepped;
  const bound = quantizeBoundFloor(Math.max(0, 1 - size));
  return Math.min(stepped, Math.max(bound, current));
}

/**
 * FR-007 + FR-008, per plan.md §2 (corrected post-review — finding 2):
 * quantize, `clamp01`, the `NormalizedSize` floor, *then* the
 * reachable-region bound (anchored at the axis's origin) applied to the
 * motion, not the value. The floor sits **inside** the canvas bound —
 * reversed from before — because outermost it could win over the bound and
 * mint an off-canvas label (finding 2a): at a near-edge origin the floor and
 * the canvas bound can conflict, and canvas containment (FR-007) must win,
 * even if that means a size below the floor is emitted at that origin — it
 * is still a domain-valid, positive size. The bound clamps motion for the
 * same off-region reason as `nudgePosition` (finding 2b): the domain does
 * not relate size to origin, so an already off-canvas size may keep
 * shrinking toward the canvas but a press that would grow it further out is
 * refused instead of snapping it down to the bound.
 */
export function resizeSize(current: number, delta: number, origin: number): number {
  const stepped = clamp01(quantize(current + delta));
  const flooredBySize = Math.max(stepped, MIN_NORMALIZED_SIZE);
  if (delta <= 0) return flooredBySize;
  const bound = quantizeBoundFloor(Math.max(0, 1 - origin));
  return Math.min(flooredBySize, Math.max(bound, current));
}
