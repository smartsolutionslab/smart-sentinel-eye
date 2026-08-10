# Feature Specification: Fab-scope the camera catalogue

**Feature Branch**: `015-camera-fab-scoping`

**Created**: 2026-08-10

**Status**: Draft

**Input**: #1397 — "LayoutComposition and CameraCatalog have no fab, so four hub frames still broadcast to every fab"

## Why this exists

Spec 013 gave a rule a fab. Spec 014 gave a system variable one. A camera —
the most physical thing in the system, a device bolted to a wall in one named
building — still belongs to nobody.

Two consequences follow. Any authenticated operator can list, register, edit
and retire every plant's cameras, because the catalogue has no fab check at
all. And a camera name must be unique across the whole installation, so two
plants cannot both call a camera `line-1-north` even though each has one.

There is a third, less visible: a layout arranges cameras, and #1397 cannot
decide what a layout's fab is while the cameras it binds have none. This
feature is the prerequisite for that.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Two plants name their cameras the same (Priority: P1)

Munich and Dresden each have a camera watching the north end of line 1, and
each calls it `line-1-north`, because that is what it is called on the shop
floor.

**Why this priority**: It is the half of the defect that bites without anyone
acting. Today the second plant to register simply cannot, and the workaround —
prefixing every name with the plant — is a convention nobody agreed and
nothing enforces.

**Independent Test**: Register `line-1-north` in both fabs and read both back.

**Acceptance Scenarios**:

1. **Given** `line-1-north` exists in Munich, **When** an operator registers
   `line-1-north` in Dresden, **Then** it is accepted and both exist.
2. **Given** the same, **When** an operator registers `line-1-north` in Munich
   again, **Then** it is refused and the refusal says the name is taken *in
   that fab*.
3. ~~**Given** a camera is retired in one fab, **Then** its name is free for
   re-use in that fab~~ — **withdrawn with FR-003**: nothing retires a camera
   today.

---

### User Story 2 - An operator sees only their own plant's cameras (Priority: P1)

An operator assigned to Dresden lists and edits cameras. Munich's are neither
listed nor reachable, including by guessing a name.

**Why this priority**: P1 alongside US1 rather than below it, because the
catalogue has *no* fab check today — this is not a narrowing of access, it is
the first access control the context has ever had. A camera's RTSP address is
in its record; reaching another plant's camera is reaching its video.

**Independent Test**: As a Dresden-only operator, list cameras and request a
Munich camera by name.

**Acceptance Scenarios**:

1. **Given** cameras exist in both fabs, **When** a Dresden-only operator
   lists them, **Then** only Dresden's appear.
2. **Given** a Munich camera's name, **When** a Dresden-only operator requests
   it, **Then** the response is indistinguishable from a name that was never
   used.
3. **Given** the same, **When** they attempt to edit or retire it, **Then** it
   is refused and the camera is unchanged.

---

### User Story 3 - Registering picks up the operator's plant (Priority: P2)

Registering a camera does not make a single-fab operator state which plant it
is for; an operator holding several must choose.

**Why this priority**: Matches how rules and variables already behave, so an
operator meets one rule across the product rather than three. Independent of
US1 and US2.

**Acceptance Scenarios**:

1. **Given** an operator assigned to one fab, **When** they register a camera
   without naming a fab, **Then** it is created in theirs.
2. **Given** an operator assigned to several, **When** they register one
   without naming a fab, **Then** they are asked to choose and nothing is
   created.
3. **Given** any operator, **When** they name a fab they do not hold, **Then**
   it is refused.

---

### User Story 4 - A downstream context knows a camera's plant without asking (Priority: P2)

A context reacting to a camera event — a stream being provisioned, an audit
row being written — can tell which plant it concerns from the event itself.

**Why this priority**: Below the operator-facing stories because nothing is
broken today that an operator sees. It is in scope because the alternative is
every subscriber calling back into the catalogue to ask, and because
StreamDistribution's own fab scoping is the next spec and would otherwise
start by adding this.

**Acceptance Scenarios**:

1. **Given** a camera is registered, **When** the integration event is
   published, **Then** it carries that camera's fab.
2. **Given** any camera lifecycle event, **Then** the fab travels with it.

---

### Edge Cases

