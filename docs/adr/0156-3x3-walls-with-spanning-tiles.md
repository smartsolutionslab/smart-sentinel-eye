# ADR-0156: Raise the wall cap to 3×3 and let tiles span cells

**Status:** Accepted
**Date:** 2026-09-26
**Supersedes:** —
**Amends:** ADR-0112 (decision 2, decision 4)
**Superseded by:** —

**Relates to:** ADR-0112 (multi-tile layouts — the aggregate and cap this
amends), ADR-0123 (the composite-and-render measurement this amendment's
gate depends on), constitution §IV (latency budget). Enables the
asymmetric "hero + thumbnails" layout requested alongside ADR-0157 (scene
rotation).

## Context

ADR-0112 capped a wall at `MaxTiles = 4` / `MaxCells = 4` (2×2) and said so
explicitly as a **gated** ceiling, not a permanent one:

> A larger wall (e.g. 3×3) is a future ADR gated on a measured decode
> budget on real kiosk hardware, not a config knob.

The product now needs two things a 2×2 uniform grid cannot express:

1. **More tiles.** Operators want walls larger than four cameras.
2. **Asymmetric layouts** — one large "hero" tile alongside two or four
   smaller tiles (a picture-in-picture / hero-and-thumbnails shape), which
   the current model cannot express at all: `GridPosition(Row, Col)` names
   exactly one cell, so every tile is the same size by construction.

This is a **human decision**, made directly by the product owner in this
conversation, not inferred or defaulted by the autonomous lane (ADR-0144
forbids the lane from making it; this ADR exists because a human made it).

## Decision

### 1. Raise the cap to `MaxCells = 9` (3×3), `MaxTiles` unchanged at the cell count

`GridDimensions.MaxCells` rises from 4 to 9. `MaxTiles` (the number of
*populated* cells, independent of grid shape once decision 2 below lands)
also rises to 9, matching a fully-populated 3×3.

**This raises the cap without yet completing ADR-0112's own measurement
gate.** The product owner chose 3×3 over 4×4 specifically because it is
the smaller, lower-risk step — closer to today's already-measured 4-tile
behaviour than a 4× jump — but 9 simultaneous WebRTC decode sessions is
still more than the 4 ADR-0112 measured. **A real-kiosk-hardware decode
measurement at 9 tiles is required before this cap ships to production**,
per ADR-0112's own reasoning: 4 tiles "sits comfortably within commodity /
kiosk-class GPU decode capacity, with margin"; 9 has not been shown to.
This is implementation work for whoever picks up the delivering issue —
record the measured figure against composite-and-render (≤ 50 ms) and
SFU→kiosk decode (≤ 120 ms) in `verification.md`, the same way ADR-0123
did for the 4-tile case. If the measurement fails either budget, the
shipped cap is whatever the measurement supports, not 9 — the number in
this ADR is a ceiling to design toward, not a guarantee.

### 2. Tiles gain general row/col span — a `Tile` now claims a rectangle, not a cell

`Tile` gains `RowSpan` and `ColSpan` (both default `1`, preserving every
existing 1×1 tile unchanged). A tile at `Position (Row, Col)` with
`RowSpan r` / `ColSpan c` occupies every cell `(Row..Row+r-1, Col..Col+c-1)`.

This is the **general** span model the product owner chose over a fixed
set of named templates ("hero+2", "hero+4"): any tile in any grid may span
any rectangle that fits, not just a curated set of shapes. The tradeoff,
accepted explicitly: a materially bigger domain and UI surface than a
handful of presets would have needed — the designer must let an operator
draw/resize a span, not just pick from a list. Chosen anyway because a
fixed template set would need its own future ADR the first time an
operator wants a fifth shape, which is exactly the "config knob added
later" pattern constitution §IX (no speculative generality) warns against
*avoiding* here by choosing the general model once, deliberately.

**Grid invariants, updated from ADR-0112 §2:**

- **≥ 1 tile** per revision — unchanged.
- **No two tiles' occupied-cell rectangles overlap** — the ADR-0112
  invariant "no two tiles share a `GridPosition`" generalizes to "no two
  tiles' spans intersect," checked cell-by-cell.
- **Every tile's full span is in-bounds**: `0 ≤ Row`, `Row + RowSpan ≤ Rows`,
  `0 ≤ Col`, `Col + ColSpan ≤ Cols` — not just the origin cell.
- **`RowSpan ≥ 1` and `ColSpan ≥ 1`** — a tile always claims at least its
  own cell; zero or negative spans are a construction error, not a valid
  degenerate case.
- **Grid dimensions bounded by `MaxCells`** (now 9) — unchanged shape of
  the rule, new number.
- **Sparse grids remain allowed** — a 3×3 grid need not have every cell
  covered by some tile's span.
- Camera reuse across tiles, overlay reuse across tiles, and the
  overlay-keyed highlight path (ADR-0112 §5) are **all unchanged** — a
  spanning tile is still exactly one `Tile{ Camera, Overlay?, Position,
  RowSpan, ColSpan }`, so nothing about *what* a tile carries changes,
  only how much grid it claims.

