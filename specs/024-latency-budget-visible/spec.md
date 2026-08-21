# Feature Specification: Every leg of the latency budget can be watched

**Feature Branch**: `024-latency-budget-visible`

**Created**: 2026-08-21

**Status**: Draft

**Input**: #1681 — "No latency-budget leg has a dashboard, which §VII forbids shipping without"

---

## Why this exists

The product's central promise is a number: something happens on the plant floor
and an operator sees it **within 800 ms**. That number is split into six legs,
each with its own budget, and the constitution calls the budget sacred.

**Nothing measures any of it.**

Not a dashboard missing from an otherwise working pipeline — there is no
pipeline. No part of the system records how long any leg takes. The six budgets
have been asserted in a document and enforced by nobody since the first feature
shipped.

The cost of that is already on record. Spec 023 found the first event after a
cold start taking **twelve to fourteen seconds against a 200 ms leg** — two
orders of magnitude — and it went unnoticed until a test written for an entirely
different purpose happened to time it. Nobody was hiding it. Nothing was looking.

**A budget nothing measures is not a budget. It is a hope with a number on it.**

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Someone can find out whether a leg is holding (Priority: P1)

An engineer or an operator can ask "is the event-to-overlay leg within its
200 ms budget right now?" and get an answer from the system, rather than by
writing a test or reasoning from first principles.

**Why this priority**: Everything else here depends on the measurement existing.
A dashboard over nothing is a picture of nothing, and the PR-time check in US3
has nothing to check. It is also the part that would have caught spec 023's
finding a year earlier.

**Independent Test**: Run the system, put load through a leg, and read that
leg's latency back out of it. Delivers value with no dashboard at all — the
number becomes obtainable, which today it is not.

**Acceptance Scenarios**:

1. **Given** the system is running, **When** work crosses an instrumented leg,
   **Then** that leg's latency is recorded as a **distribution**, not a single
   most-recent value — a budget is a statement about the tail.
2. **Given** recorded latency, **When** someone asks how a leg is doing,
   **Then** they can obtain at least a median and a high percentile without
   writing new code.
3. **Given** a leg is instrumented, **When** its latency is compared to its
   budget, **Then** the budget is part of what is reported, so a reader who does
   not know the constitution can still tell a pass from a miss.

---

### User Story 2 — A leg can be watched without being asked about (Priority: P2)

Someone responsible for a fab can see each leg against its budget on a
dashboard, and notice a leg degrading without having gone looking for it.

**Why this priority**: This is what §VII actually requires, and what turns a
measurement into something that catches a regression. It is P2 only because it
is meaningless before US1.

**Independent Test**: Open the dashboard, read each instrumented leg against its
budget without running anything.

**Acceptance Scenarios**:

1. **Given** instrumented legs, **When** the dashboard is opened, **Then** each
   shows its current latency against its budget.
2. **Given** a leg is exceeding its budget, **When** someone looks, **Then**
   that is visible as a breach rather than as a number they must interpret.
3. **Given** the end-to-end SLO of 800 ms, **When** the legs are shown together,
   **Then** their relationship to the whole is visible — a leg passing while the
   total fails is a case someone must be able to see.

---

### User Story 3 — A change can show it did not break the budget (Priority: P3)

Someone changing code on the event-to-overlay path can demonstrate the budget
still holds, which §IV already requires of every such PR and which no one has
been able to do.

**Why this priority**: It closes a governance requirement that is currently
unmeetable, and it is the difference between noticing a regression in production
and preventing it. P3 because it depends on both the measurement and a stable
way to read it.

**Independent Test**: Make a change on that path, produce the evidence, and have
a reviewer read it without rerunning anything.

**Acceptance Scenarios**:

1. **Given** a change on the event-to-overlay path, **When** its author needs to
   satisfy §IV, **Then** a repeatable procedure produces a latency figure for
   the affected leg.
2. **Given** such a figure, **When** it is put on a PR, **Then** it states what
   it establishes and what it does not — the fixture is not a fab, and specs
   020, 022 and 023 all had to say so.

---

### Edge Cases

- **A leg nobody can instrument from the server.** Decode, presentation buffer
  and composite-and-render happen in a browser on a kiosk. Nothing in the
  frontend measures latency today and there is no route from a browser to any
  telemetry sink. Whether those legs can be measured at all is an open question
  this feature must answer rather than assume.
- **Measuring changes the thing measured.** Composite-and-render has a 50 ms
  budget; instrumentation that costs a millisecond has spent 2% of it. The
  smallest budgets are the least able to absorb their own observation.
- **A leg with no traffic.** An idle system produces no measurements, and "no
  data" must not render as "healthy" — the spec 023 lesson, where a failed
  measurement printed identically to a successful one.
- **Cold versus steady state.** #1655 established that the first event of each
  message type costs seconds. A dashboard averaging that in will show a fab
  breaching its budget every time it restarts, which is either the truth worth
  showing or noise worth separating, and someone must decide which.
