# Feature Specification: Where the audit pipeline's milliseconds go

**Feature Branch**: `053-where-the-audit-milliseconds-go`

**Created**: 2026-08-31

**Status**: Draft

**Input**: Issue 1956. NFR-001 (spec 009) — audit ingest p99 ≤ 50 ms at 100 events per second.

---

## Where this sits

The audit pipeline records every change in the system. A requirement says an
event should be recorded within **50 milliseconds** of arriving, and the best
measurement anyone has taken is **85**.

**Three attempts to close that gap have already happened**, each measured and
each recorded. Two were adopted and shrank the gap from roughly 130× to roughly
1.7×. The third was built, measured, and rejected because it made things worse.

Both prior conclusions said the same thing: what remains is either a production
deployment nobody has built, or moving the requirement. **Neither was ever
tested against knowing where the time actually goes.**

**That is what this feature produces: knowledge, and nothing else.** It does not
propose moving the requirement and it does not propose a fourth attempt. Deciding
what to do with the answer comes afterwards and belongs to someone else. A
version of this that arrives at "and therefore we should…" has overstepped.

---

## The measurement everyone has been quoting is not the requirement's

The requirement names one span: **from the moment the pipeline hands over an
event, to the moment its record is committed.**

What has actually been measured is a different span, taken between two
timestamps that happen to exist. It is **longer at the front** — it includes work
the originating service does before the event is handed over — and **shorter at
the back** — it stops just before the record is written.

Three decisions have quietly treated the two as interchangeable. They are not,
and a number that is 1.7× off a budget is exactly the size where the difference
could matter. **Reporting both, and how much of the difference each end
accounts for, is a requirement here rather than a nicety.**

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Someone chasing the budget can see where it goes (Priority: P1)

An engineer asked "why is this 85 milliseconds and not 50?" can answer it. They
see the span broken into its parts — the hop between services, the work done on
arrival, and the writing itself — with a number against each, taken at the load
the requirement names.

Today the only available answer is the total.

**Why this priority**: This is the feature. Every other story exists to stop this
one being believed when it should not be.

**Independent Test**: Run the pipeline at the rate the requirement names, and
produce a breakdown that accounts for the whole span. It is testable by whether
the parts sum to the total; a breakdown that leaves an unexplained remainder has
not answered the question.

**Acceptance Scenarios**:

1. **Given** the pipeline under sustained load at the required rate, **When** the
   measurement is taken, **Then** the total span is divided into named parts and
   each carries a figure.
2. **Given** that breakdown, **Then** the parts account for the whole, and any
   unattributed remainder is stated as such rather than distributed.
3. **Given** the breakdown, **Then** each part is marked as inside or outside the
   requirement's own span.

---

### User Story 2 - The clocks are shown to agree before anything is believed (Priority: P1)

The two timestamps are written **by different processes**. Nobody has ever
checked that their clocks agree. If they disagree by tens of milliseconds, some
of the "85" is not latency at all — and every conclusion drawn from it, including
the two already recorded, inherits that error.

**Why this priority**: **P1, and it gates US1.** An attribution built on skewed
clocks is worse than no attribution: it is a confident, specific, wrong answer,
and it would be used to move a requirement. On a single development machine the
skew is probably small — but "probably small" is precisely the kind of assumption
this project has repeatedly found was load-bearing.

**Independent Test**: Establish a bound on the disagreement between the clocks
that stamp the two timestamps, independently of the pipeline being measured.

**Acceptance Scenarios**:

1. **Given** the two stamping processes, **When** their clocks are compared,
   **Then** a bound on the disagreement is produced and recorded with the
   measurement.
2. **Given** a bound that is large relative to the gap being investigated,
   **Then** the attribution is reported as **not established**, and the reason
   is the finding — not a footnote.
3. **Given** a measurement method immune to the disagreement, **Then** using it
   is preferred to bounding the error, and the choice is recorded.

---

### User Story 3 - The answer is recorded where the next person will find it (Priority: P2)

Whoever picks up the requirement next reads a breakdown and a caveat, not a
total and three ADRs that stop short of one.

**Why this priority**: P2 because the knowledge exists once US1 and US2 are done.
It is here because this project has repeatedly paid for findings that lived only
in a session — and because the issue this comes from spent months describing a
system that three decisions had already changed.

**Independent Test**: Someone who has not seen this work can read the record and
say where the milliseconds go and how much of that is trustworthy.

**Acceptance Scenarios**:

