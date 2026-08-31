# Feature Specification: Divide the span the decision is actually waiting on

**Feature Branch**: `054-divide-the-recorded-85ms`

**Created**: 2026-08-31

**Status**: Draft

**Input**: User description: "Divide the 85 ms that issue 1956 is actually about, instead of the fixture's seconds"

## Context

Spec 053 built an apparatus that divides the audit ingest span into parts, and
measured it. **It measured the wrong stack**, and said so: ADR-0135 records in its
own Consequences that the figures are from the Aspire test fixture, which at a
sustained 100 ev/s runs 1376–2642 ms, while the p99 the open decision is about —
85 ms — was recorded in run mode.

So the breakdown that exists divides a span an order of magnitude larger than the
one anybody is arguing over. **The apparatus is not the gap. The environment is.**

The work is a load driver. There is none in the repository: every run-mode figure
in the record was produced ad hoc in an earlier session and thrown away.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The breakdown describes a stack nobody runs (Priority: P1)

Someone weighing what to do about the audit latency requirement opens the
breakdown, finds the span divided into named parts, and then reads the sentence
saying these are fixture figures against a fixture span of 1.4–2.6 seconds. The
number they came to reason about is 85 ms. **They cannot use the breakdown for
the decision it was built to inform**, and the record tells them so honestly
rather than letting them notice too late.

They need the same division, of the same span, at the same paced rate, taken
where the 85 ms was taken — so the two can sit in one table.

**Why this priority**: This is the whole feature. Without it, spec 053's output
remains knowledge about a stack that exists only inside a test run, and the
decision it was meant to inform is no better supported than before.

**Independent Test**: Run the load against a running stack in run mode, at the
paced rate, and read the breakdown. Delivers the attribution regardless of
whether anything else here ships.

**Acceptance Scenarios**:

1. **Given** a stack running in run mode with the measurement switch on, **When**
   the load is driven at the paced rate, **Then** the span divides into the same
   named parts spec 053 reports, over the same number of events.
2. **Given** a completed run, **When** the breakdown is read, **Then** it states
   the achieved rate beside the intended one, the logging level in force, and the
   environment it was taken in.
3. **Given** the run-mode and fixture figures, **When** they are set side by
   side, **Then** every difference between the two runs other than the
   environment is nil, or is named.
4. **Given** a run whose conditions were not met — wrong rate, verbose logging,
   rows missing stamps — **When** it completes, **Then** it reports the breakdown
   as unusable rather than publishing figures taken under the wrong conditions.

---

### User Story 2 - A comparison nobody can quietly break (Priority: P1)

Someone repeats the measurement in three months, or on another machine, and needs
their figures to be comparable with the ones in the record. Today that is luck:
the driver, the generator, the rate, the event count and the logging level were
all decided in a session that left no trace.

They need the run shape to be committed, so that "the same run" is a thing the
repository can reproduce rather than a thing somebody remembers.

**Why this priority**: Equal first, because the comparison is the entire value of
the measurement. A run-mode figure taken with a different driver or rate cannot be
set beside the fixture figures at all, and the failure is silent — two numbers in
a table that look comparable and are not.

**Independent Test**: Check out the repository, run the committed driver, and get
a figure whose stated conditions match those in the record.

**Acceptance Scenarios**:

1. **Given** only the repository, **When** someone runs the driver, **Then** the
   generator, the event count, the concurrency, the target rate and the pacing
   are fixed by the committed artefact rather than by the operator.
2. **Given** two runs from different sessions, **When** their reported conditions
   differ in any respect, **Then** the difference is visible in the output of both
   rather than requiring a reader to know what was done.

---

### User Story 3 - The record says what changed and stops (Priority: P2)

Whoever decides about the requirement reads a record that sets the run-mode
breakdown beside the fixture one, states the spread across runs, names what
remains unestablished, and **makes no recommendation**. The pull toward "and
therefore we should…" is exactly what produced two conclusions that skipped the
measurement in the first place.

**Why this priority**: The knowledge is worthless if the record over-reaches, and
worse than worthless if it under-reports its own limits. Second only because
there is nothing to record until the measurement exists.

**Independent Test**: A reviewer reads the record and can state, without asking,
which figures are established, which are not, and what decision the record
declines to take.

**Acceptance Scenarios**:

1. **Given** the record, **When** a reviewer looks for a recommendation, **Then**
   there is none — no lever, no proposed change to the requirement's budget.
2. **Given** the record, **When** a reviewer looks for the limits, **Then** the
   unestablished figures are named as such in the same table as the established
   ones, not in a footnote.
3. **Given** a run-mode breakdown that differs in shape from the fixture's,
   **When** the record describes it, **Then** the difference is reported as a
   finding rather than treated as a defect to chase.

---

### Edge Cases

- **The stack is not in run mode, or not running.** A driver that quietly falls
  back to booting its own stack would reproduce the exact defect this feature
  exists to fix, and the fixture route is closed for that reason.
- **The rate is not achieved.** Below the target the pipeline is idle; above it
  the measurement is of overload. Either answers a different question.
