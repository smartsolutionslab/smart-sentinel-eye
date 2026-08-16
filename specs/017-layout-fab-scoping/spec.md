# Feature Specification: Fab-scope layout composition

**Feature Branch**: `017-layout-fab-scoping`

**Created**: 2026-08-16

**Status**: Draft

**Input**: #1397 — the last four hub frames still broadcast to every fab. Follows specs 013 (rules), 014 (variables), 015 (cameras) and 016 (streams).

## Why this exists

Every kiosk screen in the product connects to one hub. Six kinds of frame go
out over it. Four of them still go to **every** screen in **every** plant.

So a Dresden kiosk is told that a Munich layout was published, that a Munich
layout was archived, and — because the overlay frames carry the overlay's
**text, name and geometry** in the payload — what a Munich overlay actually
says.

LayoutComposition is the **last context in the product with no fab concept**.
`grep -ri fabidentifier src/LayoutComposition/` returns nothing.

## What makes this different from specs 013–016

Those four each gave one thing a fab. This one has **two halves that are not
the same problem**, and conflating them is the trap.

**Half A — the layout frames.** A layout is authored by an operator, so it can
carry a fab exactly as a rule, a variable, a camera and a stream now do. This
is a routine application of ADR-0114, and unlike spec 016 the write path is
real: there are **six** operator-driven writes here, so `?fabId=`, inference
for a single-fab operator, and an ambiguity refusal all apply. Spec 016 has
none of those, and copying from it would be as wrong as copying the decision
table into spec 016 was.

**Half B — the overlay frames.** An overlay has **no fab, by design**.
ADR-0115 accepted that an overlay is a *fab-neutral template* and that a
placeholder resolves in the fab of whoever is viewing it. So these two frames
cannot be scoped by "the overlay's fab" — there is none, and inventing one
would reverse an accepted decision.

They are scoped by **who references the overlay** instead: the fabs that have a
published layout whose tiles carry that overlay. That is a question
LayoutComposition can already answer about its own data, because a tile
already carries an optional overlay reference. It needs no fab on the overlay,
no call to another context, and no separately maintained index.

**Half B is the worse leak.** A layout frame tells another plant that something
exists. An overlay frame tells them what it *says*.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - An operator works only in their own plant's layouts (Priority: P1)

An operator assigned to Dresden lists layouts, opens one, publishes it. Munich's
layouts are neither listed nor reachable, and nothing they do is visible to
Munich.

**Why this priority**: It is the foundation. Half B cannot be built until a
layout has a fab, because "which fabs reference this overlay" is answered by
reading the fab off the referencing layouts.

**Independent Test**: As a Dresden-only operator, list layouts and request a
Munich layout by identifier.

**Acceptance Scenarios**:

1. **Given** layouts exist in both fabs, **When** a Dresden-only operator
   lists them, **Then** only Dresden's appear.
2. **Given** a Munich layout's identifier, **When** a Dresden-only operator
   requests it, **Then** the response is indistinguishable from a layout that
   never existed.
3. **Given** a Munich layout's identifier, **When** a Dresden-only operator
   tries to publish, archive, branch, edit or revert it, **Then** it is
   refused identically to a layout that never existed — the refusal must not
   confirm that it exists.
4. **Given** an operator assigned to exactly one fab, **When** they create a
   layout without naming a fab, **Then** it is created in their fab.
5. **Given** an operator assigned to several fabs, **When** they create a
   layout without naming one, **Then** they are refused and asked to name it
   rather than having one chosen for them.
6. **Given** an operator naming a fab they do not hold, **When** they create a
   layout, **Then** they are refused as forbidden — they named a fab, so the
   answer is about the fab.
7. **Given** an operator assigned to no fab, **When** they list, **Then** they
   are refused rather than shown an empty list.
8. **Given** a layout named "Main Wall" exists in Munich, **When** a
   Dresden-only operator creates one with that name, **Then** it succeeds —
   and in particular is not reported as a name already taken.
9. **Given** a layout named "Main Wall" exists in Dresden, **When** a Dresden
   operator creates another with that name, **Then** it is still refused.

