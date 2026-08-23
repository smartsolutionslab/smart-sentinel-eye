# Feature Specification: Retire a camera

**Feature Branch**: `028-retire-camera`

**Created**: 2026-08-23

**Status**: Draft

**Input**: Issue #1433 — `CameraStatus.Decommissioned` exists as a value and nothing in CameraCatalog ever transitions to it. The Camera aggregate has exactly one behaviour, `Register`. There is no retire command, handler or endpoint.

## Why this exists

A 250-camera fab replaces hardware. Today a camera that no longer physically
exists is **indistinguishable from one that does**: it stays in every listing,
holds its name reserved forever, and keeps its RTSP address in the catalogue.

Rules and variables both have a terminal state. Cameras have the *value* for one
and no way to reach it.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Retire a camera that no longer exists (Priority: P1)

A camera is physically removed from the fab — failed hardware, a decommissioned
line, a relocated cell. An operator records that it is gone, so the catalogue
stops describing a camera that is not there.

**Why this priority**: This is the whole feature. Every other story depends on a
camera being able to reach the retired state at all, and without it the
catalogue's accuracy degrades with every hardware change.

**Independent Test**: Retire a registered camera and observe its status change
and the retirement announced. Delivers value alone: the catalogue distinguishes
present from absent hardware even if nothing else in this spec ships.

**Acceptance Scenarios**:

1. **Given** a registered camera in fab `munich`, **When** an operator retires
   it, **Then** its status becomes retired and the retirement is announced to
   other parts of the system.
2. **Given** a camera in fab `munich`, **When** an operator authorised only for
   fab `dresden` attempts to retire it, **Then** the request is refused as
   not-found — the same answer that fab gets for any camera it cannot see.
3. **Given** a camera that has already been retired, **When** an operator
   retires it again, **Then** the request succeeds and nothing further is
   announced.

---

### User Story 2 - Reuse a retired camera's name (Priority: P2)

Replacement hardware arrives for a camera that was retired. The operator gives
the new camera the name the old one had, because that name describes a location
on the plant floor — `line-3-inlet` is where the camera points, not which unit
is bolted there.

**Why this priority**: This is the operational payoff of retiring, and the
reason the catalogue's uniqueness rule already excludes retired cameras. Without
it, retiring is bookkeeping that changes nothing an operator can act on.

**Independent Test**: Retire a camera, then register a new one with the same
name in the same fab, and observe it accepted. Requires User Story 1.

**Acceptance Scenarios**:

1. **Given** a retired camera named `line-3-inlet` in fab `munich`, **When** a
   new camera is registered as `line-3-inlet` in fab `munich`, **Then** it is
   accepted.
2. **Given** an *active* camera named `line-3-inlet` in fab `munich`, **When** a
   new camera is registered as `line-3-inlet` in the same fab, **Then** it is
   still refused — retiring is what releases a name, nothing else changed.
3. **Given** a retired camera named `line-3-inlet` in fab `munich`, **When** a
   camera named `line-3-inlet` is registered in fab `dresden`, **Then** it is
   accepted, as it already was — name reuse must not leak across fabs.

---

### User Story 3 - Retired cameras stay out of the way (Priority: P3)

An operator listing the cameras in their fab sees the cameras that exist. A
catalogue that grows monotonically with every replacement becomes less useful
every year.

**Why this priority**: Real but deferrable. The catalogue is correct once User
Story 1 ships; this is about it staying legible at 250 cameras and years of
hardware churn.

**Independent Test**: Retire a camera and list the fab's cameras; it is absent
by default and present when explicitly asked for. Requires User Story 1.

**Acceptance Scenarios**:

1. **Given** a retired camera in fab `munich`, **When** an operator lists that
   fab's cameras, **Then** the retired camera is not in the result.
2. **Given** a retired camera in fab `munich`, **When** an operator lists the
   fab's cameras asking for retired ones to be included, **Then** it appears,
   marked as retired.

---

### Edge Cases

- **Retiring a camera that was never registered** — refused as not-found, the
  same as any unknown camera.
- **Retiring the same camera twice, concurrently** — one request retires it;
  the other finds it already retired and succeeds without announcing a second
  retirement. No caller sees a failure for a state that is already true.
- **A name freed by retirement, claimed twice** — the existing uniqueness rule
  decides: the first registration wins, the second is refused.
- **Retiring a camera whose name differs only in case from another fab's** —
  unaffected. Retirement is scoped to one camera in one fab.
- **A camera retired while its stream is live** — see FR-008; this is the
  cross-context question this feature has to answer rather than leave implied.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The Camera aggregate MUST offer a behaviour that transitions a
  camera to the retired state, and MUST refuse any transition out of it —
  retirement is terminal.
