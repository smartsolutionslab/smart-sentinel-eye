# Feature Specification: An operator can watch a camera

**Feature Branch**: `043-operator-watches-camera`

**Created**: 2026-08-26

**Status**: Draft

**Issue**: 1886 *(written without a `#` deliberately — this repo's automation
closes a merely-mentioned issue on merge)*

**Input**: The operator console can list cameras, register them, rename them,
correct their addresses and retire them. It cannot show one. The component that
would has existed since spec 002, passes three unit tests, and is mounted
nowhere.

---

## Why this exists

Open a camera in the operator console and you get its name, its fab, its address,
when it was registered and its status. **You cannot see what it sees.**

This is a camera management system for a 24/7 fab. An operator who has just
corrected a camera's address has no way to check whether the picture came back.

### The viewer exists and nothing renders it

`CameraViewerPanel` was written for spec 002 and is imported by no page. Its
three unit tests pass — it renders nothing when no camera is selected, it mounts
the viewer when one is, it closes when asked. All green, on a component no
operator can reach.

**That is why this went unnoticed.** To anyone grepping the repository for
whether the console shows video, the answer reads yes.

### And it would not have worked if it were mounted

Its token getter is a placeholder that reads a browser-storage key **nothing in
the product ever writes**, with a comment saying the real wiring "lands when
react-oidc-context is added to the app shell". That landed long ago; the app
shell has supplied the operator's real token for months. So the viewer would have
mounted, asked for a credential, got nothing, and been refused by the media
server — failing in a way indistinguishable from not being there.

### Spec 002 described a flow that was never built

Its scenario reads *"Click the camera row. A viewer panel opens and the live
video is…"*. The component's shape follows: a panel pinned to the right edge,
with a Close button.

**The camera page did not exist then.** It arrived with spec 029 and now carries
rename, address correction, retirement and the camera's record. That is where an
operator already goes to deal with one camera, so that is where the picture
belongs — as part of the page, not as something to dismiss.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — An operator sees what a camera sees (Priority: P1)

An operator opens a camera and the live picture is there, on the page, with the
rest of what is known about it.

**Why this priority**: it is the feature. Everything else here keeps it honest.

**Independent Test**: open a camera; the picture is on the page.

**Acceptance Scenarios**:

1. **Given** a camera that is streaming,
   **When** an operator opens its page,
   **Then** the live picture appears there, without a further click.
2. **Given** the same page,
   **When** the operator reads it,
   **Then** the camera's name, fab, address, registration date and status are all
   still there, unchanged.
3. **Given** a camera whose stream is degraded or unreachable,
   **When** an operator opens its page,
   **Then** the viewer says which — it is a viewer's job to report what it finds,
   and an operator checking on a broken camera is the case this feature is for.
4. **Given** an operator watching one camera,
   **When** they navigate to another camera or away from the page,
   **Then** the first camera's stream is released rather than left running.

---

### User Story 2 — A retired camera does not offer a picture it cannot show (Priority: P1)

A retired camera's page shows no viewer, and says why.

**Why this priority**: the page already works this way for every other control —
a retired camera offers no rename, no address correction, no retirement, and
states the refusal rather than letting the operator discover it on submit.
A viewer that could only ever report failure would break that rule.

**Independent Test**: open a retired camera; there is no viewer, and the page
explains itself.

**Acceptance Scenarios**:

1. **Given** a retired camera,
   **When** an operator opens its page,
   **Then** no viewer is offered.
2. **Given** the same page,
   **When** the operator reads it,
   **Then** it is clear that the camera is retired and that is why there is
   nothing to watch — not left as an unexplained absence.

---

### User Story 3 — A viewer nobody can reach fails a check (Priority: P1)

The check that the console can show video opens a camera and looks, rather than
rendering the component on its own.

**Why this priority**: equal to US1. Three passing tests on an unreachable
component is exactly what the broken state looked like, and it is
indistinguishable from working. Fixing the feature and leaving that style of test
leaves the next one to hide the same way.

**Independent Test**: unmount the viewer from the page; the check goes red.

**Acceptance Scenarios**:

1. **Given** the viewer is on the camera page,
   **When** the check runs,
   **Then** it passes.
