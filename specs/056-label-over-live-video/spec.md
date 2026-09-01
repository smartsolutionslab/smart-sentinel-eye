# Feature Specification: An overlay label over live video, seen and timed

**Feature Branch**: `056-label-over-live-video`
**Created**: 2026-09-01
**Status**: Draft
**Input**: "An overlay label over live video is the product, and nothing in the repository has ever seen one — nor measured the 800 ms span whole."

---

## Why this exists

The system's purpose, stated in one line, is: **a label about the world,
drawn over a live picture of that world, fast enough to still be true.**

Every part of that is built. **No part of it has ever been checked together.**

Two absences, and they are different in kind:

1. **Nothing has ever seen it.** Every automated check that involves an
   overlay runs against a wall whose video does not arrive, and every check
   that involves video runs against a wall with no overlay bound. A tile that
   draws its label *only when the video fails* passes the entire suite as it
   stands.

2. **Nothing has ever timed it.** The six legs of the latency budget are
   instrumented individually. The span the budget is actually about —
   external event arrival to the operator seeing the label — has no
   instrument at all, and the number that would be easiest to produce is one
   that is not allowed to be produced.

This feature closes the first absence and takes an honest run at the second.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The wall does the thing the product is for (Priority: P1)

A fab operator looks at a wall. A camera's picture is moving, and over it sits
a label that reflects the current state of the world — a temperature, a batch
number, an alarm. Both at once, on the same tile, at the same moment.

**Why this priority**: This is the product. It is also the only story here
whose absence is a *silent* one: the current fixtures both pass, and their
passing is what conceals it. Until something asserts both halves of a tile
together, any change may quietly deliver half a product and no check will
object.

**Independent Test**: A wall is brought up with a camera whose video actually
arrives and an overlay bound to a variable. The tile shows a decoding video
track **and** the label's resolved text, at the same time. Removing either
half fails the check.

**Acceptance Scenarios**:

1. **Given** a published wall with one tile, a camera whose stream is being
   served, and an overlay bound to a system variable,
   **When** an operator's kiosk displays that wall,
   **Then** the tile is receiving and decoding video frames **and** is
   displaying the overlay's resolved text.
2. **Given** that same wall displayed,
   **When** the bound variable's value changes,
   **Then** the label on the tile changes to the new value while the video
   continues to decode.
3. **Given** a check asserting both halves,
   **When** the video is prevented from arriving,
   **Then** the check fails — it does not pass on the strength of the label
   alone.
4. **Given** a check asserting both halves,
   **When** the overlay is unbound or its text removed,
   **Then** the check fails — it does not pass on the strength of the video
   alone.

---

### User Story 2 - The span is timed as one span, or reported as untimed (Priority: P1)

Someone responsible for the latency budget asks the only question the budget
exists to answer: *when something happens in the fab, how long until an
operator can see it?* They get either a measured figure for the whole span, or
a clear statement that the span is still not measured — and never a number
assembled from parts.

**Why this priority**: Equal to US1 because the temptation here is specific,
strong, and already known to be wrong. Six per-leg figures exist. Adding them
produces an 800 ms-shaped number in about a minute. That arithmetic has
already been established as invalid in this repository, and a fabricated
end-to-end figure is worse than an acknowledged gap: it closes the question
while leaving the risk.

**Independent Test**: A single event is caused, its arrival and its appearance
on screen are both timed against a common reference, and the difference is
reported. If a common reference cannot be established, the record says the
span remains unmeasured and says why.

**Acceptance Scenarios**:

1. **Given** a wall displaying a tile whose overlay is bound to a variable,
   **When** that variable is set to a new, distinguishable value,
   **Then** the elapsed time between the change being submitted and the new
   text being visible on the tile is reported as one measurement.
2. **Given** an end-to-end figure,
   **When** anyone asks how it was obtained,
   **Then** the answer is a single timed span, not a sum of per-leg figures.
3. **Given** the measurement cannot be taken against a common reference,
   **When** the feature reports its result,
   **Then** it states the span is unmeasured and what prevented it, rather
   than substituting the sum.
4. **Given** a reported figure,
   **When** it is recorded,
   **Then** it is accompanied by the conditions it was taken under, because a
   figure from CI hardware is not a claim about a fab kiosk.

---

### User Story 3 - What a person still has to do is written down (Priority: P2)

Someone picking up the latency work later can tell, without re-deriving it,
which parts of the budget's assurance are discharged by automation and which
still require a human being in front of a wall.

**Why this priority**: Lower because it delivers no capability. It is here
because the alternative is what this feature exists to correct — a record
that reads as settled while the thing it describes has never been seen.
Getting that wrong once is how the leg table came to say three legs were
unbuilt for months after they were built.

**Independent Test**: A reader who knows nothing of this feature can state,
from the record alone, what has been observed, what has been measured, under
what conditions, and what remains outstanding.

**Acceptance Scenarios**:

1. **Given** the feature is complete,
   **When** the constitution's leg table is read,
   **Then** each leg's state reflects what is now true, and any leg whose
   state changed says so.
2. **Given** the feature is complete,
   **When** a reader asks whether anyone has watched a wall align,
   **Then** the record answers plainly — including if the answer is no.

---

### Edge Cases

- **The video source is slow to start.** A stream pulled on demand does not
  produce its first frame instantly. A check that asserts too early fails for
  a reason unrelated to the product; one that waits without bound hangs CI.
- **Frames decode but the picture is a still.** A source that produces one
  frame and stops satisfies "a track exists" while showing something
  indistinguishable from a frozen wall. Decoding must be observed as
  *ongoing*, not as *having happened*.
