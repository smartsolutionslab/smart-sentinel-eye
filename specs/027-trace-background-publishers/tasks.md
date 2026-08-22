# Tasks: Every journey has a beginning, not just the ones from the plant floor

**Input**: `specs/027-trace-background-publishers/` — spec.md, plan.md, research.md, quickstart.md
**Issue**: #1781

**Tests**: Yes — per-context unit tests, plus **manual dashboard observations**
(SC-001, SC-002, SC-007) that no test substitutes for. Spec 026 established there
is no cross-service test to write: each service runs as its own process and the
sink has no query interface.

## Format: `[ID] [P?] [Story] Description`

---

## No foundational phase, and that is the point

`IJourneyOrigin` shipped in spec 026 and is registered for every context by
`AddWolverineForContext`. **There is nothing to build before the user stories
start.** If a foundational phase appears here, the diagnosis was wrong.

## The two placements

Research Finding 1 changed where the code goes, and the two sites look opposite
while following one rule — *one journey per announcement*:

| Site | Journey goes | Because an iteration is |
|---|---|---|
| Stream health | the **domain event handler** | usually **no** announcement |
| Audit retention | **inside the loop**, per chunk | exactly **one** announcement |

Putting the watcher's journey in its loop fails twice over: it merges every
camera in a sweep, **and** it creates journeys for cameras that changed nothing,
because `PollOnceAsync` calls the command handler unconditionally.

---

## Phase 1: Baseline

- [X] T001 Record the current state in `specs/027-trace-background-publishers/verification.md`: cause a camera to change health, find the `StreamHealthChangedV1` publish trace and audit-observability's separate receive trace, and record **both IDs**. Do the same for one `AuditChunkArchivedV1`. Assert nothing — this is the comparison, and the only chance to record what a broken journey looked like at these two sites.
- [X] T002 [P] Record the steady-state baseline: `dotnet test tests/Integration.Tests --filter "Category=Measurement"`, **twice**, plus health-poll cadence and retention run duration. Two runs because a single run after machine churn reads exactly like a regression (spec 026). This is what T016 compares to.

**Checkpoint**: the "before" exists in writing, with trace IDs.

---

## Phase 2: User Story 1 — From a stream health record, find the check that caused it (P1) 🎯 MVP

**Goal**: a camera going unhealthy starts a journey that its downstream record can be traced back to.

**Independent test**: cause a state change, take the downstream record, follow it back without using timestamps.

- [X] T003 [US1] Inject `IJourneyOrigin` into `src/StreamDistribution/Application/EventHandlers/StreamHealthChangedDomainEventHandler.cs` and begin a journey around the publish, marking failure with `IJourney.Failed(Exception)` on the exception path. Mirror `EventIngestedDomainEventHandler`. **Not in `StreamHealthWatcher.PollOnceAsync`** — say why in a comment, because the loop is the obvious place and is wrong twice (research Finding 1).
- [X] T004 [P] [US1] Add a recording journey-origin fake and a bus fake to `tests/StreamDistribution.Application.Tests/Fakes/` — the project has neither. Model them on `tests/EventIngestion.Application.Tests/Fakes/RecordingJourneyOrigin.cs`, which must record *how many journeys were open at the moment of the publish*: a call count cannot tell a journey that caused the publish from one closed before it.
- [X] T005 [US1] Test in `tests/StreamDistribution.Application.Tests/EventHandlers/`: the publish happens **inside** the journey, each domain event gets its **own** journey, and the journey does not outlive the event.
- [X] T006 [US1] Assert the only-on-change property (FR-006, SC-005) where it actually lives — `src/StreamDistribution/Domain/Stream/Stream.cs` raises `StreamHealthChangedDomainEvent` only when `previous != State`. A poll that observes no change must produce no domain event and therefore no journey. **Assert it rather than trust it**: this is the half of the loop trap that was not anticipated.

**Checkpoint**: MVP. A camera's journey has a beginning.

---

## Phase 3: User Story 2 — From an archived chunk, find the run that archived it (P2)

**Goal**: an archived audit chunk starts a journey.

**In scope despite P2, and listed second rather than dropped**: it is the call
site most likely to be skipped, because it publishes inline and does not look
like the thing spec 026 fixed.

- [X] T007 [US2] Inject `IJourneyOrigin` into `src/AuditObservability/Application/Retention/AuditRetentionHostedService.cs` and begin a journey **inside** the chunk loop, in `ArchiveAndDropAsync`, around one chunk's publish. Mark failure on the existing `try`. **Comment the asymmetry with US1** — same rule, opposite placement — because a reader arriving from spec 026 will look for a domain event handler, not find one, and move on.
- [X] T008 [P] [US2] Add a recording journey-origin fake to `tests/AuditObservability.Application.Tests/Fakes/`. `FakeBus` already exists there. **This is the third copy of this fake in the repo**; note it in the PR rather than collapsing three test projects in a feature that is about something else.
- [X] T009 [US2] Extend `tests/AuditObservability.Application.Tests/Retention/AuditRetentionHostedServiceTests.cs`: the publish happens inside the journey, and **a run archiving several chunks produces several journeys** (FR-003, SC-003).
- [X] T010 [US2] Assert a run with nothing to archive produces no journeys (FR-006, SC-005). The service already returns early on an empty chunk list; assert the consequence.

