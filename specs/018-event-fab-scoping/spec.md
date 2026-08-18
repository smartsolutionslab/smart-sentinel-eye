# Feature Specification: Fab-scope event ingestion

**Feature Branch**: `018-event-fab-scoping`

**Created**: 2026-08-16

**Status**: Draft

**Input**: #1155 — per-fab isolation. The last unguarded operator-facing surface, and the most consequential. Follows specs 013 (rules), 014 (variables), 015 (cameras), 016 (streams), 017 (layouts).

## Why this exists

An operator with permission to read events can read **any plant's** events by
naming the plant. An operator with permission to write them can **inject
events into any plant**, where they drive that plant's automation rules and
change what appears on its screens.

Both because the fab is taken from the request and never checked against who
is asking.

## What makes this different from specs 013–017

Those five each gave something a fab that did not have one. **This context
already has one, and already filters on it.** The event carries a fab, the
read handlers narrow by it:

> `events.Where(eventEntity => eventEntity.Fab == query.Fab)`

So it looks finished. What is missing is one step earlier: nobody checks that
the caller is entitled to the fab they named. The filter is not scoping — it
is a parameter the caller supplies.

**That is why this survived five features aimed squarely at it.** A reviewer
looking for "does this context model a fab" finds yes. A reviewer looking for
"does it filter" finds yes. Only "where does the fab come from" finds the
hole.

**And it is worse in kind than what specs 013–017 closed.** A layout frame
told another plant that something existed. These endpoints return the ingested
production data itself — and the write lets one plant fabricate events in
another.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - An operator reads only their own plant's events (Priority: P1)

An operator assigned to Dresden lists events and opens one. Munich's are
neither listed nor reachable, whatever they put in the request.

**Why this priority**: It is the leak, and it is live today.

**Independent Test**: As a Dresden-only operator, list events naming Munich,
and request a known Munich event by identifier.

**Acceptance Scenarios**:

1. **Given** events exist in both fabs, **When** a Dresden-only operator
   lists them, **Then** only Dresden's appear.
2. **Given** a Dresden-only operator, **When** they name Munich explicitly,
   **Then** they are refused rather than served Munich's events.
3. **Given** a Munich event's identifier, **When** a Dresden-only operator
   requests it, **Then** the response is indistinguishable from an event that
   never existed.
4. **Given** a multi-fab operator, **When** they list without naming a fab,
   **Then** they see every fab they hold — a read does not have to choose.
5. **Given** an operator assigned to no fab, **When** they list, **Then** they
   are refused rather than shown an empty list.

---

### User Story 2 - An operator cannot inject events into another plant (Priority: P1)

An operator submitting an event by hand can only file it against a plant they
belong to.

**Why this priority**: P1 and arguably ahead of US1. Reading another plant's
data is a disclosure; writing into another plant is a **manipulation** — an
injected event drives that plant's automation rules and changes what its
operators see on screen. It is the only place in the product where one fab can
alter another's state.

**Independent Test**: As a Dresden-only operator, submit a manual event naming
Munich.

**Acceptance Scenarios**:

1. **Given** an operator holding exactly one fab, **When** they submit without
   naming one, **Then** the event is filed against their fab.
2. **Given** an operator holding several, **When** they submit without naming
   one, **Then** they are refused and asked to name it rather than having one
   chosen for them.
3. **Given** an operator naming a fab they do not hold, **When** they submit,
   **Then** they are refused as forbidden, and **no event is ingested**.
4. **Given** an operator holding no fab, **When** they submit, **Then** they
   are refused.

---

### User Story 3 - Failed events are visible only to the plant they came from (Priority: P1)

An operator reviewing rejected ingest sees only their own plant's, and never
one whose origin cannot be established.

**Why this priority**: P1 because a rejected event carries its **raw payload**
— the production data verbatim, and unvalidated. It is currently visible to
every operator with read permission, with no way to narrow it at all.

**Independent Test**: Reject one delivery per fab, plus one on a malformed
topic, and read the list as each operator.

**Acceptance Scenarios**:

1. **Given** rejected deliveries from both fabs, **When** a Dresden-only
   operator lists them, **Then** only Dresden's appear.
2. **Given** a delivery whose topic was well-formed but whose payload was
   rejected, **Then** its plant is established from the topic and its own
   plant's operators can see it.
