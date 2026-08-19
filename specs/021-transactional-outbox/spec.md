# Feature Specification: An integration event is never lost after its write commits

**Feature Branch**: `021-transactional-outbox`

**Created**: 2026-08-19

**Status**: Draft

**Input**: Issue #1605, raised from the code review of spec 020 (PR #1604).

## Why this exists

A write and the announcement of that write are two separate acts today, and only
the first is durable. The row is committed, then the announcement is attempted;
if the announcement fails, the row stays and the announcement is gone. Nothing
retries it, nobody is told, and the system looks correct from every angle
anybody would think to check — because the thing that was written **is there**.

That last part is what makes this worth a feature rather than a patch. An event
that was lost on the way in leaves a hole someone can find. An event whose
announcement was lost leaves no hole at all: it is in the database, it comes
back from the read API, and it simply never happened as far as every other part
of the product is concerned.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A stored event always reaches the contexts that act on it (Priority: P1)

An operator's plant floor produces an event. It is stored. The automation rules
that decide what the operator sees on the kiosk are driven by an announcement of
that event, and so is the audit record that says it happened. Today, if the
announcement fails at the moment of writing, the event is stored and neither
happens — no rule fires, no audit entry appears — and there is no retry and no
record of the omission.

**Why this priority**: It is the whole defect. Every other story here is a
consequence of this one or a way of proving it.

**Independent Test**: Make the announcement fail while the write succeeds, then
confirm the event still reaches the contexts that act on it, without anyone
intervening.

**Acceptance Scenarios**:

1. **Given** an event is stored successfully, **When** its announcement cannot
   be delivered at that moment, **Then** the announcement is delivered later
   without human intervention.
2. **Given** an announcement was delayed by a failure, **When** delivery
   eventually succeeds, **Then** the contexts that act on it behave exactly as
   if it had arrived first time.
3. **Given** the write itself fails, **When** the transaction is rolled back,
   **Then** no announcement is delivered for an event that does not exist.

---

### User Story 2 - The gap is closed everywhere, not only where it was found (Priority: P2)

The defect was found on the event-ingest path because that path is under load
and has just been scrutinised. It is not specific to it: every context writes
and then announces the same way. A camera registered, a layout published, a rule
activated, a stream started — each has the same window, and each has downstream
consumers that would silently miss the news.

**Why this priority**: Fixing one path and leaving eight identical ones is worse
than fixing none, because it makes the remaining eight look deliberate.

**Independent Test**: Take a context other than event ingest, make its
announcement fail after a successful write, and confirm the same guarantee
holds.

**Acceptance Scenarios**:

1. **Given** any context that writes and announces, **When** its announcement
   fails after the write commits, **Then** the announcement is delivered later.
2. **Given** a context that deliberately does not participate, **When** the
   change is reviewed, **Then** its reason is recorded rather than left as an
   omission.

---

### User Story 3 - The guarantee is written down accurately (Priority: P3)

The project already records a decision that reads as though this guarantee is in
place. It describes a durable outbox that prevents message loss. That decision
is real and its machinery is configured, but it covers announcements made while
handling an incoming message — not announcements made while serving a request or
draining a queue, which is where every one of these writes originates.

Anyone reading the decision today would conclude the system already has the
property this feature adds. That is the most expensive kind of documentation
error, because it stops people looking.

**Why this priority**: The code can be fixed without touching it, and then the
next person inherits a record that was wrong in a way nobody noticed for a year.

**Independent Test**: Read the recorded decision after the change and confirm it
describes what the system actually guarantees and where.

**Acceptance Scenarios**:

1. **Given** the change is complete, **When** the architectural record is read,
   **Then** it states which announcements are covered and which are not.
2. **Given** a reader wants to know whether a new write path is covered,
   **When** they consult the record, **Then** it tells them how to make it so.

---

### Edge Cases

- **The announcement fails and the process then dies.** The pending announcement
  must survive the restart; a guarantee that only holds while the process lives
  is the one being replaced.
- **The announcement is delivered but the delivery is never confirmed.** The
  same announcement may arrive twice. Consumers already tolerate this where it
  matters, and this feature must not make duplicates unusual enough that they
  stop being handled.
- **The announcement can never be delivered** — malformed, or a consumer that
  will always refuse it. Retrying for ever must not become its own outage, and
  whatever bound applies must be visible rather than implied.
- **A write produces several announcements and one of them fails.** The others
  must not be lost on its account.
- **The store of pending announcements grows without bound** because delivery is
  failing steadily. There must be a way to see that happening before it becomes
  a disk problem.
