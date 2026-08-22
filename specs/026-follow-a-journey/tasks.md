# Tasks: A cross-service journey can be followed end to end

**Input**: `specs/026-follow-a-journey/` — spec.md, plan.md, research.md, quickstart.md
**Issue**: #1750

**Tests**: Yes — integration against the real Aspire stack, plus **two manual
observations** (SC-001, SC-007) that no test substitutes for.

## Format: `[ID] [P?] [Story] Description`

---

## What changed, and why the numbering restarts

This list replaces the first one, which was written against a cause that turned
out to be false. **The baseline and investigation tasks are already done** and
their findings are in research.md; they are listed here as complete rather than
deleted, because the next reader needs to know they happened.

| Old | Fate |
|---|---|
| T001 baseline | **Done** — Findings 1 & 4 |
| T002 measurement baseline | Carried forward as T101 |
| T003 outbox loss | **Done, and it falsified the premise** — Finding 3 |
| T004/T005 metadata rule | **Not needed.** Nothing to stamp |
| T006 the fork | **Resolved** — Wolverine already joins every hop that has a cause |
| T007 custom span code | **Not needed** |

Nine tasks where there were twenty-three. That is the finding, not an estimate.

---

## Phase 1: Baseline

- [X] T100 Record the current state and the mechanism — **done**, in `research.md` Findings 1, 3 and 4, and in `quickstart.md`'s "Before". Both broken trace IDs and the already-working three-service trace are written down.
- [X] T101 **Missed as specified, then recovered.** No pre-change measurement was taken — implementation followed the research directly. Recovered by reverting the change in place (`git revert -n 724af4c`), measuring, and restoring: a same-session A/B rather than a comparison against figures from another day. See verification.md.

---

## Phase 2: Foundational

**Purpose**: give an ingested event a cause. This is the feature.

- [X] T102 Add the interface the Application layer sees to `src/Shared.CQRS/` — one method, "this event is the beginning of a journey", returning something disposable. Mirror `ILatencyBudget`'s arrangement and its reasoning; do not invent a second pattern for the same problem.
- [X] T103 Implement it in `src/ServiceDefaults/` over an `ActivitySource`, and register it beside `ILatencyBudget` in `WolverineDefaults.AddWolverineForContext`. **Check whether any OpenTelemetry change is needed at all** — `Extensions.cs` already registers `AddSource(builder.Environment.ApplicationName)`, so a source named for the application may already be exported. Verify; do not assume (spec 024).
- [X] T104 Start the activity in `src/EventIngestion/Application/EventHandlers/EventIngestedDomainEventHandler.cs`, around the publish. `DomainEventDispatcher` invokes handlers one domain event at a time, so **per-event falls out of the structure** rather than being defended by care.

**Checkpoint**: a plant-floor event has a beginning.

---

## Phase 3: User Story 1 — From an effect, find its cause (P1) 🎯 MVP

- [~] T105 [US1] **Not achievable, and not quietly dropped.** Aspire runs each service as its own process, so an integration test cannot observe another service's `Activity.Current`, and the Aspire dashboard exposes no supported query API to assert against. The cross-service proof is T108's dashboard walk — which is what FR-008 and SC-007 ask for — and the automated coverage is at the unit level (T106, T107). **Consequence, stated plainly: if this regresses, nothing in CI will catch it.** Closing that needs a queryable sink, which ADR-0118 defers to the production deployment.
- [X] T106 [US1] **The batch guard** (FR-006, SC-005). Assert two events ingested in the **same batch** have **different** causes and do not share a journey. This is the task most likely to fail, and the one whose absence would let the cheap version ship: a batch-level activity joins the trace and looks correct from the effect end.
- [X] T107 [P] [US1] Cover the no-cause case in `tests/ServiceDefaults.Tests/`: nothing to record means nothing recorded — no empty relationship, no invented one, no throw.
- [X] T108 [US1] **Follow it by hand** (SC-001, FR-008): walk `quickstart.md`'s "After" in the Aspire dashboard, from applied effect back to cause, and record it in `verification.md` **with a screenshot**. A passing test does not establish this; spec 024's source was registered and invisible for two days.

**Checkpoint**: MVP. US2 and US3 are checks on this, not additions to it.

---

## Phase 4: US2 + US3 — the other direction, and the guard (P1/P2)

Combined, because both are now assertions about behaviour this change inherits
rather than builds — which is exactly why they are worth writing down.

- [X] T109 [US2] From an event, its downstream work is discoverable, and an event causing **two** effects yields both (FR-003, SC-004). Fan-out is already visible in the recorded trace; assert it so it stays true.
- [X] T110 [US3] Assert SC-003 and FR-010: no span's duration grew to include a delivery wait, and the spec-025 `event → overlay state` measurement is unchanged and still does not depend on telemetry. The known-good reading to check against is trace `195d9123…` — 4305 ms overall, spans of 42/0/58/1.

---

## Phase 5: Polish

- [X] T111 Re-measure (SC-006, FR-009) and compare against T101 — **latency and ingest throughput**. The ingest path is sized for 5 000 events/s and its batching exists to protect the database round trip; an activity per event runs five thousand times a second at design load. Report the number, don't round it away.
- [ ] T112 [P] Full suite, nothing excluded or weakened (SC-008). New code lands in `ServiceDefaults` and `Shared.CQRS`, both under the Shared ≥ 90% gate (ADR-0065).
- [X] T113 [P] Format and analyzers clean on Release — collection expressions and the SonarAnalyzer metric limits (ADR-0084) fail rather than warn.
- [X] T114 Complete `verification.md`: both manual walks with screenshots, before/after trace IDs, both measurements, **and the batch check stated explicitly** — "two events from one batch, two traces" is the line that says the cheap version was not shipped.

**No ADR task.** Using a library as documented is not an architectural decision.
The previous list made an ADR conditional on writing custom span code; that code
is not being written.

---

## Dependencies

```
T101 ──┐
T102 → T103 → T104 ──→ T105, T106, T107 ──→ T108
                              └──────────→ T109, T110
                                              ↓
                                    T111, T112, T113 → T114
```

---

## Two things most likely to go wrong

**A batch-level cause.** Cheaper, joins the trace, looks right from the only end
most people check. T106 exists solely for this, and it is the task to write
first if any get dropped.

**Confirming it works without a person looking.** T108 is manual on purpose.
This programme has now been caught **seven** times by something that rendered as
success — most recently by a spec whose three central premises were all false and
all passed a quality checklist.
