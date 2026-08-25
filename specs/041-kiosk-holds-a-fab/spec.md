# Feature Specification: The kiosk holds a fab, and holds only what a kiosk needs

**Feature Branch**: `041-kiosk-holds-a-fab`

**Created**: 2026-08-25

**Status**: Draft

**Issue**: 1884 *(written without a `#` deliberately — this repo's automation
closes a merely-mentioned issue on merge)*

**Input**: A kiosk cannot display anything: the identity it signs in with carries
no plant, so every plant-scoped read is refused. The identity built for it —
carrying the plant, and carrying only what a kiosk needs — already exists and has
never been used.

---

## Why this exists

A kiosk is the screen on a fab wall. Sign in to one in the development stack and
it says **"Could not load layouts."** and stays there. It cannot list a wall, so
it cannot show one. Not a slow path or a flaky one: it has never worked.

The reason is one line of configuration. An operator's identity says which plants
they work in, and every read is scoped to those plants. The identity the kiosk
signs in with **does not carry that information at all** — so the kiosk holds no
plants, and a system that correctly refuses reads outside your plants refuses all
of them.

Signed in as the same person, at the same moment, the management app lists those
same layouts without trouble. The two apps use different identities, and only one
of them was set up to carry a plant.

### The identity it should have been using already exists

There is a second kiosk identity in the configuration, created for exactly this
and described as *replacing* the one in use. It carries the plant. It also
carries a **deliberately narrow** set of permissions — the same set granted to
every physical kiosk device the system enrols.

Nothing in the product has ever pointed at it.

### So this is a security improvement as well as a fix

The identity in use carries a broad **management** permission — the kind an
administrator holds. A kiosk only ever reads: it shows walls, cameras, overlays
and values. It has been carrying the authority to change all of them, on a screen
bolted to a factory wall, and using none of it.

Moving to the intended identity removes that authority and leaves exactly what a
kiosk needs. **The browser kiosk is currently the only kiosk in the system not
holding the kiosk permission set.**

### Why nobody noticed

The automated check that signs into a kiosk accepts *"could not load layouts"* as
one of its passing outcomes. A kiosk that can never show a wall looks, to that
check, exactly like a working one.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — A kiosk shows a wall (Priority: P1)

Someone signs into a kiosk in the development stack, picks a published layout,
and the wall appears. This is the first time that has been possible.

**Why this priority**: It is the defect. Everything else here either narrows what
the kiosk may do, or stops the failure hiding again.

**Independent Test**: Sign in, pick a layout, see the wall.

**Acceptance Scenarios**:

1. **Given** an operator who works in a plant, and a layout published there,
   **When** they sign into a kiosk and pick that layout,
   **Then** the wall opens and its tiles render.
2. **Given** the same operator,
   **When** the kiosk lists layouts,
   **Then** it lists the ones from their plant — asserted as **a wall appearing**,
   not as the absence of an error. An empty list raises no error either, and an
   empty list is exactly what an identity without a plant produces.
3. **Given** the kiosk's identity,
   **When** it is inspected,
   **Then** it carries the operator's plant.

---

### User Story 2 — A kiosk carries only what a kiosk needs (Priority: P1)

The identity a kiosk signs in with grants reading walls, cameras, overlays and
values, and nothing else. It does not grant the authority to change them.

**Why this priority**: Equal to US1, and independent of it. A fix that made the
kiosk work while leaving it holding administrative authority would close a defect
and keep a weakness — and the weakness is on the least physically secure surface
in the product.

**Independent Test**: The kiosk's identity does not carry the broad management
permission.

**Acceptance Scenarios**:

1. **Given** a kiosk that has signed in,
   **When** its identity is inspected,
   **Then** it does **not** carry the broad management permission.
2. **Given** the same identity,
   **When** its permissions are compared with the set granted to an enrolled
   kiosk device,
   **Then** they are the same set.
3. **Given** everything a kiosk actually does,
   **When** it runs with the narrowed permissions,
   **Then** nothing it does is refused.

---

### User Story 3 — The failure cannot hide again (Priority: P1)

The check that signs into a kiosk fails when the kiosk cannot show a wall.

**Why this priority**: Equal to the others. This defect has existed since the
kiosk did, and the only reason it survived is a check that could not tell working
from broken. Fixing the kiosk without fixing the check leaves the next one to
survive the same way.

**Independent Test**: Break the kiosk's identity again and the check goes red.

**Acceptance Scenarios**:

1. **Given** a kiosk that can show a wall,
   **When** the check runs,
   **Then** it passes.
2. **Given** a kiosk whose identity carries no plant,
   **When** the check runs,
   **Then** it **fails** — demonstrated by causing it, not by assuming it.

---

### User Story 4 — The disused identity is gone (Priority: P2)

The identity the kiosk used to sign in with no longer exists. It was described as
replaced; now it is.

**Why this priority**: P2 because nothing depends on it. But it is a sign-in
identity carrying administrative authority that nothing uses, and leaving it is
how the next reader ends up choosing the wrong one — which is precisely what
happened here.