---

### User Story 2 - A kiosk hears only about its own plant's layouts (Priority: P1)

A Dresden screen is told when a Dresden layout is published or archived, and is
told nothing when a Munich one is.

**Why this priority**: P1 alongside US1 because it is the leak #1397 reports.
US1 closes the API; this closes the push, and a kiosk learns about layouts
almost entirely through the push.

**Independent Test**: Connect a screen as a Dresden-only kiosk, publish and
archive a layout in each fab, and record every frame the screen receives.

**Acceptance Scenarios**:

1. **Given** a Dresden kiosk is connected, **When** a Munich layout revision
   is published, **Then** the Dresden kiosk receives nothing.
2. **Given** a Dresden kiosk is connected, **When** a Dresden layout revision
   is published, **Then** it receives the frame.
3. **Given** a Munich layout revision is archived, **Then** only Munich screens
   are told.
4. **Given** a screen whose connection holds no fab, **Then** it receives no
   lifecycle frames at all.

---

### User Story 3 - An overlay's lifecycle reaches only the plants that use it (Priority: P1)

A screen is told that an overlay was published or archived only if a layout in
its own fab actually uses that overlay.

**Why this priority**: P1 because these two frames carry the overlay's text,
name and geometry — production content, not a bare notification. It is the
more consequential half of #1397 even though it is the smaller one.

**Independent Test**: Publish an overlay referenced by a Dresden layout only,
then by layouts in both fabs, then by none, and record which screens hear
about each.

**Acceptance Scenarios**:

1. **Given** an overlay used by a published Dresden layout and by no Munich
   layout, **When** a new revision of it is published, **Then** only Dresden
   screens are told.
2. **Given** an overlay used by published layouts in both fabs, **When** it is
   published or archived, **Then** both fabs' screens are told.
3. **Given** an overlay that no published layout references, **When** it is
   published or archived, **Then** no screen is told.
4. **Given** an overlay, **Then** it never acquires a fab of its own — the
   answer is always derived from what references it.

---

### User Story 4 - Layouts that predate this feature acquire a fab (Priority: P2)

Layouts created before this change end up in a fab rather than being stranded.

**Why this priority**: Below the P1s because it is a one-time transition, but
required: a layout with no fab would be invisible to every operator and its
frames deliverable to nobody, which for existing layouts means every screen
goes dark.

**Independent Test**: Against a database of layouts with no fab, run the
migration and confirm each is attributed and still reachable.

**Acceptance Scenarios**:

1. **Given** layouts with no fab, **When** the migration runs, **Then** each
   acquires a fab and the number so attributed is recorded where an operator
   will see it.
2. **Given** the migration has run, **Then** no layout is left without a fab.

---

### User Story 5 - A layout cannot borrow another plant's camera (Priority: P1)

An operator building a Dresden layout cannot place a Munich camera on it.

**Why this priority**: P1 because it is a hole in the isolation rather than an
improvement to it. A tile carries a camera, cameras have had a fab since spec
015, and a cross-fab tile would show another plant's live video on a screen
this feature is otherwise busy protecting.

**Independent Test**: As a Dresden operator, create a layout with a tile naming
a Munich camera.

**Acceptance Scenarios**:

1. **Given** a Munich camera, **When** a Dresden operator creates a layout
   with a tile referencing it, **Then** the layout is not created and the
   reason names the offending tile.
2. **Given** an existing Dresden layout, **When** an operator edits a draft to
   reference a Munich camera, **Then** the edit is refused.
3. **Given** a tile naming a camera identifier that resolves to no camera at
   all, **Then** it is refused rather than accepted unchecked.
4. **Given** a layout attributed by the migration, **Then** its existing tiles
   are not re-validated — the backfill's guess must not fail the deployment.

---

### Edge Cases

- **An operator addressing another fab's layout by identifier**: reported
  exactly as one that never existed, on reads *and* on all six writes. A write
  that answered "forbidden" would confirm the layout exists.
