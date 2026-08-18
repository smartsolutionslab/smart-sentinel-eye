# Feature Specification: An event is never accepted until it is stored

**Feature Branch**: `020-durable-ingest-ack`

**Created**: 2026-08-18

**Status**: Draft

**Input**: #1546 — ingest drops an envelope it has already accepted when persistence fails.

## Why this exists

The system tells whoever sent an event that it has been accepted, and only
afterwards tries to store it. Between those two moments the event can be lost,
and when it is, the sender has already been told otherwise.

That gap is not hypothetical and it is not rare. It opens whenever the database
is briefly unavailable — a restart, a failover, a moment of exhaustion — and it
swallows not one event but every event that arrives during the window. It opens
again whenever the service stops, because everything waiting to be stored is
held only in memory. Nothing is reported to the sender in either case, because
the sender was told "accepted" before the trouble started.

The deliveries that arrive from the plant floor are worse off than they need to
be. Those arrive over a channel that already knows how to redeliver what was
never confirmed — the machine keeps its copy until we say we have it. The
system currently confirms on arrival rather than on storage, which discards the
one mechanism that could have recovered the event.

Two features have already narrowed the ways this can happen without addressing
the promise itself. This is the promise.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A plant's events survive a database outage (Priority: P1)

The database becomes briefly unavailable while machines are sending events. When
it comes back, every event that was sent during the outage is stored. Nobody
replays anything by hand, and no operator has to know it happened.

**Why this priority**: It is the dominant real cause. A poison event is an
oddity; a database restart is a Tuesday, and today it silently discards
everything arriving in the window.

**Independent Test**: Send a stream of events from a machine, interrupt the
database mid-stream, restore it, and compare what was sent against what is
stored.

**Acceptance Scenarios**:

1. **Given** machines sending events, **When** the database is unavailable for
   a period and then returns, **Then** every event sent during that period is
   stored, exactly once each.
2. **Given** the same, **When** an operator looks afterwards, **Then** the
   record shows the interruption and the recovery, rather than being silent
   about both.
3. **Given** events that were already stored before the interruption, **When**
   recovery happens, **Then** they are not duplicated.

---

### Update to Story 1 — the sender is told the truth (Priority: P1)

An operator or a machine that submits an event directly is told it was accepted
only if it was stored. If it cannot be stored, they are told that instead, at
the time, in the response to their own request.

**Why this priority**: Same defect, other half. Story 1 recovers what the plant
floor sends because that path can redeliver; a direct submission has no second
chance, so the only honest option is not to claim success until it is true.

**Independent Test**: Submit an event while storage is unavailable and observe
what the submitter is told, then check whether anything was stored.

**Acceptance Scenarios**:

1. **Given** storage is unavailable, **When** an operator submits an event,
   **Then** they are told it failed — not that it was accepted.
2. **Given** the same, **When** a machine posts an event, **Then** it is told
   the same thing, so its own retry logic can act on it.
3. **Given** storage is available, **When** either submits an event, **Then**
   they are told it was accepted and it is there.

---

### User Story 2 - A stopped service loses nothing that was accepted (Priority: P1)

The service is restarted — a deployment, a crash, a node moving. Everything it
had accepted and not yet stored is still stored, or was never accepted in the
first place.

**Why this priority**: P1 because it is the same promise under a different
failure, and because it is the one nobody currently sees at all: no error is
logged for events lost this way, so the count is unknown and unknowable.

**Independent Test**: Send a burst, stop the service mid-burst without warning,
restart it, and compare what was sent against what is stored.

**Acceptance Scenarios**:

1. **Given** a burst of events in flight, **When** the service stops abruptly,
   **Then** after restart every event that was acknowledged is stored.
2. **Given** the same, **When** an event was never acknowledged, **Then** the
   sender either still has it or was told it failed — it is not silently gone.

---

### User Story 3 - One unstorable event never stops the rest (Priority: P1)

A single event cannot be stored no matter how often it is tried. The rest keep
flowing, the bad one stops being retried, and it is recorded where somebody can
find it.

**Why this priority**: P1 as a guard rather than a feature. The mechanism that
makes Story 1 work — keep trying until it is stored — is exactly the mechanism
that turns one permanently-bad event into an endless loop that blocks
everything behind it. That is the defect a previous feature fixed, and it must
not come back through this door.

**Independent Test**: Introduce an event that can never be stored, and confirm
that other events continue and the bad one stops being retried.

**Acceptance Scenarios**:

1. **Given** an event that cannot be stored for reasons that will not change,
   **When** it has been tried enough times to be sure, **Then** it stops being
   retried.
2. **Given** the same, **Then** it is recorded somewhere durable, with enough
   to identify it, and its count is visible.
3. **Given** the same, **Then** every other event continues to be stored
   throughout, at the normal rate.

---

### Edge Cases

- **The same event arrives twice** because the first attempt was never
  confirmed. It must be stored once. This stops being an exceptional path and
  becomes an ordinary one, so it must be verified rather than assumed.