**Contract impact.** `LayoutRevisionPublishedV2`'s `Tiles` list gains
`RowSpan`/`ColSpan` per entry. Since this is the same pre-production,
single-repo-consumer situation ADR-0112 §3 already decided ("every
consumer is in-repo and migrates together... no dual-publish window"),
this is **V2 extended in place**, not a V3 cut — no existing published
event shape is removed or reinterpreted, a field is added with a default
(`1`) that reproduces every existing tile's current meaning exactly. The
same reasoning applies to the HTTP DTOs (`CreateLayoutRequest` /
`EditDraftRequest` / `LayoutDto`): additive fields, default `1`, no
existing request/response shape changes meaning.

**Data migration.** A new EF migration adds `row_span`/`col_span` int
columns (default `1`, `NOT NULL`) to `layout_revision_tiles`. Every
existing row already means exactly what `RowSpan = ColSpan = 1` says, so
this is a pure additive migration — no backfill logic beyond the column
default, no data reinterpretation.

## Consequences

**Positive:**

- The domain model gains real expressive power (spanning) with a single,
  general mechanism rather than a growing list of special cases.
- The V2-extended-in-place contract keeps this pre-production system's
  "no dual-publish ceremony" posture (ADR-0112 §3) rather than forcing a
  V3 cut for what is, at the data level, one additive field pair.
- Every 2×2 wall authored today continues to mean exactly what it always
  meant — `RowSpan = ColSpan = 1` on every tile, `Rows = Cols = 2` — so no
  existing dev data or downstream consumer needs anything beyond the
  default-column migration.

**Negative:**

- **The 9-tile decode budget is not yet measured.** This ADR raises the
  domain ceiling to 9 but explicitly does not claim 9 is safe — that is
  the delivering spec's job, and the shipped cap may end up lower than 9
  if the measurement says so. Do not read `MaxCells = 9` as "verified
  safe for production" until `verification.md` says so.
- **The designer UI is now a real span-editing surface**, not a grid of
  equal buttons — drag-to-resize or an equivalent interaction is needed,
  which is materially more design and frontend work than the fixed-preset
  alternative would have been. Accepted explicitly by the product owner
  for the generality it buys.
- **Overlap-checking cost.** Validating "no two spans intersect" is
  O(tiles × cells) in the worst case instead of O(tiles) for single-cell
  positions. At `MaxCells = 9` this is trivial (at most 9×9 = 81
  comparisons); worth re-examining only if the cap is ever raised far
  beyond what this ADR anticipates.

## Alternatives Considered

**A — Jump straight to 4×4 / 16 tiles, as originally requested — REJECTED
by the product owner.** Rejected in favour of the smaller 3×3 step
specifically to reduce the gap between the already-measured 4-tile case
and the new ceiling, on the reasoning that a smaller, closer-to-measured
step is less likely to blow the decode/render budget outright and cheaper
to walk back if it does.

**B — A fixed set of named span templates ("hero+2", "hero+4") instead of
general spanning — REJECTED by the product owner.** Simpler domain and UI
(no drag-to-resize needed, presets validate trivially), but bakes a UI
affordance into the domain the same way ADR-0112 §Alternatives-B already
rejected for the uniform-grid case, and would need its own follow-up ADR
the first time a fifth shape is wanted. Rejected explicitly in favour of
the general model despite the larger near-term cost.

**C — A separate "hero layout" concept alongside the existing uniform grid
— REJECTED.** Would avoid touching `GridDimensions`/`Tile` at all, but
reintroduces exactly the "is a wall one thing or two things" fragmentation
ADR-0112's own Alternative A rejected for the wall-vs-cell question —
here it would be wall-vs-hero-wall. One aggregate, one tile model, general
spans: consistent with ADR-0112's existing "one thing, not two" posture.

## Implementation Notes

- This is a **domain and contract change**, delivered through the normal
  seven-phase workflow (ADR-0037), not the autonomous lane — ADR-0144
  explicitly excludes decisions of this shape from that lane, and the
  decision itself (this ADR) was made by a human for that reason.
- The delivering spec must include the real-hardware decode measurement
  from decision 1 as an explicit, named task — not an implicit assumption
  that "9 tiles is fine because the domain now allows it."
- `GridDimensions.MaxCells`/`MaxTiles` and the span invariants belong in
  one place (the `GridDimensions`/`Tile` value objects), mirroring
  ADR-0112's existing single-source-of-truth posture.
- Frontend: the wall designer needs a span-aware editing surface; the
  kiosk's CSS-grid renderer needs `grid-row: span N` / `grid-column: span
  N` per tile instead of a fixed 1-cell placement — both are net-new
  frontend work, not a change to existing single-cell rendering (which
  becomes the `RowSpan = ColSpan = 1` case, unchanged).
