# Tasks: The overlay and the picture it annotates

**Feature**: `046-overlay-matches-its-frame` · **Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md)

**16 tasks across six phases**, in **two parts that ship independently**.

**Part 1 (T001–T005) stops the system claiming a synchronisation it does not
perform.** Its benefit is certain and verifiable by reading. **If the feature
stops there it has delivered something worth having.**

**Part 2 (T006–T014) holds each label back by its own tile's measured frame age.**
Its benefit is real, **imperceptible**, and paid for in latency on the same
800 ms budget — so it must justify itself on correctness, and must not delay
Part 1.

**The scope was chosen after a probe killed the alternative.**
[research.md](./research.md) established that full frame accuracy cannot be built
here: the capture-time extension is neither offered nor acceptable to the SFU,
the browser cannot relate its RTP timestamps to any wall clock, and — the blocker
that survives fixing both — a camera's capture clock and the event source's clock
are unrelated without PTP hardware that does not exist.

---

## Do not

- **Do not call any of this frame accuracy**, frame synchronisation or frame
  matching — in code, comments, UI, metric names or commit messages. **It makes
  a label as old as the picture; it does not pair a value with a frame** (FR-008).
  Restating the overclaim in a new form is the outcome this feature exists to
  remove, and it is the easiest mistake here to make by accident.
- **Do not start Part 2 before T001 lands.** ADR-0021 is Locked and cannot be
  built as written; picking a mechanism first is silently redesigning it.
- **Do not derive the delay from a constant.** It comes from that tile's own
  measured frame age (FR-007). A constant is an assumption wearing a
  measurement's clothes, and tiles differ.
- **Do not report the intended delay.** Report the achieved one (FR-015). Spec
  045 shipped a setpoint that could not be read back and had to be corrected.
- **Do not split the contract change across two commits.** The endpoint 400s an
  unknown measurement name and the reporter swallows failures, so a split ships a
  kiosk that reports nothing and looks healthy.
- **Do not touch the media path.** ADR-0128's rejection stands (FR-017).
- **Do not delay a tile that carries no overlay** (FR-013), and assert that as
  *no timer scheduled* — spec 045's review found the unchanged-latency form
  passes against a component doing nothing.
- **Do not write `#NNNN`-style bare issue numbers** in committed docs — the
  automation closes a merely-mentioned issue on merge.

---

## Phase 1: The record (US1, Part 1) — ships alone

- [ ] T001 Write `docs/adr/0129-labels-are-aged-not-frame-matched.md`, amending ADR-0021. It must state: that ADR-0021 cannot be built as written; **all three blockers** from [research.md](./research.md), including the one that survives fixing the others (a camera's clock and the event source's clock are unrelated without PTP); that **age-matching is adopted and is not frame accuracy**; and that ADR-0128's media-path rejection is **preserved, not revisited**.
- [ ] T002 [US1] Correct §IV's **"frame-synced"** wording and ADR-0015's leg description. §IV cannot change without T001's ADR (governance).
- [ ] T003 [US1] Record, in one place a reader will find, **what the relationship actually is** — a label describes the moment its value changed; the picture beneath it is `buffer + processing` old — **and the direction playout buffering moves it** (FR-004). Without this the next feature to add buffer will not know it widened the gap, which is how this feature came to exist.
- [ ] T004 [P] [US1] Extend `tests/Architecture.Tests/` with a guard on the corrected wording, following `FoundingDecisionRecordTests`' **consistency-check** shape rather than a text pin: it must fail if the record and the behaviour disagree, and **must not fail when the record is legitimately updated**.
- [ ] T005 [P] [US1] Test that the guard permits a legitimate rewording. Spec 047's review found a guard that made partial progress unrepresentable; this is the check that catches that class.

**Checkpoint**: **Part 1 complete and independently shippable.** The system no
longer claims something it does not do.

---

## Phase 2: The delay, as arithmetic (Part 2)

- [ ] T006 Create `apps/shared/src/observability/labelDelay.ts` — pure: given a tile's frame age, the delay to apply, bounded per FR-009. **`null`, never `0`**, when the age is unreadable: a zero reads as a perfect score for something nobody measured, which is this codebase's standing rule.
- [ ] T007 [P] `labelDelay.test.ts` — the bound, the unreadable case, and that a **zero-age tile gets no delay rather than a zero delay**. Those are different, and only one of them is honest.

**Checkpoint**: the arithmetic is testable without a browser.

---

## Phase 3: Hold the label (US2, Part 2)