1. **Given** the record, **Then** it states the breakdown, the load it was taken
   at, and the clock bound.
2. **Given** the record, **Then** it distinguishes the requirement's span from
   the one measured, with figures for both.
3. **Given** the record, **Then** it does **not** recommend moving the
   requirement or building a fourth improvement.

---

### Edge Cases

- **The parts do not sum to the total.** The remainder is reported, not
  distributed across the parts it might belong to. An unexplained gap is a
  finding.
- **The clocks disagree by more than the gap being chased.** Then the honest
  output is "this cannot be attributed with the timestamps available", and that
  is a result, not a failure.
- **Attribution changes with load.** Figures are taken at the rate the
  requirement names. If a part behaves differently at lower rates, that is worth
  recording, but the required rate is the one that answers the question.
- **A part turns out to be dominant and outside the requirement's span.** That
  is the most useful possible outcome and must not be quietly folded in.
- **The measurement itself disturbs the thing measured.** If gathering the
  breakdown changes the total, the amount it changes by is part of the result.
- **A run does not reproduce.** A single run is an anecdote; the record says how
  many were taken and how much they varied.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The observed span MUST be attributed across named parts, covering
  at least the hop between services, the work done on arrival, and the writing of
  the record.
- **FR-002**: The parts MUST account for the whole span; any remainder MUST be
  reported as unattributed rather than distributed.
- **FR-003**: Each part MUST be marked as **inside or outside** the requirement's
  own span.
- **FR-004**: Both spans — the requirement's and the one historically measured —
  MUST be reported, with how much of the difference each end accounts for.
- **FR-005**: The disagreement between the two stamping clocks MUST be bounded,
  or removed by measuring in a way that does not depend on them agreeing.
- **FR-006**: If that disagreement cannot be bounded below the gap being
  investigated, the attribution MUST be reported as **not established**.
- **FR-007**: Measurements MUST be taken at the load the requirement names, and
  the achieved rate MUST be reported alongside the intended one.
- **FR-008**: More than one run MUST be taken, and the variation between them
  reported.
- **FR-009**: If gathering the breakdown changes the total, the size of that
  change MUST be reported.
- **FR-010**: The requirement's budget MUST NOT be changed by this work.
- **FR-011**: The record MUST NOT recommend a course of action.
- **FR-012**: The record MUST be readable by someone who has not seen this work.

### Key Entities

- **The observed span**: what has been measured historically, between two
  existing timestamps in different processes.
- **The requirement's span**: what the requirement actually names. Related to the
  above but not equal to it.
- **A part**: a named segment of the span, with a figure and a statement of
  whether it falls inside the requirement.
- **The clock bound**: how far apart the two stamping clocks may be, and how that
  was established.
- **The unattributed remainder**: whatever the parts do not account for.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Someone can state, from the record alone, which part of the
  pipeline spends the largest share of the span.
- **SC-002**: The named parts account for **at least 80%** of the observed span,
  or the shortfall is reported as unattributed with that figure given.
- **SC-003**: The clock disagreement is bounded at **less than 10 ms**, or the
  attribution is declared not established.
- **SC-004**: Figures are reported for both spans, and the difference between
  them is accounted for at both ends.
- **SC-005**: At least **three runs** at the required rate, with the spread
  between them reported.
- **SC-006**: The requirement's budget is unchanged, and the record contains no
  recommendation.

---

## Scope

### In scope

Attributing the observed span, bounding the clocks, and recording the result.

### Out of scope, and named rather than implied

- **Moving the requirement.** A decision, not this work's output.
- **A fourth improvement.** Whatever the breakdown suggests, acting on it is a
  separate decision with its own evidence.
- **General observability.** Tracing other parts of the system, dashboards, or a
  production destination for telemetry are adjacent and separate — including the
  open question about what a dashboard obligation means (issue 1940).
- **Production topology.** There is no production deployment; building one to
  settle a 35 ms gap is not this.
- **The measurement test's exclusion from continuous integration.** It stays
  excluded while it fails, which is deliberate and unchanged.

---

## Assumptions

- The load the requirement names is reachable on the development stack; prior
  measurement at that rate suggests it is.
- The pipeline's behaviour under measurement resembles its behaviour without —
  FR-009 exists because this may not hold.
- The two adopted improvements stay in place; this measures the pipeline as it is
  now, not as it was.
- Development and continuous integration are the only environments available.
  Any figure here describes that environment and says so.
- Nothing here is a claim about production, which does not exist.