3. **Given** a delivery rejected because its **topic** was malformed, **Then**
   its origin cannot be established and it is shown to nobody — not to a
   single-fab operator, and not to one holding every fab.
4. **Given** any number of such deliveries, **When** an operator responsible
   for the system looks, **Then** they can see *how many* there are without
   being able to read any of them.

---

### Edge Cases

- **A caller naming a fab they do not hold**: refused as forbidden on both the
  read and the write. They named a *fab*, so the answer is about the fab and
  hides nothing.
- **An event in another fab addressed by identifier**: reported exactly as one
  that never existed — never as refused-because-forbidden.
- **A rejected delivery whose topic will not parse**: has no determinable
  plant. It exists *because* something was malformed, so this is a real case
  rather than a theoretical one, and it must fail closed — invisible to
  everyone, with only its count surfaced (FR-011, FR-012).
- **A rejected delivery whose topic parsed but whose payload did not**: the
  common case, and it *does* have a plant. The two must not be conflated: only
  a malformed address leaves the origin unknown.
- **The webhook ingress**: already establishes the plant from the caller's own
  credentials, and is deliberately left alone. See Assumptions.
- **Machine ingest over the message broker**: unchanged. The plant is part of
  the delivery address and the broker already enforces it.

## Requirements *(mandatory)*

### The reads

- **FR-001**: Listing events MUST return only events in fabs the caller holds,
  whatever fab the request names.
- **FR-002**: Naming a fab the caller does not hold MUST be refused rather
  than served.
- **FR-003**: A caller may narrow a listing to one of their own fabs; omitting
  a fab MUST span all of them rather than being refused.
- **FR-004**: An event in a fab the caller does not hold MUST be reported
  exactly as one that never existed.
- **FR-005**: An operator holding no fab MUST be refused rather than shown an
  empty result.

### The manual write

- **FR-006**: Submitting an event by hand MUST file it against a fab the
  caller holds, resolved from the caller: inferred when they hold exactly one,
  taken from an explicitly named fab when they hold several, refused when they
  hold several and name none, and refused as forbidden when they name one they
  do not hold.
- **FR-007**: A refused submission MUST ingest nothing. Partial acceptance
  would place a fabricated event in another plant's stream.

### The rejected deliveries

- **FR-008**: A rejected delivery MUST record which plant it came from, when
  that can be established from the delivery address.
- **FR-009**: Listing rejected deliveries MUST return only those from fabs the
  caller holds.
- **FR-010**: A rejected delivery whose plant cannot be established MUST NOT
  be attributed to any plant, and MUST NOT be shown as though it belonged to
  one.
- **FR-011**: A rejected delivery whose plant cannot be established MUST be
  visible to **nobody**, through any listing, and MUST NOT be defaulted to any
  plant.

  *The cost, accepted rather than overlooked*: such a delivery is then
  undiagnosable through this list, and diagnosing bad ingest is what the list
  is for. That is the fail-closed direction — an unattributed raw payload is
  production data of unknown origin, and showing it to the wrong plant is
  worse than showing it to no one — but it leaves a real gap, which FR-012
  closes by another route.

- **FR-012**: The number of rejected deliveries whose plant cannot be
  established MUST be recorded where an operator responsible for the system
  will see it, without exposing their content. Invisible is acceptable;
  invisible *and* unnoticed is not.
- **FR-013**: Rejected deliveries recorded before this feature MUST acquire
  their plant where it can be established from the address already stored, and
  MUST NOT be guessed where it cannot.

### Unchanged, deliberately

- **FR-014**: The webhook ingress MUST continue to establish the plant from
  the caller's own credentials rather than from an operator's session, and
  MUST NOT be changed by this feature. **Amended — see FR-016.** It established
  the plant in only one of its two validation modes, so "continue to" described
  something that was not happening.
- **FR-015**: The broker ingress MUST be unchanged. The plant is part of the
  delivery address and is already enforced there.
