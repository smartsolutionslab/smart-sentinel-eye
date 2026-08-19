# Feature Specification: The first event after a restart reaches its effect in time

**Feature Branch**: `023-first-event-cold-start`

**Created**: 2026-08-19

**Status**: Draft

**Input**: #1655 — "The first event after a restart takes 12-14 s to reach its effect"

---

## Why this exists

A fab restarts a service. The line keeps running. The first thing that happens
on the plant floor after that restart takes **twelve to fourteen seconds** to
change what an operator sees. Everything after it takes about **three tenths of
a second**.

Nobody noticed because nothing measured it. Spec 022 added the first test that
times an event from the broker to its effect, and the number fell out of it.

The budget for that leg is **200 ms** (constitution §IV). Thirteen seconds is not
a near miss, and a 24/7 system with rolling restarts is not a system that visits
this state rarely.

**The primary deliverable here is an explanation, not a smaller number.** A
warm-up that makes the figure drop without anyone knowing what the seconds were
would leave us unable to tell whether it is fixed, moved, or merely hidden — and
this programme has already had one green build that meant nothing.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Someone can say where the seconds go (Priority: P1)

An engineer looking at the restart gap can name which part of the journey owns
it, with evidence, rather than guessing from four plausible candidates.

Today the honest answer is "somewhere between the broker and the variable, in a
chain crossing four services". That is not enough to fix anything, and it is not
enough to decide the thing is acceptable either.

**Why this priority**: Every other outcome depends on it. A fix chosen without it
is a guess that happens to change a number, and cannot be defended when the
number moves again. It is also independently valuable: if the answer turns out to
be "nothing we control", that is a finding worth having.

**Independent Test**: Restart the stack, send one event, and read off a breakdown
that attributes the elapsed time to named stages of the journey. Delivers value
even if nothing is then changed.

**Acceptance Scenarios**:

1. **Given** a freshly started stack, **When** the first event arrives, **Then**
   the elapsed time is attributed across the stages of the journey, and the stage
   holding the largest share is identified by name.
2. **Given** that breakdown, **When** the second and third events arrive,
   **Then** the same stages are reported for them, so the **staged decay**
   (~13 s → ~4 s → ~0.3 s) is visible as a change in specific stages rather than
   as a single aggregate falling.
3. **Given** a candidate cause has been named, **When** it is tested directly,
   **Then** the result is recorded whether it confirms or refutes the candidate.

---

### User Story 2 — The first event is no longer an outlier (Priority: P2)

After a restart, the first event reaches its effect in a time comparable to every
other event, so an operator cannot tell from the screen whether a service
restarted a moment ago.

**Why this priority**: This is the outcome the fab actually cares about, but it is
P2 because it is conditional: it can only be pursued once US1 says what to
address, and it may turn out to be unreachable for a defensible reason.

**Independent Test**: Restart the stack, send one event, and compare its
arrival-to-effect against the steady-state figure from the same run.

**Acceptance Scenarios**:

1. **Given** a freshly started stack, **When** the first event arrives, **Then**
   its arrival-to-effect is materially closer to the steady-state figure than the
   twelve-to-fourteen seconds recorded today.
2. **Given** the change, **When** steady-state events are measured, **Then** they
   are no slower than before it.
3. **Given** the change moves work into startup, **When** a service starts,
   **Then** it does not report itself ready before it can serve, and the added
   startup time is stated.

---

### User Story 3 — What a fab should expect after a restart is written down (Priority: P3)

Whatever the outcome, the restart behaviour is recorded where someone
commissioning or operating a fab would find it, in place of the current silence.

**Why this priority**: It is the fallback that makes the feature worth finishing
even if US2 proves impossible — and if US2 succeeds, the same note records the
new expectation and how it was established.

**Independent Test**: Read the resulting note and answer "what happens to the
first event after I restart a service?" without running anything.

**Acceptance Scenarios**:

1. **Given** the investigation is complete, **When** its findings are recorded,
   **Then** they state the measured figures, what they establish, and explicitly
   what they do not.
2. **Given** the gap cannot be closed, **When** that is recorded, **Then** the
   reason is stated, along with the residual risk to the latency budget.

---

### Edge Cases

- **A restart under load rather than at idle.** The measurement is taken on an
  idle stack; a service restarting while events are already queued may behave
  differently, and which case is worse is not currently known.
- **One service restarting rather than all of them.** A rolling restart replaces
  services one at a time. The cost may belong to a single service, in which case
  restarting that one alone reproduces it and restarting others does not.
- **The second and third events.** The decay means "first event" understates it:
  if the second event still costs seconds, the affected window is longer than one
  event and should be described in events or seconds, not as "the first one".
