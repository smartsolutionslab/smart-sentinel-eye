# Feature Specification: The event-to-overlay leg can be measured

**Feature Branch**: `025-measure-event-to-overlay`

**Created**: 2026-08-22

**Status**: Draft

**Input**: Constitution §VII as amended by ADR-0117; follows spec 024 (#1681)

---

## Why this exists

**`develop` currently violates the constitution.**

§VII, as amended yesterday by ADR-0117, requires every *implemented* leg of the
latency budget to have a measurement and a dashboard. `event → overlay state
≤ 200 ms` is implemented. It has neither.

That is not an oversight in the amendment — it is the point of it. The old rule
bound six legs including three that do not exist, and was therefore ignored. The
new rule binds the legs that are real, which makes this one an obligation
somebody has to discharge rather than a line nobody reads.

Spec 024 built the instrument and could not feed it, because **no service sees
both ends of the leg**. The acceptance moment is known at ingestion and is gone
by the time the effect is applied.

**What is at stake is not the number.** It is that the only leg with a known
history of breaching its budget — twelve to fourteen seconds against 200 ms
(#1655) — is still the leg nothing watches.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — The whole leg is measured, not a convenient part of it (Priority: P1)

Someone asking "is the event-to-overlay leg holding?" gets an answer covering the
journey ADR-0015 describes: an event accepted through to its effect applied.

**Why this priority**: It is the feature. A measurement of part of the leg,
reported against the whole leg's budget, is worse than no measurement, because
someone will act on it.

**Independent Test**: Put events through the system and read back a latency
distribution that begins at acceptance and ends at application.

**Acceptance Scenarios**:

1. **Given** an event is accepted, **When** its effect is applied, **Then** the
   elapsed time between those two moments is recorded as one measurement.
2. **Given** that measurement, **When** it is inspected, **Then** it is marked as
   covering the **whole leg**, and that marking is true.
3. **Given** many events, **When** the leg is queried, **Then** a high percentile
   is available, because a budget is a claim about the tail.

---

### User Story 2 — The measurement agrees with something that already measures it (Priority: P1)

The figure the system reports about itself matches the figure an independent test
observes from outside.

**Why this priority**: Also P1, and not a refinement of US1. An instrument that
reports a plausible number for the wrong span invites citation, and nothing
inside the system can detect that. Spec 022's test already measures
arrival-to-effect from outside; agreement is checkable and disagreement is
information.

**Independent Test**: Run the existing journey test, compare its logged
arrival-to-effect against what the instrument recorded for the same events.

**Acceptance Scenarios**:

1. **Given** an event with both figures available, **When** they are compared,
   **Then** they agree within a stated tolerance.
2. **Given** they disagree, **When** that is investigated, **Then** the cause is
   identified before either figure is quoted anywhere.

---

### User Story 3 — Carrying the moment costs nothing that matters (Priority: P2)

The change that makes the leg measurable does not degrade the thing it measures,
and does not break consumers of the messages it touches.

**Why this priority**: A latency feature that adds latency is self-defeating, and
this one edits shared contracts, so the blast radius is wider than the leg.

**Acceptance Scenarios**:

1. **Given** the change, **When** steady-state latency is measured the same way
   as before it, **Then** it is no worse.
2. **Given** a consumer that does not know about the added information, **When**
   it receives a message carrying it, **Then** it behaves as before.
3. **Given** the instrumentation, **When** its own cost is measured, **Then** the
   figure is stated rather than assumed negligible.

---

### Edge Cases

- **The moment is missing.** Messages published before this change, or by a path
  that does not carry it, will arrive without it. A measurement must be skipped,
  not recorded as zero — a zero is a perfect score for a journey nobody timed.
- **The two ends are on different machines.** The clocks must be comparable or
  the number is fiction. Fabs run PTP and a stepped clock can put the end before
  the start; a negative duration must be discarded rather than recorded.
- **One event, many effects.** A rule can set a variable *and* highlight an
  overlay. That is two applications of one acceptance, and whether that is two
  measurements or one needs deciding rather than falling out of the code.
- **An effect nobody applies.** An event matching no rule produces no
  application, so the leg has no end. Absence of a measurement must not read as a
  fast one.
- **The cold path.** The first event of each message type costs seconds (#1655).
  It will now be visible in the distribution, which is correct — but a p99 that
  reflects one restart per deployment is describing something different from
  steady state, and the reader needs to be able to tell.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The moment an event is accepted MUST be available to the service
  that applies its effect.
- **FR-002**: That moment MUST be carried without changing the meaning of any
  existing field. A field meaning two things depending on the message is how a
  measurement quietly becomes wrong.
- **FR-003**: The elapsed time from acceptance to application MUST be recorded as
  an aggregatable distribution.
- **FR-004**: The recorded measurement MUST be marked as covering the whole leg,
  and that marking MUST be accurate.
- **FR-005**: A measurement MUST be skipped when the acceptance moment is absent,
  rather than recorded as zero or defaulted.
- **FR-006**: A negative elapsed time MUST NOT be recorded.
- **FR-007**: The instrument's figure MUST be reconciled against an independent
  measurement of the same journey before being relied upon.
- **FR-008**: Consumers unaware of the added information MUST be unaffected by
  its presence.
- **FR-009**: Steady-state latency MUST NOT regress, verified rather than
  assumed.
- **FR-010**: The instrumentation's own cost MUST be measured and stated.
- **FR-011**: Where the change touches a shared contract, whether it constitutes
  a breaking change under the project's versioning scheme MUST be established and
  recorded, not assumed.

### Key Entities

- **Acceptance moment**: when an event entered the system and became durable.
  Known at ingestion; currently lost downstream.
- **Application**: when an event's effect became observable — a variable set, an
  overlay highlighted.
- **Leg measurement**: the elapsed time between the two, aggregated across many
  events.
- **Whole-leg marking**: the claim, carried on every measurement, that it covers
  the leg rather than a fragment.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The `event → overlay state` leg's latency is readable from the
  running system as a distribution including a high percentile.
- **SC-002**: Every recorded measurement of this leg is marked as whole-leg, and
  spot-checking confirms the marking is true.
- **SC-003**: The instrument's figure and the independent journey test's figure
  agree within **20%** on the same events, or the discrepancy is explained in
  writing.
- **SC-004**: Steady-state arrival-to-effect is no worse than the 267–369 ms
  recorded by specs 022 and 023, measured the same way.
- **SC-005**: The instrumentation's overhead is stated and is under **5%** of the
  leg's 200 ms budget.
- **SC-006**: An event whose acceptance moment is absent produces no measurement,
  demonstrated rather than asserted.
- **SC-007**: The full test suite passes with nothing excluded or weakened.
- **SC-008**: §VII's measurement requirement is met for this leg, and the leg's
  row in the constitution's §IV table is updated to say so.

---

## Assumptions

- **The acceptance moment is the right start.** ADR-0015 defines the leg as
  RabbitMQ + projection, which begins when the event is accepted rather than when
  the machine produced it. The gap between those two — network and broker
  transit from the plant — belongs to no leg currently, and that is not this
  feature's problem to solve.
- **One measurement per application.** An event with two effects is recorded
  twice, because each application is a separate arrival at a screen and averaging
  them would hide a slow one behind a fast one. Recorded as an assumption because
  the alternative is defensible.
- **The cold path stays in the distribution.** Excluding it would make the
  dashboard flatter and less true. #1655 owns the cost itself; this feature owns
  showing it.
- **A dashboard is not in scope.** §VII asks for measurement *and* a dashboard.
  Where dashboards live is the ADR-0026 decision (#1707), unmade. This feature
  delivers the measurement and leaves the leg's §IV row honest about the
  remaining half.
- **Numbers from the fixture are not numbers about a fab.** Standing caveat from
  specs 020, 022, 023 and 024.

---

## Out of scope

- **A dashboard** — #1707 must be decided first.
- **The other legs** — `camera → SFU` is readable and undashboarded; three are
  unbuilt (#1714).
- **The cold-start cost** — #1655 owns it. This makes it visible, not smaller.
- **Changing the budget** — ADR-0015 is Locked.