- **A write path that nobody updates.** A context added later, or a new write
  path in an existing one, must not silently fall outside the guarantee.

## Requirements *(mandatory)*

### The guarantee

- **FR-001**: A committed write and the announcements it produces MUST be
  all-or-nothing: if the write is visible, its announcements are eventually
  delivered; if the write is rolled back, none are delivered.
- **FR-002**: An announcement that cannot be delivered when the write commits
  MUST be delivered later, without human intervention.
- **FR-003**: A pending announcement MUST survive a restart of the service that
  produced it.
- **FR-004**: Delivery MUST be at-least-once. Ordering between announcements is
  NOT guaranteed by this feature.

### Coverage

- **FR-005**: Every write path that announces MUST be covered — all nine
  repositories across the bounded contexts, not only the one where the defect
  was found.
- **FR-006**: Any write path deliberately left uncovered MUST have its reason
  recorded where the reader will find it.
- **FR-007**: A write path added later MUST either be covered by default or fail
  visibly, rather than silently losing the guarantee.

### Being able to see it

- **FR-008**: Announcements waiting to be delivered MUST be observable — how
  many, and how long the oldest has waited.
- **FR-009**: Repeated delivery failure MUST be reported, not merely retried.
- **FR-010**: If an announcement is given up on, that MUST be recorded durably
  and countably before it stops being retried.

### What must not get worse

- **FR-011**: Write latency MUST stay within the budget the affected paths
  already have. The ingest path's share of the end-to-end budget
  (constitution §IV) is the binding one.
- **FR-012**: Sustained ingest throughput MUST NOT fall below its currently
  measured figure, measured the same way as spec 020 measured it.
- **FR-013**: The behaviour a caller sees on success or failure of a write MUST
  NOT change. This feature is invisible from outside except that announcements
  stop going missing.

### The record

- **FR-014**: The architectural record MUST state what is guaranteed and for
  which announcements, so that a reader can tell whether a given write path is
  covered.

## Key Entities

- **Announcement (integration event)**: what one context tells the others has
  happened. Produced by a write, consumed by other contexts. Today it exists
  only in flight; this feature gives it a durable existence between being
  produced and being delivered.
- **Pending announcement**: an announcement that has been produced and not yet
  delivered. It is the thing that must share the write's fate and survive a
  restart.
- **Write path**: a place that changes state and announces it. Nine today, one
  or two per bounded context.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With announcement delivery made to fail for 60 seconds during a
  sustained write load, **100%** of committed writes have their announcements
  delivered after recovery, with no human intervention.
- **SC-002**: With the service killed between a commit and its announcement,
  **zero** committed writes are left without their announcement after restart.
- **SC-003**: A write that is rolled back produces **zero** announcements —
  under any failure injected in testing.
- **SC-004**: The guarantee holds on **every** write path that announces, or the
  exceptions are listed with reasons.
- **SC-005**: Sustained ingest throughput is **no lower than** the figure spec
  020 measured, and write latency stays inside its existing budget.
- **SC-006**: A backlog of undelivered announcements is visible from the record
  alone: how many and how old, without attaching a debugger.
- **SC-007**: A reader of the architectural record can correctly answer "is this
  write path covered?" for a path they have not seen before.

## Assumptions

- **The mechanism is already chosen.** ADR-0088 commits to a durable outbox held
  in the same database as the write. This feature is about applying it where it
  is not applied, not about choosing it again. If the plan finds that decision
  cannot be applied as written, that is an ADR amendment and a gate, not a
  silent substitution.
- **Consumers tolerate duplicates.** At-least-once delivery is assumed
  acceptable because the receiving contexts already deduplicate where it matters
  (spec 006 FR-002). This feature does not add deduplication.
- **Ordering is not required.** No consumer today depends on the relative order
  of two announcements from different writes. If one does, it is out of scope
  and must be raised separately.
- **The database is the durable store.** No new infrastructure is introduced;
  pending announcements live where the write lives, which is what makes them
  able to share its fate.
- **Spec 020's mitigation stays.** Raising all dispatch failures together rather
  than stopping at the first remains correct and is not undone by this feature.

## Out of scope

- **Deduplication of announcements.** Consumers own that, and already do it.
- **Ordering guarantees** between announcements.
- **The inbound side.** Messages already being handled by the messaging
  infrastructure are covered by the existing configuration; this feature is
  about the outbound edge of a write.
- **Changing what any announcement contains.** No contract in `Shared.Contracts`
  changes shape or version.
