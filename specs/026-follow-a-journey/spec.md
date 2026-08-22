# Feature Specification: A cross-service journey can be followed end to end

**Feature Branch**: `026-follow-a-journey`

**Created**: 2026-08-22 · **Revised**: 2026-08-22 (Phase 0/2 findings)

**Status**: Draft — **rewritten against the real cause**

**Input**: #1750, found while closing spec 025

---

> **This spec was rewritten after implementation began.** The first version
> named the wrong cause and, in one place, argued from a wrong premise about how
> spans work. Both are recorded in "What the first version got wrong" below
> rather than quietly removed — the requirement survived all three corrections,
> and how they were found is the more useful half.

---

## Why this exists

**Nobody can answer "what caused this?" for a plant-floor event**, and that is
where every journey in this system starts.

It has a bill:

- **Spec 023** spent a day investigating a twelve-to-fourteen second first event
  (#1655), reasoning from wall-clock timings printed by tests because the traces
  did not join up. It ended with **no cause identified** and four candidate
  explanations refuted. A joined journey might have shown the answer directly.
- **Spec 025** built a latency measurement and could not reconcile it against an
  independent figure, for a related reason.

### The cause, established by observation

Most of this system's journeys already join up. In a live trace, one automation
handler fans out to two services across RabbitMQ **and through the outbox** —
one of them 4.3 seconds later — with parentage intact:

```
automation          receive  FabEventIngestedV1            42 ms
  ├─ automation     send     OverlayHighlightRequestedV1    0 ms
  ├─ audit-obs      receive  OverlayHighlightRequestedV1   58 ms   (+0.7 s)
  └─ layout-comp    receive  OverlayHighlightRequestedV1    1 ms   (+4.3 s)
```

**One hop is broken, and only one.** A plant-floor event published by
`event-ingestion` is a trace root; the automation work it causes is a separate
trace root. Nothing connects them.

The reason is not storage. The parent that gets propagated is **the ambient
activity at publish time** — note that the receives above attach to automation's
*receive* span, not to the `send` beside them. Publishing inside a message
handler therefore inherits a cause automatically.

**Event ingestion publishes from a background service that drains a channel.**
There is no ambient activity there, so there is no cause to carry, and every
plant-floor event begins life as an orphan.

**The journey has no beginning.** That is the whole defect.

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
time would replace a missing answer with a wrong one.

**Status**: **Already observed to hold** on the mechanism this feature uses. The
joined trace above reports 4305 ms overall while its spans report 42, 0, 58 and
1 ms — each measures its own work, and the queue wait shows up in the trace's
elapsed time, which is the honest place for it. This story is now a **regression
guard** rather than an open risk, and it is cheap to keep.

**Acceptance Scenarios**:

1. **Given** a message that waited before delivery, **When** the journey is
   inspected, **Then** no reported duration includes the wait as though it were
   work.
2. **Given** the latency measurement that already exists for the
   `event → overlay state` leg, **When** this feature ships, **Then** that
   measurement is unchanged and still does not depend on telemetry.

---

### Edge Cases

- **Batching.** Ingestion stores and publishes deliveries in batches of up to
  200. If one cause is invented per batch, two hundred unrelated plant-floor
  events share a parent and the trail becomes useless in the direction US2 asks
  about. Each event needs its own beginning.
- **An event that causes nothing.** No rule matches; the journey is one step
  long. That must read as a journey that ended, not one that broke.
- **A cause no longer in the telemetry store.** Retention is finite; a reference
  to something expired must read as "not available" rather than as "no cause".
- **Fan-out.** One event causing several effects gives several trails from one
  origin, and one origin from each effect. Neither direction is a single line.
- **Redelivery.** A message delivered twice produces two arrivals from one cause,
  which is the truth and should look like it.
- **Cost on the ingest path.** Ingestion is sized for 5 000 events/s and its
  batching exists to keep the database round trip amortised. Anything added per
  event is added five thousand times a second.
- **The other untraced publishers.** Ingestion is the hop this feature is about,
  but any other publish from a background service has the same shape. Whether
  they are worth the same treatment is a question this feature should answer,
  not assume.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A plant-floor event MUST have a recorded cause that downstream work
  can attach to — it MUST NOT begin as an orphan.
- **FR-002**: Given an applied effect, its originating event MUST be identifiable
  from recorded telemetry alone, without correlating by timestamp.
- **FR-003**: Given an event, the work it caused MUST be discoverable from
  recorded telemetry.
- **FR-004**: A reported duration MUST NOT include time a message spent waiting
  for delivery as though it were work performed.
- **FR-005**: The causal relationship MUST be expressed in the standard the rest
  of the telemetry uses, not a private mechanism invented here.
- **FR-006**: Each ingested event MUST get its **own** cause. A batch MUST NOT
  collapse unrelated events onto one.
- **FR-007**: The messaging library's existing propagation MUST NOT be duplicated
  or worked around. Where it already carries the relationship, this feature adds
  nothing.
- **FR-008**: The result MUST be verified as usable in the sink this project
  actually has (ADR-0118), not merely emitted.
- **FR-009**: Steady-state latency and ingest throughput MUST NOT regress,
  verified rather than assumed.
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
- **SC-005**: Two events ingested in the same batch have **different** causes,
  and their journeys do not merge.
- **SC-006**: Steady-state arrival-to-effect is no worse than the figures
  recorded by specs 022 and 024 (267–369 ms), measured the same way.
- **SC-007**: The relationship is followable **in the project's actual sink**, by
  someone reading it rather than by a test asserting it exists.
- **SC-008**: The full test suite passes with nothing excluded or weakened.

---

## Assumptions

- **The library's propagation is kept, not replaced.** It demonstrably works
  across services and through the outbox. This feature supplies the one thing it
  has nothing to work with, and touches nothing else.
- **Both directions come from one relationship.** Giving an ingested event a
  cause should make both "what caused this effect" and "what did this event
  cause" answerable, so US2 needs no separate mechanism.
- **Per-event, not per-batch.** FR-006 and SC-005 exist because the cheap
  version of this change is per-batch and it would look like it worked.
- **Cost is small but not zero.** Measured under FR-009 rather than waved away —
  the ingest path runs this five thousand times a second at design load.
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
- **Tracing every other background publisher.** Named as an edge case to be
  answered, not as work to be done here.

---

## What the first version got wrong

Kept deliberately. Three premises, each plausible, each written with confidence,
each false — and all three survived a quality checklist that marked the
requirements "testable and unambiguous".

**1. That parentage across a delayed hop would corrupt latency percentiles.**
The spec claimed a continuation would report "a twenty-millisecond journey as
eight minutes, in every percentile it appears in". A span measures its own start
to its own end; percentiles are computed over spans. Found by checking what span
duration means. *Cost if unfound: the cheapest correct option was ruled out.*

**2. That the outbox has nowhere to keep the causal context.** Argued from
`wolverine_outgoing_envelopes` having seven columns, none named for it. The
`body` column holds the serialised **envelope**, not the message payload;
round-tripping one preserves parent, correlation and custom headers. Found by
serialising an envelope and deserialising it. *Cost if unfound: a header added to
every message in the system, to duplicate something already working.*

**3. That the outbox was where the chain broke.** It is not: a live trace shows
three services joined across a 4.3-second store-and-forward wait. The break is
that ingestion publishes from a background service with no ambient activity, so
there is no cause to propagate. Found by reading the traces instead of the
schema. *Cost if unfound: the feature would have shipped, the tests would have
passed, and the journey would still have had no beginning.*

**The pattern is one thing three times: an argument about a mechanism, made
without looking at the mechanism.** Each was caught by an experiment that took
minutes. The checklist caught none of them, because a checklist can confirm that
a requirement is testable and cannot know whether it is true.