- **A warm-up that hides rather than removes.** Work moved into startup still
  costs something; if it delays readiness, a rolling restart could stall or a
  probe could fail. Where the cost lands must be stated.
- **An empty system.** If a stage is slow only when it has no rules, no
  partitions or no cached state, the fixture's freshly-seeded stack may not
  resemble a fab that has been running for months.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The elapsed time between an event arriving and its effect becoming
  observable MUST be attributable to named stages of the journey, for the first
  event after a restart.
- **FR-002**: The attribution MUST cover the second and subsequent events as
  well, so the staged decay is explained rather than only the peak.
- **FR-003**: Each candidate cause investigated MUST have its result recorded,
  including candidates that are refuted.
- **FR-004**: The measurement MUST be reproducible by someone else from the
  recorded instructions.
- **FR-005**: The system MUST NOT become slower in steady state as a result of
  any change made here.
- **FR-006**: Any work moved into service startup MUST NOT cause a service to
  report itself ready before it can serve requests.
- **FR-007**: If startup time increases, the increase MUST be stated.
- **FR-008**: No existing test may be weakened, excluded or removed in order to
  improve a measured figure.
- **FR-009**: The findings MUST state what the measurements establish and what
  they do not, given they are taken on a development fixture rather than a fab.
- **FR-010**: If the gap cannot be closed, the reason and the residual risk MUST
  be recorded.
- **FR-011**: The existing arrival-to-effect measurement from spec 022 MUST be
  reused rather than replaced by a parallel harness.

### Key Entities

- **Arrival-to-effect**: the elapsed time from an event entering the system at
  the broker to its effect being observable through the interfaces an operator
  sees. Already measured by spec 022.
- **Stage**: a named part of that journey to which elapsed time can be
  attributed — receiving, storing, announcing, deciding, applying.
- **Restart**: a service or stack starting fresh, as after a deployment or a pod
  replacement. The state in which the cost appears.
- **Steady state**: the same journey once the system has handled events, against
  which the first event is compared.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For the first event after a restart, the elapsed time is
  attributed to named stages, and the stage owning the largest share is
  identified — with the attribution accounting for **at least 80%** of the
  measured time rather than leaving most of it unexplained.
- **SC-002**: The staged decay is explained: the reported stages account for why
  the second event costs seconds and the third costs tenths.
- **SC-003**: Every candidate cause listed in #1655 is either confirmed or
  refuted in writing.
- **SC-004**: The first event after a restart reaches its effect in **under
  1 second**, or the reason it cannot is recorded with its residual risk.
- **SC-005**: Steady-state arrival-to-effect after any change is no worse than
  the figures recorded in spec 022 (267–348 ms).
- **SC-006**: The whole integration suite passes, with no test excluded or
  weakened relative to today.
- **SC-007**: Someone who did not do the work can reproduce the headline
  measurement from the written instructions.

---

## Assumptions

- **The fixture's "first event after startup" corresponds to a fab's "first
  event after a deployment or restart".** Both are a freshly started stack whose
  first real work is this event. Taken as the premise of the whole feature; if
  the investigation shows the fixture's cold state is unrepresentative — because
  it is also freshly seeded, or because nine services share one host — that
  finding supersedes it and belongs in the record.
- **Under 1 second is the target for SC-004, not 200 ms.** The 200 ms budget is
  for the steady-state leg. Insisting on it for the first event after a restart
  would likely force pre-warming of every path in every service, which is a much
  larger change than the evidence currently justifies. One second is the point at
  which the restart stops being visible as a stall; if the investigation shows
  200 ms is cheaply reachable, better.
- **The staged decay implies more than one cause.** A single lazy initialisation
  would drop straight to warm. The feature is therefore scoped to explain a
  curve, and may end up with several small findings rather than one culprit.
- **The rule cache is unlikely to be the cause in the measured scenario.** It is
  populated when a rule is published, and the measurement publishes its rule
  seconds before the event. Recorded as a prior, not a conclusion — it still gets
  confirmed or refuted under FR-003, and a restart with pre-existing rules is a
  different case that may behave differently.
- **This may end in no production change.** "Understood, recorded, accepted" is a
  legitimate outcome. The feature is not obliged to make the number smaller, only
  to stop it being unexplained.
- **Existing behaviour is otherwise correct.** Nothing here changes what events
  do, only how long the first one takes.

---

## Out of scope

- **Throughput and sustained load.** Specs 020 and 021 cover those; this is about
  a single event in a specific state.
- **The other legs of the end-to-end budget.** Camera-to-SFU, decode, and render
  are untouched.
- **Startup time of the stack as a whole.** Only the part that makes the first
  *event* slow is in scope; a service that takes a while to start but then serves
  immediately is a different concern.
- **Production capacity planning.** Numbers here come from a development fixture
  and do not establish fab behaviour.
