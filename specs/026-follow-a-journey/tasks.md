# Tasks: A cross-service journey can be followed end to end

**Input**: `specs/026-follow-a-journey/` — spec.md, plan.md, research.md, quickstart.md
**Issue**: #1750

**Tests**: Yes. FR-002/003 are about telemetry a person reads, so the tests here
are integration tests against the real Aspire stack plus **two manual
observations** (SC-001, SC-007) that no test can stand in for.

## Format: `[ID] [P?] [Story] Description`

---

## The shape of this list, and why it is unusual

**This feature has a fork in it.** Phase 0 found that Wolverine may already do
most of the work — `Envelope.CorrelationId` and `ParentId` exist and
`WolverineTracing.StartReceiving` already builds the receive activity from the
envelope — so **carrying them through the outbox may join the journey up with no
custom span code at all.**

T006 is where that is settled. Everything after it is written for the cheap
outcome; **T007 is the branch to take if it fails**, and it is deliberately in
the list rather than left as a surprise. Writing the list as though the answer
were known would be the same mistake the spec already made once.

**T001 runs first and asserts nothing.** Same reason as spec 018's T001: "the
journey joined up" is a comparison, and there is no comparison without a
before. It is also the only chance to record what a broken journey looked like.

---

## Phase 1: Baseline (Setup)

**Purpose**: record today, so afterwards means something

- [ ] T001 Observe and record the current state in `specs/026-follow-a-journey/verification.md`: publish one plant-floor event, find the publishing trace in the Aspire dashboard, confirm it contains only `event-ingestion` spans, and record **both trace IDs** — the publishing root and the separate handling root. Assert nothing.
- [ ] T002 [P] Record the steady-state baseline by running `dotnet test tests/Integration.Tests --filter "Category=Measurement"` and noting arrival-to-effect against the 267–369 ms recorded by specs 022 and 024. This is the figure SC-006 is measured against.

**Checkpoint**: the "before" exists in writing, with trace IDs.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: get the causal context through the outbox. **Every story depends on
this and none can start before T006 resolves.**

- [ ] T003 Confirm the loss experimentally rather than by reading schema: publish an integration event, then read the row back from `wolverine_outgoing_envelopes` and show the correlation and parent context is **not** recoverable from it. Record the finding in `research.md`. The spec asserts this from a seven-column table; asserting it from a rehydrated envelope is stronger and takes ten minutes.
- [ ] T004 Implement the metadata rule as an `IEnvelopeRule` in `src/ServiceDefaults/` that stamps the current activity's context onto every outgoing `Envelope.Headers`. Follow `PropagateHeadersRule`'s shape — the mechanism Wolverine already uses for tenancy and delivery windows (FR-005). **Nothing business-related goes in a header** (constitution §VIII): trace identifiers are opaque and stay that way.
- [ ] T005 Register the rule once in `src/ServiceDefaults/WolverineDefaults.cs`, in `AddWolverineForContext`, beside the other cross-cutting registrations. One place, so a tenth context cannot be added and forgotten — the same reasoning the outbox publisher registration already carries in that file.
- [ ] T006 **The fork.** Publish an event, let it round-trip through the outbox, and determine whether Wolverine's own receive tracing now joins the journey. Record the answer in `research.md` as a third finding, with evidence. **If yes, T007 is not needed and the feature is a rule and its tests.**
- [ ] T007 *(only if T006 says no)* Emit the relationship at the receiving end from the carried headers, using OpenTelemetry's own mechanism rather than a private one (FR-005). If this task runs, say so in the PR body — it means the feature is twice the size the plan expected.

**Checkpoint**: the context survives the outbox, and it is written down which route got it there.

---

## Phase 3: User Story 1 — From an effect, find its cause (P1) 🎯 MVP

**Goal**: given something that happened, find the plant-floor event responsible.

**Independent test**: cause an effect, take its record, follow it back to the
originating event without correlating by wall-clock time.

- [ ] T008 [US1] Write an integration test in `tests/Integration.Tests/Automation/` that publishes a plant-floor event, waits for its effect, and asserts the handling work is reachable from the originating event's telemetry. **Assert on the relationship, not on a timestamp** — correlating by time is what this feature exists to replace.
- [ ] T009 [US1] Guard the trap the plan names: assert the message **actually went through the outbox**, by confirming it was written to and read back from `wolverine_outgoing_envelopes` rather than handled in-process. A test that publishes and handles in one process proves nothing about the store-and-forward hop, and would pass today.
- [ ] T010 [P] [US1] Cover FR-006 in `tests/ServiceDefaults.Tests/`: a message with **no** upstream cause records no relationship — not an empty one and not a fabricated one. There is no current activity for a scheduled job or an operator action, and the rule must stay silent rather than inventing a root.
- [ ] T011 [P] [US1] Cover FR-007 in `tests/ServiceDefaults.Tests/`: a message carrying **no** headers — anything already in the outbox when this ships — is handled exactly as today rather than failing. Degrade, don't throw.
- [ ] T012 [US1] **Follow it by hand** (SC-001, FR-008). Walk `quickstart.md`'s "After" section in the Aspire dashboard, from an applied effect back to its cause, and record it in `verification.md` **with a screenshot**. Spec 024 registered a trace source and could not confirm spans arrived for two days; a relationship nobody can follow in the sink is the same as no relationship, and a passing test does not establish this.

