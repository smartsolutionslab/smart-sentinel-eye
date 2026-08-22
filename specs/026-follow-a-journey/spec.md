# Feature Specification: A cross-service journey can be followed end to end

**Feature Branch**: `026-follow-a-journey`

**Created**: 2026-08-22

**Status**: Draft

**Input**: #1750, found while closing spec 025

---

## Why this exists

**Nobody can answer "what caused this?" across a service boundary in this
system**, and every integration event crosses one.

That is not a gap noticed in the abstract. It has a bill:

- **Spec 023** spent a day investigating a twelve-to-fourteen second first event
  (#1655), reasoning from wall-clock timings printed by tests because the traces
  did not join up. It ended with **no cause identified** and four candidate
  explanations refuted. A joined journey might have shown the answer directly.
- **Spec 025** built a latency measurement and could not reconcile it against an
  independent figure, for a related reason.

The cause is confirmed rather than suspected. The messaging library carries the
causal context in memory and builds its receive span from it — the plumbing is
all there. What is missing is **storage**: the outbox table has seven columns and
none of them can hold that context, so a message that waits there arrives having
forgotten what caused it.

**Every integration event in this system waits in that outbox.**

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — From an effect, find its cause (Priority: P1)

Someone looking at something that happened — a variable that changed, an overlay
that lit — can find the plant-floor event responsible, across the services
between them.

**Why this priority**: It is the question people actually ask, and the one that
cost spec 023 a day. Debugging starts at the symptom.

**Independent Test**: Cause an effect, take its record, and follow it back to the
originating event without correlating by wall-clock time.

**Acceptance Scenarios**:

1. **Given** an effect that has been applied, **When** someone asks what caused
   it, **Then** the originating event is identifiable from recorded telemetry
   alone.
2. **Given** an effect and its cause are in different services, **When** the
   journey is followed, **Then** the boundary between them is not where the
   trail stops.
3. **Given** a message that waited in the outbox before delivery, **When** the
   journey is followed, **Then** the wait does not break the trail.

---

### User Story 2 — From an event, find what it caused (Priority: P2)

Someone holding a plant-floor event can see what it went on to do — which rules
fired, which effects were applied, which services were involved.

**Why this priority**: The mirror of US1 and genuinely useful — "this event
should have changed something and did not" is a real question — but debugging
usually starts from the symptom, so US1 comes first.

**Acceptance Scenarios**:

1. **Given** an event, **When** someone asks what it caused, **Then** the
   downstream work is discoverable from telemetry.
2. **Given** an event that caused **two** effects, **When** its consequences are
   listed, **Then** both appear.

---

### User Story 3 — A delayed hop does not lie about how long things took (Priority: P1)

Someone reading how long a journey took is not misled by a message having waited
in a queue.

**Why this priority**: P1 alongside US1, because whatever joins the journey up
must not make a delay look like work. A reported duration that includes queue
time would replace a missing answer with a wrong one, and this codebase has been
caught five times by things that looked like success.

> **Corrected 2026-08-22 (Phase 0).** This story originally justified itself by
> claiming that making the far side a continuation of the near side would report
> "a twenty-millisecond journey as eight minutes, in every percentile". **That is
> not how span duration works** — a span measures its own start to its own end,
> parentage does not extend it, and percentiles are computed over spans rather
> than whole journeys. The requirement below stands and is worth checking; its
> original reasoning was wrong. See research.md, Finding 2.

**Acceptance Scenarios**:

1. **Given** a message that waited before delivery, **When** the journey is
   inspected, **Then** no reported duration includes the wait as though it were
   work.
2. **Given** the latency measurement that already exists for the
   `event → overlay state` leg, **When** this feature ships, **Then** that
   measurement is unchanged and still does not depend on telemetry.

---

### Edge Cases

- **A message with no cause.** Events originating inside a service — a scheduled
  job, an operator action — have no upstream. The trail must simply start there
  rather than showing an empty or fabricated link.
- **A message older than this feature.** Anything already in the outbox when it
  ships carries no context. It must degrade to today's behaviour, not to an
  error.
- **A cause no longer in the telemetry store.** Retention is finite; a reference
  to something expired must read as "not available" rather than as "no cause".
- **Fan-out.** One event causing several effects gives several trails from one
  origin, and one origin from each effect. Neither direction is a single line.
- **Redelivery.** A message delivered twice produces two arrivals from one cause,
  which is the truth and should look like it.
- **Cost on every message.** Whatever is carried is carried by every message the
  system sends, not only the ones anyone later inspects.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The causal relationship between a message and the work it triggers
  MUST survive the message waiting in the outbox.
- **FR-002**: Given an applied effect, its originating event MUST be identifiable
  from recorded telemetry alone, without correlating by timestamp.
- **FR-003**: Given an event, the work it caused MUST be discoverable from
  recorded telemetry.
- **FR-004**: A reported duration MUST NOT include time a message spent waiting
  for delivery as though it were work performed.
- **FR-005**: The causal relationship MUST be expressed in the standard the rest
  of the telemetry uses, not a private mechanism invented here.
- **FR-006**: A message with no upstream cause MUST record no relationship,
  rather than an empty or invented one.
- **FR-007**: A message published before this feature MUST behave as it does
  today rather than failing.
- **FR-008**: The result MUST be verified as usable in the sink this project
  actually has (ADR-0118), not merely emitted.
- **FR-009**: Steady-state latency MUST NOT regress, verified rather than
  assumed.
- **FR-010**: The existing `event → overlay state` measurement MUST be unaffected
  and MUST NOT become dependent on telemetry.
- **FR-011**: The messaging library's own storage MUST NOT be altered or
  migrated.

### Key Entities

- **Journey**: everything that happened because one plant-floor event arrived,
  across every service it touched.
- **Causal relationship**: the recorded fact that this work happened *because of*
  that message — distinct from "this work happened *inside* that request".
- **Delivery wait**: time a message spent in the outbox or the broker. Real, and
  not work.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Starting from an applied effect, someone who did not build the
  system can name the originating event using only recorded telemetry.
- **SC-002**: Starting from an event, its downstream work is discoverable the
  same way.
- **SC-003**: **No reported duration grows** as a result of this feature — a
  delayed delivery does not inflate any measured span.
- **SC-004**: An event causing two effects yields both when its consequences are
  listed.
- **SC-005**: A message carrying no causal context is handled without error and
  without a fabricated relationship.
- **SC-006**: Steady-state arrival-to-effect is no worse than the figures
  recorded by specs 022 and 024 (267–369 ms), measured the same way.
- **SC-007**: The relationship is followable **in the project's actual sink**, by
  someone reading it rather than by a test asserting it exists.
- **SC-008**: The full test suite passes with nothing excluded or weakened.

---

## Assumptions

- **A link or a continuation — Phase 0 reopened this.** The spec was written
  assuming a link, on reasoning that turned out to be wrong (see US3's
  correction). Wolverine already carries `CorrelationId` and `ParentId` on the
  envelope and builds its receive span from them, so **stamping those through the
  outbox may join the journey up with no custom span code at all**. The plan
  tries that first and keeps links in reserve for the arguments that survive:
  fan-in, sampling, and trace lists dominated by queue time.
- **Both directions come from one relationship.** Recording that B was caused by
  A should make both "what caused B" and "what did A cause" answerable, so US2
  needs no separate mechanism.
- **Carried on the message.** The context has to travel with the message, because
  the outbox has nowhere else to keep it and the library's own tables are not
  ours to change.
- **Cost is small but not zero.** Every message grows slightly. Measured under
  FR-009 rather than waved away.
- **This does not fix #1655.** It makes the investigation that failed there
  possible. Whether the cold-start cause is then found is a separate question.

---

## Out of scope

- **The cold-start cost** — #1655 owns it.
- **The unbuilt latency legs** — #1714.
- **A production telemetry sink** — deferred by ADR-0118 until there is a
  production deployment.
- **Retention and sampling policy.** Real questions once journeys are followable,
  and not this feature's.
