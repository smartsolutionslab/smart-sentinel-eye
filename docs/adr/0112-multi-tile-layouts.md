# ADR-0112: Multi-tile layouts — extend the Layout aggregate to N tiles

**Status:** Accepted
**Date:** 2026-06-04
**Supersedes:** —
**Superseded by:** —

**Relates to:** ADR-0073/0040 (versioned integration events, `V<N>` suffix),
ADR-0104 (revisioned-aggregate duplication), ADR-0043 (optimistic concurrency),
ADR-0067 (MigrationRunner), ADR-0090 (Guid v7 `Identifier` records), ADR-0046/0066
(hand-written value objects, `Ensure.That`), ADR-0048 (NRT-off / `Option<T>`),
ADR-0076 (replaceable real-time transport; SignalR v1), ADR-0084 (code metrics),
ADR-0108 (Playwright e2e gate). Constitution §III (bounded-context isolation),
§IV (latency budget). Extends spec 003 (LayoutComposition); enables spec 010
(Multi-tile layouts) and unblocks Scenario Simulator M2 (ADR-0111,
`docs/design/scenario-simulator-m2.md`, open question O1).

## Context

Spec 003 modelled a **Layout** as a logical chain of **Revisions**, each binding
**exactly one** `CameraIdentifier` plus one optional `OverlayIdentifier`
(`src/LayoutComposition/Domain/Layout/Revision.cs`). Spec 003 explicitly deferred
the grid (US5, P3) but committed to "the schema admits future grid additions
without a breaking migration" (spec 003 Assumptions, Resolved Clarification #1).

The product now needs a **video wall**: a single layout that shows **1..N camera
tiles** in a grid, each tile able to carry its own overlay, with a
management-web **wall designer** and a kiosk that renders the grid with
**per-tile overlay highlight**. Scenario Simulator M2 (ADR-0111) is blocked on
this exact question — its open question O1 asks whether a "wall" is one layout
(multi-tile) or N single-tile layouts. A multi-tile rolling-mill layout (4 tiles:
station-4-roughing, station-7-finishing, cooling-bed, coiler) is the downstream
consumer that makes the M2 demo's "tiles light up in narrative order" story land
on one screen.

Constraints that bound the solution:

1. **Pre-production — no external consumers.** Every consumer of the Layout
   contract (the Audit subscriber, the kiosk, management-web) lives in **this
   repo** and migrates in the same feature. There is no published/external client
   to keep on an old contract, so a **clean version cut** is preferred over a
   dual-publish back-compat window (no speculative generality — Karpathy /
   constitution §IX). Existing **dev data** must still be migrated without loss.
2. **Persistence is CRUD/EF Core**, not Marten. `Revision` is an EF *owned*
   collection on `Layout` (`LayoutConfiguration.cs`); migration is a standard EF
   migration via the `MigrationRunner` (ADR-0067), not an event-stream upcaster.
3. **The highlight path is overlay-keyed, not camera-keyed.**
   Automation → `OverlayHighlightRequestedV1(OverlayIdentifier, DurationMs)` →
   `OverlayHighlightRequestedV1Handler` → SignalR `OverlayHighlightChanged` on
   `/hubs/layouts`. The camera↔overlay link *is* the layout tile. A grid with
   multiple overlays routes by `overlayIdentifier` with **zero backend change** on
   the highlight leg.
4. **Latency budget is sacred (§IV).** A wall decodes **N simultaneous WebRTC
   tiles**, which loads "SFU → kiosk decode ≤ 120 ms" and "composite + render
   ≤ 50 ms". The tile count must be bounded with a justified NFR ceiling.
5. **No third revisioned aggregate.** ADR-0104's rule-of-three is not triggered:
   we are extending an *existing* aggregate's payload, not adding a new one.

## Decision

### 1. Extend the existing `Layout` aggregate to hold 1..N tiles — no new aggregate

A `Revision` stops carrying a single `Camera` + optional `Overlay` and instead
owns an **ordered, non-empty set of `Tile`s** plus a `GridDimensions` value
object. A `Tile` is a value object: `{ Camera: CameraIdentifier, Overlay:
Option<OverlayIdentifier>, Position: GridPosition }`.

- **A single-camera layout becomes a 1-tile layout** on a 1×1 grid. There is no
  separate "VideoWall" concept; "wall" and "cell" are the same aggregate at
  different tile counts. The kiosk's single-cell view (today's `CellPage`)
  becomes the N=1 case of the grid renderer.
