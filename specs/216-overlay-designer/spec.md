# Feature Specification: Overlay Designer

**Feature Branch:** `216-overlay-designer`

**Created:** 2026-09-27

**Status:** Draft (Phase 1 — Specify, Clarifying)

**Input:** Third feature slice of Smart Sentinel Eye — the first slice
through a new **Overlay Authoring** bounded context. Delivers the
authoring surface (editor, preview, draft/publish lifecycle,
persistence) for overlay templates that later features will layer over
camera streams. Selected as the next feature because every downstream
operator-facing surface (Layout Composition, Automation) depends on
overlays being *composable* and *versioned*, and because overlay
authoring has zero coupling to the streaming hot path — it can be
delivered in parallel with Stream Distribution work without contention.

## Clarifications

### Session 2026-11-09

- Q: Where does the editor's preview placeholder image come from? → A: A single hard-coded placeholder image bundled with the frontend (SMPTE-bars-style neutral scene). No admin asset-management UI in v1.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Compose and preview a draft overlay template (Priority: P1)

A fab admin opens the management app, creates a new overlay template,
adds text and shape elements to a canvas, adjusts their styles, and
sees the result rendered live in a preview pane inside the same
screen. The draft is auto-saved as the admin works, so a browser crash
or accidental navigation does not lose work.

**Why this priority:** Smallest possible vertical slice through the new
**Overlay Authoring** context. Delivers immediate user value (an admin
can produce a visible artefact) while exercising every load-bearing
piece: a new bounded context, its persistence, optimistic concurrency,
management-web UI wiring, and the versioned-snapshot data model that
US2's publish flow will build on. It intentionally excludes any live
data binding — that is US of a later feature (System Variables) — so
the slice stays narrow.

**Independent Test:**

1. Start the system locally via `aspire run`.
2. Sign in to `management-web` as an admin operator.
3. Create a new overlay template, name it `smoke-test-alarm`.
4. Add a Rectangle element covering the top strip of the canvas.
   Fill it red at 60% opacity, no border.
5. Add a Text element inside the Rectangle. Content: `ALARM ACTIVE`.
   Font Inter, weight 700, size 48, colour white.
6. Add a Line element across the bottom of the canvas, stroke width 4,
   dashed, colour amber.
7. Confirm each element appears in the preview pane as it is added and
   updates within one second of every style change.
8. Reload the browser. The draft is still there with all four elements
   and their styles intact.
9. Confirm a second admin cannot save on top of the first admin's
   draft without seeing a conflict warning (optimistic concurrency).

**Acceptance Scenarios:**

1. **Given** an admin is signed in to the management app,
   **When** they create a new overlay template and add a Text element,
   **Then** the element appears in the preview pane within one second
   of its creation.
2. **Given** a draft contains at least one element,
   **When** the admin changes any style attribute of that element
   (colour, position, font size, border width, opacity, rotation),
   **Then** the preview reflects the change within one second and the
   draft's `updatedAt` timestamp advances.
3. **Given** a draft exists,
   **When** the admin closes their browser tab and returns to the
   template later,
   **Then** all elements and their styles are recovered exactly as
   they were before the tab was closed.
4. **Given** two admins have the same draft loaded in different
   browsers,
   **When** the second admin attempts to save after the first admin
   has saved,
   **Then** the second admin sees a "this draft was changed by
   someone else" warning and can choose to reload the latest state or
   overwrite.
5. **Given** an operator (not an admin) is signed in,
   **When** they attempt to open the overlay editor,
   **Then** the editor is not reachable and they see a permission
   error.

### User Story 2 — Publish a draft and retain history (Priority: P1)

Once a draft is ready, the admin clicks **Publish**. The system
promotes the draft's current state to a Published version with a
monotonic version number. Older Published versions of the same
template remain readable — they are the historical record that other
bounded contexts will reference. The active Draft continues to exist
after publish so the admin can iterate towards the next version
without losing what is currently published.

**Why this priority:** Without Publish, no downstream feature can
reference an overlay by name and version. Publish is the artefact the
system's promise of stability rests on: a Published version, once
issued, must be usable by any operator UI, any automation, and any
future replay without the risk of the admin editing it out from under
them.

**Independent Test:**

