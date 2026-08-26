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
   **When** it is opened, **Then** it shows a stream that names which camera it
   is.

---

### User Story 2 — There is more than one plant (Priority: P2)

A second scenario exists — a **packaging / palletising line** — with its own
assets, overlays, sensors and wall, and it looks like a different place from the
rolling mill.

Packaging rather than a second steel process on purpose: conveyors, robots and
wrapping look nothing like hot billet, and its sensors are counts, rates and jams
rather than temperature and force. A scenario that reused the mill's sensor kinds
would prove the file format takes a second entry, not that the simulator supports
a second *kind* of plant.

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

### User Story 3 — A bulk camera is identifiably itself (Priority: P3)

A camera that belongs to no scenario still shows something, and that something
identifies which camera it is: the shared clip with its name burnt in and a
per-camera colour shift.

**Why this priority**: it is the cheapest way to make any camera answerable by
looking, and it needs no new footage. The tinted-clip form was chosen over an
obviously-synthetic test pattern, which means a screenshot of a bulk camera looks
like plant footage when it is not — worth knowing before one ends up in a slide.

The population is also much smaller than when this was first written: the e2e
suite now retires the cameras it registers (issue 1895), which took the dev
catalogue from 110 rows to 7. Bulk cameras are hand-registered ones and the
handful a run creates, not a hundred of accumulated residue.

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
- What happens to CPU when `-c copy` stops applying? Burning a label into each
  stream forces a re-encode, and `-c copy` exists precisely to avoid that. At the
  ~20 cameras FR-010 sets this is affordable; the number is what makes it so, and
  it is the assumption to revisit if the target ever moves.
- What happens to the four cameras that exist today, whose paths are already
  provisioned against the shared clip?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Two simulated cameras MUST NOT show the same footage.
- **FR-002**: A scenario asset MUST determine what its camera shows.
- **FR-003**: A camera's stream MUST carry enough identity to name the camera by
  looking at it.
- **FR-004**: A camera belonging to no scenario MUST still stream, showing the
  shared clip with its own name burnt in and a per-camera colour shift.
- **FR-005**: A second scenario MUST exist, describing a packaging / palletising
  line — a different kind of plant, not a second steel process.
- **FR-006**: A scenario MUST be addable without changing simulator code.
- **FR-007**: An asset naming a missing clip MUST fail where it is seeded, with
  the asset and clip named. It MUST NOT provision a path that never becomes
  ready.
- **FR-008**: Re-provisioning an existing path with a changed source MUST take
  effect, or MUST say it did not.
- **FR-009**: Real clips MUST each carry an attribution/licence entry, following
  `sim-loop.ATTRIBUTION.txt`.
- **FR-010**: The dev stack MUST carry **~20** simulated cameras — the two
  scenarios' assets and a few spares — with every one of them streaming at once.
  250 is **not** this feature's target: it belongs to the load-realism feature
  ruled out below. A re-encode per stream is therefore affordable, and burning a
  label into each is a design option rather than a cost to engineer around.
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
- **~20 cameras is the dev target** (FR-010), which is what makes a per-stream
  re-encode affordable. Every cost argument in this spec rests on that number.
- Existing scenario mechanics (sensor behaviours, MQTT timeline, wall seeding)
  are reused unchanged.

## Out of scope

- Streams at production scale, or varied encoding parameters for load.
- Cameras that fail, degrade, or reconnect.
- Anything in `apps/` — no viewer, layout or overlay code changes.
- Cleaning up the ~106 accumulated test cameras (issue 1895) or the 50-row list
  cap that hides the working ones (issue 1894). Both are neighbours of this
  problem and neither is this feature.

## Clarified

- **Dev-box target: ~20 cameras**, scenario assets and a few spares. 250 belongs
  to load realism, which is out of scope. FR-010.
- **Second plant: a packaging / palletising line.** Chosen for contrast — its
  look and its sensor kinds both differ from the mill's. FR-005.
- **Bulk cameras: the shared clip, labelled and tinted per camera.** FR-004.

## Still open — and it blocks Phase 2

**How many real clips, of what, and who sources them?**

This is the one question the spec cannot answer for itself: **I cannot source
video.** Someone has to supply the files, and each needs an attribution/licence
entry (FR-009).

It matters most for the packaging line. The rolling mill has footage; a
packaging scenario dressed in rolling-mill footage would be a second plant in
name only, which defeats US2.

Three ways forward, and the plan should not start until one is chosen:

1. **Supply clips** — one per packaging asset, or one shared packaging clip
   tinted per asset. Best result; needs sourcing and licence checks; each clip is
   ~5 MB against a repo that currently holds one.
2. **Derive from the existing clip** — crop, zoom, tint and label regions of the
   mill footage per asset. Ships with no new binaries and no licence question,
   but the packaging line would still *look* like a rolling mill, so US2 is only
   half met and the spec should say so rather than quietly settle.
3. **Synthetic for packaging only** — generated scenes for the new plant, real
   footage for the mill. Honest, visually distinct, and unblocked; least
   convincing in a demo.

**If no answer arrives, 2 is the default** — it is the only option that can be
built today — and the spec would then be amended to state that US2 is partially
met, rather than claiming a variety it did not deliver.

## Verification note

The failure this fixes is visual, so the check is too — and this feature should
say so as plainly as spec 043 had to.

An automated check can assert that two cameras receive different source
commands, that every asset resolves a clip that exists, and that a missing clip
is refused. **It cannot assert that a wall looks like four cameras.** That is a
person, and the plan should budget for it rather than let a green suite imply it.
