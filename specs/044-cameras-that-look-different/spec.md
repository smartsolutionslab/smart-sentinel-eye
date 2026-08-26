# Feature Specification: Simulated cameras that look like different cameras

**Feature Branch**: `044-cameras-that-look-different`

**Created**: 2026-08-26

**Status**: Draft

**Input**: User description: "we need more different simulation cameras streams more realistic"

---

## Why this exists

Every simulated camera plays the same file.

`CameraSimProvisioner` provisions each `camera-sim` path with one hard-coded
command — `ffmpeg -stream_loop -1 -re -i /media/sim-loop.mp4 -c copy` — so the
`rolling-mill` 2×2 wall shows four copies of one clip. There is one scenario
(4 assets) against a 250-camera target.

The cost is not cosmetic. A wall of identical tiles cannot demonstrate a layout,
because every arrangement looks the same. It cannot demonstrate an overlay,
because the thing underneath carries no identity. And it cannot be checked by a
person, because "is this the right camera?" has no visible answer.

That last one matters here more than usual. **A picture appearing is the one
claim no automated check in this repo can make** — spec 043 established that in
writing, and this feature is what a person looks at when making it.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — A wall shows several different cameras (Priority: P1)

An engineer opens a simulated wall and sees tiles that are visibly different
from one another. Which tile is which is answerable by looking.

**Why this priority**: It is the whole complaint, and it is the smallest thing
that makes layouts, overlays and highlights demonstrable. Everything else here
is depth behind it.

**Independent Test**: Open the `rolling-mill` wall with nothing else from this
feature built. Four tiles, four distinguishable pictures.

**Acceptance Scenarios**:

1. **Given** the rolling-mill scenario is seeded, **When** an engineer opens its
   wall, **Then** no two tiles show the same footage.
2. **Given** a tile is showing, **When** the engineer looks at it alone, **Then**
   they can tell which named asset it is without consulting the layout.
3. **Given** a camera with no scenario asset (a hand-registered or test camera),
   **When** it is opened, **Then** it shows something identifiably itself and
   identifiably a simulation.

---

### User Story 2 — There is more than one plant (Priority: P2)

A second scenario exists, with its own assets, overlays, sensors and wall, and
looks like a different place from the rolling mill.

**Why this priority**: One scenario cannot show that scenarios are a capability.
Until there are two, every mechanism is indistinguishable from a hard-coded
rolling mill — which is exactly how the single shared clip got there.

**Independent Test**: Seed both scenarios; open each wall. Two plants, not one
plant twice.

**Acceptance Scenarios**:

1. **Given** both scenarios are seeded, **When** an engineer lists cameras,
   **Then** each scenario's assets are present and named for their own plant.
2. **Given** the second scenario's wall, **When** it is opened, **Then** its
   tiles, overlays and sensor behaviour describe that plant, not the mill's.

---

### User Story 3 — A bulk camera is honestly a simulation (Priority: P3)

A camera that belongs to no scenario still shows something, and that something
does not pretend to be footage of a real machine.

**Why this priority**: There are ~106 such cameras today (issue 1895) and there
will be more. They should be legible, not misleading — a bulk camera that looked
like real plant footage would be worse than one that obviously does not.

**Independent Test**: Register a camera by hand and open it.

**Acceptance Scenarios**:

1. **Given** a camera registered outside any scenario, **When** it is opened,
   **Then** it shows a stream carrying its own identity.
2. **Given** two such cameras, **When** both are open, **Then** they are
   distinguishable from each other.

---

### Edge Cases

- What happens when an asset names a clip that is not present? A path that
  provisions and never becomes ready looks exactly like a broken camera —
  the failure must name itself at seed time, not at watch time.
- What happens on a worker restart, when the path already exists with the *old*
  command? Today's provisioner treats "already exists" as success and returns,
  so a changed clip would silently not take effect.
- What happens at 250 cameras if each stream is re-encoded rather than copied?
  CPU on a dev box is a real constraint; the current `-c copy` exists for that
  reason.
- What happens to the four cameras that exist today, whose paths are already
  provisioned against the shared clip?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Two simulated cameras MUST NOT show the same footage.