1. Continue from US1 with a draft `smoke-test-alarm` in place.
2. Click **Publish**. Enter a short release note ("v1: initial
   alarm layout").
3. The template list shows `smoke-test-alarm` with Published version
   1 and note "v1: initial alarm layout".
4. The active draft remains editable. Edit the Text element's
   contents to `ALARM SILENCED` and click **Publish** again with note
   "v2: silenced state".
5. The template list now shows Published version 2 as the latest,
   with version 1 still readable via a version dropdown.
6. Read version 1 through the same UI: the elements and styles match
   exactly what was captured on the first publish (i.e. **version 1
   was not overwritten**).
7. Confirm the system emits one — and only one — integration event
   per publish, carrying the template identifier and the new version
   number.

**Acceptance Scenarios:**

1. **Given** a draft with at least one element exists,
   **When** the admin publishes,
   **Then** a new Published version is created with a version number
   equal to the previous latest published version plus one (starting
   at 1).
2. **Given** a template has previously-published versions,
   **When** the admin publishes again,
   **Then** every prior Published version remains byte-identical to
   what it was at its own publish time (immutability).
3. **Given** a draft has no elements,
   **When** the admin attempts to publish,
   **Then** publish is refused with a clear message
   ("cannot publish an empty template").
4. **Given** a template is published,
   **When** other parts of the system query the template by identifier,
   **Then** they receive the latest Published version's contents
   (drafts are not visible to consumers outside Overlay Authoring).
5. **Given** publish succeeds,
   **When** subscribers are listening for overlay integration events,
   **Then** exactly one event is emitted per publish containing the
   template identifier and the new version number.

### User Story 3 — List templates with lifecycle state (Priority: P2)

An admin opening the Overlay Designer sees a list of every template,
whether it has a draft, whether it has been published, its latest
published version number, and when it was last edited. The admin can
filter by lifecycle state (draft-only, published, both) and by name.

**Why this priority:** Without a list view, admins would have to
remember template identifiers. As the count grows past a handful, the
list becomes the primary navigation surface. Delivered separately
from US1/US2 so the first slice can ship without full-catalogue UX.

**Independent Test:**

1. Create three templates via US1: `alarm`, `line-status`,
   `shift-info`.
2. Publish `alarm` (v1). Leave `line-status` as draft-only. Publish
   `shift-info` (v1) then edit its draft (now has both a Published v1
   and a newer draft).
3. Open the Overlay Designer list view.
4. Verify each row shows the template name, lifecycle state,
   published version if any, and last-edited timestamp.
5. Filter to "published only": `alarm` and `shift-info` appear;
   `line-status` does not.
6. Filter to "draft only": `line-status` and `shift-info` appear
   (both have a draft newer than their latest publish or no publish).
7. Filter by name substring "info": only `shift-info` remains.

**Acceptance Scenarios:**

1. **Given** at least one template exists,
   **When** the admin opens the list view,
   **Then** every template is shown with its name, lifecycle badges
   (Draft / Published / Both), latest Published version number if any,
   and last-edited timestamp.
2. **Given** the admin selects a lifecycle filter,
   **When** the list re-renders,
   **Then** only templates matching the filter are shown.
3. **Given** the admin enters a name substring,
   **When** the list re-renders,
   **Then** only templates whose names contain that substring are
   shown (case-insensitive).
4. **Given** an operator (not an admin) opens the list,
   **When** they view it,
   **Then** they see the same rows as an admin **but** they cannot
   open the editor for any row — the row's edit affordance is hidden
   or disabled.

### User Story 4 — Preview against a sample-data pane (Priority: P2)

The editor includes a side pane where the admin can type a JSON
document representing what a future dynamic overlay might receive at
runtime (system-variable values, event fields). In v1 the elements'
style and content are all static, so the pane has no visible effect
on what renders — but the pane exists and its contents are persisted
with the draft. This means later features that add expression
bindings can start from a working editor rather than needing to
re-architect the UI.

**Why this priority:** Explicit forward-compatibility with a known,
scheduled feature (System Variables). Cheap to include now; expensive
to retrofit once the editor's layout is set.

**Independent Test:**

1. Open a draft in the editor.
2. Enter a JSON document in the Sample Data pane:
   `{ "line": "L3", "pressure": 42.7, "alarm": true }`.
3. Save. Reload the draft. The pane still contains the same JSON.
4. Confirm none of the currently-visible preview elements changed as
   a result of the sample data change (v1 has no bindings, so this is
   the expected outcome).

**Acceptance Scenarios:**

1. **Given** a draft is open in the editor,
   **When** the admin edits the Sample Data pane and saves,
   **Then** the sample data is persisted with the draft and survives
   reloads.
2. **Given** the Sample Data pane contains valid JSON,
   **When** the admin types invalid JSON,
   **Then** the editor shows a validation error inline in the pane
   and disables save until the JSON parses.
3. **Given** an admin publishes a template,
   **When** they publish,
   **Then** the sample data is NOT included in the Published
   snapshot — it is authoring-side scratch state, not part of the
   consumable template.

### User Story 5 — Duplicate an existing template (Priority: P3)

An admin can duplicate any existing template (draft or published) as
a new draft under a new name. The elements, styles and sample data
are cloned; the version history of the original template is not.

**Why this priority:** A convenience feature for producing variations
(different colour schemes for different fabs, different lines).
Skippable in the first release without blocking any other user story.

**Independent Test:**

1. Publish a template `alarm-red` (v1).
2. Right-click / three-dot-menu → Duplicate.
3. Enter new name `alarm-amber`.
4. `alarm-amber` opens in the editor as a fresh draft with all of
   `alarm-red`'s elements, cloned.
5. Editing `alarm-amber` does not touch `alarm-red`.
6. `alarm-amber` has no Published versions of its own until the
   admin publishes it.

**Acceptance Scenarios:**

1. **Given** a source template exists,
   **When** the admin duplicates it under a new name,
   **Then** a new template is created with a fresh identifier, a new
   Draft copied from the source's *current draft or latest Published
   version* (whichever is the most recent authored state), and no
   Published versions.
2. **Given** an admin attempts to duplicate under an already-taken
   name,
   **When** they submit,
   **Then** the duplicate is refused with a clear message.

### Edge Cases

- **Very large templates.** How does the editor behave if a template
  has 500 elements? (Assumption: 100 elements is the practical upper
  bound for an overlay; 500 is stressed only in tests. The editor's
  scroll and selection performance must remain acceptable at 100.)
- **Very long text elements.** Text content longer than the fabric
  the element occupies should clip at the element boundary; overflow
  behaviour is clip, not wrap, in v1.
- **Rotation and hit-testing.** When an element is rotated, its
  selection handles rotate with it. Clicking on the element's rotated
  bounding box selects it; clicking outside it does not.
- **Publishing while another publish is in flight.** The second
  publish waits for the first to complete or fails cleanly; the
  system does not create two versions with the same number.
- **Deleting a template.** In v1 deletion is not supported. Templates
  can only be superseded by new versions or renamed. Rationale:
  audit-safety — a template referenced by any historical operator
  action must remain readable.
- **Renaming a template.** Renaming is allowed and does not change
  the template's identifier. The name is a display attribute; the
  identifier is what integrations reference.
- **Empty Published version content.** Publish refuses to promote a
  draft with zero elements (see US2 acceptance 3).
- **Concurrent draft edits.** Two admins on the same draft see the
  losing edit rejected on save (optimistic concurrency, see US1
  acceptance 4). No conflict merger UI in v1.

## Requirements *(mandatory)*

### Functional Requirements

**Composition & editing**

- **FR-001**: The system MUST allow an admin to create a new
  overlay template with a unique display name and a fresh identifier.
- **FR-002**: The system MUST support the following element types in
  a template: Text, TextWithBorder, Rectangle, Circle, and Line.
- **FR-003**: The system MUST allow the admin to place, resize, and
  rotate any element on a fixed-aspect-ratio canvas.
- **FR-004**: The system MUST support these style attributes on
  every applicable element: position, size (width/height for
  rectangular elements; endpoints for Line), z-order, opacity,
  rotation.
- **FR-005**: The system MUST support text-specific attributes on
  Text and TextWithBorder: text content, font family (from an admin-
  curated allow-list), font size, font weight, colour.
- **FR-006**: The system MUST support border attributes on all
  border-capable elements: border colour, border width, border style
  (solid / dashed / dotted).
- **FR-007**: The system MUST support fill attributes on all fill-
  capable elements (Rectangle, Circle, TextWithBorder background):
  fill colour, fill opacity.
- **FR-008**: The system MUST auto-save the draft as the admin edits,
  such that a browser crash or navigation away does not lose more
  than the most recent unsaved change (target: at most 5 seconds of
  edits lost in the worst case).

**Preview**

- **FR-009**: The system MUST render a live preview of the current
  draft inside the editor screen. Style and content changes MUST be
  visible in the preview within one second of the change.
- **FR-010**: The system MUST allow the admin to enter arbitrary
  valid JSON into a Sample Data pane on the editor and persist it
  with the draft.
- **FR-011**: The system MUST show a validation error inline when
  the Sample Data pane contains invalid JSON and MUST prevent saving
  the draft until the JSON parses.
- **FR-012**: The system MUST NOT include Sample Data content in
  Published snapshots.
- **FR-013**: The system MUST render the preview against a single
  hard-coded placeholder image bundled with the frontend (a
  SMPTE-bars-style neutral scene). No admin asset-management UI
  (upload, per-template selection) in v1.

**Lifecycle**

- **FR-014**: The system MUST persist every template with a Draft
  containing the current authoring state, and zero or more Published
  versions.
- **FR-015**: The system MUST issue Published versions with a
  monotonic per-template version number starting at 1.
- **FR-016**: Once a version has been Published, the system MUST NOT
  permit further modification of its content.
- **FR-017**: The system MUST refuse to publish a Draft that has
  zero elements.
- **FR-018**: The system MUST retain every Published version;
  callable retention or pruning is NOT supported in v1 (see
  Clarifications).
- **FR-019**: The system MUST allow the admin to enter a short
  release note (≤ 200 characters) at publish time. The note is stored
  with the Published version.

**Listing**

- **FR-020**: The system MUST provide a list view showing every
  template with its name, lifecycle state (Draft-only / Published /
  Both), latest Published version number (if any), and last-edited
  timestamp.
- **FR-021**: The list MUST support filtering by lifecycle state and
  by case-insensitive name substring.
- **FR-022**: Operators and viewers MUST see the same list rows as
  admins but MUST NOT see or reach any edit affordances.

**Duplication**

- **FR-023**: The system MUST support duplicating an existing
  template under a new name. The duplicate MUST be a fresh template
  (new identifier, no Published versions), initialised with a Draft
  copied from the source's most recent authored state.

**Concurrency**

- **FR-024**: The system MUST enforce optimistic concurrency on
  Draft saves: a save that is based on a stale version of the Draft
  MUST be rejected with a conflict indication, and the admin MUST be
  able to choose between reloading the current state or overwriting.

**Authorisation**

- **FR-025**: Only principals with the admin scope MUST be able to
  create, edit, publish, or duplicate templates.
- **FR-026**: Operators and viewers MUST be able to list templates
  and read Published versions but MUST NOT be able to reach any edit,
  publish, or duplicate action.

**Integration signalling**

- **FR-027**: Exactly one integration event MUST be emitted per
  successful publish, carrying the template identifier and the new
  version number.
- **FR-028**: No integration events MUST be emitted for Draft edits
  or auto-saves. Drafts are private to the Overlay Authoring context.

**Forward-compatibility**

- **FR-029**: The persisted schema of an element's attributes MUST
  be designed so that a future release can add an
  expression-typed alternative to any static value without changing
  the shape of existing Published versions (i.e. old Published
  versions remain readable). This is an authoring-side design
  constraint; no user-visible behaviour in v1.

### Key Entities

- **OverlayTemplate**: An authored artefact identified by a stable
  identifier. Carries a display name, a Draft, and an ordered list
  of Published versions. The unit of publish and the unit of
  reference for downstream contexts.
- **DraftVersion**: The current, editable authoring state of an
  OverlayTemplate. Contains an ordered list of OverlayElements, a
  StyleSpec per element, a Sample Data JSON blob, and an
  optimistic-concurrency stamp.
- **PublishedVersion**: An immutable snapshot of an OverlayTemplate
  at publish time. Contains an ordered list of OverlayElements, a
  StyleSpec per element, a monotonic version number, a release note,
  and a publisher identifier / timestamp.
- **OverlayElement**: One of Text, TextWithBorder, Rectangle,
  Circle, or Line. Carries geometry (position, size or endpoints,
  z-order, rotation, opacity) and a StyleSpec.
- **StyleSpec**: The style attributes applicable to the element's
  type. Text attributes for Text/TextWithBorder; border attributes
  for border-capable elements; fill attributes for fill-capable
  elements.
- **FontAllowList**: An admin-curated list of font families
  available in the editor. Its contents are treated as configuration,
  not user data.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An admin can go from a blank canvas to a Published
  version 1 of a first template in under 5 minutes.
- **SC-002**: Preview reflects any style or content change within
  1 second at least 95% of the time on standard admin workstation
  hardware.
- **SC-003**: A browser crash mid-edit loses at most 5 seconds of
  the admin's most recent edits and never loses previously-saved
  state.
- **SC-004**: Once published, a Published version's rendered
  appearance is byte-identical across at least three subsequent
  reads over any duration up to 12 months.
- **SC-005**: Zero cases in the acceptance test suite where an
  operator or viewer principal is able to reach any edit, publish,
  or duplicate action.
- **SC-006**: Publishing 100 templates in sequence produces exactly
  100 integration events, in order, one per publish, with matching
  identifiers and version numbers.
- **SC-007**: A template with 100 elements remains fluid in the
  editor: scrolling, selection, and property edits complete within
  100 ms of user input at least 95% of the time.

## Assumptions

- The management app already provides an admin-only navigation
  entry-point which the Overlay Designer will hang off. Adding the
  route is in scope; the app shell already exists (feature 001).
- Authentication and role scopes are already wired through
  Keycloak for the management app. The Overlay Designer reuses this
  wiring; no auth stack changes are in scope.
- A fixed template canvas aspect ratio is acceptable in v1 (16:9).
  Multi-aspect canvases (portrait, arbitrary cell aspect) come with
  Layout Composition.
- Operators / viewers are not in the primary personas for v1 beyond
  the read-only list view; the Overlay Designer is primarily an
  admin tool.
- Font files are shipped with the frontend or served from a static
  origin the management app already trusts. No dynamic font upload
  in v1.
- Rotation is expressed in degrees, `0` at natural orientation,
  clockwise positive.

## Dependencies

- **Feature 001 (register-camera)**: reuses the same Postgres
  cluster, the same Aspire composition, the same Keycloak realm,
  and the same management-web app shell.
- **Feature 002 (watch-camera-live)**: no direct runtime dependency.
  This feature is intentionally decoupled from the streaming hot
  path.
- **Constitution §IV (latency budget)**: this feature is off the
  event-to-overlay path. Any authoring-time delay affects only the
  admin's editor experience and is bounded by SC-002/SC-007.
- **ADR-0043 (optimistic concurrency)**: applied to Draft saves.
- **ADR-0091 (naming, no shortcuts)**: applied to every ubiquitous
  language addition — `OverlayTemplate`, `OverlayElement`,
  `DraftVersion`, `PublishedVersion`, `StyleSpec`, `FontAllowList`,
  `OverlayTemplateIdentifier`.
- **A future feature (System Variables, spec ≥ next)**: is the
  consumer of the forward-compatibility surface defined in FR-029.

## Explicit Non-Goals

- **No live camera stream in the preview.** The preview background
  is a placeholder image. Rendering an overlay over an actual
  WebRTC stream is a separate feature.
- **No cell / layout binding.** How templates get attached to layout
  cells is the concern of Layout Composition, not this feature.
- **No expression / variable / event binding.** Every value in v1
  is static. Bindings arrive with System Variables.
- **No import / export as JSON.** The template schema is internal.
  Interchange formats are deferred.
- **No multi-user co-editing.** Single-writer per draft; conflicts
  resolve by optimistic concurrency.
- **No template deletion.** Templates supersede by new versions or
  are renamed; audit-safety takes precedence over cleanup in v1.
- **No stream recording integration.** Overlay-on-recording is out
  of scope because recording itself is out of scope for the platform.

## Open Clarifications

The following ambiguities remain for the current `/speckit-clarify`
session to resolve:

- **[NEEDS CLARIFICATION: FontAllowList contents]** — Which font
  families should ship in v1's admin-curated allow-list? Candidates:
  Inter, Roboto, IBM Plex Sans (Latin cover), IBM Plex Mono, and
  a serif fallback. Constraint: every font ships bundled with the
  frontend or is served from the origin the management app already
  trusts. No dynamic upload in v1.

- **[NEEDS CLARIFICATION: Published-version retention]** — Are every
  Published version retained forever (unbounded), or is there a
  retention cap (e.g. keep latest N, or archive beyond M months)?
  v1 leans "retain forever" for audit safety, but this decision has
  a long tail on storage growth if templates are frequently
  republished.
