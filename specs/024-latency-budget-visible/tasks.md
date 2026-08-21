---

description: "Task list for 024-latency-budget-visible"
---

# Tasks: Every leg of the latency budget can be watched

**Input**: Design documents from `/specs/024-latency-budget-visible/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md)

**Issues**: #1684–#1713, one per task, all verified on Project #13.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on incomplete work)
- **[Story]**: the user story it serves

---

## Read this before starting

**Two legs get instrumented. Four get an explanation.** Phase 0 found three legs
whose code does not exist and one that is arithmetic. **T012–T015 are the largest
part of this feature's output**, and they produce no code, which makes them the
ones that will quietly not happen.

**The measurement must span the leg, not a fragment of it.** ADR-0015 defines
leg 4 as RabbitMQ + projection. A histogram around one handler yields a number
that looks like the budget and is not, and it would be worse than no number
because someone would trust it.

**Check the exporter before comparing anything.** Spec 023 measured
instrumentation cost before and after, then had to record that the comparison
might be vacuous — the OTLP exporter only attaches when
`OTEL_EXPORTER_OTLP_ENDPOINT` is set and nobody checked. **T016 comes before
T017 for that reason and the order is not negotiable.**

**This feature ends with §VII still unmet.** Saying so is T019, not an
afterthought.

---

## Phase 1: Setup — establish the premise before changing anything

- [ ] T001 Try to answer, on the running system, "what is the p99 of the
      event-to-overlay leg?" without writing code. Record how far you get.
      **If it can be answered, stop** — the premise has changed and that is the
      finding.
- [ ] T002 [P] Confirm whether the OTLP exporter is actually attached in the
      integration fixture — is `OTEL_EXPORTER_OTLP_ENDPOINT` set for the
      services? Record the answer. Everything in Phase 5 depends on it, and
      spec 023 shipped a before/after comparison without knowing.

---

## Phase 2: Foundational — one instrument, registered once

- [ ] T003 Add a latency instrument to `src/ServiceDefaults/` recording a
      **distribution**, not a gauge or a most-recent value. It goes here, beside
      spec 023's trace source, for the same reason: the leg spans four services
      and instrumenting one end of it in one context measures a fragment.
- [ ] T004 Carry the leg's **budget** with the measurement (FR-003), so a reader
      who does not know the constitution can tell a pass from a breach without
      looking anything up.

**Checkpoint**: something can record a leg's duration. Nothing records one yet.

---

## Phase 3: US1 — someone can find out whether a leg is holding (P1) 🎯 MVP

- [ ] T005 [US1] Record the `event → overlay state` leg against the instrument,
      spanning **what ADR-0015 says the leg spans** — event accepted through to
      effect applied. Not one handler. Not one hop.
- [ ] T006 [US1] Verify the recorded span is the leg and not a fragment: compare
      what the instrument reports against the arrival-to-effect figure spec 022's
      `EventReachesItsEffectsTests` already logs. **If they disagree, one of them
      is measuring something else** — find out which before either is quoted.
- [ ] T007 [US1] Confirm a percentile can be obtained from the running system
      without writing code (SC-001). A histogram nobody can read is not a
      measurement.
- [ ] T008 [P] [US1] State what the figure **excludes** — delivery to a kiosk,
      which is legs 2, 3 and 5 and does not exist (FR-009). A number that
      silently means less than its name is how this programme keeps getting
      caught.

**Checkpoint**: SC-001 observable. The question the feature exists to make
answerable can be answered for one leg.

---

## Phase 4: US1 — the cheap second leg (P1)

- [ ] T009 [US1] Enable MediaMTX's metrics in
      `src/AppHost/Resources/mediamtx.yml`, and expose them if a scrape target
      is needed. Config, not code — the cheapest win in the feature.
- [ ] T010 [US1] Confirm `camera → SFU` latency is obtainable and readable
      against its 80 ms budget.
- [ ] T011 [US1] Confirm the media path is unaffected — a camera still streams.
      This is the streaming path, and a mistake here is visible to anyone
      watching a camera.

---

## Phase 5: US1 — what it cost (P1)

**T016 before T017. Not negotiable — see the header.**

- [ ] T016 [US1] Using T002's answer, establish that the before/after comparison
      can mean anything at all. If the exporter is not attached in the fixture,
      say so and measure somewhere it is, or record that the cost is unmeasured
      here and why.
- [ ] T017 [US1] Measure the warm path before and after the instrument, the same
      way both times, and confirm the overhead is under **5% of the measured
      leg's budget** (SC-004, FR-006). State the figure rather than calling
      instrumentation free.

---

## Phase 6: US1 — the four legs that get an explanation (P1) ⭐ the larger half

**These produce no code, which is exactly why they are marked as the
deliverable.** Phase 0 found them; this phase is where they are written down in
a form someone can act on.

- [ ] T012 [US1] Record **SFU → kiosk decode** as unmeasurable, with the reason:
      `apps/kiosk-web` contains no `<video>`, no `MediaStream`, no
      `RTCPeerConnection`. The kiosk decodes nothing. Note that live video lives
      in `management-web` via the shared `WhepClient`, so the capability exists
      and is not where the SLO points.
- [ ] T013 [P] [US1] Record **presentation buffer** as unmeasurable: PTP appears
      in ADR-0014 and in spec 002's *out of scope* section as a "future-add".
      Nothing implements it.
- [ ] T014 [P] [US1] Record **composite + render** as partially existing:
      overlays render, over nothing. Say what would have to exist first.
- [ ] T015 [P] [US1] Record **headroom** as arithmetic — the remainder of the
      other five against 800 ms — not a segment that can be timed.
- [ ] T018 [US1] Make the distinction legible in all four: **"not built" is a
      different problem from "built but unmeasured"** (FR-007), and a reader who
      cannot tell them apart will file the wrong follow-up.

**Checkpoint**: SC-003 met — every leg has a measurement or a reason, and no leg
is unaddressed and unmentioned.

---

## Phase 7: US2 — a leg can be watched without being asked about (P2)

- [ ] T020 [US2] Show at least one leg against its budget on a dashboard, such
      that a reader who does not know the constitution can tell a pass from a
      breach (SC-002).
- [ ] T021 [US2] Make **"no data" distinguishable from "within budget"**
      (FR-005). An idle system produces no measurements, and a blank panel
      reading as healthy is the failure this programme has met twice — a green
      thing that never ran, and a 401 that printed like an empty list.

---

## Phase 8: US3 — a change can show it did not break the budget (P3)

- [ ] T022 [US3] Write the procedure a PR author follows to produce a figure for
      the event-to-overlay leg (§IV, FR-008), such that someone who did not build
      this can follow it (SC-005).
- [ ] T023 [US3] Attach to that procedure what its output does **not** establish
      — the fixture is not a fab, as specs 020, 022 and 023 each had to say.

---

## Phase 9: Decisions and closure

- [ ] T019 **Say where this leaves §VII, plainly.** Two legs measured, four not,
      the principle still unmet. Present the options — amend §VII, accept the gap
      with a recorded reason, or treat the unbuilt legs as blocking — and leave
      the choice to the reviewer. **A feature that closes an issue by explaining
      why it cannot be closed has to say so at the end, not have it discovered at
      review.**
- [ ] T024 Put the **ADR-0026 decision** in front of the reviewer (FR-011):
      enact, amend, or split. It is Locked, its comparison phase never started,
      and its sunset clause has nothing to sunset. Do not resolve it by
      implementation.
- [ ] T025 **File the product finding separately**: the 800 ms path is not
      assembled end to end — three of six legs are unbuilt. This feature
      discovered it and should not absorb it (#1655's precedent).
- [ ] T026 Run the full integration suite and `scripts/coverage-check.ps1
      -Configuration Release`. Nothing excluded, nothing weakened (SC-008).
- [ ] T027 Confirm steady-state latency is no worse than before (SC-006,
      FR-012), measured the same way as T017.
- [ ] T028 Walk [quickstart.md](./quickstart.md) and write `verification.md`.
      **"Done" is the observations**, and step 4 is the one that will get
      skipped.
- [ ] T029 Open the PR with `Closes #1681`, stating what is measured, what is
      not, and that §VII remains unmet.
