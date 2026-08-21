---

description: "Task list for 025-measure-event-to-overlay"
---

# Tasks: The event-to-overlay leg can be measured

**Input**: Design documents from `/specs/025-measure-event-to-overlay/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md)

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on incomplete work)
- **[Story]**: the user story it serves

---

## Read this before starting

**T001 is a question and it may delete half this list.** Phase 0 found the trace
context is lost at the same hop as the timestamp. If it turns out to be available
or merely linked, the leg is measurable with **no contract change** and T004–T006
never happen. Answering costs one event and one trace.

**Do not take the timestamp route because it is familiar.** It is the route the
spec assumed, it is well understood, and it is the one that adds a private
mechanism beside a general one that may already work.

**Two silent failures are guarded, not hoped about.** A missing moment recorded
as zero is a perfect score for a journey nobody timed; a PTP-stepped clock can
yield a negative latency. Both flatter the dashboard, which is the direction this
codebase has been caught by four times.

**Reconciliation is not optional.** An instrument reporting a plausible number
for the wrong span cannot detect its own error, and someone will cite it.

---

## Phase 1: The question that shapes the feature

- [ ] T001 Send one event and read its trace end to end. Establish what happens
      to trace context at the outbox hop: **propagated** (the automation
      `receive` has a `parentSpanId` from event-ingestion's `send`), **linked**
      (separate traces joined by a span link), or **dropped** (separate traces,
      no link, `receive` is a root). Spec 024's captures suggest dropped, but
      were taken for another purpose — confirm rather than inherit.
- [ ] T002 Record the answer and its consequence: **available or linked** → the
      leg is derivable from spans and T004–T006 are unnecessary; **dropped** →
      the timestamp route stands and T003 files the trace gap as its own issue.
- [ ] T003 If context is dropped, file it separately. It is a bigger prize than
      this leg: every cross-service "what caused this" question is currently
      unanswerable across the outbox, and spec 023's cold-start investigation had
      to reason from timings for exactly this reason.

**Checkpoint**: the route is chosen on evidence. Everything below is conditional
on it.

---

## Phase 2: Foundational — carry the moment *(only if T001 says dropped)*

- [ ] T004 Add the acceptance moment as an **optional** field, without changing
      the meaning of any existing one (FR-002). Phase 0 established the shape:
      `EventMetadata` is positional with 15 construction sites and almost no
      readers, and a fifth optional parameter is source- and wire-compatible both
      directions — **not breaking under ADR-0073, no V2 needed**.
- [ ] T005 Forward it through Automation's fan-out, which currently mints fresh
      metadata and drops what it received.
- [ ] T006 [P] Confirm a consumer unaware of the field is unaffected by its
      presence (FR-008) — demonstrated, not reasoned.

---

## Phase 3: US1 — the whole leg is measured (P1) 🎯 MVP

- [ ] T007 [US1] Record acceptance-to-application into `LatencyBudget` as a
      distribution, via whichever route T002 chose.
- [ ] T008 [US1] Add the whole-leg segment to `LatencyBudget` with
      `is_whole_leg` **true**, and make sure it is true. Spec 024 defined a
      fragment segment and deliberately recorded nothing through it; this is the
      one that supersedes it.
- [ ] T009 [US1] A missing acceptance moment records **nothing** — not zero
      (FR-005). Demonstrate with an event that lacks it (SC-006).
- [ ] T010 [P] [US1] A negative elapsed time records **nothing** (FR-006). Fabs
      step clocks with PTP and the end can precede the start.
- [ ] T011 [US1] Confirm a percentile is obtainable from the running system
      without writing code (SC-001).

**Checkpoint**: SC-001 observable. The leg the constitution budgets is measured.

---

## Phase 4: US2 — the measurement agrees with something independent (P1)

**Not a refinement of Phase 3.** An instrument that reports the wrong span
plausibly is worse than none.

- [ ] T012 [US2] Compare the instrument's figure against spec 022's
      `EventReachesItsEffectsTests`, which already logs arrival-to-effect for the
      same journey from outside.
- [ ] T013 [US2] Agreement within **20%** (SC-003), or the discrepancy explained
      in writing **before either number is quoted anywhere**.

**Checkpoint**: the instrument is known to measure the leg, not a fragment of it.

---

## Phase 5: US3 — it costs nothing that matters (P2)

- [ ] T014 [US3] Measure the warm path before and after, same method both times.
      The exporter is attached in the fixture (spec 024 T002), so unlike spec
      023's first attempt this comparison is not vacuous.
- [ ] T015 [US3] Confirm the overhead is under **5% of the leg's 200 ms budget**
      (SC-005) and state the figure rather than calling instrumentation free.
- [ ] T016 [P] [US3] Confirm steady-state arrival-to-effect is no worse than the
      267–369 ms recorded by specs 022 and 023 (SC-004, FR-009).

---

## Phase 6: Closure

- [ ] T017 **Update the leg's row in constitution §IV**: measured **yes**,
      dashboard **no** (SC-008). ADR-0117 warned that a stale row exempts a leg
      by clerical error — this is the first feature to change a row, and if the
      discipline does not hold now it never will.
- [ ] T018 Say plainly that §VII is **half**-discharged for this leg. The
      dashboard half is #1707's ADR-0026 decision, and building one here would
      settle a Locked ADR by implementation.
- [ ] T019 Run the full integration suite and
      `scripts/coverage-check.ps1 -Configuration Release`. Nothing excluded,
      nothing weakened (SC-007).
- [ ] T020 Walk [quickstart.md](./quickstart.md) and write `verification.md`.
      **"Done" is the observations**, and step 1 is the one that decides what the
      rest of the feature was.
- [ ] T021 Open the PR, stating which route T001 chose and why, what is measured,
      and what remains.
- [ ] T022 Add every issue created for these tasks to **Project #13**
      (`gh project item-add 13 --owner smartsolutionslab --url <issue-url>`), and
      **verify with `item-list`** — `item-add` prints nothing on success and
      nothing on failure, the board holds 300+ items, and a short `--limit` finds
      nothing while looking identical to a failed add. That exact failure
      happened once already on spec 024.

---

## Dependencies

```
Phase 1  (T001-T003)   the question — shapes everything
Phase 2  (T004-T006)   CONDITIONAL: only if context is dropped
Phase 3  US1 (T007-T011)  🎯 MVP — the leg measured
Phase 4  US2 (T012-T013)  ⭐ reconciliation — proves it is the leg
Phase 5  US3 (T014-T016)  cost and no regression
Phase 6  (T017-T022)      constitution, suite, PR
```

- **T001 blocks everything.** It decides whether Phase 2 exists.
- T007 needs Phase 2 only on the timestamp route.
- **T012 blocks quoting any figure**, including in the PR.
- T014 needs T007, or it measures the cost of nothing.

## Notes

**Why T001 is a task rather than an assumption.** The spec was written believing
a contract change was required, and Phase 0 found evidence that it might not be.
That evidence was sitting in spec 024's captures, collected for a different
purpose and never re-read. The cheapest way to be wrong here is to build the
familiar thing without looking.

**Why Phase 4 is P1.** Spec 024's equivalent check fired before any code was
written and stopped a fragment being published against the leg's budget. The same
mistake is available here and the instrument cannot detect it alone.

**Why T017 is its own task.** ADR-0117 introduced a table that only works if it
is maintained, and warned that a stale row exempts a leg silently. The first test
of that discipline is the first feature that changes a row.
