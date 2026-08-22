# Feature Specification: Every journey has a beginning, not just the ones from the plant floor

**Feature Branch**: `027-trace-background-publishers`

**Created**: 2026-08-22

**Status**: Draft

**Input**: #1781, found by code review on spec 026's PR (#1780)

---

## Why this exists

Spec 026 established that work published from a background service **begins as an
orphan**: what gets propagated as a cause is whatever work is in progress at the
moment of publishing, and a loop draining a queue has none. It fixed the one hop
#1750 was filed about and scoped itself there, naming the rest as a question to
be answered rather than assumed.

**It has been answered.** Every place this system publishes an integration event,
classified by whether it has a cause to inherit:

| Publisher | Inherits from | |
|---|---|---|
| Event ingestion's persistence loop | — | **fixed by spec 026** |
| Stream health watcher | — | **orphan** |
| Audit retention | — | **orphan** |
| Automation's event handler | the message it is handling | fine |
| Camera catalog, identity, layouts, overlays, variables | the operator's request | fine |

The nine handlers that are fine are fine for free — a request or a message
already establishes the cause. **Only background loops publish into nothing**, and
two of them remain.

### What that costs

**A camera going unhealthy is exactly what someone asks "what caused this?"
about.** Today that announcement and the audit record of it are two unconnected
trails, which is the state spec 026's verification note documents as "before".

The second is easier to miss and worth naming: **audit retention publishes
straight from its loop**, not through a domain event handler like every other
publisher in the system. Anyone fixing this by pattern-matching on spec 026's
change will fix the first and walk past the second.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — From a stream health record, find the check that caused it (Priority: P1)

Someone looking at a record of a camera going unhealthy can find the health check
that observed it, and everything else that observation caused.

**Why this priority**: It is the one an operator asks. A camera dropping out is a
visible, urgent event, and "what happened, and when did we notice" is the first
question.

**Independent Test**: Cause a camera to go unhealthy, take the downstream record,
and follow it back to the observation without correlating by wall-clock time.

**Acceptance Scenarios**:

1. **Given** a camera whose stream has changed state, **When** someone asks what
   caused the downstream record, **Then** the observation is identifiable from
   recorded telemetry alone.
2. **Given** several cameras change state in the same sweep, **When** each is
   followed, **Then** they are **separate** journeys and do not share one.

---

### User Story 2 — From an archived audit chunk, find the run that archived it (Priority: P2)

Someone looking at an archived chunk can find the retention run responsible.

**Why this priority**: Real and less urgent than a camera dropping out — archival
is a background housekeeping concern nobody watches in real time. It is P2 rather
than dropped because it is the call site most likely to be missed, and a feature
that closes one of two gaps while reporting the problem solved is worse than one
that says which half it did.

**Acceptance Scenarios**:

1. **Given** a chunk that has been archived, **When** someone asks what caused
   the announcement, **Then** the retention work is identifiable from telemetry.
2. **Given** a run archiving several chunks, **When** each is followed, **Then**
   they are separate journeys.

---

### User Story 3 — A failed announcement does not read as a quiet success (Priority: P1)

Someone reading the record of an announcement that failed can tell it apart from
one that succeeded and caused nothing.

**Why this priority**: P1 because it is a defect this programme has already
shipped once and caught in review. A journey that ends without saying it failed
looks identical to a healthy one nothing subscribed to — same name, no children,
no error. Those are opposite facts.

**Acceptance Scenarios**:

1. **Given** an announcement that could not be made, **When** its record is
   inspected, **Then** it is marked as failed.
2. **Given** an announcement that succeeded but caused nothing downstream,
   **When** its record is inspected, **Then** it is **not** marked as failed.

---

### Edge Cases

- **The loop, not the item.** Both publishers work in loops — cameras in a sweep,
  chunks in a run. One journey per loop is less code, produces a joined trail,
  and looks correct from the downstream end while merging everything the loop
  touched onto one origin. This is spec 026's FR-006 trap in a new place, and
  **more tempting here because the loop is directly in view.**
