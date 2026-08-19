---

description: "Task list for 022-event-reaches-its-effects"
---

# Tasks: A plant-floor event reaches the things it is supposed to drive

**Input**: Design documents from `/specs/022-event-reaches-its-effects/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md)

**Tests**: this feature *is* a test. No production code changes (FR-010).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on incomplete work)
- **[Story]**: the user story it serves

---

## Read this before starting

**A green run is not evidence here.** 228 green tests coexisted with the break
this feature exists to catch. The evidence is **T010** — breaking the journey on
purpose and watching this test fail. Everything before it is setup for that.

**The assertion is the whole design.** Four things that would have passed
against the known failure, and must not be asserted:
`FabEventIngestedV1` was published · the rule was evaluated ·
`SystemVariableValueRequestedV1` was published (**this is what broke**) ·
the event is stored and readable.

**`POST /rules` mints a Draft.** A rule that is not published cannot fire. A test
that seeds one and stops passes for the wrong reason, for ever.

---

## Phase 1: Setup

- [X] T001 Confirm the journey currently works, by hand, before writing anything
      that depends on it. Seed and publish a rule, publish a matching event, watch
      the variable change. **If it does not work, stop** — spec 021 claims to have
      fixed this path and a broken one is a defect to raise (spec.md Assumptions),
      not something to write a failing test around.

---

## Phase 2: Foundational

- [X] T002 Add `tests/Integration.Tests/Automation/EventReachesItsEffectsTests.cs`
      with the fixture wiring and a rule-seeding helper that **creates and
      publishes** a rule, then asserts it reads back as `Active` before returning
      it (FR-006). The assertion is the point: without it a downstream failure is
      indistinguishable from a rule that was never eligible.
- [X] T003 Add the polling helper: wait for an effect to a deadline, and on
      expiry report whether **anything** changed so "late" and "never" are
      distinguishable (FR-009). No fixed sleeps — slower and less informative.

**Checkpoint**: a rule can be made genuinely active and an effect can be waited
for. Nothing is asserted yet.

---

## Phase 3: US1 — an event changes what the operator sees (P1) 🎯 MVP

- [X] T004 [US1] The value effect: seed an active `SetVariableValue` rule,
      publish a matching event **over MQTT** (FR-002 — the real ingress, not a
      shortcut into the middle of the chain), and assert the variable's value
      changed, read back through the SystemVariables API rather than from the
      database (data-model.md).
- [X] T005 [P] [US1] The highlight effect: seed an active highlight rule,
      connect a `HubConnection` as the existing LayoutComposition integration
      tests do, publish a matching event, and assert the frame arrives. The two
      effects travel to different contexts by different routes, so covering one
      proves nothing about the other.
- [X] T006 [US1] The negative case: publish an event matching no active rule,
      assert the variable is unchanged and no frame arrives (FR-003, SC-003).
      Without this, a test asserting a value equals what it already was would
      pass on a completely dead system.
- [X] T007 [P] [US1] Redelivery: publish the same event twice, assert the effect
      applied once. SystemVariables dedups by event, and redelivery stopped being
      rare with spec 020 — an ordinary case now, not an edge one.

**Checkpoint**: SC-001 and SC-003 observable. This is the shippable increment.

---

## Phase 4: US2 — the proof is of the effect, not of the attempt (P1)

**Not a refinement of US1.** A test of this journey that asserts the wrong thing
is worse than no test, because it reports the path as covered.

- [X] T008 [US2] Review the assertions written in Phase 3 against the four
      forbidden ones above, and record in the test's own documentation what it
      would have done against the failure that prompted it (SC-006). A reader
      must be able to tell without running it.
- [X] T009 [US2] Make the failure messages name the **effect** that did not
      arrive, not the message that was not seen. A failure saying "no
      SystemVariableValueRequestedV1" would send the next person to the wrong
      layer — the messages were fine; nothing consumed them.

---

## Phase 5: US2 — the deliberate break (P1) ⭐ the deliverable

- [X] T010 [US2] **Break the journey and watch this fail.** In
      `src/ServiceDefaults/OutboxEventBus.cs`, make the `ambient.Envelope is not
      null` branch unreachable — the exact failure spec 021 shipped — run this
      test, confirm it **fails**, confirm the rest of the suite still passes, and
      restore the line. Record all of it (SC-002).
      **If it passes, the test is worthless and this feature is not done.**

**Checkpoint**: the test is known to detect the thing it exists to detect. Until
this task, nothing is established.

---

## Phase 6: US3 — it runs where it will be noticed (P2)

- [X] T011 [US3] Run the full integration suite three consecutive times with
      this test included, and confirm it passes each time (SC-004). Flakiness
      across four services is the expected risk.
- [X] T012 [US3] If it cannot be made reliable, record the reason **and the
      cost** — that this path returns to having no automated coverage at all
      (FR-008). `Category=Disruptive` is a last resort here: specs 020 and 021
      each excluded one test defensibly, and a third exclusion on the one path
      that most needs watching would put the system back where the break got
      through.

---

## Phase 7: Polish

- [X] T013 Measure arrival-to-effect and cite it against the
      `event → overlay state ≤ 200 ms` leg (SC-005) — **and state what it does
      not establish.** The fixture runs nine services and a broker on one host;
      spec 020 was explicit that a figure taken there is not a figure about a
      fab, and the same applies.
- [X] T014 Run `scripts/coverage-check.ps1 -Configuration Release`. Gates should
      be unmoved — this feature adds no production code — and a change would mean
      something unintended was touched.
- [X] T015 Walk [quickstart.md](./quickstart.md) end to end and record the
      observations. **"Done" is the observations**, and step 3 is the one that
      cannot be skipped.
- [ ] T016 Close **#1635** with `Closes #1635` in the PR body, and state what
      this does not cover: one event rather than load, the effects that exist
      rather than an exhaustive matrix, and the hub frame rather than a browser.
- [X] T017 Add every issue created for these tasks to **Project #13**
      (`gh project item-add 13 --owner smartsolutionslab --url <issue-url>`).
      CLAUDE.md's phase-3 gate has always required this and specs 018–021 all
      missed it; 022 is where it starts being done.

---

## Dependencies

```
Phase 1  (T001)        confirm the path works before testing it
Phase 2  (T002–T003)   seeding and waiting — blocks everything
Phase 3  US1 (T004–T007)  🎯 MVP — the effects
Phase 4  US2 (T008–T009)  the assertions are the right ones
Phase 5  US2 (T010)       ⭐ the deliberate break — the actual evidence
Phase 6  US3 (T011–T012)  it runs routinely
Phase 7  (T013–T017)      polish, measurement, PR
```

- T002 blocks everything: without an Active rule nothing fires.
- T004–T007 need T002 and T003.
- T010 needs Phase 3 complete — there must be a test to watch fail.
- T011 needs T010, or it is three green runs of something unproven.

## Notes

**Why T001 exists.** Writing a test against a path you have not seen work means
that when it fails you cannot tell whether the test or the product is wrong.
Five minutes by hand first removes that ambiguity for the whole feature.

**Why T010 has a phase to itself.** It is the deliverable. Every other task
produces a test that runs green, and a suite of tests running green is precisely
what was in place while this path was broken. T010 is the only task that
establishes the test can fail for the right reason.

**Why T006 is not optional.** A positive assertion that cannot fail is
indistinguishable from a passing one. The negative case is what proves the
positive case is load-bearing.

**Why T017 is a task rather than a habit.** Four specs missed the board gate
because `/speckit-taskstoissues` stops at creating issues. Making it a task is
the only way it stops being forgotten.