- **Clocks.** Legs that span two machines need their ends compared. Fabs run
  PTP, and the kiosk deliberately uses monotonic time rather than wall clock
  because PTP steps it. A latency computed across a stepped clock can be
  negative.
- **Retention.** A budget breach noticed on Monday about Friday needs Friday's
  data to still exist.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Each instrumented leg MUST record its latency as an aggregatable
  distribution, not only as individual traces.
- **FR-002**: Each leg's recorded latency MUST be readable without writing new
  code.
- **FR-003**: The budget for a leg MUST be reported alongside its measurement,
  so a breach is legible without external reference.
- **FR-004**: Each instrumented leg MUST be visible on a dashboard showing it
  against its budget.
- **FR-005**: The dashboard MUST distinguish "no data" from "within budget".
- **FR-006**: Instrumentation MUST NOT consume a material share of the budget it
  measures; the cost MUST be measured and stated rather than assumed negligible.
- **FR-007**: For every leg **not** instrumented by this feature, the reason MUST
  be recorded — what was tried, what blocked it, and what would unblock it.
- **FR-008**: A repeatable procedure MUST exist for producing a latency figure
  for the event-to-overlay leg, suitable for citing on a PR (§IV).
- **FR-009**: Any figure produced MUST be accompanied by what it does and does
  not establish, given it is taken outside a fab.
- **FR-010**: Where a measurement spans two machines, the approach MUST state how
  clock differences are handled, or state that the measurement is single-machine.
- **FR-011**: The observability decision recorded in ADR-0026 MUST be either
  enacted or amended to describe what is actually built — it currently describes
  a comparison phase that never started.
- **FR-012**: No existing behaviour may regress in latency as a result of this
  feature, and that MUST be verified rather than assumed.

### Key Entities

- **Leg**: one of the six segments of the end-to-end path, each with a budget
  from ADR-0015.
- **Budget**: the maximum time a leg may take. Six of them, summing with
  headroom to the 800 ms SLO.
- **Latency measurement**: an observation of how long one traversal of a leg
  took, aggregatable across many traversals.
- **Dashboard**: where a leg's measurements are read against its budget without
  running anything.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The `event → overlay state` leg's latency can be read from the
  running system, as a distribution including a high percentile, without writing
  code.
- **SC-002**: At least one dashboard shows a leg against its budget, and a
  reader who does not know the constitution can tell a pass from a breach.
- **SC-003**: Every one of the six legs has either a measurement **or** a written
  reason why not — no leg is left unaddressed and unmentioned.
- **SC-004**: The overhead of the instrumentation is measured and stated, and is
  under **5%** of the budget of the leg it measures.
- **SC-005**: Someone who did not build it can produce a latency figure for the
  event-to-overlay leg from the written procedure.
- **SC-006**: Steady-state latency after this feature is no worse than before it,
  measured the same way.
- **SC-007**: ADR-0026 describes what exists.
- **SC-008**: The full test suite passes with nothing excluded or weakened.

---

## Assumptions

- **"Watched" means aggregated, not traced.** Spec 023 added spans to one leg,
  which answers "where did this one event go" and not "is this leg holding".
  Both matter; this feature is about the second.
- **Not all six legs will be instrumented here, and that is planned for.** Three
  are in a browser with no telemetry route, and one — headroom — may not be a
  measurable leg at all so much as the arithmetic remainder. FR-007 and SC-003
  are written so the feature succeeds by *addressing* every leg, which is not
  the same as instrumenting every leg.
- **Priority follows evidence.** `event → overlay state` goes first: it is the
  only leg with spans, it is where the one known breach was found, and it is the
  leg §IV names in its PR requirement.
- **Dev and CI first, production second.** The fixture is where measurement can
  be exercised today. Numbers from it are not numbers about a fab, and specs
  020, 022 and 023 each said so; that caveat travels with everything here.
- **The ADR-0026 decision is a decision, not a discovery.** The comparison phase
  it describes never started. Whether to start it or to record a simpler reality
  is a judgement for the reviewer, and the plan should present the options rather
  than quietly pick one.
- **This feature does not fix any latency.** It makes latency visible. If
  visibility reveals a breach, that is a finding to file, not scope to absorb —
  #1655 is precedent.

---

## Out of scope

- **The cold-start cost.** #1655 owns it. This feature may make it more visible;
  it does not explain or fix it.
- **Alerting and on-call.** ADR-0026 names Alertmanager. Deciding who gets woken
  at 3 a.m. for a breached leg is a separate conversation from being able to see
  the breach.
- **Changing the budgets.** ADR-0015 is Locked. If measurement shows a budget is
  wrong, that is an ADR-class review, not an edit here.
- **Log and error observability.** This is about latency. The wider telemetry
  story is only touched where the two share a pipeline.