**Checkpoint**: US1 is independently shippable. This is the MVP.

---

## Phase 4: User Story 3 — A delayed hop does not lie (P1)

**Goal**: nothing reports a queue wait as though it were work.

**Runs after US1 because it checks US1's mechanism.** It is P1 and not optional:
if this fails, the feature has replaced a missing answer with a wrong one, which
is worse than shipping nothing.

- [ ] T013 [US3] Assert SC-003 directly: publish through a **deliberately delayed** delivery, and assert **no span's duration grew** to include the wait. The publish span measures publishing; the handling span measures handling. Compare against T001's recorded figures rather than against intuition.
- [ ] T014 [US3] Assert FR-010: the `event → overlay state` measurement from spec 025 is unchanged and **still does not depend on telemetry**. It is computed in-process from `RootIngestedAt`; this feature must not become an input to it. `tests/ServiceDefaults.Tests/EventToOverlayLatencyTests.cs` already covers the behaviour — the check here is that nothing new reaches it.
- [ ] T015 [US3] Observe what happened to the **trace listing** (research.md's remaining argument for links). If every trace is now minutes long and sorted by duration, the dashboard is harder to use than before and that is a finding, not a detail. Record it either way — "it was fine" is also an answer, and an unrecorded one reads as unchecked.

**Checkpoint**: the journey joins up and no number got worse.

---

## Phase 5: User Story 2 — From an event, find what it caused (P2)

**Goal**: given an event, see what it went on to do.

**Expected to need no new mechanism** — recording that B was caused by A should
make both directions answerable. These tasks check that expectation rather than
assuming it.

- [ ] T016 [US2] Extend the integration coverage: from an event, its downstream work is discoverable (FR-003). If this needs anything the US1 mechanism did not already provide, that is worth recording — the spec's assumption said it would not.
- [ ] T017 [US2] Cover fan-out (SC-004): an event causing **two** effects yields both when its consequences are listed. Neither direction is a single line, and one-of-two would look exactly like success.
- [ ] T018 [US2] **Follow it by hand in the other direction** and record it in `verification.md`. Same reason as T012: SC-002 is about a person, and the dashboard is the only place that is true.

**Checkpoint**: both directions work.

---

## Phase 6: Polish & Cross-Cutting

- [ ] T019 Re-measure steady state (SC-006, FR-009) with `--filter "Category=Measurement"` and compare against T002. **Headers on every message are not free** — the point is to know the cost, not to assume it is negligible. If it regressed, say by how much rather than rounding it away.
- [ ] T020 [P] Run the full suite with nothing excluded, skipped or weakened (SC-008). Watch the coverage gates: the new code is in `ServiceDefaults`, which sits under the Shared ≥ 90% gate (ADR-0065).
- [ ] T021 [P] Format and analyzers clean — Release build, so `dotnet_style_prefer_collection_expression` and the SonarAnalyzer metric limits (ADR-0084) fail rather than warn.
- [ ] T022 Complete `verification.md`: both manual walks with screenshots, the before/after trace IDs, both measurement figures, and **which route T006 took**. A future reader needs to know whether this feature is a rule or a rule plus custom span code.
- [ ] T023 Update `docs/adr/` **only if T007 ran**. Taking the cheap route is using a library as documented and needs no ADR; writing custom span emission is an architectural choice and needs one.

---

## Dependencies

```
T001, T002  (baseline — before anything changes)
      ↓
T003 → T004 → T005 → T006 ──┬── (yes) ──→ Phase 3
                            └── (no) ───→ T007 → Phase 3
      ↓
Phase 3 (US1) ──→ Phase 4 (US3, checks US1's mechanism)
      ↓
Phase 5 (US2) — independent of Phase 4, needs Phase 3
      ↓
Phase 6
```

**US3 depends on US1** rather than being parallel to it, despite equal priority:
there is nothing to check for inflated durations until the mechanism exists.

**US2 does not depend on US3.** They can run in either order once US1 lands.

---

## Parallel opportunities

- **T010, T011** — different concerns, same test project, no shared file.
- **T020, T021** — independent verification runs.
- **T002** runs alongside T001.

Most of this list is sequential, which is honest: the foundational phase is one
rule threaded through one registration, and there is nothing to fan out.

---

## Implementation strategy

**MVP is Phases 1–3.** US1 answers the question people actually ask and is the
one that cost spec 023 a day.

**Phase 4 is not optional despite being a separate phase.** US3 is P1, and a
journey that joins up while inflating a duration is a regression dressed as a
feature.

**Stop and reconsider if Phase 2 grows.** The plan expects a rule, a
registration and their tests. If T007 runs and the code keeps growing past that,
the cheap route failed for a reason worth understanding before building around
it.

---

## Two things most likely to go wrong

**A test that passes without exercising the outbox.** T009 exists for this. The
entire feature is about the store-and-forward hop; in-process publish-and-handle
already joins up today and would pass a naive test unchanged.

**Confirming it works without a person looking.** T012 and T018 are manual on
purpose. Spec 024's trace source was registered and invisible for two days, and
this programme has now been caught six times by something that rendered as
success — most recently by a **checklist** that passed a claim nobody had
checked against the mechanism.