- **A sweep that changes nothing.** Most health polls find every camera as it
  was. Those must not produce journeys for work that did not happen.
- **A publisher added later.** The survey above is true today. Nothing stops the
  next background loop from being written without an origin, and nothing would
  notice.
- **Retention publishing inline.** It does not go through a domain event handler,
  so the shape of spec 026's change does not transfer directly.
- **Cost on a poll cadence.** The health watcher runs continuously against every
  camera; the retention run is periodic. Neither should get slower.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A stream health change MUST have a recorded cause that downstream
  work can attach to.
- **FR-002**: An archived audit chunk MUST have a recorded cause that downstream
  work can attach to.
- **FR-003**: Each item a loop processes MUST get its **own** cause. A sweep or a
  run MUST NOT collapse unrelated items onto one.
- **FR-004**: An announcement that fails MUST be recorded as failed, distinctly
  from one that succeeded and caused nothing.
- **FR-005**: The causal relationship MUST be expressed through the mechanism
  spec 026 established, not a second one invented here.
- **FR-006**: Work that changes nothing MUST NOT record a journey.
- **FR-007**: The health poll cadence and the retention run MUST NOT regress,
  verified rather than assumed.
- **FR-008**: The result MUST be verified as followable in the sink this project
  actually has (ADR-0118), by a person, not merely emitted.
- **FR-009**: After this feature, **no** publisher of an integration event may
  begin as an orphan. The survey MUST be complete and recorded, not sampled.

### Key Entities

- **Journey**: everything that happened because one observation was made, across
  every service it touched.
- **Origin**: the work that begins a journey — here, noticing one camera's state
  changed, or archiving one chunk.
- **Orphan**: an announcement with no recorded cause, so nothing downstream can
  be traced back to it.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Starting from the record of a camera going unhealthy, someone who
  did not build the system can name the observation that caused it, using only
  recorded telemetry.
- **SC-002**: The same holds for an archived audit chunk.
- **SC-003**: Two cameras changing state in one sweep produce **two** journeys,
  and two chunks in one run produce two.
- **SC-004**: An announcement that fails is distinguishable from one that
  succeeded and caused nothing, by looking at it.
- **SC-005**: A sweep in which nothing changed produces no journeys.
- **SC-006**: Health poll cadence and retention duration are no worse than
  recorded before the change, measured the same way and **measured more than
  once**.
- **SC-007**: Both relationships are followable **in the project's actual sink**,
  by someone reading it rather than by a test asserting it exists.
- **SC-008**: Every publisher of an integration event in the system is accounted
  for in writing, as either having a cause or not needing one.
- **SC-009**: The full test suite passes with nothing excluded or weakened.

---

## Assumptions

- **The mechanism from spec 026 is reused unchanged.** It is registered for every
  context already; two call sites are missing, not the machinery. **If this
  feature grows past two call sites and their tests, the diagnosis is wrong**
  rather than the estimate.
- **Per item, not per loop.** FR-003 and SC-003 exist because the cheap version
  is per-loop and would look like it worked.
- **Failure marking is required at both sites**, because omitting it is the
  defect spec 026's review found and closed, and reintroducing it in new code
  would be worse than never having fixed it.
- **Cost is small but not zero.** Measured under FR-007, and measured twice —
  spec 026 nearly reported a regression that did not exist from a single
  contaminated run.
- **No cross-service test is possible.** Each service runs as its own process and
  the telemetry sink has no query interface, so the automated coverage is
  per-service and the cross-service proof is a person looking. Established by
  spec 026; not re-litigated here.

---

## Out of scope

- **Which plant these events belong to.** Neither announcement carries a fab, and
  settling that is #1155's remaining work. It would widen this feature into a
  contract change.
- **The unbuilt latency legs** — #1714.
- **A production telemetry sink** — deferred by ADR-0118.
- **Stopping the next orphan from being written.** FR-009 makes the current state
  complete and recorded; it does not make it enforceable. Whether that deserves
  an architecture test is a real question and a separate one.