- **FR-016**: The webhook **integration registry** MUST be unchanged by this
  feature. Whether an integration should belong to a plant, or stay a shared
  template whose entitlement is proven per delivery, is a real question with
  two coherent answers — and answering it here would widen a feature whose
  purpose is closing a live leak. Tracked separately.

  > **AMENDED 2026-08-18, after phase-6 review (#1545).** FR-014 and FR-016
  > together rested on a premise that turned out to be false: that the webhook
  > ingress *does* establish the plant from the caller's own credentials. It
  > does so only in `BearerValidationMode.Jwt`. In `StaticHash` — the default,
  > and the mode of every integration until it is rotated — the token hash is
  > matched and `?fabId=` is never consulted, because the integration carried no
  > plant to compare against. A token issued for one plant could file events
  > into another: the same manipulation FR-006 closes on the manual write.
  >
  > This feature also *made that reachable*. A cross-fab write previously failed
  > at the insert because `events` had no partition for any fab but munich;
  > adding one for dresden was necessary and removed an accidental backstop.
  >
  > So the question FR-016 deferred is answered here rather than deferred
  > again, and the answer is the first reading: **an integration belongs to the
  > plant that registered it.** It gains a `FabIdentifier`, the ingress refuses
  > any delivery naming another plant in **both** validation modes, and the
  > registry's own three endpoints are scoped like every other operator-facing
  > read and write. Deferring a second time would have meant shipping a feature
  > whose stated purpose is closing cross-fab writes while knowingly leaving one
  > open.
  >
  > **Still deferred**, because neither is a tenancy hole: integration names stay
  > globally unique rather than per-fab (the name is the ingest route's path
  > segment, and per-fab names would make that route ambiguous), so registering a
  > name taken in another plant still answers `409` and thereby discloses that it
  > exists. Recorded on #1545.

### Key Entities

- **Event** — already carries a plant. Unchanged; only who may ask for it
  changes.
- **Rejected delivery** — gains a plant, established from the delivery address
  where possible and absent where not.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator assigned to one plant sees, in every event listing,
  only that plant's events — 100% of rows — including when they explicitly
  name another plant.
- **SC-002**: A request for another plant's event is indistinguishable from a
  request for one that never existed, compared field by field rather than by
  status alone.
- **SC-003**: No operator can cause an event to be filed against a plant they
  do not belong to — 100% of attempts refused, with nothing ingested.
- **SC-004**: An operator assigned to one plant sees, in the rejected-delivery
  list, only that plant's — and never one whose origin is unestablished.
- **SC-005**: Every rejected delivery recorded before this change either
  carries the plant its address establishes, or is recorded as having none —
  none is guessed.
- **SC-006**: Machine ingest over the broker and the webhook is unaffected: the
  same deliveries succeed, at the same rate, before and after.
- **SC-007**: The count of rejected deliveries with no establishable plant is
  observable without their content being readable by anyone.

## Assumptions

- **The fab already exists on the event; only its provenance changes.** This
  is not a modelling feature. The event carries a fab and the queries filter on
  it — the fix is that the fab must come from the caller's entitlements rather
  than from the request.
- **The read keeps its optional narrowing.** Omitting a fab spans everything
  the caller holds; naming one narrows to it, and naming one they do not hold
  is refused. That is the same asymmetry with the write path that specs 013–017
  established, and the request shape does not change for a caller who was using
  it legitimately.
- **The rejected delivery's plant is derived, not asked for.** Nothing authors
  a rejected delivery — it exists because an ingest failed — so its plant comes
  from the address it arrived on, exactly as spec 016 derived a stream's fab
  from its camera. Where the address itself is the malformed part, there is
  nothing to derive from, which is what FR-010 and FR-011 are about.
- **The pre-existing rejected deliveries can be attributed without guessing.**
  Their delivery address is already stored, so the plant is recoverable from
  data this context already holds — unlike spec 016, which had to derive at
  runtime from another context's database.
- **The integration registry is deliberately left for a separate decision.**
  Whether a webhook integration belongs to a plant or is a shared template is a
  real question — the per-delivery credential check already proves entitlement,
  so both answers are coherent — and settling it here would widen a feature
  whose job is closing a live leak. Recorded as FR-016 so its absence reads as
  a decision rather than an oversight.
- **The webhook ingress is exempt for the same reason spec 016 exempted the
  media-server callback.** Its caller is a machine presenting its own
  credentials, not an operator with a session, and it already checks the plant
  against those credentials. Recorded here rather than left as an unexamined
  fourth endpoint.
