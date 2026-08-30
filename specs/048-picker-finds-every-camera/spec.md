# Feature Specification: The camera picker finds every camera

**Feature Branch**: `048-picker-finds-every-camera`
**Created**: 2026-08-30
**Status**: Draft
**Input**: Issue 1979, filed while walking spec 046 — a camera that existed, was registered, and was simply not in the picker.

---

## Why this is a correctness problem, not a limit

An operator builds a wall by choosing a camera for each tile. Today the chooser
offers the **50 most recently registered** cameras in the fab and says nothing
about the rest. A camera registered 51st ago is not marked unavailable, not
greyed out, and not mentioned — it is **absent**, and absence is
indistinguishable from "that camera does not exist".

The production target is **250 cameras per fab** (constitution §Scale, line
365). So at the scale this system is built for, **four cameras in five cannot be
put on a wall at all**, and nothing tells anyone.

**The same information is already on screen elsewhere.** The camera list page
reads the same source, tracks the total, and says "showing 1 to 50 of 250" with
working previous/next. One screen tells an operator the truth; another shows a
fifth of it in silence. This feature is not adding a capability the product
lacks — it is making one chooser honest, in the way the rest of the product
already is.

**Two facts constrain the answer, and both were verified rather than assumed:**

1. **A single request cannot return a whole fab.** The camera source refuses any
   request for more than **200** cameras at once — it rejects rather than
   trimming. 200 < 250, so "ask for all of them" is not available at target
   scale, whatever number is written in the chooser.
2. **Nothing anywhere lets an operator search cameras by name.** The camera
   source can be filtered by fab, sorted, and paged. It cannot be asked for
   "the cameras called *furnace*". So "just let them type it" is a larger change
   than it sounds.

Fact 1 is why raising the number is not the fix. It moves the cliff from 50 to
200 and leaves it silent.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The operator is never silently shown an incomplete list (Priority: P1)

An operator opens the tile chooser. If it is not showing every camera they could
choose, **it says so**, and says how many there are.

**Why this priority**: This is the defect. Every other story here changes *how
many* cameras are reachable; this one changes whether an operator can trust what
they see. A chooser that shows 50 of 250 and admits it is safe — the operator
knows to look further. A chooser that shows 50 of 250 in silence has told them a
falsehood, and they will conclude the camera was never registered. **Shipped
alone, this story removes the harm even if the count never changes.**

**Independent Test**: With more cameras in a fab than the chooser displays, open
it and confirm it states both what is shown and what exists. Then remove the
notice and confirm the test fails — an assertion that passes on a complete list
proves nothing.

**Acceptance Scenarios**

1. **Given** a fab with more cameras than the chooser can display at once,
   **When** an operator opens it, **Then** the chooser states how many cameras
   are shown and how many exist in total.
2. **Given** a fab whose cameras all fit, **When** an operator opens the
   chooser, **Then** no truncation notice appears — the notice means something
   because it is not always there.
3. **Given** a fab with no cameras at all, **When** an operator opens the
   chooser, **Then** they are told the fab has none, which is different from
   being shown an empty list that might be loading.

---

### User Story 2 - Every camera in the fab can be chosen (Priority: P2)

An operator can put **any** registered, non-retired camera in their fab onto a
tile, including the 250th.

**Why this priority**: The story that makes the product work at its stated
scale. Second only because US1 removes the *deception* and can ship first; an
operator who knows the list is incomplete can at least ask someone, whereas one
who does not know cannot.

**Independent Test**: Register more cameras than any single request may return
(over 200), then place the last-registered and the first-registered on tiles.
Both must be reachable. Testing only the recent end passes with the defect
present.

**Acceptance Scenarios**

1. **Given** a fab of 250 cameras, **When** an operator looks for the one
   registered first, **Then** they can select it and save the wall.
2. **Given** a fab of 250 cameras, **When** the chooser is opened, **Then** the
   operator is not required to know a camera's position or registration date to
   reach it.
3. **Given** a camera is retired, **When** the chooser is opened, **Then** it is
   not offered — reaching *every* camera means every choosable one, not every
   row that ever existed.

---

### User Story 3 - Finding a camera does not mean reading the whole list (Priority: P3)

An operator who knows their camera is called "Furnace 3" can get to it without
scanning a list of 250.

**Why this priority**: US2 makes every camera *reachable*; this makes them
*findable*. The distinction matters at 250: a chooser listing all of them is
correct and still close to unusable, because the operator's real task is "find
the one I mean", not "browse everything". Ranked third because it is the largest
change — nothing in the system can currently filter cameras by name — and
because US1 and US2 together already end the data loss.

**Independent Test**: In a fab of 250, reach a named camera without paging
through the list. Measured by the operator's actions, not by response time.

**Acceptance Scenarios**

1. **Given** a fab of 250 cameras, **When** an operator types part of a camera's
   name, **Then** only matching cameras are offered.
2. **Given** a typed fragment matching nothing, **When** the operator looks at
   the chooser, **Then** they are told nothing matched, rather than shown an
   empty list that reads as "still loading".

---

### Edge Cases

- **The list changes while the chooser is open.** A camera registered or retired
  by someone else mid-selection. The operator must not be shown a stale total
  presented as current, and must not lose a selection they already made.
- **A camera is retired after being selected but before the wall is saved.** The
  wall must not save a tile pointing at a camera that can no longer be chosen,
  and the operator must be told which tile is affected — not handed a generic
  failure.
- **The operator's permissions span more than one fab.** The count shown must be
  the count they can actually choose from. A total including cameras they cannot
  select is a different lie in the same place.