- [ ] T008 [US2] Create `apps/kiosk-web/src/features/cell/useLabelDelay.ts`. Schedule on **monotonic time** (`performance.now()`), never epoch time — fab clocks are PTP-stepped and `CellPage` already carries that reasoning for its highlight timers.
- [ ] T009 [US2] Preserve ordering and drop nothing (FR-012). The existing monotonic **version guard** on overlay text is the model; two updates inside one delay window must arrive in order, and neither may vanish.
- [ ] T010 [US2] Wire it in `CellPage`: each tile gets **its own** delay, sourced from `useWallAlignment`'s per-tile figure. A tile with no overlay is untouched (FR-013).
- [ ] T011 [US2] Every failure path shows the label **immediately** — unreadable age, arithmetic `null`, a thrown timer (FR-011, FR-014). Video and overlays both keep working.

**Checkpoint**: **US2 shippable.**

---

## Phase 4: Measure what was applied (Part 2)

- [ ] T012 **One task, one commit, both sides.** Add the measurement name to the closed set in `src/StreamDistribution/Api/StreamEndpoints.cs` **and** send it from `apps/shared/src/observability/kioskLatency.ts`. Update the validation message to name every accepted value. **Report the achieved delay, not the intended one** (FR-015).
- [ ] T013 [P] Decide **and record** whether this is a `LatencySegment` or its own instrument. It *is* a duration, so a segment is defensible — **but it is not one of ADR-0015's six legs**, and spec 045's `WallSkew` exists precisely because filing a quantity under a name that means something else is how this codebase gets caught. Whichever is chosen, write the reason next to it.

---

## Phase 5: Tests that could fail

- [ ] T014 [P] Test that **inducing buffer changes the label delay** (SC-004). **Induced, never observed passively**: a small difference between label delay and frame age proves nothing if neither was moved — spec 045's central lesson, and its review still found five vacuous tests after that lesson was written down.
- [ ] T015 [P] Test that a tile with **no overlay schedules no timer** (SC-006) — asserted as the absence of a scheduled timer, not as unchanged latency.

---

## Phase 6: Verify

- [ ] T016 Run the full backend and frontend suites the way CI does, **not a subset** — spec 045 shipped a green subset and CI caught an architecture test never run locally. Then walk a real wall: induce buffer on a tile, confirm its label delay follows, confirm the end-to-end budget still holds with the delay counted in, and **record that nobody can see this** — ~30 ms is below the threshold at which an eye distinguishes a label from the frame under it, so there is no human confirmation step and the note must say so rather than imply one happened.

---

## Dependencies

```
T001 ─▶ T002, T003 ─▶ T004 ─▶ T005            Part 1 — ships alone
  │
  └─▶ T006 ─▶ T007
             │
             ▼
           T008 ─▶ T009 ─▶ T010 ─▶ T011        Part 2
                              │
                              ├─▶ T012 ─▶ T013
                              └─▶ T014, T015
                                        │
                                        ▼
                                      T016
```

**T001 gates everything.** ADR-0021 is Locked and unbuildable as written.

**Part 1 does not depend on Part 2.** That is the point of the split, and the
dependency graph is drawn to make stopping after T005 a clean outcome.

## Parallel opportunities

- **T004 and T005** — the guard and its permits-progress test.
- **T014 and T015** — different behaviours, different files.
- **T007** — pure arithmetic, no dependency on the hook.
- **Phase 3 is NOT parallel**: T008 → T011 is one mechanism threaded through one
  file.

## Implementation strategy

**Ship Part 1 first and separately.** It is the certain half. Part 2's benefit is
imperceptible and costs latency, so it should be reviewed on its own merits
rather than carried in on Part 1's coat-tails.

**T012 and T013 are one commit** for the reason in the Do-not list.

---

## Three things most likely to go wrong

1. **Something gets called frame sync.** A metric name, a variable, a comment.
   FR-008 forbids it and a reviewer should grep for it — because the whole
   feature exists to remove exactly that overclaim, and reintroducing it in a
   new form would be a complete own goal.

2. **A test observes rather than induces.** Nobody can see 30 ms, so a check
   that passively compares label delay to frame age passes whether or not the
   mechanism works. Every check induces buffer first (T014). Spec 045 wrote this
   lesson down and *still* shipped five vacuous tests, caught only in review.

3. **Part 2 ships and Part 1 quietly does not.** The mechanism is the
   interesting work; the record correction is the certain benefit. Phase 1 is
   first and ships alone specifically to stop that ordering inverting.

---

## What the automated checks do and do not prove

| Claim | Proved by | Not proved by |
|---|---|---|
| The record no longer claims frame synchronisation | T004 | — |
| The guard permits a legitimate rewording | T005 | — |
| An unreadable frame age yields no delay, not a zero | T007 | — |
| Inducing buffer changes the label delay | T014 | any passive comparison |
| A tile with no overlay schedules nothing | T015 | an unchanged-latency assertion |
| The end-to-end budget still holds | T016 | — |
| **That an operator is better off** | **nothing** | **everything above** |

The last row is the honest one. **The gap is below what an eye resolves**, so no
person can confirm the improvement and this feature promises none. Part 2 earns
its place on correctness alone — which is precisely why Part 1, whose benefit
*is* verifiable, is the half that ships first.
