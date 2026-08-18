# Feature Specification: A plant that exists can store its events

**Feature Branch**: `019-fab-event-partitions`

**Created**: 2026-08-18

**Status**: Draft

**Input**: #1547 — adding a fab needs a hand-written events partition, and nothing enforces it.

## Why this exists

Event storage is divided per plant. A plant that has no division of its own
cannot store anything, and today that division is created only by hand, in a
separate act from creating the plant.

So a plant can exist for every purpose except the one that matters. Its
operators can sign in, they are shown their plant, they can file an event —
and the event is accepted, acknowledged, and then discarded. Nothing tells
them. The system reports success at every point a person can see.

Spec 018 made this reachable rather than theoretical. Before it, the plant an
event was filed against came from the request, and every request in practice
named the one plant that had storage. Now it comes from who the caller is, so
adding a person to a new plant is enough to start losing that plant's events.
That was the right change — it closed a cross-plant write — and this is the
consequence it exposed.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A new plant can store events from the moment it exists (Priority: P1)

Someone responsible for the system creates a new plant and assigns an operator
to it. The operator files an event. It is stored, and they can read it back.
Nobody had to know that storage is divided per plant, and nobody had to
perform a second, unrelated act to make the first one work.

**Why this priority**: It is the whole feature. Every other story here is about
what happens when this one has not taken effect yet.

**Independent Test**: Create a plant that has never existed, assign an operator,
file an event as them, and read it back.

**Acceptance Scenarios**:

1. **Given** a plant that has just been created and has never had events,
   **When** the system next prepares its storage, **Then** that plant can store
   events, without anyone writing anything by hand.
2. **Given** a plant created while the system was already running, **When** its
   storage has been prepared, **Then** an operator of that plant can file an
   event and read it back.
3. **Given** a plant whose storage already exists, **When** the system prepares
   storage again, **Then** nothing changes and no existing event is affected.
4. **Given** several plants, **When** storage is prepared, **Then** each gets
   its own, and no plant's events become visible to another.

---

### User Story 2 - An event that cannot be stored is never reported as accepted (Priority: P1)

An event arrives for a plant whose storage is not ready. It must not be
acknowledged and then dropped. Whoever submitted it learns it did not land,
and whoever runs the system can see why.

**Why this priority**: P1, and deliberately not folded into Story 1. Story 1
removes the common cause; this removes the *silence*, which is what made the
common cause survive unnoticed. Any future cause — a plant created between two
preparation runs, a storage division removed by hand, a plant named in a way
the system cannot use — lands here.

**Independent Test**: Present an event for a plant with no storage and confirm
it is reported as failed, both to the submitter and in the record an operator
reads.

**Acceptance Scenarios**:

1. **Given** a plant with no storage, **When** an operator files an event for
   it, **Then** they are told it was not stored — not that it was accepted.
2. **Given** the same, **When** whoever runs the system looks, **Then** the
   record says which plant had no storage, distinguishably from any other
   ingest failure.
3. **Given** a delivery from a machine rather than an operator, **When** its
   plant has no storage, **Then** the same distinguishable record appears.
4. **Given** a plant whose storage exists, **When** events are filed normally,
   **Then** nothing about the accepted path changes.

---

### User Story 3 - Removing a plant never destroys its events (Priority: P1)

A plant is decommissioned and its group removed. Its events remain readable to
whoever still has the right to read them.

**Why this priority**: P1 because it is a data-loss guard rather than a
feature. Deriving storage from the list of plants makes "the plant is gone"
reachable for the first time, and the destructive reading of that — remove the
storage — would delete every event that plant ever recorded. It costs nothing
to get right and cannot be undone if got wrong.

**Independent Test**: Prepare storage for two plants, remove one from the list,
prepare storage again, and confirm its events are still there.

**Acceptance Scenarios**:

1. **Given** a plant with stored events, **When** the plant is removed from the
   list of plants and storage is prepared again, **Then** its events are
   unchanged and still present.
2. **Given** the same, **When** an operator who still holds that plant reads,
   **Then** they see those events.

---

### Edge Cases

- **A plant named in a way the system cannot use**: skipped, and the run
  continues for every other plant. One unusable name must not stop a new plant
  from getting its storage — the same failure mode as an unusable group in a
  read, which spec 018 settled the same way.
- **The list of plants cannot be reached** when storage is prepared: the run
  must fail rather than conclude that no plants exist. "No plants" and "cannot
  tell" look identical from the inside and mean opposite things — and treating
  the second as the first would quietly prepare nothing.
- **A plant added between two preparation runs**: covered by Story 2 until the
  next run. This is the residual gap the feature narrows but does not close,
  and it is why Story 2 is P1.
- **Preparation runs twice at once** (a scheduled run overlapping a deployment):
  must be safe, and neither run may fail because the other created something
  first.
- **A plant exists in the list but its storage was removed by hand**: the next
  preparation run restores it; until then, Story 2 applies.