- The revisioned-aggregate lifecycle (Draft→Published→Archived, branch, revert,
  at-most-one-Published-per-chain) is **unchanged**. Only the revision *payload*
  changes — exactly the variation ADR-0104 anticipated ("Revision payload
  differs"). ADR-0104's rule-of-three is **not** triggered (no new aggregate).

### 2. Grid model — explicit `rows × cols` with per-tile coordinates; sparse allowed

The grid is **`GridDimensions(Rows, Cols)`** with each tile at an explicit
`GridPosition(Row, Col)`. This is more expressive than fixed presets and lets the
designer offer presets (1×1 / 1×2 / 2×1 / 2×2) as a UI convenience without baking
them into the domain. Invariants enforced inside the aggregate (`Ensure.That`,
`Result<T,Error>` at the command boundary):

- **≥ 1 tile** per revision (a revision is never empty).
- **No two tiles share a `GridPosition`** (no overlapping cells).
- **Every tile is in-bounds**: `0 ≤ Row < Rows`, `0 ≤ Col < Cols`.
- **Grid dimensions are bounded** by the max-tiles ceiling (decision 4):
  `Rows × Cols ≤ MaxCells` and the number of *populated* tiles `≤ MaxTiles`.
- **Sparse grids are allowed** (a 2×2 grid may carry 3 tiles; the 4th cell is
  empty). Empty cells render as a placeholder on the kiosk.
- **A camera MAY be reused across tiles** (the same feed in two cells is a valid
  operator choice).
- **An overlay MAY be reused across tiles.** The highlight path is overlay-keyed,
  so one `OverlayHighlightRequestedV1` lights **every tile bound to that overlay**.
  v1 adopts this "highlight all matching tiles" semantic deliberately (product
  decision) rather than enforcing overlay-uniqueness — the kiosk routes a single
  `OverlayHighlightChanged` frame to all matching tiles.

### 3. Contract versioning + migration — a clean V2 cut (pre-production)

Because every consumer is in-repo and migrates together (Context constraint 1),
there is **no dual-publish back-compat window**.

**Integration events.** `LayoutRevisionPublishedV1` (single `Camera: Guid`) is
**replaced** by **`LayoutRevisionPublishedV2`** (ADR-0073 `V<N>` suffix marks the
shape change) carrying the tile set: `{ Layout, RevisionNumber, Name, Tiles:
IReadOnlyList<{ Camera, Overlay?, Row, Col }>, GridRows, GridCols, PublishedAt,
PublishedBy, Metadata }`. **V1 is removed in the same feature** and the only
subscriber (Audit's `IntegrationEventAuditHandler`) is updated to V2. There is no
dual-publish. `LayoutRevisionArchivedV1` is **unchanged** (it carries no tile
payload).

**HTTP DTOs / requests.** `CreateLayoutRequest` / `EditDraftRequest` take a
`Tiles` collection + `Grid` dimensions as the **only** shape; a single-camera wall
is simply a 1×1 grid with one tile. The legacy single-scalar
`cameraIdentifier`/`overlayIdentifier` request/response fields are **removed**, not
retained — the kiosk and `layouts.api.ts` are updated to read `tiles` in the same
feature. `LayoutDto` / `PublishedLayoutDto` carry the tile set + grid.

**Data migration (EF, not upcaster).** A new EF migration (via `MigrationRunner`,
ADR-0067) adds a `layout_revision_tiles` owned table `{ revision_id (FK), row, col,
camera_id, overlay_id? }` + `grid_rows`/`grid_cols` columns on `layout_revisions`,
**backfills** every existing revision's `camera_id`/`overlay_id` into a single tile
at `(0,0)` on a 1×1 grid, then **drops the now-redundant `camera_id`/`overlay_id`
columns in the same migration** (clean cut — no read window). Existing rows
survive as 1-tile walls; zero data loss.

### 4. Latency NFR — max tiles per wall (v1)

A wall decodes **N simultaneous WebRTC peer connections** in one browser tab.
This loads two budget legs (§IV): **SFU → kiosk decode ≤ 120 ms** and
**composite + render ≤ 50 ms**. Each tile is an independent `RTCPeerConnection`
with its own decode pipeline; browser hardware-decode sessions are finite.

**Decision: v1 caps a wall at `MaxTiles = 4` (max grid 2×2), which is also the
designer default.** Reasoning:

- A 250-camera fab is irrelevant to *per-wall* tile count: a wall is a single
  operator surface showing a handful of correlated cameras (the rolling-mill
  demo is exactly 4). Walls are not "show me all 250".
- 4 simultaneous HW-decoded WHEP streams sits comfortably within commodity /
  kiosk-class GPU decode capacity, with margin against software-decode fallback
  that would blow the ≤ 120 ms leg even on weaker hardware. It is the conservative,
  measured-safe ceiling for v1; it covers the demo exactly.
- The composite + render ≤ 50 ms leg is a CSS-grid of `<video>` elements — cost is
  sub-frame at 4 tiles; the binding risk is decode, not composite.
- The cap is a **domain invariant** (decision 2), so an operator cannot author a
  wall the kiosk cannot decode within budget. **A larger wall (e.g. 3×3) is a
  future ADR** gated on a measured decode budget on real kiosk hardware, not a
  config knob (no speculative generality — constitution §IX).

This is a real NFR ceiling, not config. `MaxTiles = 4` / `MaxCells = 4` and the
2×2 default live as constants on the `GridDimensions` value object so the invariant
and the designer share one source of truth.

### 5. Highlight path stays overlay-keyed — no backend change on the highlight leg

`OverlayHighlightRequestedV1` and `OverlayHighlightRequestedV1Handler` are
**unchanged**. The kiosk subscribes to the existing overlay-keyed
`OverlayHighlightChanged` SignalR frame (one subscription per wall) and routes the
highlight to **every tile whose `overlayIdentifier` matches** `message.overlay`.
Because overlay reuse across tiles is allowed (decision 2), the routing is "all
matching tiles" — typically one, but N if the operator bound the same overlay to
several cells. **Note:** the frontend does not consume `OverlayHighlightChanged`
today (the backend broadcasts it; `layoutHub.ts`/`CellPage` do not yet subscribe).
Wiring the kiosk subscription + per-tile highlight CSS is **net-new frontend work
in spec 010**, not a change to existing behaviour.

## Consequences

**Positive:**

- One aggregate, one lifecycle, one migration. The "cell = 1-tile wall" framing
  means there is no parallel code path: the kiosk grid renderer handles N=1 and
  N=4 identically.
- Highlight routing is free on the backend (overlay-keyed already); per-tile
  highlight is a pure frontend addition.
- The clean V2 cut keeps the contract surface small — one published event shape,
  one request/response shape, no transitional dual-publish code or retirement
  follow-up to track. Existing dev layouts are preserved by the EF backfill.
- Unblocks Scenario Simulator M2 O1: a "wall" is now one multi-tile layout.

**Negative:**

- "Highlight all matching tiles" means an operator who reuses one overlay across
  cells gets all of them lit by a single highlight. Accepted as the chosen v1
  semantic (it is also the more intuitive behaviour); a future per-tile-scoped
  highlight would need a camera/tile-keyed highlight contract.
- A hard `MaxTiles = 4` cap will frustrate any future "bigger wall" ask until a
  measured-decode ADR raises it. Accepted: the budget is sacred and 4 covers the
  v1 product need.
- EF owned-collection-of-value-objects (tiles) is heavier to map than the prior
  two scalar columns. Accepted as the cost of the grid.
- The clean cut means the migration must land the EF backfill **and** the updated
  Audit subscriber + frontend reads in one feature (no partial-deploy safety net).
  Acceptable pre-production; revisit if/when the system ships externally.

## Alternatives Considered

**A — Separate `VideoWall` aggregate referencing N `Layout`s — REJECTED.** Would
keep `Layout` untouched and compose walls from existing single-cell layouts.
Rejected by the locked product decision (extend `Layout`), and because it
fractures the lifecycle (publish/archive/highlight would have to coordinate across
N independent chains), re-introduces the exact "is a wall one thing or N things"
ambiguity M2-O1 is trying to kill, and forces the kiosk to manage N SignalR
reconciliation paths. One aggregate, one lifecycle is simpler.

**B — Fixed grid presets (1×1/2×2…) as the domain model — REJECTED.** Simpler
validation, but bakes a UI affordance into the domain and blocks sparse grids and
non-square walls (e.g. 1×2 a row of two). Presets are offered as a *designer*
convenience over the explicit `rows × cols` model instead.

**C — Dual-publish `LayoutRevisionPublishedV1` + `V2` for a back-compat window —
REJECTED for this pre-production system.** Standard when external subscribers pin a
version, but here every subscriber is in-repo and migrates in the same feature.
Dual-publish + retained legacy DTO fields + a retirement follow-up would be pure
ceremony (speculative generality). The clean V2 cut + EF backfill is smaller and
leaves no cruft. (Revisit the dual-publish discipline once the system has external
consumers.)

**D — Marten upcaster for migration — REJECTED / N/A.** LayoutComposition is
CRUD/EF, not event-sourced. The migration is a standard EF migration + data
backfill via `MigrationRunner` (ADR-0067). No event stream exists to upcast.

## Implementation Notes

- Spec 010 (`specs/010-multi-tile-layouts/`) carries the user stories, acceptance
  scenarios, and the independent e2e procedure. This ADR is the architectural
  decision it references.
- Migration ordering: add tile table + grid columns → backfill primary tile from
  `camera_id`/`overlay_id` → **drop the legacy `camera_id`/`overlay_id` columns**
  in the same migration. No legacy read window; no follow-up retirement task.
- `MaxTiles = 4` / `MaxCells = 4` (2×2) and the 2×2 default live as constants on
  the `GridDimensions` value object so the invariant and the designer share one
  source of truth. Raising the cap (e.g. to 3×3) is a future measured-decode ADR.
