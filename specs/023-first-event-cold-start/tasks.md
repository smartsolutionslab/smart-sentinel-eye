---

description: "Task list for 023-first-event-cold-start"
---

# Tasks: The first event after a restart reaches its effect in time

**Input**: Design documents from `/specs/023-first-event-cold-start/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md)

**Tests**: this feature reuses spec 022's measurement (FR-011). New test code is
for measuring, not for asserting behaviour that does not exist yet.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on incomplete work)
- **[Story]**: the user story it serves

---

## Read this before starting

**The deliverable is the explanation, not a smaller number.** Warming the path
is a small, obvious change that would make the figure drop and leave nobody able
to say what the seconds had been. An unexplained improvement is indistinguishable
from a hidden one. **T012 is the deliverable**; everything before it exists to
make it possible, and T014+ is only worth doing once T012 has an answer.

**"It is diffuse" is not an answer.** SC-001 requires ≥ 80% of the elapsed time
on named stages. Falling short means the instrumentation is not good enough yet —
that is the next task, not a conclusion.

**A refuted hypothesis is a result.** T013 tests the one candidate that predicts
the observed curve. If it is wrong, the note says so. A prediction reported only
when it succeeds is not evidence.

**One task can end the feature early.** T004's bad outcome is a correctness bug,
not a latency one — see the task.

---

## Phase 1: Setup

- [ ] T001 Reproduce the curve before trusting it, per
      [quickstart.md](./quickstart.md) §1: run
      `EventReachesItsEffectsTests` from a cold stack and record
      arrival-to-effect for each test **together with its execution order**.
      Order is part of the observation — whichever test runs first pays. **If the
      curve is not there, stop**: the premise of the feature is gone and that is
      the finding.

---

## Phase 2: Foundational — the free split, before anything changes

- [ ] T002 Add a temporary measurement to
      `tests/Integration.Tests/Automation/` that marks three times for one
      event on a cold stack: the publish returning, the event becoming readable
      through the EventIngestion read API, and the variable becoming readable
      with its new value. Report the two intervals.
- [ ] T003 Record which half owns the seconds — ingress-and-store, or
      announce-decide-apply. This does **not** satisfy SC-001 and is not meant
      to; it decides where to look before any production change exists to argue
      about, and it survives as the cross-check on the spans in T011.

**Checkpoint**: the search is narrowed to half the journey, and nothing has been
changed to achieve it.

---

## Phase 3: The question that might not be about latency

- [ ] T004 Restart Automation with a rule already `Active` in the database and
      send a matching event. **If the rule fires**, something hydrates
      `InMemoryRuleCache` at startup and that cost belongs to this feature. **If
      it does not fire, stop and file it**: rules silently stopping after a
      restart is a correctness bug materially more serious than this latency
      one, and it must not be folded into a performance feature where it would
      be fixed as a side effect and never noticed.

**Why this is its own phase**: it is cheap, and one of its two answers changes
what everyone should be working on.

---

## Phase 4: US1 — someone can say where the seconds go (P1) 🎯 MVP

- [ ] T005 [US1] Register Wolverine's activity source in
      `src/ServiceDefaults/Extensions.cs` so publishes, transit and handler
      execution appear as spans with context propagated across services.
      Confirm the source name by observing spans rather than by assuming it —
      Phase 0 could not verify it from the assembly.
- [ ] T006 [P] [US1] Confirm spans actually cross the service boundary: a trace
      that starts in EventIngestion must contain the Automation and
      SystemVariables work under the same trace id. Without propagation the
      spans are per-service stopwatches and cannot attribute a journey.
- [ ] T007 [US1] Measure the warm path before and after T005 and record both
      (FR-005). Instrumentation is not free, and an observer effect here is a
      finding rather than an inconvenience.
- [ ] T008 [US1] Capture the traces for the **first three events** after a
      restart, not just the first. The decay is the clue; one trace cannot show
      it.
- [ ] T009 [US1] Attribute the elapsed time of the first event to named stages,
      accounting for **≥ 80%** (SC-001), and name the stage holding the largest
      share. **If under 80%, the next task is better instrumentation, not a
      conclusion.**
- [ ] T010 [US1] Explain the decay across the three events as specific stages
      getting cheaper (SC-002) — an aggregate falling is a restatement of the
      problem, not an explanation of it.
- [ ] T011 [US1] Cross-check the span attribution against T003's buckets. If
      they disagree, one is wrong; find out which before either is quoted.

**Checkpoint**: SC-001 and SC-002 observable. This is the shippable increment —
if the feature stopped here, the gap would be understood and documented, which
is the thing it was created to deliver.

---

## Phase 5: US1 — the candidates get verdicts (P1) ⭐ the deliverable

- [ ] T012 [US1] **Give every candidate cause in #1655 a verdict in writing**,
      including the ones Phase 0 already weakened (the ingest loop's poll, the
      outbox schema build) and the one it strengthened. Refuted candidates are
      recorded as refuted (FR-003, SC-003).
      **This is the deliverable. Until it exists, nothing else here is
      established.**
- [ ] T013 [US1] Test the first-publish-per-message-type hypothesis directly:
      send each of the three message types once at startup and see whether the
      curve collapses. **Record the result either way.** It is the only candidate
      that predicts a staged decay rather than a single step, so refuting it
      would mean the shape is still unexplained — which is a finding, not a
      failure.

**Checkpoint**: the seconds have a named owner, or it is written down that they
do not.

---

## Phase 6: US2 — the first event is no longer an outlier (P2)

**Conditional on Phase 5.** Do not start this before T012 has an answer.

- [ ] T014 [US2] Address the cause T012 named — most likely by warming the path
      during startup rather than lazily on the first real event. Smallest change
      that addresses the measured cause; no speculative warming of paths the
      measurement did not implicate.
- [ ] T015 [US2] Confirm the first event after a restart now reaches its effect
      in **under 1 s** (SC-004), measured the same way as T001 so the before and
      after are comparable.
- [ ] T016 [P] [US2] Confirm steady-state arrival-to-effect is no worse than
      spec 022's 267–348 ms (SC-005). A first-event win paid for out of the warm
      path is not a win.
- [ ] T017 [US2] State where the cost landed: the added startup time (FR-007),
      and confirmation that the service does not report ready before it can
      serve (FR-006). Moving a cost is a legitimate fix; moving it silently is
      not.

---

## Phase 7: US3 — what a fab should expect is written down (P3)

- [ ] T018 [US3] Record the findings: the measured figures, the attribution,
      what they establish, and **explicitly what they do not** (FR-009). The
      fixture runs nine services and a broker on one host; spec 020 and spec 022
      both said a figure from there is not a figure about a fab, and it applies
      unchanged.
- [ ] T019 [US3] If the gap could not be closed, record the reason and the
      residual risk to the latency budget (FR-010). **A permitted ending** — the
      obligation was that the number stops being unexplained, not that it
      shrinks.

---

## Phase 8: Polish

- [ ] T020 Run the full integration suite. Nothing excluded, nothing weakened
      (FR-008, SC-006).
- [ ] T021 [P] Run `scripts/coverage-check.ps1 -Configuration Release`.
- [ ] T022 Raise the observability gap for a decision rather than leaving it
      patched in a corner: §VII says a leg without a dashboard cannot ship, and
      this one shipped across six features with neither dashboard nor spans.
      T005 closes the span half for one journey. **File the rest** — the
      dashboard, and the other legs — and note whether it warrants an ADR
      amendment.
- [ ] T023 Walk [quickstart.md](./quickstart.md) end to end and write
      `verification.md`. **"Done" is the observations**, and step 3 is the one
      that cannot be skipped.
- [ ] T024 Open the PR with `Closes #1655`, stating what the measurement does
      not establish and what remains uncovered.