- **The label is correct by coincidence.** An overlay whose text matches the
  variable's initial value proves nothing about binding. The value must change
  and the label must follow it.
- **The clock is not shared.** The event's arrival and the label's appearance
  may be timed on different clocks. Where that is so, the difference between
  them is not a duration.
- **The wall is aligned but late.** Alignment is bought with delay drawn from
  this same budget. A wall can satisfy alignment and breach the span, and a
  per-leg view cannot show that.
- **The check becomes the slowest thing in CI.** Video in a headless browser
  is not free. A fixture that doubles the end-to-end job is one that gets
  disabled, and a disabled check is indistinguishable from an absent one.

---

## Requirements *(mandatory)*

### Functional Requirements

**The fixture (US1)**

- **FR-001**: A wall MUST be brought up, under automated conditions, whose
  tile has both a camera stream that actually arrives and an overlay bound to
  a system variable.
- **FR-002**: The check MUST assert that video is **decoding on an ongoing
  basis** — increasing over time — rather than that a video element or a
  media track merely exists.
- **FR-003**: The check MUST assert the overlay's **resolved text** is
  displayed on the same tile at the same time.
- **FR-004**: The check MUST fail if either half is absent. Both directions
  MUST be demonstrated, not merely asserted.
- **FR-005**: The check MUST assert that a change to the bound variable is
  reflected in the label while video continues to decode, so that a label
  that is correct by coincidence cannot pass.
- **FR-006**: The fixture MUST clean up whatever it creates, including on a
  partial run.

**The measurement (US2)**

- **FR-007**: The span from a variable's value being submitted to the
  resulting text being visible on the tile MUST be measured as **one span**.
- **FR-008**: An end-to-end figure MUST NOT be produced by summing per-leg
  figures, under any circumstances.
- **FR-009**: If a common reference cannot be established between the two ends
  of the span, the feature MUST report the span as **unmeasured**, naming what
  prevented it, and MUST NOT substitute a derived figure.
- **FR-010**: Any reported figure MUST be accompanied by the conditions it was
  taken under — what hardware, what else was running, what the wall contained.
- **FR-011**: The measurement MUST be repeated, and its spread reported. A
  single run is not a measurement.
- **FR-012**: Where the measured span is compared to the 800 ms budget, the
  comparison MUST state which legs the span actually covers, since a span
  measured from a variable submission does not begin where the budget begins.

**The record (US3)**

- **FR-013**: The constitution's leg table MUST be updated to reflect what is
  true after this feature, and any leg whose recorded state changes MUST be
  identified.
- **FR-014**: The record MUST distinguish **observed** from **measured**, and
  state which parts of the budget's assurance still require a person in front
  of a wall.
- **FR-015**: The record MUST NOT claim that inter-display synchronisation,
  representative-hardware figures, or dashboard obligations are discharged by
  this feature.

**Cost**

- **FR-016**: The added end-to-end run time MUST be measured and reported, and
  MUST be a stated budget rather than an emergent property.

### Key Entities

- **A tile under check**: one camera, one bound overlay, one clip. The
  smallest thing that can exhibit the product's central behaviour.
- **The span**: a single duration with a defined start event and a defined end
  observation, both attributable to one clock or explicitly not.
- **The conditions**: what a figure was obtained under, without which the
  figure is not interpretable.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An automated check exists that fails when a tile shows a label
  without live video, and fails when it shows live video without a label.
  Both failures are demonstrated rather than assumed.
- **SC-002**: Someone can state, from the record, how long this system takes
  to put a changed value in front of an operator — or state that it is not
  known and why.
- **SC-003**: No figure describing the whole span is arrived at by addition.
- **SC-004**: A reader of the constitution's leg table can tell, per leg,
  whether it has been observed, measured, both, or neither — and the table
  agrees with what the repository does.
- **SC-005**: The end-to-end suite's total run time after this feature is
  known, stated, and within an agreed budget.
- **SC-006**: What still requires a person in front of a wall is written down
  and is discoverable without reading this specification.

---

## Assumptions

- **The clips already in the repository are the video source.** They are
  tracked, so any automated environment has them without a download step. No
  new media is introduced.
- **One tile is enough.** The gap is that no check has both halves, not that
  no check has four of them. Multi-station video is already the scenario
  simulator's job.
- **Both a "changed value appears" figure and per-leg figures are useful, and
  they are not the same figure.** This feature adds the former without
  disturbing the latter.
- **The measurement will be taken on whatever hardware the automated
  environment provides.** That is a real limit on what the figure means, and
  stating the limit is part of the deliverable rather than a caveat on it.
- **A figure may come back showing the budget comfortably met, or breached.**
  Either is a result. The feature is not conditional on a favourable one.

---

## Out of scope

- **Whether readable-in-a-sink discharges the dashboard obligation.** A live
  disagreement between two decision records, with its own issue. Untouched
  here.
- **Re-reading the overlay-draw leg on representative kiosk hardware.** Its
  own open work; this feature runs on what the automated environment has.
- **Inter-display synchronisation.** Still requires time synchronisation
  across displays, still unbuilt, and not one of the six legs.
- **Making anything faster.** This feature measures and observes. If it finds
  a breach, that is a finding, and what to do about it is a separate decision.
- **A second scenario simulator.** One camera, one tile, one overlay, one
  clip.
- **Changing the budget.** The 800 ms figure and its per-leg breakdown are
  not in question here.

---

## Dependencies

- A media server able to serve a stream in the automated environment, and a
  camera registration that points at it.
- An overlay bound to a system variable whose value can be changed during a
  run.
- The existing per-leg instrumentation, which this feature reads but does not
  replace.