- **A screen holding no fab**: receives nothing. The transition fails closed —
  a screen showing nothing is recoverable; one showing another plant's overlay
  text is not.
- **An overlay referenced only by a *draft* layout**: does not count (FR-013).
  That fab is not told when the overlay is archived and finds the draft broken
  at publish time — accepted, because the alternative sends overlay text to a
  fab that displays it nowhere.
- **An overlay whose last referencing layout is archived**: the fabs that hear
  about it change over time, because the answer is a live query rather than a
  stored attribute. That is intended: the answer follows current usage.
- **A layout moved between fabs**: not possible. As with a camera (spec 015
  FR-004), the fab is fixed at creation.
- **A revision**: has no fab of its own. It belongs to its layout's fab, so
  the two can never differ.
- **A camera decommissioned or unknown when a tile names it**: refused
  (FR-015). Accepting an unresolvable camera would make FR-014 bypassable by
  naming an identifier that resolves to nothing.
- **A pre-existing layout whose tiles look cross-fab after the backfill**:
  left alone (FR-018). The mismatch comes from the migration's own guess, not
  from an operator, and failing over it would block the deployment.

## Requirements *(mandatory)*

### Half A — the layout carries a fab

- **FR-001**: Every layout MUST belong to exactly one fab.
- **FR-002**: A layout's fab MUST be fixed when it is created and MUST NOT be
  changeable afterwards.
- **FR-003**: A revision MUST NOT carry a fab of its own; it belongs to its
  layout's fab.
- **FR-004**: Creating a layout MUST resolve the fab from the caller: inferred
  when they hold exactly one, taken from an explicitly named fab when they hold
  several, refused when they hold several and name none, and refused as
  forbidden when they name one they do not hold.
- **FR-005**: Listing layouts MUST return only those in fabs the caller holds.
- **FR-006**: A layout in a fab the caller does not hold MUST be reported
  exactly as one that never existed, on reads and on every write alike.
- **FR-007**: An operator holding no fab MUST be refused rather than shown an
  empty result.

- **FR-019**: A layout name MUST be unique **within a fab** rather than
  globally. Two fabs MUST each be able to hold a layout of the same name, and a
  name already used in another fab MUST NOT be reported as taken.

  *Why this is a requirement and not a tidy-up*: the uniqueness check is
  enforced in the create handler today and is global. Left global it both
  blocks a plant from using an obvious name because another plant did, and —
  worse — reports "name taken" for a layout the caller cannot see, which is
  the enumeration oracle FR-006 closes on the read path reappearing on the
  write path.

### Half A — the layout frames

- **FR-008**: The layout-published and layout-archived frames MUST reach only
  screens in that layout's fab.
- **FR-009**: A screen whose connection holds no fab MUST receive no lifecycle
  frames.

### Half B — the overlay frames

- **FR-010**: The overlay-published and overlay-archived frames MUST reach only
  the fabs that reference that overlay, and MUST reach every such fab.
- **FR-011**: An overlay that nothing references MUST reach no screen.
- **FR-012**: An overlay MUST NOT be given a fab of its own. The set of fabs
  told about it MUST be derived from what references it, so that a change in
  usage changes the answer without anything being re-recorded.
- **FR-013**: A layout MUST be counted as referencing an overlay only when a
  **published** revision of it has a tile carrying that overlay. A draft's
  tiles MUST NOT count.

  *Consequence, accepted rather than overlooked*: a fab whose only use of an
  overlay is in an unpublished draft is not told when that overlay is
  archived, and will find the draft broken when it tries to publish. That is
  the fail-closed direction — the alternative tells a fab about overlay text
  it does not display anywhere.

### Cross-context integrity

- **FR-014**: Every tile in a layout MUST reference a camera belonging to that
  layout's own fab. A tile naming a camera from another fab MUST be refused,
  on creation and on edit alike.

  *Why this is here rather than deferred*: cameras have carried a fab since
  spec 015, so a cross-fab tile became expressible then. Without this, an
  operator could put another plant's camera on their own layout and watch its
  live video — walking straight around the isolation this feature exists to
  build, and around spec 016's stream scoping with it.