- **Run mode spans more than one machine.** Then `occurred_at` and `received_at`
  no longer share an OS clock and the front of the span acquires the clock
  problem the write leg already has. This must be established, not assumed.
- **Run mode inherits verbose logging.** Development pins Debug, which costs
  2–3× throughput; a run that inherits it measures the logging.
- **The measurement switch is off**, so rows carry no stamps and there is nothing
  to divide.
- **The audit store already holds unrelated rows.** Run mode is persistent and has
  months of history; a query that does not isolate this run's events divides the
  wrong population.
- **A single pair of runs is used to state an effect size.** One side of this
  measurement is far noisier than the other.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST drive a sustained, rate-controlled load against a
  stack running in run mode, without booting a stack of its own.
- **FR-002**: The run MUST report the achieved rate beside the intended one.
- **FR-003**: The run MUST report the environment it was taken in, so a figure
  cannot be attributed to the wrong stack by a reader.
- **FR-004**: The run MUST report the logging level in force in the services
  under measurement, and MUST refuse to report a breakdown taken at a verbose
  level.
- **FR-005**: The run MUST divide the observed span into the same named parts
  spec 053 defines, and MUST report the per-row residual as the check that the
  parts cover each row.
- **FR-006**: The run MUST report both spans — the requirement's and the observed
  one — with the requirement's given as a floor and a ceiling rather than a single
  figure.
- **FR-007**: The run MUST isolate the events it generated from all other rows in
  the audit store.
- **FR-008**: The run MUST establish whether the stamping processes share a clock,
  and MUST report the attribution as not established when they cannot be shown to
  agree closely enough.
- **FR-009**: The run shape — generator, event count, concurrency, target rate,
  pacing — MUST be fixed by a committed artefact rather than chosen per run.
- **FR-010**: The record MUST state the spread across **at least three** runs, and
  MUST NOT state an effect size from a single pair.
- **FR-011**: The record MUST set the run-mode figures beside the fixture figures
  and name every difference between the two runs other than the environment.
- **FR-012**: The record MUST state what was measured and stop. It MUST NOT
  propose a lever, and MUST NOT propose changing the requirement's budget.
- **FR-013**: The record MUST name the figures that remain unestablished, in the
  same table as the established ones.

### Out of scope

- **Closing the write leg.** It subtracts a stamp taken by a host process from one
  taken inside the database's container, and the disagreement between those clocks
  is the same size as the leg. Run mode has the same split, so **the write leg and
  the requirement span's floor remain unestablished after this feature.** Closing
  it needs a stamp taken after commit — a second round trip on a path this work
  only observes — which spec 053 considered and rejected. That rejection stands
  here: this feature's question is where the *observed* span goes, and the front
  of that span, which is where the answer lies, does not depend on the write leg.
- **Any change to the audit pipeline.** Nothing is supposed to get faster.
- **Any change to what Development logs at.** That trade-off belongs to whoever
  works in it daily.
- **Deciding anything about the requirement.**

### Key Entities

- **Run conditions**: the environment, achieved and intended rate, logging level,
  event count, concurrency, and measurement-switch state under which a breakdown
  was taken. A breakdown without these is not comparable with anything.
- **Attribution**: the observed span divided into parts, with the residual that
  proves the parts cover each row, and the requirement's span as a bounded range.
- **Comparison**: two attributions and the enumerated differences between the runs
  that produced them.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The breakdown is produced against a stack running in run mode, at an
  achieved rate within 15% of the rate the requirement names.
- **SC-002**: Every reported part is accompanied by the conditions it was measured
  under, such that a reader can tell without asking which stack produced it.
- **SC-003**: The parts cover each row's span exactly, evidenced by a per-row
  residual of zero rather than by the reported figures reconciling.
- **SC-004**: **"We could not tell" is a reportable outcome.** A run whose clocks,
  rate, logging level or stamps fail their conditions reports the attribution as
  not established rather than publishing figures.
- **SC-005**: At least three runs, with the spread reported, and no effect size
  claimed from fewer.
- **SC-006**: A reader can set the run-mode and fixture breakdowns side by side and
  find every difference between the two runs, other than the environment, either
  nil or named.
- **SC-007**: The record contains no recommendation, no proposed lever, and no
  change to the requirement's budget.

## Assumptions

- **Run mode is the closest available stand-in for the environment the recorded
  figure came from.** There is no production deployment, so "production topology"
  cannot be measured; run mode is what produced the 85 ms and is therefore the
  environment that makes the comparison meaningful.
- **The apparatus from spec 053 is reused unchanged.** The stamps, the switch,
  the division, the clock probe and the conditions guards already exist and are
  merged; this feature supplies the environment and the driver, not a new
  measurement.
- **The measurement remains excluded from continuous integration**, as its
  sibling is, because it depends on a stack that CI does not run.
- **Whoever runs this can start the stack in run mode** and set the measurement
  switch and logging level for it.
- **The audit store in run mode is persistent and already populated**, so the run
  must identify its own events rather than assuming an empty table.
