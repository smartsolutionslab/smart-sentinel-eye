# Tasks — 053 where the audit milliseconds go

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Research**: [research.md](./research.md) · **Data model**: [data-model.md](./data-model.md) · **Contract**: [contracts/the-attribution.md](./contracts/the-attribution.md)

Thirteen tasks. **The deliverable is a document, not a behaviour change** — so
the usual "does it work" checks are replaced by "is the number trustworthy",
which is why the clock phase gates everything.

---

## Do not

- **Do not change NFR-001's budget.** A passing number obtained by moving the
  line reports the requirement as met when it is not.
- **Do not propose a fourth lever**, however obvious the breakdown makes one.
  That is a separate decision with its own evidence.
- **Do not re-source `OccurredAt` or `ReceivedAt` from the database.** It would
  change production behaviour to suit a measurement, and break comparison with
  every figure already recorded.
- **Do not add a telemetry sink, dashboard or exporter**, and do not touch the
  dashboard-obligation question (issue 1940).
- **Do not un-exclude the measurement test from CI.** It stays excluded while it
  fails, which is deliberate and unchanged.
- **Do not re-litigate ADR-0124, ADR-0126 or ADR-0127.** They are measured and
  recorded; this measures what they did not.
- **Do not write bare `#NNNN` issue numbers** in committed docs.

---

## Phase 1 — The clocks *(the gate)*

- [x] T001 Add a clock-offset probe in `tests/Integration.Tests/AuditObservability/ClockOffsetProbe.cs`: ask the shared Postgres server its time from a process, compare with that process's own, and halve the round trip as the standard correction. Return the offset **and its residual** — a figure without its uncertainty is not a measurement.
- [x] T002 [P] Compute the **relative skew** between the two stamping processes as the difference of their offsets, in `tests/Integration.Tests/AuditObservability/ClockOffsetProbe.cs`. This is the number the attribution depends on; the individual offsets are working, the difference is the result.
- [x] T003 [P] **Test the failure path**, in `tests/Integration.Tests/AuditObservability/`: when the measured offset exceeds 10 ms, the attribution is reported as **NOT ESTABLISHED**. Assert on that outcome, not on a log line. SC-003 makes "we could not tell" a reportable result, and a result nothing tests is a sentence.

**Checkpoint — this is the gate, and it is an epistemic one.** An attribution
over skewed clocks is a confident, specific, wrong answer, and it would be used
to move a requirement — which is worse than having no attribution at all.

---

## Phase 2 — The apparatus, behind a switch

- [ ] T004 Add the nullable measurement timestamps to the audit row in `src/AuditObservability/Domain/AuditEvent/AuditEvent.cs` per data-model §3 — enqueued, handler entered, committed. `OccurredAt` and `ReceivedAt` are untouched.
- [ ] T005 Add the migration for the nullable columns in `src/AuditObservability/Infrastructure/Persistence/Migrations/`, and the switch that controls whether they are written, **defaulting to off**.
- [ ] T006 Write the timestamps in `src/AuditObservability/Application/EventHandlers/AuditingMessageHandler.cs` only when the switch is on.
- [ ] T007 [P] **Test that the switch is off by default and the row is unchanged**, in `tests/AuditObservability.Application.Tests/`: write a row through the normal path with no configuration and assert the measurement columns are **absent**. This puts apparatus on a production write path; the default must be verified rather than intended.
- [ ] T008 [P] In `tests/AuditObservability.Application.Tests/`, test that with the switch on each timestamp is present and ordered — enqueued ≤ handler entered ≤ committed. An out-of-order stamp is a bug that would show up as a negative part.

---

## Phase 3 — US1: the attribution *(P1)*

- [ ] T009 [US1] Extend the measurement run in `tests/Integration.Tests/AuditObservability/NFR001_AuditIngestLatencyTests.cs` to read the parts beside the total in one query, and to report the **achieved rate next to the intended one**. A run that intended 100 ev/s and delivered 60 answers a different question and must say so.
- [ ] T010 [US1] **Report the remainder rather than distributing it**, in `tests/Integration.Tests/AuditObservability/NFR001_AuditIngestLatencyTests.cs`, with its own assertion: the parts must sum to the total, and whatever they do not account for is reported as unattributed. **A breakdown whose parts quietly absorb an unexplained gap is the most dangerous output here, because it looks complete.**
- [ ] T011 [US1] In `tests/Integration.Tests/AuditObservability/NFR001_AuditIngestLatencyTests.cs`, **report both spans** — the requirement's (broker hand-over → row committed) and the observed one (originating change → row stamped) — with the difference attributed at the front and the back separately. Three ADRs have used one figure for both; at 1.7× off a budget that difference may be the answer.
- [ ] T012 [US1] In `tests/Integration.Tests/AuditObservability/NFR001_AuditIngestLatencyTests.cs`, measure the apparatus' own cost: the same run shape with the switch off and on, and the difference in the total reported. **Measured, not argued** — and if it is large relative to the parts being attributed, the attribution says so.

---

## Phase 4 — US3: the record *(P2)*

