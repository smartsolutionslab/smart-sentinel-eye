# Spec 279 — The age a lone tile keeps

**Issue**: #2498 · **Branch**: `fix-2498-one-tile-frame-age-staleness` · **Phase**: 1 (Specify)
**Date**: 2026-09-28 · **Base**: `3b046175` (`origin/develop`)
**Context**: `apps/kiosk-web/src/features/cell/useWallAlignment.ts` only. No bounded-context code, no
contract, no other file.
**Engineer**: delivered directly in this session (no subagent split) · **Reviewer**: self-review against
the diff, documented in §6
**Phase 4a colour**: **behaviour-changing** — new fact observed red first (§5), per the product-owner
decision in §2.
**ADRs**: ADR-0128 (receiver playout alignment, the feature this hook implements), ADR-0129 (labels aged
to match the picture, not frame-paired — the consumer of `frameAgeFor`), ADR-0036 (smallest change),
ADR-0139 (new behaviour observed red first), ADR-0037 (phases).
**Constitution**: §IV — Presentation-buffer leg (event→overlay). This changes only *how long a stale
sample survives before `frameAgeFor` reports it*; it does not touch the alignment math, the settle
cadence, or anything in the budget path. **N/A** for a latency figure — no leg's timing changes.
**New ADR needed**: **No.** Extends an existing constant (`LAG_STALE_AFTER_MS`) to a tile count it
already applies to conceptually; no new decision.

---

## 1. The defect

`useWallAlignment`'s only interval that prunes stale entries out of `lagsRef` is created inside the
`aligning` (`tileCount >= 2`) branch of the effect at line ~113 — the whole effect returns early below
two tiles, so the interval that ages a stale sample out is never created. `frameAgeFor` (line 239) reads
straight from `lagsRef` with no staleness check of its own, so on a one-tile wall a departed camera's
last reported `lagMilliseconds` is returned **forever**, not aged out.

This was latent before spec 204 (#2303): `frameAgeFor` was previously read only behind a settle-interval
re-render that also never happened on a one-tile wall, so the gap was unreachable. Spec 204 decoupled
`Tile`'s read of `frameAgeFor` from tile count specifically to make one-tile walls work (its own SC-5),
which means this read path is now genuinely exercised — a dead camera on a 1×1 wall now holds a fresh-
looking age indefinitely.

## 2. Product-owner decision (made; not re-opened here)

Extend the stale-sample aging logic to run independent of `aligning`, so a one-tile wall ages a departed
tile out of `frameAgeFor` exactly like a larger wall does. (Decided over the alternative — narrowing the
doc comment and leaving the one-tile gap as accepted behaviour.)

## 3. Scope

**In**: a second, always-active interval in `useWallAlignment` that prunes `lagsRef` of samples older
than `LAG_STALE_AFTER_MS`, independent of `aligning`; a new characterisation-shaped fact in
`useWallAlignment.test.ts`; a small precision edit to `frameAgeFor`'s doc comment.

**Out** — do not do:
- Touch the existing settle-cycle interval, its pruning, or anything about `target`/`held`/`released`
  (all genuinely about multi-tile coordination and correctly stay gated behind `aligning`).
- Change `LAG_STALE_AFTER_MS` itself, or `SETTLE_INTERVAL_MS`.
- Touch `targetFor`, `reportLag`, or any consumer of `frameAgeFor` (`Tile`, `useLabelDelay`).

## 4. User story

### US1 (P1) — A departed camera's age forgets itself on every wall size, not just two-or-more

As a kiosk operator watching a one-tile wall, I want a camera that stops reporting to stop showing a
frame age at all (so the tile can fall back to its "no fresh sample" treatment) rather than showing
whatever age it last measured, forever — so a silently-dead camera does not look like a slightly-old one.

**Independent test**: the new fact (§5) fails today (the age never clears on a one-tile wall) and passes
after; both existing `frameAgeFor` facts and the whole rest of the suite pass unmodified.

## 5. Failing test observed first (ADR-0139)

New fact: `Reports no frame age for a departed tile on a one-tile wall, same as a larger one`, in
`useWallAlignment.test.ts`, mirroring the existing three-tile fact's shape (report once, advance past
`LAG_STALE_AFTER_MS`, assert `null`) but with `useWallAlignment(1)`. Verbatim red output captured in the
PR body.

## 6. Design

One `useEffect` with no dependency array items beyond `[]` (unconditional — no `aligning` guard), holding
its own `window.setInterval` at `SETTLE_INTERVAL_MS`, doing exactly the prune loop the settle cycle
already does (`now - sample.at > LAG_STALE_AFTER_MS` → delete). Cleared on unmount like the existing
interval.

**Why a second interval instead of hoisting the prune out of the `aligning`-gated effect:** the
`aligning`-gated effect's interval also builds `lags`, calls `settleAlignment`, and drives `target`/
`held`/`released` — none of which should run below two tiles (FR-004: *"a single-tile wall must not pay
a millisecond for a feature about walls"*). Merging the two would mean either running that whole
machinery unconditionally (violating FR-004) or threading a conditional through the settle cycle for a
concern (pruning) that is otherwise identical in both cases. Two small intervals, one always on, one
gated, is the smaller diff and keeps FR-004 intact.

**Redundant pruning, harmless.** When `aligning` is true both intervals prune the same map; deleting an
already-absent key is a no-op. Not worth branching around.

## 7. Latency budget impact

N/A — see the Constitution line above. No leg's timing changes; this only shortens how long a stale
sample is treated as current before `frameAgeFor` reports it as absent.