- Cameras that exist before this feature: they belong to the one plant that
  was live, and the number reassigned is stated at the moment it happens
  rather than assumed.
- An operator assigned to no fab: refused rather than shown an empty list, so
  a misconfigured account does not read as "there are no cameras".
- The same name registered in a second fab while the first is live: accepted.
- A name freed by retiring a camera: reusable within that fab only.
- A multi-fab operator whose name matches a camera in two of their own fabs:
  asked which they mean rather than served an arbitrary one.
- A camera reachable at the same RTSP address as one in another fab: out of
  scope. Address collision is a physical-network question, not a fab one.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Every camera MUST belong to exactly one fab.
- **FR-002**: A camera name MUST be unique within a fab, and MUST be usable in
  another fab at the same time.
- **FR-003**: ~~Retiring a camera MUST release its name for reuse within its
  own fab~~ — **withdrawn 2026-08-10.**
  *A camera cannot be retired. `CameraStatus.Decommissioned` exists as a value
  and nothing in the context ever transitions to it; the aggregate has one
  behaviour, `Register`. Discovered at T005, after the requirement was written
  and approved.*
  *The unique index does carry a `status <> 'Decommissioned'` filter, so the
  behaviour will hold the moment a retire behaviour lands — but it is inert
  today and this spec does not deliver it. Adding one would widen spec 015
  past the fab-scoping boundary it was deliberately given. Tracked separately;
  see [research.md](./research.md) §3.*
- **FR-004**: A camera MUST NOT be moved between fabs. Relocating a device
  means registering it afresh.
- **FR-005**: Listing cameras MUST return only those in fabs the caller holds.
- **FR-006**: A camera in a fab the caller does not hold MUST be reported
  exactly as a name that was never used — never as refused-because-forbidden.
- **FR-007**: Registering MUST place the camera in the caller's fab when they
  hold exactly one, and MUST require them to name one when they hold several.
- **FR-008**: Naming a fab the caller does not hold MUST be refused.
- **FR-009**: An operator holding no fab MUST be refused rather than shown an
  empty result.
- **FR-010**: A camera name that resolves in more than one of the caller's own
  fabs MUST be refused as ambiguous, naming the candidates.
- **FR-011**: Cameras existing before this feature MUST be assigned to the
  single fab that was live, MUST end with a fab set, and the number so
  assigned MUST be stated where an operator applying the change will see it.
- **FR-012**: Every camera lifecycle event published to other contexts MUST
  carry the camera's fab.
- **FR-013**: An operator holding several fabs MUST be able to tell two
  same-named cameras apart when reading them, and to choose a fab when
  registering.

### Key Entities

- **Camera** — gains a fab, fixed at registration. Its name is unique only
  within that fab.
- **Fab** — not a stored entity here. A camera names the fab it belongs to;
  the authoritative list of fabs an operator holds lives in identity.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Two plants can each hold a camera of the same name, and each
  reads back its own.
- **SC-002**: An operator assigned to one plant sees, in every listing, only
  cameras of that plant — 100% of rows, with no exception for any camera
  state.
- **SC-003**: A request for another plant's camera is indistinguishable from a
  request for a name that never existed, compared field by field rather than
  by status alone.
- **SC-004**: An operator registering a camera is asked which plant it belongs
  to only when they are responsible for more than one.
- **SC-005**: Every camera that existed before the change ends with a fab, and
  the count attributed is visible to whoever applies it.
- **SC-006**: A context consuming camera events can determine the plant from
  the event alone, without a further request.

## Assumptions

- The one fab live before this feature is `munich`, consistent with spec 013
  and spec 014's backfills. A deployment that predates those in another plant
  is out of scope and is the case the announced count exists to surface.
- Fab resolution reuses the mechanism ADR-0114 already defines and specs 013
  and 014 already apply. This feature adds no resolution mechanism and no new
  way for an operator to express which plant they mean.
- A camera's fab is assigned from the operator registering it, not derived
  from any physical location attribute. No such attribute exists today, and
  inventing one to derive a fab would be a larger change with its own
  correctness question.
- Scope is the camera catalogue alone. Streams inheriting their camera's fab,
  and layouts deriving theirs from the cameras they bind (#1397), are
  separate features that depend on this one and do not belong in it.
