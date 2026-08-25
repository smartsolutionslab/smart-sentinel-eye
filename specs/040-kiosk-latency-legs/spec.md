# Feature Specification: Two latency legs stop being exempt, and start being watched

**Feature Branch**: `040-kiosk-latency-legs`

**Created**: 2026-08-25

**Status**: Draft

**Issue**: 1714 *(written without a `#` deliberately — this repo's automation
closes a merely-mentioned issue on merge)*

**Input**: Two of the six latency legs are built and recorded as unbuilt. The
record exempts them from an obligation every implemented leg carries. Correct the
record, then carry the obligation.

---

## Why this exists

The constitution states an end-to-end promise — an event on the plant floor
reaching a kiosk screen within 800 ms — and splits it into six legs. Beside the
budget it keeps a second table: which legs are **built**, and which are
**watched**. That table exists because the rule attached to it is conditional.
A leg that exists must be watched, and a leg that does not exist yet is not yet
subject.

The table also carries a warning about itself:

> *"Keep this table current: a leg left recorded as unbuilt after it is built
> would exempt itself from §VII by clerical error."*

**That is what happened.** Two legs — the kiosk decoding video, and the overlay
being composited onto it — are recorded as not built. Both are built. They have
been carrying no obligation for as long as the record has been wrong.

### Where the error came from, and why it spread

A measurement feature looked for video in the kiosk's own directory, found none,
and concluded the kiosk decodes nothing. The kiosk does decode: it renders a
**shared** component that owns the video, and that component also draws the
overlay on top of the live frame. The search was scoped to one directory; the
capability lives one directory over.

From that one note the claim reached the constitution's table, the repository's
own guide, and the issue asking for the work. **Four documents agree with each
other and none of them agrees with the code.** Nothing noticed, because a leg
recorded as unbuilt raises no obligation to check.

### What correcting it costs

This is not bookkeeping. The moment the table is right, two legs move from *not
yet subject* to *cannot ship further work without being watched* — and this
feature inherits an obligation it did not create. That is the rule working as
designed: the obligation attaches to whoever establishes the leg exists.

Saying so plainly matters, because the alternative reading — correct the table,
call it a documentation fix, move on — leaves the same gap with better paperwork.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — The record says what is true (Priority: P1)

Someone asks which parts of the 800 ms path exist. Every document they might
reach agrees, and agrees with the code. No leg is described as unbuilt while its
code path runs on every kiosk in the fab.

**Why this priority**: Everything else in this spec follows from it. Until the
record is right, the two legs raise no obligation and there is nothing to
discharge.

**Independent Test**: Every statement about which legs are built matches what
runs.

**Acceptance Scenarios**:

1. **Given** the kiosk decodes video and composites overlays onto it,
   **When** the record of which legs are built is read,
   **Then** both legs are recorded as **built**.
2. **Given** the record is corrected in one place,
   **When** the other places that repeat the claim are read,
   **Then** they agree — the claim reached more than one document and the
   correction must reach all of them.
3. **Given** the presentation buffer is genuinely not built,
   **When** the record is read,
   **Then** it still says so, and still points at the work that would build it.

---

### User Story 2 — Each leg has a number of its own (Priority: P1)

Someone asking whether the kiosk is inside its budget gets two answers, because
there are two budgets. How long the picture takes to arrive is a different
question from how long it takes to draw the overlay onto it, and a single figure
answers neither.

**Why this priority**: Equal to US1. A leg that is watched by a number covering
two legs is not watched; it shares a number with something else, and either can
breach while the total looks healthy.

**Independent Test**: Two figures exist, each attributable to one leg.

**Acceptance Scenarios**:

1. **Given** a kiosk showing live video with an overlay,
   **When** the timings are examined,
   **Then** there is a figure for the picture arriving and a separate figure for
   the overlay being drawn.
2. **Given** those two figures,
   **When** either is compared against its budget,
   **Then** the comparison is meaningful without knowing the other.

