# Feature Specification: Author an Overlay and Render It on a Kiosk

**Feature Branch:** `004-overlay-designer`

**Created:** 2026-05-27

**Status:** Draft (Phase 1 — Specify)

**Input:** Fourth feature of Smart Sentinel Eye — the first end-to-end
slice through the **OverlayDesigner** bounded context, completing the
walking-skeleton goal *1 camera + 1 cell + 1 overlay* (constitution
§I + ADR-0036). Builds on spec 001 (registered cameras), spec 002
(live streams), and spec 003 (published Layouts on kiosks). Selected as
spec 004 because:

1. The walking skeleton is incomplete without an overlay — every
   downstream feature (event-driven status badges, automation rules,
   variable bindings) assumes the authoring + rendering pipeline
   exists. We unblock those by landing it now.
2. The latency NFR in constitution §IV (`event arrival → overlay
   rendered, frame-synced ≤ 800 ms`) is unmeasurable until there's an
   overlay to render. Spec 004 produces the substrate; spec 005+ wires
   variables to it.

Spec 004 is **authoring + rendering only** — no variable binding, no
event-driven updates. ``{{placeholder}}`` tokens in overlay text are
intentionally rendered literally on the kiosk in v1; spec 005+ wires
them to typed system variables.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Admin authors and publishes an overlay (Priority: P1)

A fab admin opens the management app, clicks **New overlay**, types
text into the WYSIWYG canvas (e.g. `Production Line 1`), positions
and sizes it by drag-and-drop, picks a font size, and clicks
**Publish**. The overlay becomes the active version of that named
overlay and is broadcast to any kiosk currently rendering a Layout
that references it.