- [ ] T025 Add every issue created for these tasks to **Project #13**
      (`gh project item-add 13 --owner smartsolutionslab --url <issue-url>`).
      Verify with `item-list` — `item-add` prints nothing on success, and the
      board has over 250 items so a short `--limit` will not find a new one.

---

## Dependencies

```
Phase 1  (T001)        reproduce before trusting
Phase 2  (T002-T003)   the free split — narrows the search, changes nothing
Phase 3  (T004)        the rule-cache question; may end the feature early
Phase 4  US1 (T005-T011)  🎯 MVP — make it observable, attribute the seconds
Phase 5  US1 (T012-T013)  ⭐ verdicts on every candidate — the deliverable
Phase 6  US2 (T014-T017)  conditional: fix what T012 named
Phase 7  US3 (T018-T019)  write down what a fab should expect
Phase 8  (T020-T025)      suite, coverage, the observability gap, PR
```

- T002 needs nothing and should be done before T005, so there is an
  instrument-free number to check the spans against.
- T005 blocks T006–T011: without spans there is nothing to attribute.
- T009 blocks T012 — a verdict needs the attribution behind it.
- **T014 must not start before T012.** The whole ordering exists to prevent a
  fix that precedes its justification.
- T016 needs T014, or it measures nothing.

## Notes

**Why the fix is P2 and not P1.** Reversing them is the natural instinct and it
is the one thing that would waste this feature. Warming the path first produces a
smaller number and no knowledge; the next time it regresses, nobody can tell
whether the warm-up stopped working or something new appeared. Spec 021 already
shipped an improvement nobody had verified, and 228 green tests agreed with it.

**Why T003 is kept even though it cannot satisfy SC-001.** It costs nothing, it
needs no production change, and it is the only independent check on the spans. An
attribution with nothing to disagree with it is a story.

**Why T012 has a phase to itself.** Every other task produces something that
looks like progress. T012 is the one that produces the thing the feature was
created for, and it is the one most easily skipped once the number gets smaller.

**Why T004 is early and separate.** It is cheap, and its bad outcome — rules not
firing after a restart — is worse than the problem this feature exists to solve.
Folding it into a performance feature would fix it as a side effect of warming
something, with nobody ever knowing it had been broken.

**Why T022 exists.** T005 closes the span half of §VII for one journey. Leaving
the rest unfiled would mean a constitutional principle was found unmet and
quietly patched where it was convenient.
