# Feature Specification: Author a Layout and Render It on a Kiosk

**Feature Branch:** `003-layout-composition`

**Created:** 2026-05-26

**Status:** Draft (Phase 1 — Specify)

**Input:** Third feature of Smart Sentinel Eye — the first end-to-end
slice through the **LayoutComposition** bounded context, building on
specs 001 (registered cameras) and 002 (live streams). Brings the
**kiosk-web** React app online for the first time and lands the real-
time push transport that ADR-0076 already plans for. Selected as spec
003 because it advances the walking-skeleton's "1 cell" milestone —
moving past "an admin watches a single camera" to "an admin publishes
a viewable surface that an operator picks up at a kiosk."

The cell content is deliberately minimal — a **single camera tile per
layout**. The grid (N×M rows/cols, overlays, automation bindings) is
left to spec 004+; we lock the *workflow* primitives here (Draft →
Published → Archived state machine, revision chain on edit-after-
publish, force-disconnect on archive) and lock the *transport* primitive
(WebSocket per ADR-0076 v1).

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Admin authors and publishes a Layout (Priority: P1)

A fab admin opens the management app, clicks **New layout**, picks a
registered camera, names the layout, saves it as a **Draft**, then
clicks **Publish**. The layout immediately becomes selectable in any
connected kiosk's picker.

**Why this priority:** Smallest possible vertical slice through
LayoutComposition. Exercises every primitive the walking-skeleton
needs: Layout aggregate with state machine, integration event on
publish, Postgres-backed CRUD persistence (per CLAUDE.md, this context
is not a Marten/event-sourcing candidate), and the management-web
authoring UI that future grid / overlay specs will extend.

**Independent Test:**

1. Start the system locally via `aspire run` (Postgres, RabbitMQ,
   Keycloak, MediaMTX, camera-catalog, stream-distribution, the new
   **layout-composition** API, and the **kiosk-web** app shell).
2. Sign in to `management-web` as admin.
3. Register a camera (spec 001 flow) and wait for the stream badge to
   leave Provisioning.
4. Click **Layouts** in the navigation. The page lists Drafts /
   Published / Archived (empty initially).
5. Click **New layout**, enter a unique name (e.g. *Line-1 Entrance*),
   pick the registered camera, click **Save as draft**. The new row
   shows state **Draft**.
6. Click the row's **Publish** action. The state flips to **Published**
   within ≤ 1 s. An integration event `LayoutRevisionPublishedV1` is
   on the bus.

**Acceptance Scenarios:**

1. **Given** an authenticated admin and at least one registered camera,
   **When** the admin `POST`s to `/layouts` with
   `{ name, cameraIdentifier }`,
   **Then** the response is `201 Created` with the new layout's
   identifier and state `Draft`.
2. **Given** a Draft layout,
   **When** the admin `POST`s to `/layouts/{id}/publish`,
   **Then** the response is `200 OK` with state `Published`, the
   layout's `publishedAt` is set, and `LayoutRevisionPublishedV1` is
   on the integration bus within ≤ 200 ms.
3. **Given** a Draft layout whose name collides with an existing
   non-archived layout's name,
   **When** the admin tries to save,
   **Then** the response is `409 Conflict` with code `LAYOUT_NAME_TAKEN`
   and no side effects.
4. **Given** the admin lacks the `sse.management` scope,
   **When** the admin tries any `/layouts/*` write,
   **Then** the response is `403 Forbidden` with no side effects.

---

### User Story 2 — Operator at a kiosk picks a Published layout (Priority: P1)

An admin walks up to a shop-floor kiosk that's already showing the
**kiosk-web** picker, signs in via the same Keycloak realm, sees a
list of Published layouts, taps one, and the single-cell view opens
with the camera's live stream rendered (reusing the CameraViewer
composite from spec 002).

**Why this priority:** This is the moment the system becomes
*useful* — until a layout reaches a kiosk screen, the project is just
a management dashboard. Brings **kiosk-web** online as the second
React app per ADR-0074 and proves the kiosk side of the
publish-then-view loop.