**Why this priority:** Smallest vertical slice through OverlayDesigner.
Exercises every primitive the walking-skeleton needs: ``Overlay``
aggregate with revision chain (mirroring spec 003's ``Layout``), the
WYSIWYG editor (drag + resize handles + font-size slider per Phase-1
Q&A), and the SignalR fan-out for cross-aggregate updates.

**Independent Test:**

1. Start the system locally via `aspire run`.
2. Sign in to `management-web` as admin.
3. Click **Overlays** in the top nav, then **New overlay**.
4. Enter a unique name (e.g. *Line-1 Title*).
5. In the editor: type `Production Line 1` into the label;
   drag-and-drop the label to roughly `{ x: 0.5, y: 0.05 }` (top
   centre); drag a resize handle to set width; pick `48px` font.
6. Click **Save as draft**. The new row shows state **Draft**.
7. Click **Publish** on the row. State flips to **Published** within
   ≤ 1 s. `OverlayRevisionPublishedV1` lands on the integration bus.

**Acceptance Scenarios:**

1. **Given** an authenticated admin,
   **When** the admin `POST`s to `/overlays` with
   `{ name, label: { text, normalizedX, normalizedY, normalizedWidth,
   normalizedHeight, fontSizePx } }`,
   **Then** the response is `201 Created` with the new overlay's
   identifier and state `Draft`.
2. **Given** a Draft overlay,
   **When** the admin `POST`s to
   `/overlays/{id}/revisions/{n}/publish`,
   **Then** the response is `200 OK`, state is `Published`,
   `OverlayRevisionPublishedV1` is on the bus within ≤ 200 ms,
   and the SignalR `LayoutLifecycle` hub broadcasts the change to
   connected kiosks.
3. **Given** a name collision with an existing non-archived overlay,
   **When** the admin tries to save,
   **Then** the response is `409 Conflict` with code
   `OVERLAY_NAME_TAKEN`.
4. **Given** the admin lacks the `sse.management` scope,
   **When** the admin tries any `/overlays/*` write,
   **Then** the response is `403 Forbidden`.

---

### User Story 2 — Admin binds an overlay to a Layout (Priority: P1)

The admin opens an existing Published Layout, picks an overlay from a
list of all Published overlays, and the binding goes live. The kiosk
rendering that Layout immediately composites the overlay text over
the camera frame at the normalized coordinates.

**Why this priority:** Without a binding, the overlay is unrenderable.
Equal P1 with US-1 because the spec is "rendered on a kiosk", not
just "authored". Extends LayoutComposition with an
`OverlayIdentifier?` field on a Layout revision (Phase-1 Q&A:
optional, latest-Published binding).

**Independent Test:**

1. Continuing from US-1 (an overlay is Published).
2. In management-web → **Layouts**, open an existing Published
   layout. Click **Edit (new draft)** to spawn a draft revision (the
   spec-003 flow).
3. In the draft-edit dialog (extended in spec 004), pick the
   Published overlay from a dropdown. Save + publish.
4. Open kiosk-web on a second browser, sign in, tap the layout.
5. Within ≤ 3 s the camera frame appears with the overlay text
   composited over it at the authored position.

**Acceptance Scenarios:**

1. **Given** a Published Layout with no overlay bound,
   **When** the admin opens its edit-as-new-draft dialog and selects
   an overlay,
   **Then** the new Layout revision's payload includes
   `overlayIdentifier`. Publishing the revision triggers the
   spec-003 atomic-swap path; the kiosk picks up the new binding.
2. **Given** a Layout revision references an Overlay,
   **When** kiosk-web fetches `GET /layouts/{id}` and the Published
   revision has `overlayIdentifier`,
   **Then** the kiosk fetches `GET /overlays/{overlayIdentifier}`
   (its Published revision) and composites the label over the
   camera viewer.
3. **Given** a Layout revision has no `overlayIdentifier`,
   **When** the kiosk renders the cell view,
   **Then** the camera viewer renders alone (no overlay layer).
   Existing spec-003 behaviour is preserved exactly.

---

### User Story 3 — Editing a Published overlay propagates instantly (Priority: P1)

Per the Phase-1 Q&A "always latest Published" binding: when an admin
edits and republishes an overlay, every Layout that references it sees
the new revision live. Connected kiosks update within ≤ 1 s via
SignalR push.

**Why this priority:** The trade-off the admin explicitly took — a
single overlay shared across N Layouts, where changing the overlay
changes them all. Without this push path, kiosks would stay frozen on
the old text until the operator manually re-opens the layout.

**Independent Test:**

1. Continuing from US-2 (a kiosk is rendering a Layout that binds an
   overlay).
2. In management-web → **Overlays**, click **Edit (new draft)** on
   the Published overlay. Change the text to *Production Line 1 —
   Maintenance*. Save + Publish.
3. Within ≤ 1 s the kiosk's rendered overlay updates to show the new
   text without operator action.

**Acceptance Scenarios:**

1. **Given** at least one kiosk has an active SignalR connection
   and is rendering a Layout that references an Overlay,
   **When** the admin publishes a new revision of that Overlay,
   **Then** the existing `LayoutLifecycle` hub broadcasts
   `OverlayRevisionPublished` to all connected clients within
   ≤ 1 s, the kiosk re-fetches the bound overlay, and the rendered
   label updates without page reload.
2. **Given** the SignalR channel was disconnected when the overlay
   was republished,
   **When** the channel reconnects,
   **Then** the kiosk invalidates the overlay cache and re-fetches
   within ≤ 5 s (mirrors spec 003's reconcile-on-reconnect).

---

### User Story 4 — Edit-after-publish via revision chain (Priority: P1)

Editing a Published overlay creates a new Draft revision in the same
logical chain — the existing Published revision stays live until the
admin clicks **Publish** on the new draft. Mirrors spec 003 US-4
exactly so admins have one mental model.

**Why this priority:** Without it, editing means "archive and recreate"
— operator-hostile and loses the audit trail. Equal P1 with US-1 to
US-3 because all four together form the smallest publishable
authoring experience.

**Independent Test:**

1. Continuing from US-1 (an Overlay is Published, revision 1).
2. In management-web → **Overlays**, click **Edit (new draft)**.
3. The original row stays **Published v1**; a new row appears below
   it: state **Draft v2** with the existing label pre-filled.
4. Change the text + position. Click **Publish** on revision 2.
5. Revision 1 transitions to **Archived**; revision 2 transitions to
   **Published**. Any Layout that referenced the overlay
   automatically sees the new content (US-3 push fires).

**Acceptance Scenarios:**

1. **Given** an Overlay with a Published revision (N),
   **When** the admin `POST`s to `/overlays/{id}/draft`,
   **Then** the response is `201 Created` with revision N+1 in state
   `Draft`, the same logical `overlayIdentifier`, and the label
   payload copied from revision N.
2. **Given** an Overlay with revisions N (Published) and N+1
   (Draft),
   **When** the admin publishes revision N+1,
   **Then** revision N atomically transitions to `Archived` in the
   same transaction; the chain still has exactly one Published
   revision.

---

### User Story 5 — Variable binding, animation, image/shape primitives, multi-overlay grids (Priority: P3)

The overlay grows beyond a single static text label: `{{variable}}`
tokens bind to typed system variables (spec 005+); overlays can carry
multiple labels, shapes, images, and animation/transition rules;
layouts compose many overlays per cell.

**Why this priority:** Out of scope for v1. Listed so the v1 design
doesn't paint us into a corner — the Overlay aggregate must admit
future field additions without rework. *(Deferred to specs 005+.)*

---

### Edge Cases

- **Overlay referenced by a Layout is archived:** Existing Layout
  revisions still reference the archived overlay identifier. Kiosk
  fetches the overlay; the API returns 404 (no Published revision).
  Kiosk renders the camera viewer alone and surfaces a single-line
  banner ("overlay unavailable"). Admin should re-bind to a different
  overlay (US-2 flow).
- **Two admins publish two new Overlay revisions concurrently:**
  Optimistic concurrency on the chain's `Version` field (ADR-0043);
  the loser returns `409 Conflict` with `OVERLAY_REVISION_STALE`.
- **Admin renames an overlay:** Allowed on Draft revisions only;
  chain name is the canonical identifier. Same rule as Layout.
- **Overlay placeholder syntax appears inside non-placeholder text
  (e.g. literal `{{`):** Per Phase-1 Q&A, v1 renders the literal
  characters anyway (no escape syntax). Spec 005 introduces escaping
  if/when variable binding lands.
- **Label is dragged outside the 0..1 bounding box:** Clamped at the
  edit layer; the API also clamps in the value-object constructor.
  No silent clipping on the kiosk.
- **Multiple kiosks render the same Layout, one is mid-publish-frame
  when the overlay republishes:** The SignalR push reaches both;
  each independently invalidates its cache and re-fetches. No global
  consistency guarantee — eventual consistency within ~1 s.
- **Kiosk disconnects + the bound overlay's Layout is also archived
  while the kiosk is offline:** Reconnect-and-reconcile from spec
  003 handles the Layout side; the kiosk force-disconnects to the
  picker before it ever notices the overlay is stale.
- **Kiosk renders an overlay whose `fontSizePx` would push the label
  off-screen at the kiosk's resolution:** Kiosk-side rendering uses
  CSS `vw`/`vh`-derived font sizing (input fontSizePx is the
  author-time reference at 1080p). Labels stay on-screen at lower
  resolutions because they scale proportionally.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001:** The system MUST persist an **Overlay** as a logical
  chain of one or more **Revisions**, mirroring the spec-003 ``Layout``
  shape exactly. Each revision has its own identifier, a
  `revisionNumber` (1-indexed, monotonic), a `state` (Draft |
  Published | Archived), and a single ``Label`` payload.
- **FR-002:** A logical Overlay chain MUST allow **at most one
  Published revision at any time**. Enforced inside the aggregate +
  partial unique index in Postgres (the same belt-and-braces pattern
  as Layout's `ux_layout_revisions_one_published`).
- **FR-003:** Overlay state transitions MUST follow:
  - `Draft → Published` (Publish action; archives the previously-
    Published sibling in the same transaction)
  - `Published → Draft` (Revert)
  - `Published → Archived` (Archive)
  - `Draft → Archived` (Abandon)
  - All other transitions are forbidden.
- **FR-004:** Editing a Published revision MUST create a new Draft
  revision in the same chain (`revisionNumber + 1`). Editing a Draft
  revision MUST mutate in place. Identical rules to Layout.
- **FR-005:** A ``Label`` value object carries:
  - `text` (string, ≤ 256 chars, non-empty after trimming)
  - `normalizedX` and `normalizedY` (decimal in 0..1)
  - `normalizedWidth` and `normalizedHeight` (decimal in 0..1, both
    > 0)
  - `fontSizePx` (int, 8..256)
  Constructors validate via `Ensure.That(...)`.
- **FR-006:** Overlay names MUST be unique across the set of non-
  Archived chains. (Same rule + same application-level enforcement
  as Layout name uniqueness in spec 003.)
- **FR-007:** The system MUST expose:
  - `POST /overlays` — create first revision (Draft).
  - `POST /overlays/{id}/draft` — branch a new Draft revision off
    the chain's current Published.
  - `PATCH /overlays/{id}/revisions/{n}` — edit a Draft revision's
    label payload.
  - `POST /overlays/{id}/revisions/{n}/publish` — publish a Draft.
  - `POST /overlays/{id}/revisions/{n}/revert` — Published → Draft.
  - `POST /overlays/{id}/revisions/{n}/archive` — archive any
    revision.
  - `GET /overlays?state=...` — list with optional state filter.
  - `GET /overlays/{id}` — full chain + revisions.
- **FR-008:** All `/overlays/*` write endpoints MUST require an
  authenticated user with the `sse.management` scope.
  `GET /overlays?state=published` and `GET /overlays/{id}` MUST also
  require authentication (kiosk-web reuses the existing admin OIDC
  flow per spec 003 Phase-1 choice).
- **FR-009:** The **Layout aggregate** in LayoutComposition MUST gain
  an optional `OverlayIdentifier` field on each Revision. When set,
  the kiosk MUST fetch and render the Published revision of that
  overlay. The binding is "always latest Published" — no overlay
  revision pinning per Phase-1 Q&A.
- **FR-010:** Publishing an Overlay revision MUST publish
  `OverlayRevisionPublishedV1` on the integration bus. Archiving
  MUST publish `OverlayRevisionArchivedV1`. Both are versioned per
  ADR-0073 and live in `Shared.Contracts/OverlayDesigner/`.
- **FR-011:** The SignalR `LayoutLifecycle` hub from spec 003 MUST
  broadcast `OverlayRevisionPublished` and `OverlayRevisionArchived`
  events. Kiosks rendering a Layout that references the affected
  Overlay MUST update or fall back within ≤ 1 s.
- **FR-012:** The management-web **Overlays** page MUST render a
  WYSIWYG editor with: drag-to-position, eight-handle resize, and a
  font-size slider per the Phase-1 Q&A. The editor MUST show the
  label over a representative camera frame (placeholder image
  acceptable in v1 — embedding a live frame is a stretch goal).
- **FR-013:** Unbound `{{placeholder}}` tokens MUST render verbatim
  on the kiosk in v1 (no variable binding yet). Spec 005+ introduces
  the binding.
- **FR-014:** Kiosk-side overlay rendering MUST composite the label
  over the camera viewer at the normalized coordinates, with font
  size scaling proportionally to the kiosk viewport. No new viewer
  resolution constraints.
- **FR-015:** No cross-context project references between
  OverlayDesigner and LayoutComposition / CameraCatalog /
  StreamDistribution. ``OverlayIdentifier`` is a value-copied
  identifier on Layout revisions (same pattern as
  ``CameraIdentifier``).
- **FR-016:** Overlay aggregates MUST use optimistic concurrency
  (`Version` field per ADR-0043). Concurrent revision-state changes
  return `409 Conflict` with code `OVERLAY_REVISION_STALE`.
- **FR-017:** Overlay state changes MUST be observable through the
  audit log (Audit & Observability subscribes to the V1 events,
  same as Layout events).

### Key Entities

- **Overlay (aggregate root, OverlayDesigner.Domain):** One per
  logical overlay chain. Owns:
  `overlayIdentifier` (Guid v7, per ADR-0090),
  `name` (OverlayName VO),
  `revisions` (collection of Revision entities),
  `createdAt`, `createdBy`,
  `version` (concurrency).
- **Revision (entity inside Overlay):** One per edit. Owns:
  `revisionIdentifier`, `revisionNumber`, `state`, **`label`**
  (Label VO — see FR-005), `createdAt`, `createdBy`, `publishedAt`,
  `archivedAt`.
- **Label (value object):** `{ text, normalizedX, normalizedY,
  normalizedWidth, normalizedHeight, fontSizePx }` per FR-005.
- **OverlayRevisionState (value object):** Enum-style record;
  `Draft | Published | Archived`.
- **OverlayRevisionPublishedV1 (integration event):**
  `{ OverlayIdentifier, RevisionNumber, Name, Text, NormalizedX,
  NormalizedY, NormalizedWidth, NormalizedHeight, FontSizePx,
  PublishedAt, PublishedBy }`. Primitive types only.
- **OverlayRevisionArchivedV1 (integration event):**
  `{ OverlayIdentifier, RevisionNumber, ArchivedAt, ArchivedBy }`.

### Cross-context contracts

- **Inbound (subscribed):** none in v1. Future spec wires
  ``LayoutRevisionPublished/Archived`` so OverlayDesigner can audit
  who references which overlay (out of scope).
- **Outbound (published):** `OverlayRevisionPublishedV1`,
  `OverlayRevisionArchivedV1` — new files under
  `Shared.Contracts/OverlayDesigner/`. Consumed by:
  - LayoutComposition's existing `LayoutLifecycle` SignalR pump
    (broadcasts to kiosks),
  - Audit & Observability (future).
- **No project references** between OverlayDesigner and
  LayoutComposition / CameraCatalog / StreamDistribution (NetArchTest
  enforces).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001:** Create an overlay → operator sees the bound layout
  pick it up — **≤ 5 seconds** at p95 from publish-overlay to
  kiosk-render.
- **SC-002:** Publish a new Overlay revision → connected kiosks
  rendering bound layouts update their overlay text — **≤ 1
  second** at p95 (FR-011).
- **SC-003:** Publish a new Overlay revision atomically archives
  the previous Published revision (same-transaction invariant
  enforced by aggregate + partial unique index).
- **SC-004:** Kiosk renders the overlay label over the camera frame
  at the authored normalized coordinates with ≤ ± 2 % drift at the
  default kiosk resolution.
- **SC-005:** `GET /overlays?state=published` returns within
  **≤ 100 ms** at p95.
- **SC-006:** Integration test verifies the full author → publish →
  bind → render → republish loop within **≤ 60 seconds** of wall-
  clock test time.
- **SC-007:** Architecture tests still pass — no new cross-context
  references (ADR-0027) and the Domain layer of OverlayDesigner has
  no infrastructure dependencies (ADR-0044).
- **SC-008:** Coverage thresholds from ADR-0065 hold for the new
  context: OverlayDesigner.Domain ≥ 90 %, .Application ≥ 80 %,
  Shared.Contracts ≥ 90 %.
- **SC-009:** No regressions in existing budgets: spec 002 click-to-
  first-frame p95 stays ≤ 3 s; spec 003 archive-to-force-disconnect
  p95 stays ≤ 1 s.

## Assumptions

- **WYSIWYG editor over a placeholder background.** The author-time
  preview shows the label over a static representative image, not a
  live camera frame. Embedding the live frame in the editor is a
  stretch goal that adds streaming-while-authoring complexity and
  isn't load-bearing for the spec.
- **Single text label per overlay in v1.** Multi-label, shapes, and
  images are deferred to spec 005+. The schema permits a future
  collection without a breaking migration (revisions are isolated;
  expanding a revision's payload is additive).
- **No variable binding in v1.** `{{placeholder}}` is literal. Spec
  005 wires system variables.
- **Latest-Published binding.** A Layout revision points at an
  Overlay chain, not a specific revision number. Publishing a new
  overlay revision propagates instantly to every Layout that
  references it (FR-009 + FR-011). Pinned bindings remain a v2
  consideration if cross-layout coupling becomes painful.
- **SignalR hub reuse.** The existing `LayoutLifecycle` hub from
  spec 003 grows two new server-to-client methods
  (`OverlayRevisionPublished` / `OverlayRevisionArchived`). No new
  hub endpoint or auth changes.
- **Drag library choice deferred to plan.md.** Likely candidates:
  `react-rnd` (mature, drag + resize built-in), `@dnd-kit` (more
  flexible, no resize built-in), or hand-rolled. Plan picks one.
- **CameraViewer extension model.** The kiosk's `<CameraViewer>`
  composite gains an optional `overlay?: { label }` prop. No
  separate overlay component — keeps the layering inside one
  composite so frame-sync is straightforward.

## Resolved Clarifications (Phase 1)

Eight clarifications resolved during the Phase 1 Q&A round:

| # | Marker | Resolution |
|---|---|---|
| 1 | Aggregate boundary | **Separate Overlay aggregate**, Layout revision references it. Overlay can be authored independently and reused across layouts later. |
| 2 | Content model | **Single text label with `{{placeholder}}` syntax.** Placeholders are literal in v1; spec 005+ wires them to variables. |
| 3 | Coordinates | **Normalized 0..1 over the cell.** Resolution-independent; scales to any kiosk size. |
| 4 | Authoring surface | **Drag-on-canvas WYSIWYG**, drag + resize handles + font-size slider. |
| 5 | Overlay lifecycle | **Full Draft↔Published+Archived revision chain** mirroring Layout. Admins iterate without affecting live kiosks until Publish. |
| 6 | Layout-Overlay binding | **Always latest Published** of the referenced overlay. Republishing the overlay propagates instantly to every Layout that references it. |
| 7 | Unbound placeholders | **Render the literal `{{name}}` text** in v1. Spec 004 is the authoring + rendering pipeline only. |
| 8 | Editor scope | **Drag + resize handles + font-size slider.** Snap-to-grid deferred. |

## Open clarifications

None — Phase 1 Q&A closed every marker. Ready for Phase 2 (Plan).