- **Events arrive faster than they can be stored** for a sustained period. The
  senders that can be slowed down must be slowed down rather than dropped; the
  senders that cannot must be told.
- **The service stops while an event is mid-store.** It is either stored or not
  acknowledged; never acknowledged-and-absent.
- **Storage is unavailable for longer than any sender is willing to wait.**
  What gives way must be a decision, not an accident, and it must be visible
  when it happens.
- **An event that cannot be stored is also one whose failure cannot be
  recorded** — the same outage often prevents both. The system must not treat
  "I could not even record the failure" as "the failure did not happen".

## Requirements *(mandatory)*

### The promise

- **FR-001**: The system MUST NOT report an event as accepted before it is
  stored, on any ingress.
- **FR-002**: An event that cannot be stored MUST result in the sender either
  retaining it for redelivery or being told it failed — never in a report of
  success.
- **FR-003**: An event that is redelivered because a previous attempt was not
  confirmed MUST be stored exactly once.

### Recovering from an interruption

- **FR-004**: While storage is briefly unavailable, the system MUST keep events
  that senders can redeliver, and MUST store them once it returns, without
  human intervention.
- **FR-005**: Recovery MUST be bounded and visible: how long the system keeps
  trying, and what it does when that is exhausted, MUST be a stated decision
  and MUST be recorded when reached.
- **FR-006**: An interruption and its recovery MUST both be recorded, including
  how many events were affected.

### Not blocking on one bad event

- **FR-007**: An event that cannot be stored for unchanging reasons MUST stop
  being retried after a bounded number of attempts.
- **FR-008**: Such an event MUST be recorded durably, identifiably, and
  countably before it stops being retried, and MUST NOT be discarded silently.
- **FR-009**: Other events MUST continue to be stored while one is failing —
  no single event may block the flow.

### What must not get worse

- **FR-010**: Sustained throughput MUST remain at the level the system was
  built for. Ingest MUST NOT become one round-trip per event.
- **FR-011**: The order in which events from one source are stored MUST be
  preserved, as it is today.
- **FR-012**: The time from an event arriving to it being visible MUST stay
  within its share of the end-to-end budget, and MUST be measured rather than
  assumed.
- **FR-013**: When the system cannot keep up, senders capable of being slowed
  MUST be slowed rather than having events dropped; senders that cannot MUST be
  told clearly. Whatever replaces today's "too many requests" answer MUST be
  specified rather than left to fall out of the change.

### Deliberately unchanged

- **FR-014**: Which events an operator may see or file MUST NOT change.
- **FR-015**: How an event's plant is established, and how storage for a plant
  is provisioned, MUST NOT change.

### Key Entities

- **Event** — unchanged in content. What changes is when the system claims to
  have it.
- **Acknowledgement** — the moment the system tells a sender it has the event.
  Currently on arrival; becomes on storage. This is the whole feature.
- **Undeliverable event** — one that cannot be stored and will not become
  storable. Newly needs a home, because "keep trying" without one is an
  infinite loop.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With storage interrupted for **60 seconds** during a sustained
  send, **100%** of events sent are stored after recovery, each exactly once.
- **SC-002**: With the service killed without warning mid-burst, **zero**
  acknowledged events are missing after restart.
- **SC-003**: A submitter is never told an event was accepted when it was not —
  **zero** such responses under any failure injected in testing.
- **SC-004**: One permanently unstorable event stops being retried within a
  stated bound, is recorded, and costs **no** loss of throughput for other
  events.
- **SC-005**: Sustained ingest throughput is **no lower than today's** measured
  figure for the same load, and per-source order is preserved.
- **SC-006**: The arrival-to-visible time stays within its share of the
  end-to-end budget, measured before and after.
- **SC-007**: Every interruption is visible afterwards from the record alone:
  how long, how many events, and whether all were recovered.

## Assumptions

- **Machine deliveries can be redelivered.** The plant-floor channel keeps a
  delivery until it is confirmed; the system currently confirms too early. This
  feature relies on that mechanism rather than building another.
- **Direct submissions are low-volume.** Operator and machine submissions over
  the request interface are control-plane actions, so storing before answering
  costs little; the buffer that exists for burst absorption exists for the
  plant-floor path, not for them. If that ceases to be true, FR-010 is the
  requirement that will notice first.
- **Duplicate suppression already exists** and is keyed on the event's own
  identifier. Redelivery therefore collapses to a single stored event. This
  feature exercises that path constantly rather than rarely, which is why
  FR-003 asks for it to be proven rather than trusted.
- **A bounded escape is required, not optional.** A channel that redelivers
  until confirmed will redeliver forever, so "keep trying" without a stopping
  rule reintroduces a defect an earlier feature fixed.
- **The retry window is a deployment decision.** How long to keep trying before
  giving up depends on how long outages last in a given plant; the spec
  requires it to be stated and visible, not that it takes a particular value.
