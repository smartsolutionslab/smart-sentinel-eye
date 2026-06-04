# Feature Specification: Multi-tile layouts (video wall)

**Feature Branch:** `010-multi-tile-layouts`

**Created:** 2026-06-04

**Status:** Specified (Phase 1 complete — clarifications resolved at the gate)

**ADR:** [ADR-0112](../../docs/adr/0112-multi-tile-layouts.md) — extends
[spec 003 (LayoutComposition)](../003-layout-composition/spec.md).

**Input:** First real product feature on top of LayoutComposition's deferred
grid (spec 003 US5, P3). Extends the **Layout** aggregate so a single layout
holds **1..N camera tiles** in a grid — each tile = a camera (required) + an
optional overlay + a grid position. Ships the management-web **wall designer**
(grid editor with the existing draft→publish lifecycle) and the kiosk **grid
renderer** with **per-tile overlay highlight**. A single-camera layout becomes a
**1-tile wall**. Unblocks Scenario Simulator M2
([docs/design/scenario-simulator-m2.md](../../docs/design/scenario-simulator-m2.md),
open question O1) which will later seed a 4-tile rolling-mill layout — **M2 itself
is out of this spec's scope**, listed only as the downstream consumer.

The architectural decisions (extend `Layout` not a new aggregate; `rows × cols`
grid with explicit tile coordinates; **clean V2 event cut + EF backfill
migration**; **`MaxTiles = 4` (2×2)**; **overlay reuse allowed → highlight all
matching tiles**; overlay-keyed highlight unchanged) are locked in **ADR-0112**.
This spec carries the user stories, acceptance scenarios, and the independent
end-to-end test procedure.

## Resolved clarifications (Phase-1 gate, 2026-06-04)

| # | Question | Decision |
|---|---|---|
| 1 | Max tiles per wall | **4 (2×2)** — conservative, measured-safe for the ≤120 ms decode leg on kiosk-class GPUs; covers the rolling-mill demo exactly. A bigger wall is a future measured-decode ADR. |
| 2 | Contract back-compat | **Clean V2 cut** — replace `LayoutRevisionPublishedV1` with `V2`, update the in-repo Audit subscriber, EF-backfill existing dev layouts to 1-tile. No dual-publish, no legacy DTO fields, no retirement follow-up (pre-production; all consumers migrate together). |
| 3 | Designer scope v1 | **Full** — grid resize + per-tile camera/overlay assignment + sparse cells + edit-after-publish (US4 in scope). |
| 4 | Overlay reuse across tiles | **Allowed** — a highlight for an overlay lights **all** tiles bound to it. |
| 5 | Grid model | **Explicit `rows × cols`** with per-tile coordinates; the designer offers presets as a UI convenience. |
| 6 | Empty / sparse tiles | **Allowed** — an empty cell renders as a kiosk placeholder. |

## Ground truth (verified against the codebase)

- **Persistence is CRUD/EF Core, not Marten.** `Revision` is an EF *owned*
  collection on the `Layout` aggregate (`LayoutConfiguration.cs`,
  `LayoutCompositionDbContext`). Migration is a standard EF migration via the
  `MigrationRunner` (ADR-0067) — **no event-stream upcaster**.
- **Today a `Revision` binds exactly one `Camera` + optional `Overlay`**
  (`src/LayoutComposition/Domain/Layout/Revision.cs`). The revisioned-aggregate
  lifecycle (Draft→Published→Archived, branch, revert, at-most-one-Published) is
  unchanged by this spec; only the revision *payload* changes (ADR-0104 anticipated
  this; rule-of-three NOT triggered — no new aggregate).
- **The highlight path is overlay-keyed and needs no backend change.** Automation
  → `OverlayHighlightRequestedV1(OverlayIdentifier, DurationMs)` →
  `OverlayHighlightRequestedV1Handler` → SignalR `OverlayHighlightChanged` on
  `/hubs/layouts`. The camera↔overlay link is the layout tile.
- **The kiosk does NOT yet consume `OverlayHighlightChanged`.** The backend
  broadcasts it (`SignalRLayoutLifecycleBroadcaster.OverlayHighlightedAsync`), but
  `apps/shared/src/realtime/layoutHub.ts` and `CellPage.tsx` do **not** subscribe
  to it — there is no `ssE-overlay-highlight` consumer anywhere in `apps/`. Wiring
  per-tile highlight is **net-new frontend work**, not a behaviour change.