- [ ] T013 [US3] Write the ADR and `verification.md` in `docs/adr/` and `specs/053-where-the-audit-milliseconds-go/`: the breakdown, the load it was taken at, the clock bound and its residual, both spans, the spread across **at least three runs**, and the apparatus' cost. **State what was measured and stop.** No recommendation, no proposed lever, no changed budget — a reviewer should push back if it does otherwise, because the pull towards "and therefore we should…" is exactly what produced two recorded conclusions that skipped this measurement.

---

## Mutations that must each kill a test

| # | Mutation | Must be killed by |
|---|---|---|
| 1 | Make the switch default to **on** | T007 |
| 2 | Write the measurement columns when the switch is off | T007 |
| 3 | Distribute the remainder across the parts instead of reporting it | T010 |
| 4 | Report one figure for both spans | T011 |
| 5 | Drop the clock residual from the reported offset | T001 |
| 6 | Let the 10 ms threshold pass silently when the measured offset exceeds it | T003 |
| 7 | Stamp the timestamps out of order | T008 |

**Mutation 6 is the one to run first.** It is the only one whose survival would
produce a *plausible* wrong answer rather than an obviously broken one.

---

## Dependencies

```
T001 ─▶ T002        Phase 1 (GATE)
   └──▶ T003
          │
          ▼
T004 ─▶ T005 ─▶ T006 ─▶ T007, T008     Phase 2
                          │
                          ▼
T009 ─▶ T010, T011, T012               Phase 3 (US1)
          └──────┬───────┘
                 ▼
               T013                     Phase 4 (US3)
```

**Phase 1 gates everything.** Phase 2 is sequential — one row, one handler, one
migration. Phase 3's reporting tasks are independent of each other once the run
produces parts.

---

## Parallel opportunities

- **T002 and T003 are parallel** — one computes the skew, the other tests what
  happens when it is too large.
- **T007 and T008 are parallel** — opposite sides of the switch, different files.
- **T010, T011 and T012 are parallel** — three separate claims about the same
  run's output.
- **T004–T006 are strictly sequential**: a field, its migration, its writer.

---

## Implementation strategy

**Phase 1 first, and it is a gate rather than a phase.** Everything downstream is
a number, and a number is only worth having if it is trustworthy. The clock
question is the only thing standing between "the broker hop costs 40 ms" and "the
broker hop appears to cost 40 ms because two processes disagree about what time
it is".

**The deliverable is a document.** There is no feature to demonstrate and nothing
gets faster. The equivalent of "does it work" here is "is the number
trustworthy", which is why three of thirteen tasks are about the measurement of
the measurement rather than the pipeline.

**The feature issue is on Project #13** — Phase 3's gate is satisfied.

**Coverage gates apply and may be cited.** AuditObservability's Domain and
Application layers are touched, so ADR-0065's **90% Domain / 80% Application**
thresholds are live.

**The measurement test stays excluded from CI.** It fails, and the budget stays
at the requirement's 50 ms rather than being tuned to whatever the stack
produces. Nothing here changes that.

---

## Three things most likely to go wrong

1. **The breakdown gets believed before the clocks are checked.** It is the
   natural order to work in — build the apparatus, get numbers, then tidy up the
   caveats — and it is backwards. A specific, confident attribution is far more
   persuasive than a total, and if it rests on a skew nobody measured it will be
   used to move a requirement. T003 exists so the failure case is a reportable
   outcome rather than an embarrassment, and mutation 6 runs first.

2. **The remainder quietly disappears.** If the parts do not sum, the tempting
   move is to attribute the difference to whichever part seems most likely. That
   produces a breakdown that looks complete and is not. T010 asserts the
   remainder is reported; a breakdown with an unexplained gap is a **finding**.

3. **The record ends with a recommendation.** A breakdown is persuasive, and both
   prior conclusions reached for "production topology or move the requirement"
   without this measurement. The whole point is to supply evidence for that
   decision, not to take it. T013 says so, and a reviewer should push back.

---

## What the automated checks do and do not prove

| Claim | Proved by | Not proved by |
|---|---|---|
| The clocks agree closely enough | T001, T002 — measured against a shared reference | both processes being on one machine |
| "We could not tell" is reportable | T003 | a sentence in the record |
| The apparatus is off by default | T007, asserting **absence** on a normal row | the default looking right |
| The stamps are coherent | T008 | the run completing |
| Where the span goes | T009, T010 — parts summing to the total | a total and an intuition |
| The requirement's span vs the observed one | T011 | treating them as interchangeable, as three ADRs did |
| The apparatus is cheap | T012, off and on | it looking cheap |
| **That NFR-001 is achievable** | **nothing — not the question** | a breakdown that suggests a lever |
| **That any lever would help** | **nothing** | an obvious-looking dominant part |
| **Anything about production** | **nothing** | there is no production deployment |

The last three rows are the honest ones. **This feature's output is knowledge,
and the checks above are about whether that knowledge is trustworthy — not about
whether anything got faster, because nothing here is supposed to.**