**Independent Test**: The identity is absent from the configuration and nothing
refers to it.

**Acceptance Scenarios**:

1. **Given** the change is complete,
   **When** the configuration is read,
   **Then** the old kiosk identity is absent.
2. **Given** the same,
   **When** the product is searched for its name,
   **Then** nothing outside historical records refers to it.

---

### Edge Cases

- **An operator who works in no plant.** Their kiosk lists nothing, which is
  correct, and must remain distinguishable from today's failure — one is an empty
  list, the other is an error.
- **An operator who works in several plants.** They see layouts from all of them.
  Unchanged by this, but worth confirming it stays true.
- **A layout published in a plant the operator does not work in.** Still refused.
  Narrowing what the kiosk may *do* must not widen what it may *see*.
- **Anything the kiosk does that the narrowed permissions do not cover.** Would
  break. Every call the kiosk makes must be checked against the narrowed set
  before this ships — including the newest one, which reports display timings.
- **A kiosk deployed against the old identity.** Would stop signing in. There is
  no production deployment, so this coordinates with nothing — stated here so a
  reader does not have to wonder.
- **The two permission sets drifting apart later.** They agree today because two
  people wrote the same list. Nothing checks that they stay agreed.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A kiosk's identity MUST carry the plants its operator works in.
- **FR-002**: A kiosk MUST be able to list published layouts for those plants and
  open one.
- **FR-003**: A kiosk's identity MUST NOT carry the broad management permission.
- **FR-004**: A kiosk's permissions MUST be the same set granted to an enrolled
  kiosk device. There is one notion of what a kiosk may do, and a browser kiosk
  is not a second one.
- **FR-005**: Every action the kiosk performs MUST be permitted by that set. If
  one is not, that is a **finding to raise** — either the action does not belong
  on a kiosk, or the set is wrong — and **not** a reason to widen the set
  quietly.
- **FR-006**: The kiosk MUST request no permission its identity does not hold.
- **FR-007**: The check that signs into a kiosk MUST **fail** when the kiosk
  cannot show a wall. Accepting an error as a passing outcome is what let this
  survive.
- **FR-008**: The previously-used kiosk identity MUST be removed, and nothing
  outside historical records may refer to it.
- **FR-009**: Something MUST detect the kiosk's permissions drifting from the
  enrolled-device set. They agree today by coincidence rather than by
  construction.
- **FR-010**: Any document stating which identity the kiosk uses MUST be
  corrected. At least one tells a reader the wrong one.
- **FR-011**: Nothing about what the kiosk *does* may change — same walls, same
  video, same overlays, same reconnection. This changes who it signs in as.

### Key Entities

- **Kiosk identity**: what a kiosk signs in as. Determines both which plants it
  can see and what it may do in them. Two exist; the product uses the wrong one.
- **Plant claim**: the piece of an identity naming which plants its holder works
  in. Absent from the identity in use, which is the whole defect.
- **Kiosk permission set**: the canonical list of what a kiosk may do — read
  walls, cameras, overlays and values, and report events. Already granted to
  every enrolled kiosk device.
- **Management permission**: broad authority to change things. Currently held by
  the browser kiosk; held by no other kiosk.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A kiosk in the development stack opens a wall — demonstrated by the
  wall, not by the absence of an error.
- **SC-002**: **Zero** kiosks hold the broad management permission.
- **SC-003**: The browser kiosk's permissions and an enrolled device's are the
  same set, compared as sets rather than sampled.
- **SC-004**: A kiosk that cannot show a wall **fails** a check — demonstrated by
  causing it.
- **SC-005**: **Zero** references to the removed identity outside historical
  records.
- **SC-006**: Nothing the kiosk does is refused after the narrowing.
- **SC-007**: The kiosk behaves exactly as before in every respect other than who
  it signs in as.

---

## Assumptions

- **The intended identity is correct as it stands.** It was created for this,
  carries the plant, and its permissions already match the enrolled-device set
  exactly. This feature points the product at it rather than redesigning it. If
  it turns out to be wrong, that is a finding rather than a licence to edit it
  here.
- **Removing the old identity coordinates with nothing.** There is no production
  deployment — the same reason the production telemetry sink is deferred — so
  nothing is signed in against it anywhere but a developer's machine, where the
  configuration is re-imported on every start.
- **The narrowed permissions cover everything the kiosk does today**, including
  the display-timing report added most recently. This was checked rather than
  assumed, and FR-005 says what to do if a gap appears.
- **Least privilege is the point, not a side effect.** A kiosk is the least
  physically secure surface in the product — a screen on a wall in a building
  with visitors. It should hold the least, and it currently holds the most.
- **The management app is not in scope**, even though it requests a permission
  its identity may not carry. Worth someone's attention; not this change's.

---

## Out of Scope

- **The management app's identity**, and the permission it requests. Noted, not
  touched.
- **Enrolment of physical kiosk devices**, which already grants the right set.
- **Anything the kiosk does once it can show a wall.**
- **The display-timing figures** this unblocks. They belong to the feature that
  measures them.
- **Any production rollout.** There is no production deployment.