---

### User Story 3 — A journey nobody timed produces no number (Priority: P1)

A kiosk starts up, loses its stream, or sits on a tab nobody is looking at. No
timing is invented for any of it.

**Why this priority**: Equal to the others, because the failure it prevents is
worse than having no measurement at all. A zero recorded for an untimed journey
reads as a perfect score, and a budget that reports perfection for the cases it
could not observe is worse than one that reports nothing.

**Independent Test**: A leg with no observable start records nothing — asserted as
an absence, not as a zero.

**Acceptance Scenarios**:

1. **Given** a leg whose start cannot be established,
   **When** its end occurs,
   **Then** **nothing** is recorded — not zero.
2. **Given** an elapsed time that is negative or implausibly large,
   **When** it would be recorded,
   **Then** it is not, because it describes a clock rather than a journey.
3. **Given** a kiosk whose page has been backgrounded,
   **When** it returns,
   **Then** no timing spanning the gap is recorded.

---

### User Story 4 — Someone can look at the numbers (Priority: P2)

An engineer asking "is the kiosk inside its budget?" opens the place the answer
lives and reads it. They do not attach a debugger, add a log line, or take
somebody's word for it.

**Why this priority**: P2 because US1–US3 must land first — there is nothing to
read until the numbers exist and are trustworthy. But **readable is the point**:
the obligation is that a leg is *watched*, and a number nobody can see is not
being watched. This is where an earlier leg stopped, and its state is recorded
as **half** discharged rather than rounded up.

**Independent Test**: Both figures can be read by a person, in the environment
where such things are read.

**Acceptance Scenarios**:

1. **Given** a kiosk that has been showing video,
   **When** an engineer looks where telemetry is read,
   **Then** both figures are there, distinguishable, and current.
2. **Given** the same,
   **When** they look,
   **Then** they need no special build, no debugger and no code change to see it.

---

### Edge Cases

- **No stream.** A kiosk whose camera is unavailable renders no picture. Nothing
  to time; nothing recorded. Covered by US3.
- **The stream drops and recovers.** A recovery is a new journey, not a
  continuation of the interrupted one; timing it as one would report a figure
  spanning an outage.
- **A backgrounded tab.** Browsers throttle work in tabs nobody is looking at, so
  a figure measured across that gap describes the throttling. A fab kiosk is
  always foreground, which makes this rare rather than impossible — and rare
  wrong numbers are harder to notice than common ones.
- **Several tiles on one wall.** A wall shows several cameras at once. Whether
  the figures are per-tile or per-wall determines what a breach means, and the
  answer must be deliberate rather than incidental.
- **The environment where nothing can be read.** Telemetry reaches one place per
  environment, and for production that place is deliberately not chosen yet. This
  feature can only satisfy the obligation where the place exists.
- **Whether the legs can be exercised automatically at all.** Both need a real
  video stream. If an automated check cannot produce one, the honest answer is
  that the measurement is verified by a person following a written procedure —
  and saying so is better than an automated check that asserts nothing.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The record of which legs are built MUST say **built** for the leg
  where the kiosk decodes video and for the leg where the overlay is composited
  onto it.
- **FR-002**: Every document repeating that claim MUST be corrected. The claim
  reached four places; a correction reaching one leaves three disagreeing with
  the code.
- **FR-003**: The record MUST continue to say the presentation buffer is not
  built, and continue to point at the work that would build it.
- **FR-004**: The correction MUST record **why the error occurred** — a search
  scoped to one directory when the capability lived in another — because the
  mechanism is more reusable than the correction.
- **FR-005**: The kiosk MUST produce a figure for **how long the picture takes to
  arrive**, attributable to that leg alone.
- **FR-006**: The kiosk MUST produce a figure for **how long the overlay takes to
  be drawn onto the picture**, attributable to that leg alone.