- **An event for a plant that is genuinely not a plant** (a name nobody
  provisioned): no storage is created for it. Creating storage on demand for
  whatever name arrives would let a typo provision a plant.

## Requirements *(mandatory)*

### Storage follows the plants

- **FR-001**: The system MUST derive which plants need event storage from the
  list of plants it already maintains, rather than from anything written by
  hand for that purpose.
- **FR-002**: The system MUST ensure event storage exists for every plant on
  that list, as part of the routine preparation it already performs before
  services start and on its regular schedule.
- **FR-003**: Preparing storage MUST be repeatable with no effect the second
  time, and MUST NOT disturb events already stored.
- **FR-004**: Preparing storage for a new plant MUST also make that plant ready
  for the time-based division the system already maintains, in the same pass —
  a plant that gains storage but no current period can still store nothing.
- **FR-005**: A plant whose name the system cannot use MUST be skipped, MUST
  NOT halt preparation for other plants, and MUST be recorded so it is not
  merely ignored.
- **FR-006**: The system MUST NOT remove event storage for a plant that has
  left the list. Removal is out of scope and MUST remain a deliberate human
  act.

### Nothing is accepted that cannot be stored

- **FR-007**: An event whose plant has no storage MUST NOT be reported to its
  submitter as accepted.
- **FR-008**: That failure MUST be distinguishable — in the record an operator
  reads — from any other failure to store an event, and MUST name the plant.
- **FR-009**: The same treatment MUST apply however the event arrived, whether
  filed by an operator or delivered by a machine.
- **FR-010**: Events for plants whose storage exists MUST be unaffected: same
  acceptance, same throughput, same behaviour under load.

### Reaching the list of plants

- **FR-011**: If the list of plants cannot be read, preparation MUST fail
  visibly and MUST NOT proceed as though the list were empty.
- **FR-012**: Preparation MUST NOT begin until the list of plants is reachable,
  and the wait MUST be bounded so a permanently unreachable list fails rather
  than hangs.

### Deliberately unchanged

- **FR-013**: The existing time-based division of storage MUST keep its current
  behaviour; this feature adds a step before it and changes nothing about it.
- **FR-014**: Who may read or write which plant's events MUST NOT change. Spec
  018 settled that and this feature does not revisit it.
- **FR-015**: Storage MUST NOT be created in response to an incoming event.
  Provisioning follows the list of plants, never traffic — otherwise a
  mistyped plant name provisions a plant.

### Key Entities

- **Plant** — already exists as an entry in the list the system maintains for
  deciding who may see what. This feature gives that list a second consequence:
  it now also decides where events are kept.
- **Event storage for a plant** — the division of event storage belonging to
  one plant. Currently created by hand; becomes derived. It is never destroyed
  by this feature.
- **Event** — unchanged. Only whether it can be stored, and what happens when
  it cannot, is in scope.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A plant that has never existed before can store and return an
  event **without any hand-written change**, within one routine preparation
  after it is created.
- **SC-002**: Adding a plant requires exactly **one** action by the person
  adding it. It is two today, and the second is silent when forgotten.
- **SC-003**: An event that cannot be stored is reported as not stored to
  whoever submitted it — **zero** events are acknowledged and then discarded
  for this cause.
- **SC-004**: Whoever runs the system can tell, from the record alone, that the
  cause was a missing plant division and which plant it was, without inspecting
  the database.
- **SC-005**: Preparation run twice in a row leaves the system in the same
  state, and every previously stored event still readable.
- **SC-006**: A plant removed from the list keeps every event it recorded.
- **SC-007**: Ingest of events for existing plants is unchanged — same
  acceptance behaviour and no measurable throughput cost on the ingest path.

## Assumptions

- **The list of plants is the right source.** It already decides who may read
  and write which plant's events (spec 018), so a plant that exists to the
  authorization system but not to storage is already an inconsistency rather
  than a valid state.
- **Preparation happens often enough.** It runs before every service start and
  on a schedule, so a new plant becomes storable within one cycle rather than
  at the next deployment. The gap between creating a plant and the next run is
  real, bounded, and covered by Story 2 rather than eliminated.
- **Names come from a trusted place, and are still checked.** The list of
  plants is maintained by administrators, not by callers; the names are
  nonetheless validated against the plant-name rule before use, because they
  now reach a place where they were previously known to come from the database
  itself.
- **The related silent-drop defect (#1546) is separate.** An event whose
  storage is missing is one cause of that defect; this feature makes that cause
  distinguishable and rare, and does not fix the general case. If #1546 is
  fixed first, this feature's requirements still hold unchanged; if it is fixed
  later, nothing here needs revisiting.
- **One realm holds every plant** in the environments this targets. Should
  plants ever be split across separate identity realms, FR-001's "list of
  plants" becomes the union across them, and nothing else in this spec changes.