- **FR-015**: A tile whose camera cannot be resolved to a fab MUST be refused
  rather than accepted unchecked. Failing open here would make FR-014
  bypassable by referencing an unknown identifier.

### Half B — transition

- **FR-016**: Layouts existing before this feature MUST acquire a fab, and the
  number so attributed MUST be recorded where an operator will see it.
- **FR-017**: After the transition no layout may be without a fab.
- **FR-018**: Tiles existing before this feature MUST NOT be retro-validated
  against FR-014. A pre-existing layout whose tiles now look cross-fab is a
  consequence of the backfill's own guess, not an operator's choice, and
  failing the migration over it would block the deployment it is meant to fix.

### Key Entities

- **Layout** — gains a fab, fixed at creation and never changed. Owns a chain
  of revisions.
- **Revision** — unchanged. Belongs to its layout's fab; carries the tiles.
- **Tile** — unchanged. Already carries the optional overlay reference that
  Half B reads.
- **Overlay** — not stored here and deliberately given no fab (ADR-0115). It is
  reached only through the tiles that reference it.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator assigned to one plant sees, in every listing, only
  that plant's layouts — 100% of rows.
- **SC-002**: A request for another plant's layout is indistinguishable from a
  request for one that never existed, compared field by field rather than by
  status alone, on a read and on each of the six writes.
- **SC-003**: Over a session in which layouts are published and archived in
  both fabs, a single-fab screen receives **zero** frames belonging to the
  other fab.
- **SC-004**: An overlay lifecycle frame reaches exactly the set of fabs
  referencing that overlay — no fab missing, no fab extra — measured across
  the referenced-by-one, referenced-by-both and referenced-by-none cases.
- **SC-005**: Every layout that existed before this change carries a fab; none
  is left unattributed.
- **SC-006**: No tile referencing a camera outside its layout's fab can be
  created or edited into existence — 100% of attempts refused, including one
  naming a camera that does not exist.
- **SC-007**: The same layout name is usable in every fab, and using a name
  another fab holds is not distinguishable from using a name nobody holds.
- **SC-008**: No measurable regression on the push path, against a baseline
  taken before the change.

## Assumptions

- **A layout is authored, so its fab is resolved from the caller.** This is the
  opposite of spec 016, where nothing authors a stream and the fab is derived.
  The six write endpoints here make ADR-0114's decision table applicable in
  full, including the ambiguity refusal that spec 016 correctly has no use for.
- **The backfill can be done in SQL, unlike spec 016's.** Layouts and their
  tiles are in this context's own database, so a migration can attribute them
  without reading another context's data. The precedent is spec 015's, which
  attributes pre-existing rows to `munich` and raises a warning naming the
  count, rather than failing — refusing would block a deployment whose layouts
  really are Munich's, which is every deployment that currently exists. Spec
  016 could not do this only because cameras were in a different database.
- **The set of fabs told about an overlay is computed, not stored.** It follows
  current usage, so archiving the last referencing layout silently narrows the
  audience. This is preferred over a stored attribute that would need
  invalidating on every layout edit.
- **The hub's group mechanism is reused unchanged.** Screens already join one
  group per fab on connect, and two of the six frames already use it. This
  feature supplies the missing input — which fab a frame belongs to — and
  changes nothing about delivery.
- **Knowing a camera's fab is a new dependency, and the spec does not pick the
  mechanism.** FR-014 requires LayoutComposition to know which fab a camera
  belongs to, which it does not today. `CameraRegisteredV1` has carried the
  camera's fab since spec 015 and this context could learn from it, or it
  could ask CameraCatalog directly — that is a plan-level decision, and it is
  the one place this feature adds coupling. It is called out here so the plan
  argues it rather than inherits it, as ADR-0116 had to for spec 016.
- **Overlay lifecycle frames are not on the latency-critical leg.** Publishing
  or archiving an overlay revision is an authoring action, not a per-event
  push; the resolved-text push that *is* on the event-to-overlay path was
  scoped already and is untouched here.