**Independent Test:**

1. Continuing from US-1, open the **kiosk-web** URL from the Aspire
   dashboard.
2. Sign in via the Keycloak redirect using the same admin credentials.
3. The kiosk shows a picker listing one entry — the layout from US-1.
4. Tap the entry. The picker is replaced by the single-cell view; the
   camera's live stream is visible within ≤ 3 s (matches spec 002's
   click-to-first-frame SLO since the renderer is the same composite).

**Acceptance Scenarios:**

1. **Given** an authenticated admin on kiosk-web with at least one
   Published layout,
   **When** the page calls `GET /layouts?state=published`,
   **Then** the response is `200 OK` with one entry per Published
   revision: `{ layoutIdentifier, name, cameraIdentifier,
   publishedAt }`. Drafts and Archived revisions are absent.
2. **Given** the operator taps a layout in the picker,
   **When** kiosk-web navigates to the cell view,
   **Then** the page renders `<CameraViewer cameraIdentifier=... />`
   (the shared composite) and a live WebRTC frame appears within
   ≤ 3 s at p95.
3. **Given** zero Published layouts exist,
   **When** the picker loads,
   **Then** the page shows an empty-state message ("No layouts
   published yet — ask your administrator") and no error.

---

### User Story 3 — Admin archives a Published layout, kiosk force-disconnects (Priority: P1)

An admin archives the layout an operator is currently watching. The
kiosk receives a real-time push from the backend and **force-
disconnects** the operator back to the picker, with no warning, within
≤ 1 s.

**Why this priority:** Locks in the real-time push primitive per ADR-
0076. Without it, the spec's strict "force-disconnect immediately"
revocation guarantee (chosen in Phase 1 Q&A) is unenforceable;
operators would stare at archived content indefinitely. Equal priority
with US-1 + US-2 because the revocation contract is the v1 promise
that future overlay/automation specs will lean on for live updates.

**Independent Test:**

1. Continuing from US-2 (a kiosk is rendering a layout's cell view).
2. From management-web on a second browser, archive the layout
   (action on the Layouts list row).
3. Within ≤ 1 s the kiosk's cell view is replaced by the picker; the
   archived layout is **absent** from the list.

**Acceptance Scenarios:**

1. **Given** an authenticated kiosk-web session connected to the
   real-time channel,
   **When** the admin archives a Published layout,
   **Then** the kiosk receives a `layout-archived` push within
   ≤ 1 s and the renderer transitions to the picker without operator
   interaction.
2. **Given** a kiosk that lost its real-time channel briefly,
   **When** the channel reconnects and a stale layout-archived event
   would have been missed,
   **Then** on reconnect the kiosk re-fetches `GET /layouts?state=
   published` and reconciles — any layout it was rendering that's no
   longer in the response triggers the same force-disconnect path.
   Reconnect-and-reconcile MUST happen within ≤ 5 s of the channel
   coming back.
3. **Given** the real-time push delivery fails for ≥ 30 s (e.g. the
   network is down end-to-end),
   **When** the kiosk eventually reconnects,
   **Then** the kiosk falls back to the reconnect-and-reconcile path
   above. There is no "frozen renderer" steady state.

---

### User Story 4 — Admin edits a Published layout via revision chain (Priority: P1)

An admin clicks **Edit** on a Published layout. The system creates a
new **Draft revision** in the same logical layout's chain (sharing the
name); the previously-Published revision stays live until the admin
clicks **Publish** on the new draft, at which point the old revision
is auto-archived and the new one takes its place.

**Why this priority:** Preserves the audit trail without freezing
Published layouts. Without revisions, the only safe edit path would
be "archive + new" (operator-hostile) or "edit-live" (no rollback,
no audit). Equal priority with US-1 because spec 003 has to lock the
edit story before plan.md commits the schema — adding revisions
later would be a breaking migration.

**Independent Test:**

1. Continuing from US-1 (a layout is Published).
2. In management-web, click **Edit** on the Published row. A new row
   appears below it: same name, state **Draft**, `revisionNumber = 2`.
3. The original row is still **Published**; any kiosk picker still
   lists it.
4. Change the draft's camera to a different registered camera, click
   **Publish**.
5. The original Published row transitions to **Archived**; the new
   draft transitions to **Published**. Kiosks rendering the original
   receive the force-disconnect from US-3.

**Acceptance Scenarios:**

1. **Given** a Published layout with `revisionNumber = N`,
   **When** the admin `POST`s to `/layouts/{id}/draft`,
   **Then** the response is `201 Created` with a new revision having
   `revisionNumber = N + 1`, state `Draft`, the same logical
   `layoutIdentifier` (the chain identity), and a fresh
   `revisionIdentifier`.
2. **Given** a logical layout with one Published revision (N) and one
   Draft revision (N+1),
   **When** the admin publishes revision N+1,
   **Then** revision N transitions `Published → Archived` atomically
   with N+1 transitioning `Draft → Published`. Both transitions are
   single-transaction; no kiosk ever sees zero or two Published
   revisions for the chain.
3. **Given** a Draft revision the admin wants to abandon,
   **When** the admin `DELETE`s (archives) it,
   **Then** the revision transitions to `Archived`; the currently-
   Published revision (if any) is unaffected.

---

### User Story 5 — Per-cell PTZ controls, multi-cell grids, overlays, automation bindings (Priority: P3)

The viewer panel grows beyond a single camera tile: grids of N cells,
overlays that bind to system variables, automation rules that publish
or archive layouts on event triggers.

**Why this priority:** Out of scope for v1 of this spec. Listed so
the v1 design doesn't paint us into a corner — the Layout aggregate
must admit future cell-collection / overlay-collection / binding
fields without rework. *(Deferred to specs 004+.)*

---

### Edge Cases

- **Layout created while the kiosk picker is open:** The kiosk
  receives a `layout-published` push within ≤ 1 s and the picker
  appends the new entry without a full reload.
- **Network partition between kiosk and backend:** The kiosk's
  real-time channel closes. The renderer keeps playing the current
  cell (WebRTC is independent of the layout-lifecycle channel) until
  either (a) the channel reconnects and reconciliation runs (US-3
  scenario 2), or (b) the WebRTC peer also disconnects (handled by
  spec 002's reconnecting banner). No special "channel down" UI in
  v1 — kiosks are on a flat L2 network with the backend (per
  spec 002's network assumption).
- **Concurrent publish attempts on two drafts of the same logical
  layout:** Optimistic concurrency on the logical-layout version
  field (per ADR-0043). One transaction wins, the other returns
  `409 Conflict` with code `LAYOUT_REVISION_STALE`. UI re-fetches
  and presents the conflict.
- **Admin edits a Draft a second time before publishing:** The Draft
  is mutated in place — editing a Draft does NOT spawn another
  revision. Revisions only branch off a Published parent.
- **Admin renames a layout:** Allowed on a Draft revision only; the
  Published revision's name is the chain's canonical name and is
  immutable. Renaming a Draft propagates to the chain on publish.
  *(Detail deferred to plan — the spec only commits to "renames are
  not lossy" and "name uniqueness is enforced at the chain level".)*
- **Admin archives an already-Archived revision:** Idempotent no-op;
  returns 200 with the current state.
- **Camera referenced by a Published layout is decommissioned (future
  spec):** Out of scope here. The kiosk renderer will see a stream
  that's `Offline` (spec 002 path) — the layout itself stays
  Published. A follow-up spec wires `CameraDecommissionedV1` →
  layout archival.
- **Kiosk's bearer token expires during a long-running viewing
  session:** The kiosk's real-time channel returns a `401`-class
  close frame; kiosk-web re-runs the OIDC silent-renewal flow and
  reopens the channel. While the channel is down, the WebRTC stream
  keeps playing (it's independent). After renewal, US-3 scenario 2
  reconciliation runs.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001:** The system MUST persist a **Layout** as a logical chain
  of one or more **Revisions**. Each revision has its own identifier,
  a `revisionNumber` (1-indexed, monotonically increasing within the
  chain), a `state` (Draft | Published | Archived), and a
  `cameraIdentifier`.
- **FR-002:** A logical layout chain MUST allow **at most one
  Published revision at a time.** The aggregate enforces this
  invariant across the entire revision chain.
- **FR-003:** Layout state transitions MUST follow:
  - `Draft → Published` (Publish action; archives the previously-
    Published sibling revision in the same chain in the same
    transaction)
  - `Published → Draft` (Revert action — allows editing without
    spawning a new revision)
  - `Published → Archived` (Archive action; terminal for that
    revision)
  - `Draft → Archived` (Abandon action; terminal for that revision)
  - All other transitions are forbidden.
- **FR-004:** Editing a Published revision MUST create a new Draft
  revision in the same chain (`revisionNumber + 1`) rather than
  mutating the Published revision. The Published revision remains
  live until the new draft is published.
- **FR-005:** Editing a Draft revision in place MUST be allowed and
  MUST NOT spawn another revision.
- **FR-006:** Layout names MUST be unique across the set of
  non-Archived chains. (Two archived chains can share a name; an
  archived chain doesn't block a new chain reusing its name.)
- **FR-007:** The system MUST expose:
  - `POST /layouts` — create a Draft (first revision of a new chain).
  - `POST /layouts/{layoutIdentifier}/draft` — branch a new Draft
    revision off the chain's current Published revision.
  - `PATCH /layouts/{layoutIdentifier}/revisions/{revisionNumber}` —
    edit a Draft revision (camera, name).
  - `POST /layouts/{layoutIdentifier}/revisions/{revisionNumber}/publish`
    — publish a Draft revision.
  - `POST /layouts/{layoutIdentifier}/revisions/{revisionNumber}/revert`
    — revert a Published revision to Draft.
  - `POST /layouts/{layoutIdentifier}/revisions/{revisionNumber}/archive`
    — archive a Draft or Published revision.
  - `GET /layouts?state=...` — list with optional state filter.
  - `GET /layouts/{layoutIdentifier}` — full chain with all revisions.
- **FR-008:** All `/layouts/*` write endpoints MUST require an
  authenticated user with the `sse.management` scope (per ADR-0023).
  `GET /layouts?state=published` MUST also require authentication
  (the kiosk-web operator signs in via OIDC per ADR-0080's "admin
  signs in" Phase-1 choice; unattended kiosk auth is deferred).
- **FR-009:** The system MUST expose a **real-time channel** over
  WebSocket (per ADR-0076 v1 transport choice). Connected kiosks
  MUST receive `layout-published`, `layout-archived`, and
  `layout-revision-replaced` events for any layout whose state
  affects the kiosk picker.
- **FR-010:** The WebSocket channel MUST authenticate the bearer
  token (Keycloak OIDC, same realm as the HTTP API) on connection.
  Connections with no token or wrong scope MUST be rejected.
- **FR-011:** When a layout is archived, kiosks currently rendering
  that layout MUST force-disconnect to the picker within **≤ 1 s** at
  p95 of the archive command completing.
- **FR-012:** Kiosks MUST reconcile on reconnect: if the
  real-time channel was disconnected and the kiosk was rendering a
  layout that is no longer Published, the kiosk MUST trigger the
  force-disconnect path within ≤ 5 s of channel reconnect.
- **FR-013:** Publishing a Layout revision MUST publish
  `LayoutRevisionPublishedV1` on the integration bus. Archiving MUST
  publish `LayoutRevisionArchivedV1`. Both are versioned per ADR-0073
  and live in `Shared.Contracts/LayoutComposition/`.
- **FR-014:** Layout aggregates MUST use optimistic concurrency
  (`version` field per ADR-0043). Concurrent revision-state changes
  return `409 Conflict` with code `LAYOUT_REVISION_STALE`.
- **FR-015:** The management-web UI MUST render a **Layouts** page
  that lists all chains with state filtering, plus a layout-editor
  dialog (name + camera picker) that handles both new-chain and
  new-revision flows. Reuses the spec 002 `<DataTable>` composite.
- **FR-016:** The new **kiosk-web** React app MUST sign the user in
  via the existing Keycloak realm, fetch `GET /layouts?state=
  published`, render a picker, and on selection render
  `<CameraViewer cameraIdentifier=... />` (the shared composite
  from spec 002 — used unchanged).
- **FR-017:** No cross-context project references between
  LayoutComposition and StreamDistribution / CameraCatalog. The
  kiosk-web app calls multiple context APIs directly; merge happens
  in the browser.
- **FR-018:** Layout state changes MUST be observable through the
  audit log (Audit & Observability context subscribes to the
  `LayoutRevision*V1` events).

### Key Entities

- **Layout (aggregate root, LayoutComposition.Domain):** One per
  logical layout chain. Owns:
  `layoutIdentifier` (Guid v7 per ADR-0090),
  `name` (NameValueObject — TBD validation, unique across non-archived
  chains),
  `currentRevisionNumber` (the highest revision in the chain),
  `revisions` (collection of Revision entities),
  `createdAt`, `createdBy`,
  `version` (concurrency token per ADR-0043).
- **Revision (entity inside Layout):** One per edit. Owns:
  `revisionIdentifier` (Guid v7, distinct from the chain's identifier),
  `revisionNumber` (1-indexed within the chain),
  `state` (LayoutRevisionState VO),
  `cameraIdentifier` (foreign reference, value-copied per ADR-0040),
  `createdAt`, `createdBy`,
  `publishedAt` (Option),
  `archivedAt` (Option).
- **LayoutRevisionState (value object):** Enum-style record. Values:
  `Draft`, `Published`, `Archived`. Transitions enforced by the
  aggregate.
- **LayoutRevisionPublishedV1 (integration event):**
  `{ LayoutIdentifier, RevisionNumber, Name, Camera, PublishedAt,
  PublishedBy }`. Lives in
  `Shared.Contracts/LayoutComposition/`.
- **LayoutRevisionArchivedV1 (integration event):**
  `{ LayoutIdentifier, RevisionNumber, ArchivedAt, ArchivedBy }`.
- **LayoutRevisionReplacedV1 (integration event):** Emitted in the
  same transaction as a "publish new revision" — carries the chain's
  previous-Published `revisionNumber` and the new Published one so
  kiosks can flip the picker entry atomically. Optional in v1; can
  be derived from a pair of Archive+Publish events.

### External Dependencies

- **Postgres** (already provisioned by Aspire — gets a new
  `layout_composition` schema and outbox per ADR-0088).
- **RabbitMQ** (already provisioned — gets per-context queues per
  ADR-0088).
- **Keycloak** (already provisioned — adds the kiosk-web client to
  the existing realm; no new realm).
- **MediaMTX / StreamDistribution.Api** (consumed by the kiosk's
  `CameraViewer`; no new code on those sides).

### Cross-context contracts

- **Inbound (subscribed):** none in v1. Future specs add
  `CameraDecommissionedV1` → cascade-archive layouts referencing the
  decommissioned camera.
- **Outbound (published):** `LayoutRevisionPublishedV1`,
  `LayoutRevisionArchivedV1`, `LayoutRevisionReplacedV1` (optional)
  — new files in `Shared.Contracts/LayoutComposition/`.
- **No project references** between LayoutComposition and
  StreamDistribution / CameraCatalog (NetArchTest enforces).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001:** Create a layout (first revision) → operator sees it in
  the kiosk picker — **≤ 5 seconds** at p95.
- **SC-002:** Archive a Published layout → connected kiosk's
  renderer transitions to the picker — **≤ 1 second** at p95
  (FR-011).
- **SC-003:** Publish a new revision of an existing chain → the
  previous Published revision is Archived and the new one is
  Published in a single transaction (no observable interleaving;
  enforced by aggregate invariant test).
- **SC-004:** Kiosk renders the cell view (camera tile) — first
  decoded frame within **≤ 3 seconds** at p95 (reuses spec 002's
  SC-002 budget; LayoutComposition does not regress it).
- **SC-005:** `GET /layouts?state=published` returns within
  **≤ 100 ms** at p95.
- **SC-006:** Integration test (Aspire fixture + the new
  layout-composition resource + kiosk-web) verifies the full publish
  → render → archive → force-disconnect loop within **≤ 60 seconds**
  of wall-clock test time.
- **SC-007:** Architecture tests still pass — no new cross-context
  references (ADR-0027) and the Domain layer of LayoutComposition
  has no infrastructure dependencies (ADR-0044).
- **SC-008:** Coverage thresholds from ADR-0065 hold for the new
  context: LayoutComposition.Domain ≥ 90 %, .Application ≥ 80 %,
  Shared.Contracts ≥ 90 %.
- **SC-009:** No regressions in existing budgets: `POST /cameras`
  p95 ≤ 200 ms; click-to-first-frame on the camera viewer p95
  ≤ 3 s (the kiosk's cell view reuses the same composite).

## Assumptions

- **Walking-skeleton scope: 1 cell per layout.** The Layout
  aggregate persists a single `cameraIdentifier`; future specs
  generalize this to a cell collection. The spec commits to "the
  schema admits future grid additions without a breaking migration"
  (revisions are isolated → expanding a revision's payload is
  additive).
- **Admin signs into kiosk-web (no operator role yet).** Per
  Phase-1 Q&A, kiosk-web uses the same Keycloak OIDC flow as
  management-web. The unattended-kiosk credential and the
  operator-role distinction are explicitly deferred.
- **Single fab, single Keycloak realm.** Same assumption as specs
  001-002.
- **WebSocket transport is acceptable infra cost.** Phase-1 Q&A
  rejected polling and SSE in favour of ADR-0076 v1's WebSocket. The
  spec commits to this; plan.md picks the .NET implementation
  (SignalR vs raw `Microsoft.AspNetCore.WebSockets`).
- **Browser- and kiosk-side WebSocket support is assumed.** Same
  Chromium-based kiosk image story as spec 002.
- **Layout-name validation is light.** Non-empty, ≤ 80 chars, no
  newlines. Stricter rules (slugged URL, locale-aware sorting) are
  deferred — the picker shows names verbatim.

## Resolved Clarifications (Phase 1)

Six clarifications resolved during the Phase 1 Q&A round:

| # | Marker | Resolution |
|---|---|---|
| 1 | Cell-shape minimality | **Strictly 1 cell per layout.** Layout = `{ name, cameraIdentifier }`. No grid math, no rows/cols, no cell-as-entity in v1. Future specs add the grid additively. |
| 2 | Publish lifecycle | **Draft ↔ Published, plus Archived (terminal per revision).** Drafts can be edited freely; Published can revert to Draft; Archived is terminal for that revision. Logical chain is never deleted. |
| 3 | Kiosk routing | **Operator-picks-from-list.** Kiosk-web shows a picker of Published layouts; the operator taps one to open the cell view. No per-kiosk assignment (Kiosk-as-an-entity) and no URL-driven routing. |
| 4 | Kiosk auth | **Admin signs in at the kiosk via OIDC.** Reuses the existing Keycloak realm; kiosk-web is a second OIDC client. Unattended-device auth (ADR-0080's "custom kiosk flow") is explicitly deferred. |
| 5 | Edit-after-publish | **Creates a new Draft revision in the same chain.** Published revisions are immutable. Publishing the new revision auto-archives the previous Published revision in the same transaction. |
| 6 | Live revocation | **Force-disconnect immediately on archive (no warning), via WebSocket push** (ADR-0076 v1 transport). Kiosks reconcile on reconnect to recover from missed events. |
| 7 | Revision model | **Revisions chained to a logical Layout.** A logical Layout (aggregate identity) has 1..N revisions (sub-entities). At most one revision is Published at any time. Audit trail preserved across edits. |
| 8 | Visibility | **Admin sees all states; kiosk picker shows Published only.** No tenancy/role-based filtering in v1. |

## Open clarifications

None — Phase 1 Q&A closed every marker. Ready for Phase 2 (Plan).