---

## Phase 4: User Story 3 — A failed announcement does not read as a quiet success (P1)

**P1, and after US2 despite that**, because it asserts a property of *both* sites
and cannot complete until both exist. Same sequencing spec 026 used for its US3.

- [X] T011 [US3] Assert at both sites that a refused publish marks the journey failed and still ends it (FR-004, SC-004). One test per context, beside the tests from T005 and T009.
- [X] T012 [US3] Assert the negative: a publish that **succeeds** leaves the journey unmarked. A status that is always set carries no information, and this is the half that makes SC-004 meaningful.

---

## Phase 5: Polish & Cross-Cutting

- [X] T013 **Write the survey down** (FR-009, SC-008) as a table in `verification.md`: every `IEventBus.PublishAsync` call site in product code, each classified as having a cause or not needing one. Finding the orphans was the expensive part of this feature; an undocumented survey means the next person repeats the search.
- [~] T014 **Open, not done.** The camera registrations that would have shown it happen at boot and had aged out of the dashboard's retained window by the time it was looked for. Marked open rather than upgraded to observed. Nothing in this feature depends on the answer. Original: **Close the HTTP inference** (research Finding 3). Nine publishers are classified as fine because a request establishes their cause; message-driven is observed directly, HTTP is observed one layer short. The stack is up for T015 anyway — register a camera through the API and check the `send` span sits under the `POST` span. If it does not, that is a new finding and a new issue, not a change here.
- [~] T015 **Half done.** The stream-health walk is recorded with the joined trace `0c7f1153`; the retention walk is not, because the sweep runs on a long timer and no archival occurred in the window. Original: **Follow both journeys by hand** (SC-001, SC-002, SC-007, FR-008): walk `quickstart.md`'s "After" in the dashboard for a camera health change **and** an archived chunk, and record both in `verification.md` with screenshots. Also check the dashboard is **not** full of journeys for cameras that changed nothing — that is what the unanticipated half of the loop trap looks like in the sink.
- [X] T016 Re-measure (FR-007, SC-006) and compare against T002 — measurement suite **twice**, plus poll cadence and retention duration. Report the numbers; a single run is not evidence.
- [ ] T017 [P] Full suite, nothing excluded or weakened (SC-009). Watch the coverage gates: both contexts' Application layers sit under the ≥ 80% gate (ADR-0065).
- [X] T018 [P] Format and analyzers clean on Release — collection expressions and SonarAnalyzer metric limits (ADR-0084) fail rather than warn.
- [ ] T019 Complete `verification.md`: both walks with screenshots, before/after trace IDs, both measurements, the survey table, the no-change check, and the answer to T014.

**No ADR task.** Using an existing abstraction at two more call sites is not an
architectural decision.

---

## Dependencies

```
T001, T002  (baseline — before anything changes)
      ↓
T003 → T004 → T005, T006          (US1)
      ↓
T007 → T008 → T009, T010          (US2)
      ↓
T011, T012                        (US3 — needs both sites)
      ↓
T013, T014, T015, T016, T017, T018 → T019
```

**US1 and US2 are independent of each other** once the baseline is recorded — the
sequencing above is priority order, not a technical constraint. They touch
different contexts and different test projects, so they could run in parallel if
two people were on it.

---

## Parallel opportunities

- **T004** and **T008** — the two fakes, different projects.
- **T017, T018** — independent verification runs.
- **T002** alongside T001.

---

## Implementation strategy

**MVP is Phases 1–2.** A camera going unhealthy is the journey an operator
actually asks about.

**Phase 3 is not optional despite P2.** Half a fix that reads as a whole one is
worse than a fix that says which half it did — and SC-008's survey makes the
remaining gap visible either way.

**Stop and reconsider if this grows past two source files.** The plan expects
`StreamHealthChangedDomainEventHandler` and `AuditRetentionHostedService`,
nothing else. Anything more means the diagnosis is wrong rather than the
estimate.

---

## Three things most likely to go wrong

**The journey in the watcher's loop.** Right there, fewer lines, wrong twice —
merges the sweep *and* invents journeys for cameras that changed nothing. T006
and T015 exist for the second half, which is the one nobody anticipated.

**Retention skipped.** It publishes inline. Anyone pattern-matching on spec 026's
change will miss it, which is why it is a story rather than a bullet.

**Failure marking omitted in new code.** The defect was shipped in spec 026 and
caught in review three commits ago. Reintroducing it here would regress a fix
younger than the feature it fixed.