- **FR-002**: A scenario asset MUST determine what its camera shows.
- **FR-003**: A camera's stream MUST carry enough identity to name the camera by
  looking at it.
- **FR-004**: A camera belonging to no scenario MUST still stream, and MUST be
  visibly a simulation.
- **FR-005**: A second scenario MUST exist, describing a different plant.
- **FR-006**: A scenario MUST be addable without changing simulator code.
- **FR-007**: An asset naming a missing clip MUST fail where it is seeded, with
  the asset and clip named. It MUST NOT provision a path that never becomes
  ready.
- **FR-008**: Re-provisioning an existing path with a changed source MUST take
  effect, or MUST say it did not.
- **FR-009**: Real clips MUST each carry an attribution/licence entry, following
  `sim-loop.ATTRIBUTION.txt`.
- **FR-010**: The dev stack's CPU cost MUST NOT scale with a per-stream re-encode
  at the 250-camera target. [NEEDS CLARIFICATION: is 250 a real dev-box target
  for this feature, or is the dev target smaller — say 20 — with 250 reserved
  for a load-realism feature that is explicitly out of scope here?]
- **FR-011**: Everything the simulator already does — sensors, overlays,
  highlights, walls, the MQTT timeline — MUST behave as before.

### Key Entities

- **Scenario**: a plant. Owns assets, a wall, and a timeline. One exists
  (`rolling-mill`); the feature adds a second and the means to add more.
- **Asset**: a named camera position within a scenario. Already carries
  `Camera { Path, Loop }`, an overlay, a highlight rule and sensors. Gains
  whatever names its picture.
- **Clip**: a video source. Today a single shared file; becomes a set, with
  attribution, plus a generated form for cameras with no asset.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On the rolling-mill wall, **zero** pairs of tiles show identical
  footage.
- **SC-002**: A person shown a single tile can name which asset it is, without
  the layout, on the **first** attempt.
- **SC-003**: **Two** scenarios are seedable, and adding a third needs no
  simulator code change.
- **SC-004**: **Zero** cameras in the catalogue stream nothing because their
  clip was missing — a missing clip is refused at seed time instead.
- **SC-005**: Every real clip in the repo has an attribution entry. **Zero**
  unattributed binaries.
- **SC-006**: Everything in the current scenario — sensors, overlays, highlights,
  wall — behaves as it does today.

## Assumptions

- **Dev-only.** `camera-sim` and `scenario-simulator` sit inside
  `if (isRunMode && !isE2ETests)`; nothing here runs in CI, E2E or production,
  and this feature does not change that.
- **Load realism and failure injection are out of scope**, by decision rather
  than oversight. Varied resolution/bitrate at 250 concurrent streams, and
  cameras that drop out or degrade to exercise the health sweep, are each their
  own feature.
- **Repo size is a real constraint.** The existing clip is 5.5 MB; "a small set"
  of real clips means single digits, not one per camera.
- Existing scenario mechanics (sensor behaviours, MQTT timeline, wall seeding)
  are reused unchanged.

## Out of scope

- Streams at production scale, or varied encoding parameters for load.
- Cameras that fail, degrade, or reconnect.
- Anything in `apps/` — no viewer, layout or overlay code changes.
- Cleaning up the ~106 accumulated test cameras (issue 1895) or the 50-row list
  cap that hides the working ones (issue 1894). Both are neighbours of this
  problem and neither is this feature.

## Open questions for `/speckit-clarify`

1. **FR-010's target.** How many simulated cameras must a dev box carry?
2. **How many real clips, and of what?** Single digits, and who sources them.
3. **What does a bulk camera show** — the shared clip labelled and tinted, or an
   obviously synthetic source?
4. **Which second plant?** The choice decides the assets, sensors and overlays.

## Verification note

The failure this fixes is visual, so the check is too — and this feature should
say so as plainly as spec 043 had to.

An automated check can assert that two cameras receive different source
commands, that every asset resolves a clip that exists, and that a missing clip
is refused. **It cannot assert that a wall looks like four cameras.** That is a
person, and the plan should budget for it rather than let a green suite imply it.
