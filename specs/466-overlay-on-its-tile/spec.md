# Feature Specification: An Overlay Seen on Its Tile

**Feature Branch**: `2521-overlay-on-its-tile`

**Created**: 2026-10-29

**Status**: Draft

**Input**: User description: "An operator-facing WYSIWYG view for overlays per layout tile: the management console shows the whole tile as it appears on the wall (camera backdrop at the tile's real aspect ratio, the tile's overlay with resolved placeholder text, neighbouring elements in context) and edits the overlay in place. Each tile keeps at most one overlay (no domain change to Tile, no ADR). Overlays stay fab-neutral templates (ADR-0115). Motivation: the current overlay editor shows one overlay revision in isolation on a 16:9 canvas with a frozen frame or checkerboard, so the operator cannot see how it looks on a real tile in a layout."

## Clarifications

### Session 2026-10-29

- Q: Shared overlay edited from one tile — can the operator detach it? → A: No. Editing always changes the shared overlay (a new draft of its chain), with the tile-count notice; no copy or detach operation in this feature.
- Q: Which layout state does the tile view show? → A: The layout designer's current, possibly unsaved state; the view opens from the designer, not from a saved layout alone.
- Q: Who may use the tile view? → A: Anyone who may edit a layout may open it; overlay-editing controls appear only with the existing overlay write right, otherwise read-only with a notice. No new permission.
- Q: How exactly must the view match the wall? → A: On the overlay layer: geometry within 1 %, identical text wrap and overflow, and colour and opacity equal to a wall screenshot at the same tile shape.
- Q: Must in-view editing work without a mouse? → A: No. Viewing is fully keyboard- and screen-reader-operable; editing in the view may need a pointer, and the keyboard route for editing remains the existing editor, reachable from the view.

## User Scenarios & Testing *(mandatory)*

<!--
  Today an operator designs an overlay in isolation: one revision, a fixed 16:9
  canvas, a checkerboard or a single frozen frame behind it. Which camera it
  will sit over, how large the tile will be on the wall, and what the
  placeholders will say there are all invisible while designing. The overlay
  is only ever seen in context after it is published and a wall shows it.
-->

### User Story 1 - See an overlay on the tile it will occupy (Priority: P1)

An operator opens a layout's tile and sees that tile the way the wall shows
it: the tile's camera behind, the tile's overlay drawn over it with each
placeholder replaced by the value it currently resolves to, at the size and
shape the tile has on the wall. They can read at a glance whether the label is
legible, whether it covers something it should not, and whether it still fits.

**Why this priority**: This is the whole gap. Everything else builds on being
able to see the overlay where it will live. On its own it already lets an
operator catch a label that is unreadable or misplaced before it reaches a
wall.

**Independent Test**: Open a layout that has a tile with a camera and a
published overlay containing a placeholder. The tile is shown with that
camera's picture behind it and the overlay over it, the placeholder showing
its current value.

**Acceptance Scenarios**:

1. **Given** a layout tile with a camera and a published overlay, **When** the
   operator opens the tile view, **Then** the camera's picture and the overlay
   are shown together in a frame of the tile's shape, each overlay element at
   the same relative position and size it has on the wall.
2. **Given** the overlay's label references a variable, **When** the tile view
   opens, **Then** the label shows the variable's current value in the
   operator's fab, not the raw placeholder text.
3. **Given** a tile with no overlay assigned, **When** the operator opens the
   tile view, **Then** the camera's picture is shown with no overlay and the
   operator is offered to assign or create one.
4. **Given** the camera's picture cannot be obtained, **When** the tile view
   opens, **Then** a neutral backdrop of the tile's shape is shown with a
   notice saying why, and the overlay is still shown and still editable.

---

### User Story 2 - Edit the overlay in place on that tile (Priority: P2)

From the tile view the operator changes the overlay directly: moves and
resizes elements, changes text and colour, adds or removes elements. What they
see while editing is what the wall will show, so they never have to publish to
find out.

**Why this priority**: Seeing without being able to change would send the
operator back to the isolated editor to act. It is the second half of the
request, and it can ship after viewing because the existing editor still works.

**Independent Test**: In the tile view, drag an element and change its text;
the frame updates as the edit is made and the saved draft, reopened in the
tile view, shows the same result.