- **The camera source is unreachable.** The chooser must say so. It must not
  render as an empty list, which reads as "this fab has no cameras" — the exact
  confusion this feature exists to remove.
- **Two cameras share a name.** Names are not known to be unique; an operator
  who searches must still be able to tell two results apart.

---

## Requirements *(mandatory)*

### Functional Requirements

**Honesty about completeness (US1)**

- **FR-001**: The chooser MUST state how many cameras it is offering and how
  many exist in the operator's fabs whenever those numbers differ.
- **FR-002**: The chooser MUST NOT display a truncation notice when the list is
  complete.
- **FR-003**: An empty chooser MUST distinguish between "this fab has no
  cameras", "still loading", and "the camera list could not be retrieved".

**Reaching every camera (US2)**

- **FR-004**: An operator MUST be able to select any registered, non-retired
  camera in their fabs, at the 250-camera production target.
- **FR-005**: The chooser MUST NOT depend on a single retrieval returning every
  camera, because the camera source refuses requests above 200.
- **FR-006**: Retired cameras MUST NOT be offered.
- **FR-007**: A camera the operator's permissions do not cover MUST NOT be
  offered, and MUST NOT be counted in the total shown.

**Finding by name (US3, if in scope after planning)**

- **FR-008**: An operator MUST be able to narrow the offered cameras by typing
  part of a camera's name.
- **FR-009**: A search matching nothing MUST say so explicitly.

**Behaviour that must not regress**

- **FR-010**: Choosing a camera MUST remain possible without a network round
  trip per keystroke being required to select an already-visible camera.
- **FR-011**: A selection already made MUST survive the list being refreshed or
  extended.
- **FR-012**: The overlay chooser in the same dialog MUST be left alone. It is
  not paginated and does not truncate; changing it would be a change without
  evidence.

### Key Entities

- **Camera** — what an operator picks. Has a name they recognise, a fab, and a
  retired/not-retired state. Identity is not its name; two may share one.
- **The operator's reachable set** — cameras in the fabs their permissions
  cover, excluding retired ones. This, not "all cameras", is what any count
  shown must describe.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In a fab of 250 cameras, an operator can place the
  earliest-registered camera on a tile. Today this is impossible.
- **SC-002**: Whenever the chooser is incomplete, an operator can state from the
  screen alone how many cameras they are not being shown.
- **SC-003**: No camera that an operator is entitled to choose is absent from
  the chooser without the chooser saying a camera is absent.
- **SC-004**: An operator finds a camera they can name in a 250-camera fab
  without reading more than a screenful of names. *(US3 only.)*
- **SC-005**: The count the chooser reports equals the number of cameras the
  operator can actually select — verified against an operator whose permissions
  cover fewer than all fabs.

### Explicitly not claimed

- **No latency target.** This feature is not on the event-to-overlay path; wall
  building is a configuration task, not a monitoring one. Any speed claim here
  would be invented rather than budgeted.
- **No claim about fabs beyond 250.** 250 is the constitution's number.
  Whichever option is chosen, the spec states whether it holds beyond that
  rather than implying it scales indefinitely.

---

## Scope

### In scope

- The camera chooser in the wall/layout editor.
- Whatever change to how cameras are retrieved that chooser needs.

### Deferred, and named rather than implied

- **Every other consumer of a paginated list.** This chooser is one instance of
  a general shape — a caller that requests a page and renders it as though it
  were the whole. The instinct to audit them all is right and belongs in its own
  issue, because a spec that fixes one chooser and a spec that reviews every
  list are different sizes of work and the second will swallow the first.
  **To be filed during planning.**
- **The 200-cap sitting below the 250-camera target.** That number looks
  arbitrary and no reasoning for it was found. Whether it is a defect, a
  deliberate bound needing a written reason, or correct as-is is a decision this
  spec surfaces but does not settle. **To be filed during planning.**
- **The overlay chooser** (FR-012). Unbounded overlay growth may be a real
  concern; it is a different problem with different evidence.

---

## Assumptions

- **Operators recognise cameras by name.** The chooser already labels them by
  name, so this is how the product already behaves rather than a new claim.
- **250 is the number to design against**, per constitution §Scale. Not a
  guess — a stated production target.
- **Camera names are not unique**, since nothing enforces uniqueness. The spec
  therefore does not let a name stand in for identity.
- **Retired cameras are not choosable**, matching the retirement concept already
  in the product.
- **The chooser is used while building or editing a wall**, not while watching
  one. Nobody is waiting on a live event when they open it.

---

## Dependencies

- The camera source must be able to answer "how many cameras can this operator
  choose from" — it already reports a total alongside each page.
- US3 depends on the camera source being able to filter by name, **which it
  cannot do today**. That is the change that makes US3 the largest of the three,
  and the reason it is ranked below stories that need no such change.

---

## Open decision for planning

Three shapes were considered and this spec deliberately does not pick one — the
choice depends on how the deferred items above are settled:

| Shape | Reaches 250? | Silent? | Cost |
|---|---|---|---|
| Ask for more at once | **No** — the source refuses above 200 | Still silent at the new cliff | One line |
| Retrieve successive pages until the total is reached | Yes | Solved by US1 regardless | Moderate; no change to the camera source |
| Filter by name as the operator types | Yes, and beyond | Solved by US1 regardless | Largest; needs the camera source to gain a capability it lacks |

**US1 is orthogonal to all three** and is why it is P1: whichever is chosen, the
chooser must stop being silent, and that alone ends the failure this feature was
raised for.