- **FR-007**: The two figures MUST be distinguishable from one another. A single
  combined figure satisfies neither budget.
- **FR-008**: When a leg's start cannot be established, **nothing** MUST be
  recorded for it. Not a zero — a zero is indistinguishable from a perfect
  journey.
- **FR-009**: An elapsed time that is negative, or large enough to describe a
  suspended page rather than a journey, MUST NOT be recorded.
- **FR-010**: Both figures MUST be **readable by a person** in the environment
  where telemetry is read, without a debugger, a special build or a code change.
- **FR-011**: Nothing about what the kiosk **does** may change. This feature
  observes; it does not alter the picture, the overlay, or the connection behind
  either.
- **FR-012**: The measurement MUST NOT itself consume a meaningful share of the
  budget it measures. A 50 ms budget cannot afford an expensive observer.

### Key Entities

- **Leg**: one segment of the end-to-end path, with a budget and a recorded state
  — built or not, watched or not. Six exist; this feature concerns two.
- **The record**: the table stating which legs are built. Load-bearing rather
  than descriptive: the obligation to watch a leg is conditional on it.
- **Picture-arrival figure**: how long a decoded frame takes to reach the screen
  after the streaming service sends it. Budget: 120 ms.
- **Overlay-draw figure**: how long the overlay takes to be composited onto the
  frame. Budget: 50 ms.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: **Zero** legs are recorded as unbuilt while their code path exists.
- **SC-002**: Every document stating which legs are built agrees with every other
  and with the code.
- **SC-003**: Two figures exist, each attributable to exactly one leg, and each
  comparable against its own budget without reference to the other.
- **SC-004**: A journey whose start is unknown produces **no** figure —
  demonstrated by the absence of a recording, not by a recording of zero.
- **SC-005**: An engineer can read both figures in under a minute, without a
  debugger, a special build or a code change.
- **SC-006**: The kiosk behaves exactly as before: same picture, same overlay,
  same reconnection behaviour.
- **SC-007**: The state of every leg after this feature is stated explicitly,
  including which remain unwatched and why. A leg silently rounded up to
  "watched" would repeat the failure this feature exists to correct.

---

## Assumptions

- **This feature inherits an obligation it did not create, and accepts it.**
  The rule says the obligation attaches to whichever work establishes a leg
  exists. Correcting the record establishes it. The alternative — correct the
  table and file the measurement separately — was considered and rejected: it
  converts a hidden gap into a known one and calls that progress.
- **"Watched" means readable, not merely recorded.** An earlier leg reached the
  recorded-but-unreadable state and the record calls it **half** discharged. This
  feature targets the whole obligation. If it lands half-discharged too, the
  record must say so in the same words rather than rounding up — see SC-007.
- **Readable means readable where telemetry is read today.** One environment has
  a place for it; production deliberately does not yet. This feature satisfies
  the obligation where it can be satisfied and states plainly where it cannot.
- **The budgets themselves are not in question.** 120 ms and 50 ms are settled;
  this feature measures against them rather than revisiting them. A figure that
  turns out to breach its budget is a finding to raise, not a reason to move the
  line.
- **Automated verification may not be possible for either leg**, because both
  need a real video stream and automated environments have none. A written
  procedure a person follows is an acceptable answer; an automated check that
  passes without exercising the measurement is not.
- **No new promise to operators.** Nothing an operator sees changes.

---

## Out of Scope

- **The presentation buffer leg.** Genuinely unbuilt, stays filed, and its row
  keeps saying so.
- **Anything the kiosk does.** The connection, the picture, the overlay and the
  reconnection behaviour are untouched (FR-011).
- **A production place to read telemetry.** Deliberately deferred until there is
  a production deployment.
- **The remaining half of the event-to-overlay leg**, which has its own issue.
- **The camera-to-streaming-service leg**, which is measured already and has its
  own gap.
- **Revisiting any budget.** See Assumptions.