**Acceptance Scenarios**:

1. **Given** a tile with an overlay open in the tile view, **When** the
   operator moves an element, **Then** it moves in the frame as they drag and
   the draft keeps the new position as a fraction of the picture, unchanged in
   kind from today.
2. **Given** the operator edits an element's text to add a placeholder,
   **When** the edit settles, **Then** the frame shows that placeholder's
   resolved value.
3. **Given** the overlay is a published revision, **When** the operator starts
   editing, **Then** the edit goes into a new draft the way it does in the
   existing editor and the published revision keeps running on the wall until
   the draft is published.
4. **Given** the operator's edit would make the label unreadable against the
   picture, **When** they make it, **Then** the same legibility warning the
   existing editor gives is shown here.

---

### User Story 3 - Judge the overlay against its neighbours (Priority: P3)

The tile view shows the tile's neighbours in the layout, so the operator can
judge the overlay in the context of the whole wall: the tiles around it, with
their own overlays, in their positions.

**Why this priority**: It answers "does it look right with the rest of the
wall", but one tile viewed correctly already removes most of the guessing, so
this is valuable and not essential.

**Independent Test**: Open a tile in a layout of several tiles; the other
tiles appear around it, each with its camera and overlay, and the tile being
edited is clearly marked.

**Acceptance Scenarios**:

1. **Given** a layout of several tiles, **When** the operator opens one, **Then**
   the layout's other tiles are shown in their grid positions around it, not
   editable, with the open tile marked.
2. **Given** a tile spanning several grid cells, **When** it is shown, **Then**
   it occupies those cells in the context view as it does on the wall.

---

### Edge Cases

- A tile whose overlay was archived after it was assigned: shown the way the
  wall treats it (no overlay) with a notice, not an error.
- A tile whose overlay has a draft but no published revision: the view shows the
  draft with a notice that the wall shows nothing yet.
- A placeholder naming a variable that does not exist or has no value yet: shown
  the way the wall shows it, with the existing placeholder advisory.
- The camera is offline or has no live picture: the backdrop falls back as in
  scenario 4 of User Story 1; the overlay is not hidden by it.
- Very long resolved text: shown at the size and clipping the wall uses, so the
  operator sees the overflow rather than a flattering preview.
- The same overlay is used on several tiles: editing it from one tile affects
  all of them; the operator is told how many tiles use it before saving. There
  is no copy or detach operation in this feature; making a tile's overlay its
  own means creating a new overlay in the existing way.
- Two operators edit the same overlay: the existing stale-revision conflict
  behaviour applies unchanged.
- The designer holds unsaved changes while the operator edits the overlay: the
  overlay's own draft is saved on its own terms, independent of the layout; the
  designer's unsaved state is neither lost nor saved by it.
- A layout whose tile shape differs from the 16:9 the isolated editor assumes
  (spanning tiles, non-square grids): the frame takes the tile's shape.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The operator MUST be able to open any populated tile from the
  layout designer in a tile view that shows the tile's camera picture and its
  overlay together. The view reflects the designer's current state, including
  camera, overlay and span choices not yet saved.
- **FR-002**: The tile view MUST show each overlay element at the same relative
  position and size, with the same colour and opacity, and with text wrapped and
  clipped at the same places, that the wall shows it for the same tile shape.
- **FR-003**: The tile view MUST show each placeholder's current resolved value
  in the operator's fab, and MUST NOT show the raw placeholder text where a
  value exists.
- **FR-004**: The frame MUST take the shape of the tile as the wall shows it,
  including a tile that spans several cells. A tile's shape on the wall follows
  the wall display's aspect ratio, which a layout does not record, so the
  operator MUST be able to choose the display shape in the tile view (for
  example 16:9, 16:10 or portrait); the frame and the neighbours follow that
  choice, and a default is used until they choose.
- **FR-005**: The operator MUST be able to edit the tile's overlay in the tile
  view: move, resize, change text and colour, add and remove elements, with
  every change reflected in the frame as it is made.
- **FR-006**: Editing a published overlay revision MUST create or reuse a draft
  and MUST leave the published revision running on the wall until the draft is
  published, exactly as the existing editor does.