- **The kiosk single-cell view is `CellPage.tsx`**, routed at
  `/layouts/:layoutIdentifier` (`apps/kiosk-web/src/app/router.tsx`), with
  `PickerPage.tsx` at `/`. It renders one `<CameraViewer>` and reads
  `published.cameraIdentifier` / `overlayIdentifier` from `useGetLayoutQuery` —
  this read is migrated to the tile set by this spec.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Admin authors and publishes a 2×2 wall (Priority: P1)

A fab admin opens management-web → Layouts → **New layout**, chooses a **grid
size** (e.g. 2×2), assigns a **camera (required)** and an **optional overlay** to
each tile, names the wall, saves it as a **Draft**, then clicks **Publish**. The
wall immediately becomes selectable in any connected kiosk's picker.

**Why this priority:** Smallest independently-shippable vertical slice that
delivers the feature's core value — a multi-tile wall authored and published.
Exercises every new primitive: the multi-tile `Revision` payload, grid invariants,
the V2 publish event, the EF tile table, and the designer UI. A single-tile layout
is the N=1 case of this same flow, so it subsumes spec 003's authoring.

**Independent Test:**

1. Start the system via `aspire run` (Postgres, RabbitMQ, Keycloak, MediaMTX,
   camera-catalog, stream-distribution, overlay-designer, layout-composition,
   management-web, kiosk-web).
2. Sign in to `management-web` as admin. Register 4 cameras (spec 001 flow); wait
   for streams to leave Provisioning. Publish at least one overlay (spec 004).
3. Click **Layouts** → **New layout**. Pick grid **2×2**. The designer shows a
   2×2 grid of empty tiles.
4. For each of the 4 cells, pick a camera; for at least one cell pick an overlay.
   Enter a unique wall name. Click **Save as draft**. The row shows state
   **Draft** and a "4 tiles, 2×2" summary.
5. Click **Publish**. The state flips to **Published** within ≤ 1 s. A
   `LayoutRevisionPublishedV2` carrying the full tile set is on the integration bus.

**Acceptance Scenarios:**

1. **Given** an authenticated admin with `sse.layouts.write`, 4 registered
   cameras, and 1 published overlay,
   **When** the admin `POST`s to `/layouts` with
   `{ name, grid: { rows: 2, cols: 2 }, tiles: [ { camera, overlay?, row, col } × 4 ] }`,
   **Then** the response is `201 Created` with the new layout's identifier and a
   first revision in state `Draft` carrying all 4 tiles.
2. **Given** a 2×2 Draft wall,
   **When** the admin `POST`s to `/layouts/{id}/revisions/1/publish`,
   **Then** the response is `200 OK` with state `Published`, `publishedAt` set, and
   `LayoutRevisionPublishedV2` (full tile set) on the bus within ≤ 200 ms.
3. **Given** a single-camera wall create body
   `{ name, grid: { rows: 1, cols: 1 }, tiles: [ { camera, overlay?, row: 0, col: 0 } ] }`,
   **When** the admin `POST`s to `/layouts`,
   **Then** the response is `201 Created` and the layout is persisted as a 1×1,
   1-tile wall — proving the single-camera case is just N=1 of the same shape.

---

### User Story 2 — Operator at a kiosk renders the grid (Priority: P1)

An admin walks up to a kiosk showing the picker, signs in via Keycloak, sees the
published wall in the list, taps it, and the kiosk renders the **2×2 grid** of
live camera tiles in a CSS grid — each tile a `<CameraViewer>` with its bound
overlay; empty cells show a placeholder.

**Why this priority:** Until the wall reaches a kiosk screen, the feature is just
a designer. This brings the grid renderer online and proves N simultaneous WebRTC
tiles decode within the latency budget (§IV, ADR-0112 §4). The N=1 case is exactly
today's `CellPage`, so this evolves `CellPage` into the grid renderer rather than
adding a parallel page.

**Independent Test:**