- [X] T030 Add every issue created for these tasks to **Project #13**
      (`gh project item-add 13 --owner smartsolutionslab --url <issue-url>`).
      Verify with `item-list` — `item-add` prints nothing on success and the
      board holds hundreds of items, so a short `--limit` finds nothing and looks
      identical to a failed add.

---

## Dependencies

```
Phase 1  (T001-T002)   establish the premise and the exporter's state
Phase 2  (T003-T004)   the instrument
Phase 3  US1 (T005-T008)  🎯 MVP — the one implemented, budgeted leg
Phase 4  US1 (T009-T011)  the cheap second leg
Phase 5  US1 (T016-T017)  what it cost — T016 FIRST
Phase 6  US1 (T012-T015, T018)  ⭐ the four explanations
Phase 7  US2 (T020-T021)  a dashboard
Phase 8  US3 (T022-T023)  a PR can cite something
Phase 9  (T019, T024-T030)  decisions, suite, PR
```

- **T002 blocks T016, which blocks T017.** A before/after over a pipeline that
  exports nothing measures nothing.
- T003 blocks T005: no instrument, nothing to record against.
- T006 blocks T007: confirm the instrument measures the leg before quoting a
  percentile from it.
- T009 blocks T010–T011.
- Phase 6 depends on nothing and could run first. It is placed after the
  instrumentation only so the contrast is concrete.

## Notes

**Why Phase 6 is marked the larger half.** Four of six legs end there. It is the
answer to the question #1681 asked, and it is entirely prose, which means it
competes for attention with tasks that produce running code and loses. Spec 023
ended the same way — its most valuable output was a list of things that were not
true — and that only survived because it was a numbered task.

**Why T006 exists.** An instrument that reports a plausible number for the wrong
span is worse than no instrument: someone will cite it. Spec 022's harness
already measures arrival-to-effect, so there is an independent figure to check
against, and disagreement is information rather than an obstacle.

**Why T021 is not a detail.** Twice now this programme has been caught by a
failure that rendered identically to success — a green suite that never ran, and
a 401 that printed like an empty list. A dashboard panel with no data must not
look like a passing budget.

**Why T019 and T024 are tasks rather than judgement calls.** Both are decisions
above the implementer's pay grade — a constitutional reading and a Locked ADR —
and both would otherwise be settled silently by whatever the code happened to do.