- **FR-007**: A tile MUST still carry at most one overlay, and the overlay
  remains a fab-neutral template; assigning or replacing a tile's overlay from
  the tile view changes the designer's unsaved state in the same way the
  layout designer does, and takes effect on the wall only when the layout is
  saved and published.
- **FR-008**: If the camera picture cannot be obtained, the tile view MUST show
  a neutral backdrop of the tile's shape with a notice, and MUST keep the
  overlay visible and editable.
- **FR-009**: The tile view MUST show the same legibility and placeholder
  advisories the existing overlay editor shows.
- **FR-010**: Before saving an edit to an overlay used by more than one tile,
  the operator MUST be told how many tiles use it, counted across the layouts
  the operator can see and stated as "at least" that many, since layouts the
  operator cannot see are not counted.
- **FR-011**: The tile view MUST be able to show the layout's other tiles around
  the open tile, read-only, with the open tile marked. Each neighbour shows a single still picture of its
  camera with its own overlay over it, not a live stream, so the context view
  adds no streaming load to the operator console.
- **FR-012**: Whoever may edit a layout MUST be able to open the tile view. The
  controls that edit the overlay MUST be offered only to an operator who may
  already edit overlays; without that right the view is read-only and says so.
  The view MUST NOT expose cameras, overlays or fabs the operator cannot
  otherwise see, and introduces no new permission.
- **FR-013**: The existing isolated overlay editor MUST keep working unchanged;
  the tile view adds a way in and replaces nothing.
- **FR-014**: The tile view MUST NOT change what the wall shows or how fast it
  shows it; it adds no work to the event-to-overlay path.
- **FR-015**: Everything the tile view shows (opening a tile, reading the frame,
  its backdrop and resolved values, its notices) MUST be operable and
  announced without a mouse. Editing in the tile view MAY require a pointer;
  the keyboard route for editing stays the existing isolated editor, and the
  tile view MUST offer a way into it for the tile's overlay.

### Key Entities

- **Layout tile**: One cell (or span of cells) of a layout, holding a camera
  and at most one overlay. The unit this view is about.
- **Overlay revision**: The overlay's current published or draft state; its
  elements are positioned as fractions of the picture, which is why one
  revision can be shown on tiles of different shape.
- **Resolved placeholder value**: What a variable currently reads in the
  operator's fab, shown in place of the placeholder.
- **Tile frame**: The preview area shaped like the tile; carries the backdrop
  and the overlay and nothing else.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator can see any tile's camera and overlay together, from
  the layout, in no more than two steps (select the layout, select the tile).
- **SC-002**: What the tile view shows for an overlay matches the wall, compared
  on the overlay layer alone (the camera picture is excluded, since a still
  differs from live video): for the same layout, overlay, resolved values and
  tile shape, (a) element positions and sizes differ by no more than one percent
  of the tile's width or height, (b) text wraps and overflows at the same
  places, and (c) each element's colour and opacity are the same as in a wall
  screenshot taken at that tile shape.
- **SC-003**: An operator can change an overlay on its tile and see the result
  without leaving the view or publishing, in a single sitting.
- **SC-004**: After an edit settles, the frame reflects it within one second.
- **SC-005**: An operator who previously had to publish to judge an overlay no
  longer has to: in a trial with a representative set of overlays, none needs a
  publish solely to see how it looks on its tile.
- **SC-006**: No measured change in the event-to-overlay latency on the wall
  with the feature in place.

## Assumptions

- A tile keeps one overlay; multiple overlays per tile are out of scope and
  would be a separate decision.
- Overlays stay fab-neutral templates (ADR-0115); placeholders resolve in the
  operator's fab, as they do for a viewer.
- The existing overlay and layout permissions govern the tile view; it adds
  none of its own.
- The existing draft/publish chain, optimistic concurrency and placeholder
  resolution behaviour are reused as they are.
- The camera picture comes from the same sources the existing editor already
  uses for its backdrop.
- A chat-driven way to edit overlays (in the manner of Canva) is planned for
  later and is out of scope here. It is recorded so the planning phase keeps
  the tile view's edits as discrete, nameable changes to an overlay's elements
  rather than as gestures bound to the pointer; no chat behaviour is specified
  or built by this feature.
- Inter-display wall alignment and presentation-buffer behaviour are out of
  scope; the view shows geometry, not timing.
