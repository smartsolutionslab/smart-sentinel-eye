# Feature Specification: Fab-scope stream distribution

**Feature Branch**: `016-stream-fab-scoping`

**Created**: 2026-08-10

**Status**: Draft

**Input**: #1155 — per-fab isolation; follows spec 015, which gave a camera its fab

## Why this exists

A stream is the live video of one camera. Spec 015 gave the camera a fab; the
stream showing its picture still belongs to nobody.

So an operator assigned to Dresden can list every plant's streams, and — because
a stream carries the MediaMTX path its video is served on — can reach the
picture from a camera they are not entitled to see. Spec 015 closed the
catalogue and left the video open, which is the more consequential half.

## What makes this different from specs 013, 014 and 015

Those three asked the operator which fab, because a rule, a variable and a
camera are each authored by someone. **Nothing authors a stream.** A stream
exists because a camera was registered, and `StreamDistribution` already learns
that from `CameraRegisteredV1` — which, since spec 015, carries the camera's
fab.

So this feature asks nobody anything. The fab is **derived**, and a stream whose
fab differs from its camera's should be unrepresentable rather than merely
discouraged.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - An operator sees only their own plant's video (Priority: P1)

An operator assigned to Dresden lists streams and opens one. Munich's are
neither listed nor reachable.

**Why this priority**: It is the exposure spec 015 left behind. A stream record
carries the path its video is served on, so listing another plant's streams is
the first step to watching them.

**Independent Test**: As a Dresden-only operator, list streams and request a
Munich camera's stream by identifier.

**Acceptance Scenarios**:

1. **Given** streams exist in both fabs, **When** a Dresden-only operator lists
   them, **Then** only Dresden's appear.
2. **Given** a Munich camera's identifier, **When** a Dresden-only operator
   requests its stream, **Then** the response is indistinguishable from a
   camera that has no stream at all.
3. **Given** an operator assigned to no fab, **When** they list, **Then** they
   are refused rather than shown an empty list.

---

### User Story 2 - A stream inherits its camera's fab (Priority: P1)

A stream provisioned for a Munich camera belongs to Munich, without anyone
saying so.

**Why this priority**: P1 alongside US1 because it is what makes US1 true. A
scoped read over an unattributed stream shows nothing to anyone.

**Independent Test**: Register a camera in each fab, then read back the streams
provisioned for them.

**Acceptance Scenarios**:

1. **Given** a camera registered in Dresden, **When** its stream is
   provisioned, **Then** the stream belongs to Dresden.
2. **Given** any stream, **Then** its fab equals its camera's fab — there is no
   operation that can make them differ.
3. **Given** a camera-registered event carrying no fab, **When** it is handled,
   **Then** no stream is provisioned and the drop is recorded.

---

### User Story 3 - Streams that predate this feature acquire their fab (Priority: P2)

Streams provisioned before this change end up correctly attributed, not
guessed.

**Why this priority**: Below the P1s because it is a one-time transition, but
in scope because the alternative — attributing every existing stream to the one
fab that was live — is a guess this feature can avoid. Cameras already carry a
real fab; the derivation is available.

**Independent Test**: Against a database of streams with no fab, run the system
and observe each acquiring the fab of its camera.

**Acceptance Scenarios**:

1. **Given** streams with no fab, **When** the system starts, **Then** each
   acquires the fab of its own camera and the number so filled is recorded.
2. **Given** a stream whose fab is not yet filled, **When** any operator lists,
   **Then** it is not shown to anyone.
3. **Given** a stream whose camera can no longer be found, **Then** it remains
   unattributed and that is recorded rather than defaulted.

---

### Edge Cases

- A camera-registered event with no fab: no stream provisioned, and the drop
  recorded. Silence would be indistinguishable from success.
- A stream whose fab is not yet filled: visible to nobody. The transition fails
  closed — a stream shown to the wrong plant is worse than one shown to no one
  for a few seconds.
- An operator assigned to no fab: refused rather than shown an empty list.
- The MediaMTX authorisation callback: **not fab-scoped**, deliberately. The
  caller is the media server itself, not an operator, and it holds no fab. See
  Assumptions.
- A camera moved between fabs: impossible by spec 015 FR-004, so a stream's
  derived fab cannot go stale.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Every stream MUST belong to exactly one fab.
- **FR-002**: A stream's fab MUST be the fab of the camera it serves, and MUST
  NOT be settable independently.
- **FR-003**: Provisioning MUST take the fab from the camera-registered event
  rather than from the caller. No request may name a fab.
- **FR-004**: A camera-registered event carrying no fab MUST provision no
  stream, and MUST be recorded.
- **FR-005**: Listing streams MUST return only those in fabs the caller holds.
- **FR-006**: A stream in a fab the caller does not hold MUST be reported
  exactly as a camera with no stream — never as refused-because-forbidden.
- **FR-007**: An operator holding no fab MUST be refused rather than shown an
  empty result.
- **FR-008**: Streams existing before this feature MUST acquire the fab of
  their own camera, and the number so attributed MUST be recorded where an
  operator will see it.
- **FR-009**: A stream whose fab is not yet known MUST be visible to no one,
  and MUST NOT be defaulted to any fab.
- **FR-010**: A stream whose camera cannot be resolved MUST remain
  unattributed, and that MUST be recorded rather than treated as success.

### Key Entities

- **Stream** — gains a fab, derived from its camera at provisioning and never
  independently set.
- **Camera** — not stored here. It is the source of a stream's fab, and reaches
  this context on the camera-registered event.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator assigned to one plant sees, in every listing, only
  that plant's streams — 100% of rows.
- **SC-002**: A request for another plant's stream is indistinguishable from a
  request for a camera that has no stream, compared field by field rather than
  by status alone.
- **SC-003**: Every stream provisioned after this change carries the fab of its
  camera, with no case where the two differ.
- **SC-004**: Every stream that existed before this change either carries its
  camera's fab or is recorded as unresolved — none is guessed.
- **SC-005**: No measurable regression on the leg this touches, against a
  baseline taken before the change.

## Assumptions

- **The fab is derived, never asked for.** A stream exists only because a
  camera does, and `CameraRegisteredV1` has carried the camera's fab since spec
  015. Asking an operator would let them provision a stream into a fab
  different from its camera's, which is nonsense the model should not be able
  to express.
- **The backfill cannot be a SQL join.** Cameras and streams live in separate
  databases on the same server, so a migration cannot read the cameras table.
  Existing streams therefore acquire their fab at runtime, from the context
  that owns it, rather than by a guess in SQL. This is why FR-009 exists: there
  is a window in which a stream has no fab, and it must be invisible rather
  than assumed.
- **`POST /authorize` is deliberately not scoped.** It authenticates MediaMTX,
  not an operator; there is no caller fab to resolve. Scoping it would mean
  inventing a per-fab identity for the media server, which does not exist and
  would need its own decision about how many instances there are per fab.
  Recorded here rather than left as an unexamined third endpoint.
- **This feature depends on spec 015 being merged.** It reads
  `CameraRegisteredV1.Metadata.Fab`, which spec 015 populates, and resolves
  existing streams against a camera catalogue that is itself fab-scoped.