- **FR-002**: Retiring a camera MUST raise a domain event recording that it was
  retired, who retired it, and when.
- **FR-003**: An operator MUST be able to retire a camera through the API, using
  the same fab resolution as every other camera endpoint.
- **FR-004**: A retire request for a camera belonging to another fab MUST be
  answered as not-found, not as forbidden — a fab MUST NOT be able to discover
  which camera names exist in another fab by the shape of the refusal.
- **FR-005**: Retiring an already-retired camera MUST succeed and MUST NOT raise
  a second domain event.
- **FR-006**: A retired camera's name MUST become available for registration
  again **within its own fab**, and MUST NOT affect name availability in any
  other fab.
- **FR-007**: Listing a fab's cameras MUST exclude retired cameras by default,
  and MUST provide a way to include them, marked as retired.
- **FR-008**: When a camera is retired, its video stream MUST stop being served:
  the SFU path MUST be removed and the stream MUST reach a terminal state. The
  stream's record MUST be retained rather than deleted — retirement records that
  hardware *was* there.
- **FR-008a**: Stream teardown MUST NOT be required for the retirement itself to
  succeed. A camera is retired in the catalogue whether or not stream
  distribution has caught up; the two are connected by an announcement, not by a
  shared transaction.
- **FR-009**: The retirement MUST be announced to other bounded contexts as an
  integration event, whether or not any context acts on it in this feature.
- **FR-010**: Retiring a camera MUST be recorded in the audit trail, as camera
  registration is.

### Key Entities

- **Camera**: gains a terminal retired state and the behaviour to reach it.
  Retains its name, fab, and RTSP address — retiring records that the hardware
  is gone, it does not erase the history that it was there.
- **Camera retirement event**: what happened, to which camera, in which fab, by
  whom, when. Consumed by the audit trail and, depending on FR-008, by stream
  distribution.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator can retire a camera and see it disappear from their
  fab's camera list in a single action.
- **SC-002**: A replacement camera can take the retired camera's name, with no
  manual database intervention and no waiting period.
- **SC-003**: 100% of retire attempts against another fab's cameras are refused
  and are indistinguishable from attempts against names that do not exist.
- **SC-004**: A retired camera never reappears in a default camera listing, at
  any point after retirement.
- **SC-005**: Retiring a camera is recorded in the audit trail and can be traced
  back to the operator who did it.
- **SC-006**: Retiring a camera does not change the behaviour of any camera that
  was not retired — every existing camera test continues to pass unchanged.

## Assumptions

- **Retirement is terminal.** A retired camera is not reinstated; replacement
  hardware is registered as a new camera that may take the old name. Chosen
  because it matches the terminal states rules and variables already have, and
  because un-retiring would make name reuse ambiguous — two cameras could claim
  one name.
- **Retiring is idempotent** rather than a conflict, matching the archive
  behaviours elsewhere in this system, where re-archiving an already-archived
  entity succeeds rather than failing.
- **Retirement is an operator action, not an automatic one.** Nothing infers
  retirement from a camera being unreachable — an offline camera is a fault to
  investigate, not a decommissioning. This is deliberately not a health-driven
  feature.
- **No schema change is expected for name reuse.** The catalogue's uniqueness
  constraint already excludes retired cameras, so FR-006 should be a matter of
  asserting behaviour rather than changing storage. If that proves wrong it is a
  finding for the planning phase, not a silent scope increase.
- **Stream teardown is in scope, and the stream's record survives (FR-008).**
  Decided rather than answered: the question was put to the user twice and the
  work was told to continue, so the recommendation was adopted and is recorded
  here as a decision to overturn rather than a consensus to cite.

  Two reasons it is not deferred. First, a retired camera that keeps its SFU
  path leaves the system pulling RTSP from hardware that is not there —
  precisely the "keeps its RTSP address in the catalogue" complaint in #1433.
  Second, and newly true: since #1801 was fixed, the health watcher announces
  *every* health change rather than one per sweep, so each retired-but-still-
  provisioned camera becomes a permanent source of health-change announcements
  and audit rows for hardware that does not exist. Deferring would turn that fix
  into ongoing noise.

  The record is kept rather than deleted because a stream that once existed is
  history an audit trail should be able to explain.

  **If overturned**, the fallback is to announce the retirement and file stream
  teardown separately; FR-009 already requires the announcement, so nothing else
  in this spec depends on the choice.

- **Fab scoping is not in scope** — spec 015 delivered it. This feature uses it.
- **The operator's identity is already available** on camera-writing endpoints,
  as it is for registration, so FR-002 and FR-010 need no new authentication
  work.