1. Continuing from US-1, open the kiosk-web URL from the Aspire dashboard, sign in.
2. The picker lists the published wall. Tap it.
3. The picker is replaced by a 2×2 grid; each tile shows its camera's live stream
   within ≤ 3 s at p95 (reuses spec 002's click-to-first-frame budget per tile).
   Tiles with a bound overlay show the overlay label; empty cells show a
   placeholder.

**Acceptance Scenarios:**

1. **Given** an authenticated operator with at least one Published wall,
   **When** the page calls `GET /layouts?state=published`,
   **Then** the response is `200 OK` with one entry per Published chain carrying
   the grid dimensions and the tile set `{ camera, overlay?, row, col }`.
2. **Given** the operator taps a 2×2 wall,
   **When** the kiosk navigates to the wall view,
   **Then** it renders a 2×2 CSS grid of `<CameraViewer>` composites (the shared
   composite from spec 002, used unchanged) and a live frame appears in each tile
   within ≤ 3 s at p95.
3. **Given** a sparse 2×2 wall with 3 tiles (one empty cell),
   **When** the wall renders,
   **Then** the 3 populated cells render `<CameraViewer>` and the empty cell
   renders a placeholder (no broken viewer, no error).
4. **Given** a 1-tile layout (including any migrated from before this feature),
   **When** the operator taps it,
   **Then** it renders as a single full-area tile (1×1 grid) — identical to the
   pre-feature single-cell view.

---

### User Story 3 — Per-tile overlay highlight on the kiosk (Priority: P1)

An overlay bound to a tile of a published wall is highlighted (Automation fires
`HighlightOverlay`, or a manual highlight). On the kiosk rendering that wall,
**every tile bound to that overlay** flashes the highlight for the requested
duration (typically one tile; N if the operator reused the overlay); tiles bound
to other overlays are unaffected.

**Why this priority:** This is the feature's payoff and the M2 unblock — "the right
tile lights up." It proves the overlay-keyed highlight routes correctly in a grid.
It is also the only piece requiring net-new frontend wiring of the existing
backend `OverlayHighlightChanged` broadcast.

**Independent Test:**

1. Continuing from US-2 (a kiosk renders a 2×2 wall; tile A is bound to overlay X,
   no other tile bound to X).
2. Trigger a highlight on overlay X (publish `OverlayHighlightRequestedV1` for X,
   e.g. via an Automation rule or a test harness).
3. Within ≤ 1 s, **tile A** shows the `ssE-overlay-highlight` treatment for
   `DurationMs`, then auto-reverts. Tiles bound to other overlays never change.

**Acceptance Scenarios:**

1. **Given** a kiosk rendering a 2×2 wall where tile A (only) is bound to overlay X,
   **When** an `OverlayHighlightChanged` frame for overlay X arrives,
   **Then** tile A applies the highlight class for `DurationMs` and auto-reverts;
   the other tiles are unaffected.
2. **Given** a 2×2 wall where tiles A and C are **both** bound to overlay X,
   **When** an `OverlayHighlightChanged` frame for overlay X arrives,
   **Then** **both** tile A and tile C apply the highlight for `DurationMs`
   (highlight-all-matching semantic, ADR-0112 §5); tiles B/D are unaffected.
3. **Given** two `OverlayHighlightChanged` frames for overlay X with overlapping
   durations,
   **When** both land,
   **Then** the highlight on the matching tile(s) survives until the later expiry
   (OR'd, matching the existing contract on `OverlayHighlightRequestedV1`).
4. **Given** a highlight for an overlay not bound to any tile on the rendered wall,
   **When** the frame arrives,
   **Then** the kiosk applies no highlight (no error, no spurious flash).

---

### User Story 4 — Admin edits a published wall's tiles via the revision chain (Priority: P2)

An admin clicks **Edit** on a Published wall, gets a new **Draft revision** in the
same chain (spec 003 US4 lifecycle, unchanged), changes the grid (resize, swap a
tile's camera/overlay, add/remove a tile), and publishes — the old revision
auto-archives, kiosks force-disconnect (spec 003 US3 path, unchanged).

**Why this priority:** Reuses the spec 003 revision lifecycle wholesale; the only
new surface is the designer editing a multi-tile draft. Lower than US1–3 because it
adds no new lifecycle primitive — it is the designer applied to `BranchDraft` +
`EditDraft` with the multi-tile payload. **In scope for v1** (full designer,
resolved clarification #3).

**Acceptance Scenarios:**

1. **Given** a Published 2×2 wall (revision N),
   **When** the admin `POST`s to `/layouts/{id}/draft`,
   **Then** a new Draft revision N+1 is created **pre-filled with revision N's grid
   and tiles** (branch-from-baseline, as today).
2. **Given** a Draft revision,
   **When** the admin `PATCH`es it with a new grid + tile set (e.g. swap a tile's
   camera, add a 2nd tile to a 1×2),
   **Then** the draft is mutated in place (no further revision spawned), subject to
   the grid invariants (FR set below).
3. **Given** the admin publishes revision N+1,
   **When** the publish completes,
   **Then** revision N transitions Published→Archived and N+1 Draft→Published in one
   transaction; kiosks rendering N force-disconnect to the picker (spec 003 US3).

---

### Edge Cases

- **Empty tile set on create/publish:** rejected — a revision MUST have ≥ 1 tile
  (`400` / `LAYOUT_GRID_EMPTY`).
- **Two tiles at the same `(row, col)`:** rejected (`400` /
  `LAYOUT_TILE_POSITION_DUPLICATE`).
- **Tile out of grid bounds** (`row ≥ rows` or `col ≥ cols`): rejected (`400` /
  `LAYOUT_TILE_OUT_OF_BOUNDS`).
- **Grid exceeds the cap** (`rows × cols > 4` or populated tiles `> 4`): rejected
  (`400` / `LAYOUT_GRID_TOO_LARGE`). See ADR-0112 §4.
- **Same overlay on two tiles in one revision:** **allowed** — a highlight lights
  all matching tiles (ADR-0112 §5).
- **Same camera on two tiles:** allowed (a valid operator choice).
- **Tile bound to an overlay that is later archived:** the tile shows the existing
  "Overlay unavailable" treatment (spec 004 path on `CellPage`, applied per tile);
  the wall stays Published.
- **Tile's camera goes Offline:** that tile shows the spec 002 offline state; other
  tiles keep playing (per-`CameraViewer` already isolates this).
- **Migrated 1-tile layout:** renders identically to the pre-feature single-cell
  view; no operator-visible change.
- **A wall is archived while a kiosk renders it:** the spec 003 US3
  force-disconnect path fires unchanged (the wall is one chain, one archive).

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001:** A Layout **Revision** MUST carry an ordered, **non-empty** set of
  **Tiles** and a **GridDimensions** `{ rows, cols }`. A Tile is
  `{ camera (required), overlay (optional), position { row, col } }`.
- **FR-002:** The aggregate MUST enforce, inside its transaction: ≥ 1 tile; no two
  tiles at the same position; every tile in-bounds; `rows × cols ≤ 4` and populated
  tiles `≤ 4` (ADR-0112 §4). Violations return `Result<…, Error>` mapped to `400`
  with the codes in Edge Cases.
- **FR-003:** A camera MAY be reused across tiles; an overlay MAY be reused across
  tiles (a highlight lights all matching tiles, FR-012).
- **FR-004:** The revisioned-aggregate lifecycle (Draft→Published→Archived, branch,
  revert, at-most-one-Published-per-chain, optimistic concurrency per ADR-0043) is
  **unchanged** from spec 003. Only the revision payload changes.
- **FR-005:** `POST /layouts` accepts the multi-tile body `{ name, grid, tiles }`
  (a single-camera wall is a 1×1 grid with one tile). `PATCH
  /layouts/{id}/revisions/{n}` MUST accept the multi-tile draft edit. The legacy
  single-scalar `cameraIdentifier`/`overlayIdentifier` request shape is **removed**
  (clean cut — all in-repo callers migrate in this feature).
- **FR-006:** `GET /layouts/{id}` and `GET /layouts?state=...` MUST return the grid
  dimensions and tile set per revision. The legacy scalar
  `cameraIdentifier`/`overlayIdentifier` response fields are **removed**; consumers
  read `tiles`.
- **FR-007:** Publishing a revision MUST publish **`LayoutRevisionPublishedV2`**
  (full tile set + grid) in `Shared.Contracts/LayoutComposition/`, versioned per
  ADR-0073. `LayoutRevisionPublishedV1` is **removed** and the Audit subscriber
  (`IntegrationEventAuditHandler`) updated to V2 in the same feature.
  `LayoutRevisionArchivedV1` is unchanged.
- **FR-008:** All `/layouts/*` writes MUST require `sse.layouts.write`; reads
  require `sse.layouts.read` (unchanged from spec 003 / `LayoutEndpoints.cs`).
- **FR-009:** A data migration MUST backfill every existing revision into a single
  primary tile at `(0,0)` with a 1×1 grid, then **drop the now-redundant
  `camera_id`/`overlay_id` columns in the same migration** (clean cut), with
  **zero data loss**, via an EF migration through the `MigrationRunner` (ADR-0067).
  Existing published layouts MUST keep working as 1-tile walls.
- **FR-010:** The management-web **wall designer** MUST let the admin choose grid
  dimensions (≤ 2×2 in v1), assign a camera (required) + optional overlay per tile,
  leave cells empty, resize the grid, **edit a published wall via a new draft
  (US4)**, validate against FR-002 with inline feedback (RHF + Zod, ADR-0079;
  Radix + Tailwind, ADR-0077/0078), and save/publish through the existing
  draft→publish lifecycle.
- **FR-011:** The kiosk MUST render the published wall as a CSS grid of
  `<CameraViewer>` tiles (the spec 002 composite, unchanged), with empty cells as a
  placeholder. The N=1 case MUST render identically to today's single-cell
  `CellPage`.
- **FR-012:** The kiosk MUST subscribe to the existing overlay-keyed
  `OverlayHighlightChanged` SignalR frame (one subscription per wall) and apply the
  `ssE-overlay-highlight` treatment to **every tile whose overlay matches**
  `message.overlay` for `DurationMs`, then auto-revert. Overlapping highlights on
  the same overlay are OR'd. This is net-new frontend wiring — the backend
  broadcast already exists and is unchanged.
- **FR-013:** No cross-context project references (ADR-0027). The kiosk merges
  camera/overlay reads in the browser. NetArchTest still passes.

### Key Entities

- **Layout (aggregate root)** — unchanged identity/lifecycle; owns Revisions.
- **Revision (entity)** — now owns **Tiles** (collection) + **GridDimensions**,
  replacing the single `Camera` + optional `Overlay`. Lifecycle unchanged.
- **Tile (value object, new):** `{ Camera: CameraIdentifier, Overlay:
  Option<OverlayIdentifier>, Position: GridPosition }`.
- **GridPosition (value object, new):** `{ Row, Col }` (0-indexed), in-bounds vs.
  the owning revision's `GridDimensions`.
- **GridDimensions (value object, new):** `{ Rows, Cols }` with `Rows × Cols ≤ 4`;
  holds the `MaxTiles = 4` / default-`2×2` constants (single source of truth shared
  with the designer).
- **LayoutRevisionPublishedV2 (integration event, new — replaces V1):**
  `{ Layout, RevisionNumber, Name, Tiles[{ Camera, Overlay?, Row, Col }], GridRows,
  GridCols, PublishedAt, PublishedBy, Metadata }` in
  `Shared.Contracts/LayoutComposition/`.

### External Dependencies

- **Postgres** (new `layout_revision_tiles` owned table + grid columns; legacy
  scalar columns dropped; EF migration via MigrationRunner).
- **RabbitMQ** (the V2 event ships on the existing LayoutComposition queues).
- **Keycloak** (no new scopes — reuses `sse.layouts.write` / `sse.layouts.read`).
- **MediaMTX / StreamDistribution** (consumed per tile by `CameraViewer`; no new
  code there — but N simultaneous WHEP sessions per kiosk; see §IV impact).

### Cross-context contracts

- **Outbound (published):** `LayoutRevisionPublishedV2` (new, replaces V1),
  `LayoutRevisionArchivedV1` (unchanged).
- **Inbound (subscribed):** `OverlayHighlightRequestedV1` (unchanged handler;
  overlay-keyed; the grid routing is entirely kiosk-side).
- **Audit** (`IntegrationEventAuditHandler`) updated to consume V2.
- **No project references** between LayoutComposition and StreamDistribution /
  CameraCatalog / OverlayDesigner (NetArchTest enforces).

## Latency Budget Impact (constitution §IV — REQUIRED)

| Leg | Impact |
|---|---|
| Camera → SFU (≤ 80 ms) | N/A — unchanged. |
| **SFU → kiosk decode (≤ 120 ms)** | **AFFECTED.** A wall opens **N simultaneous WHEP `RTCPeerConnection`s** in one tab. Each must HW-decode within 120 ms. Mitigation: `MaxTiles = 4` cap (ADR-0112 §4) keeps every tile within kiosk-class GPU HW-decode capacity with margin, avoiding software-decode fallback that blows the leg. |
| Presentation buffer (≤ 200 ms) | N/A — per-tile PTP playout is unchanged (spec 002 path per `CameraViewer`). |
| **Event → overlay state (≤ 200 ms)** | **Unchanged path, no new code.** The highlight leg (RabbitMQ → `OverlayHighlightRequestedV1Handler` → SignalR) is byte-identical; the grid only changes *which DOM node(s)* the existing frame targets, client-side. |
| **Composite + render (≤ 50 ms)** | **AFFECTED (low risk).** Rendering a CSS grid of ≤ 4 `<video>` elements + per-tile highlight class toggle is sub-frame; the binding cost is decode, not composite. |
| Headroom (≤ 150 ms) | Preserved by the tile cap. |

**Verification at Phase 5:** measure click-to-first-frame per tile on a 2×2 wall
on kiosk-class hardware; confirm decode stays ≤ 120 ms (no software-decode
fallback) and the existing single-cell budget does not regress.

## Independent End-to-End Test Procedure

Runs against the live Aspire stack (ADR-0103 fixture; ADR-0108 Playwright gate):

1. `aspire run`. Sign in to management-web as admin.
2. Register 4 cameras; publish ≥ 1 overlay.
3. Create a **2×2** wall: assign a camera to each cell, an overlay to tile (0,0);
   leave the flow valid. Save as draft → **Publish**.
4. Open kiosk-web, sign in, tap the wall. Assert a **2×2 grid of 4 live tiles**
   renders; tile (0,0) shows its overlay label.
5. Trigger an `OverlayHighlightRequestedV1` for tile (0,0)'s overlay. Assert
   **tile (0,0)** flashes `ssE-overlay-highlight` for the duration, then reverts.
6. From management-web, **archive** the wall. Assert the kiosk force-disconnects to
   the picker within ≤ 1 s (spec 003 US3 path).

**Automatable in the e2e gate (ADR-0108):** steps 1–4 and 6 (author → publish →
grid renders → archive → force-disconnect) extend `e2e/layouts.spec.ts`. Step 5
(highlight routing) is automatable if the harness can publish a highlight (via an
Automation rule seeded in-test, or a test-only highlight trigger); otherwise the
per-tile highlight routing is covered by a component test (kiosk grid + a
synthetic `OverlayHighlightChanged`) and step 5 is the manual dev-run check.

## Success Criteria *(mandatory)*

- **SC-001:** Author a 2×2 wall → operator sees it in the kiosk picker — ≤ 5 s p95.
- **SC-002:** Each tile of a published wall renders its first decoded frame within
  ≤ 3 s p95 (per-tile, reusing spec 002 SC); decode stays ≤ 120 ms (no software
  fallback) at the 4-tile (2×2) cap.
- **SC-003:** A highlight for an overlay bound to exactly one tile lights **only
  that tile** within ≤ 1 s; reusing the overlay on N tiles lights all N (and no
  others).
- **SC-004:** Existing single-camera layouts published before this feature render
  unchanged (1-tile, 1×1) and appear in the V2 published list — proving the EF
  backfill and the migrated read path.
- **SC-005:** Grid invariants (≥1 tile, no dup positions, in-bounds, ≤4) are
  enforced by aggregate unit tests; violating writes return `400` with the
  documented codes.
- **SC-006:** `GET /layouts?state=published` returns within ≤ 100 ms p95 (no
  regression).
- **SC-007:** NetArchTest passes — no new cross-context references; Domain has no
  infrastructure dependency.
- **SC-008:** Coverage gates (ADR-0065) hold: LayoutComposition.Domain ≥ 90 %,
  .Application ≥ 80 %, Shared.Contracts ≥ 90 %.
- **SC-009:** No regression to the spec 003 lifecycle (publish/branch/revert/archive
  + force-disconnect) or the single-cell budgets.

## Assumptions

- **Cell = 1-tile wall.** The single-cell view (`CellPage`) becomes the N=1 grid
  renderer; there is no separate WallPage (ADR-0112 decision 1).
- **Reuse the spec 003 lifecycle wholesale.** Only the revision payload, the
  designer, and the kiosk renderer change.
- **Highlight stays overlay-keyed; no backend change on that leg.** Grid routing is
  entirely kiosk-side.
- **Clean V2 cut is safe** because the system is pre-production and every contract
  consumer is in-repo (ADR-0112 Context constraint 1).
- **Admin signs into kiosk-web** (spec 003 assumption, unchanged; unattended-kiosk
  auth still deferred).
- **Single fab, single Keycloak realm** (unchanged).