2. **Given** the viewer removed from the page but the component still present and
   still unit-tested,
   **When** the check runs,
   **Then** it **fails** — demonstrated by causing it, not by assuming it.
3. **Given** the viewer handed a credential the operator does not actually have,
   **When** the check runs,
   **Then** it **fails**. A viewer wired to a credential nobody issues fails
   exactly like no viewer at all, and passes any check that only asks whether
   something rendered.

---

### Edge Cases

- **A camera in a fab the operator does not work in.** Its page already reads
  exactly as a camera that does not exist. Unchanged: no viewer, because no page.
- **A camera that is registered but has never streamed.** The viewer reports
  what it finds. This is not the retired case — the camera is expected to work,
  and an operator needs to see that it is not.
- **An operator moving between several cameras in a row.** Each stream is
  released as they leave. A console that quietly accumulates connections while an
  operator works through a list is a different kind of broken.
- **A stream that fails while being watched.** The viewer already recovers on its
  own; nothing here changes that.
- **The three existing unit tests.** They describe a panel that opens and closes.
  Half of that behaviour is being removed. Tests that outlive their subject are
  how a component ends up looking supported.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A camera's page MUST show that camera's live picture.
- **FR-002**: The picture MUST be part of the page, not something the operator
  opens and dismisses.
- **FR-003**: The viewer MUST receive the operator's own credential — the same
  one the rest of the console uses.
- **FR-004**: A retired camera MUST offer no viewer, and its page MUST say why.
- **FR-005**: Leaving a camera's page MUST release its stream.
- **FR-006**: A check MUST fail if the viewer is not reachable from the camera's
  page. Rendering the component in isolation does not count as reaching it.
- **FR-007**: A check MUST fail if the viewer is handed a credential the operator
  does not hold.
- **FR-008**: Everything else on the camera's page MUST behave exactly as
  before — the record it shows, and rename, address correction and retirement.
- **FR-009**: The camera list MUST NOT gain a viewer of its own. One place shows
  a camera.
- **FR-010**: Tests describing behaviour this removes MUST go with it rather than
  be left passing.

### Key Entities

- **Camera**: what the console manages. Has a name, a fab, an address, a
  registration date and a status; from now on, also a picture.
- **Camera page**: where an operator deals with one camera. Already holds every
  per-camera action.
- **Viewer**: the shared component that connects to a camera and renders its
  stream, reporting its own state — connecting, live, reconnecting, offline,
  failed. Already used, and working, on the kiosk.
- **Retired camera**: one whose record is kept and whose hardware is gone. Offers
  no actions, and now no picture.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator reaches a camera's live picture in **one** step from
  its page — opening the camera is the whole interaction.
- **SC-002**: **Zero** cameras offer a viewer that cannot serve a picture.
- **SC-003**: A viewer that no page reaches **fails** a check — demonstrated by
  causing it.
- **SC-004**: A viewer handed a credential the operator does not hold **fails** a
  check — demonstrated by causing it.
- **SC-005**: **Zero** streams remain open after an operator leaves a camera's
  page.
- **SC-006**: Every existing camera action behaves as it did.

---

## Assumptions

- **The viewer works.** It is the same component the kiosk renders on every
  tile, and real video was observed through it on a four-tile wall this week.
  This feature reaches it; it does not rebuild it.
- **The operator's credential is sufficient.** The console's identity already
  carries what the media server asks for. Nothing here needs new permission, and
  if that turns out to be wrong it is a finding rather than a licence to widen
  anything.
- **A retired camera has no stream.** Retirement is what happens when the
  hardware is gone.
- **One picture per page is enough.** An operator dealing with one camera wants
  that camera. Watching several at once is what the kiosk is for.
- **No production deployment exists**, so this coordinates with nothing.

---

## Out of Scope

- **Steering the camera** — pan, tilt, zoom. Spec 002 raises it as a later
  concern and it needs a credential bound to the camera, which is a separate
  piece of work.
- **A viewer on the camera list.** One place shows a camera.
- **Anything the kiosk does.**
- **Recording, snapshots or playback.** This is the live picture.
- **Any production rollout.**
