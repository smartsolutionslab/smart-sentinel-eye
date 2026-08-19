---

description: "Task list for 021-transactional-outbox"
---

# Tasks: An integration event is never lost after its write commits

**Input**: Design documents from `/specs/021-transactional-outbox/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/event-bus.md](./contracts/event-bus.md)

**Tests**: included. The spec asks for a test that commits, makes the publish
fail, and asserts the message is eventually delivered — and says why: the test
that would pass today asserts only that the row exists.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on incomplete work)
- **[Story]**: the user story it serves

---

## Read this before starting

**Every happy-path test in this repository passes identically before and after
this feature.** Nothing observable changes when things work. If a task's
verification does not break something on purpose, it is not verifying anything.

**T004 inverts the order of two lines and is the most dangerous change in the
feature.** Handlers that ran after a successful commit will run before it, which
means a handler that throws now fails the write rather than leaving the row
behind. Every repository task repeats this so it is checked rather than skimmed.

---

## Phase 1: Setup

- [X] T001 Record the loss per [quickstart.md](./quickstart.md) step 0, against
      the current build, in a temporary integration case under
      `tests/Integration.Tests/EventIngestion/`. Stop RabbitMQ, write an event,
      and capture three numbers: the event row exists, the outbox table is
      **empty**, and no consumer acted. **Assert nothing** — this reports, and
      it is deleted at T027 once the observations are on the PR. Do the same for
      one non-ingest context so it is on record that this was never an ingest
      problem.

---

## Phase 2: Foundational

**Blocking. Every story depends on the seam.**

- [X] T002 Add the outbox-backed publisher in
      `src/ServiceDefaults/OutboxEventBus.cs`, replacing `WolverineEventBus`'s
      immediate `IMessageBus.PublishAsync` with a publish into the
      `IDbContextOutbox` bound to the calling context's `DbContext`.
      `IEventBus` (Shared.CQRS) does **not** change — the Application layer
      stays Wolverine-free (ADR-0057) and no domain-event handler is touched.
- [X] T003 Register the outbox-backed `IEventBus` per context, one line in each
      of the nine `<Context>InfrastructureModule.cs` files. `IDbContextOutbox<T>`
      is generic in the `DbContext`, so each context binds its own. **[P]** with
      T002 only after T002's shape is settled; before that it has nothing to
      bind to.

**Checkpoint**: the seam publishes into an outbox nobody flushes yet, so nothing
is delivered. That is expected and is why T004 immediately follows.

---

## Phase 3: US1 — a stored event always reaches the contexts that act on it (P1) 🎯 MVP

**Goal**: close the window on the path where the defect was found, and prove it
with the test the issue named.

- [X] T004 [US1] Invert the order in
      `src/EventIngestion/Infrastructure/Persistence/EventRepository.cs`:
      dispatch **before** the commit, and commit via
      `SaveChangesAndFlushMessagesAsync`. Keep spec 020's failure collection —
      every event is offered to the dispatcher and failures are raised together,
      so one bad handler cannot strand the other 199.
      **⚠ Handlers now run pre-commit. A handler that throws fails the write.**
- [X] T005 [US1] Unit case in
      `tests/EventIngestion.Infrastructure.Tests/` asserting the dispatch happens
      before the save, not after — the ordering is the guarantee, and a test that
      only checks "both happened" passes against the defect.
- [X] T006 [P] [US1] Unit case asserting a **failed commit publishes nothing**.
      This is the failure a naive fix introduces: capturing the message early is
      only safe if it is discarded when the write is not committed. A false
      announcement is worse than a lost one.
- [X] T007 [US1] Integration case under `tests/Integration.Tests/EventIngestion/`
      per [quickstart.md](./quickstart.md) step 1: broker down, write an event,
      assert **a pending outbox row exists**; broker back, assert the row drains
      and the consumers act. The pending row is the whole feature — before it,
      there was nothing to point at.
- [X] T008 [US1] Integration case per [quickstart.md](./quickstart.md) step 2:
      force a write to fail after its domain event is raised, assert **zero**
      outbox rows (SC-003).

**Checkpoint**: SC-001 and SC-003 observable on the ingest path. This is a
shippable increment on its own.

---

## Phase 4: US2 — the gap is closed everywhere, not only where it was found (P2)

**Goal**: the other eight write paths, and a guard so a ninth added later cannot
quietly opt out.

- [X] T009 [P] [US2] `src/Automation/Infrastructure/Persistence/RuleRepository.cs` — dispatch before commit, commit via the outbox. **⚠ pre-commit handlers.**
- [X] T010 [P] [US2] `src/CameraCatalog/Infrastructure/Persistence/CameraRepository.cs` — same. **⚠ pre-commit handlers.**
- [X] T011 [P] [US2] `src/EventIngestion/Infrastructure/Persistence/WebhookIntegrationRepository.cs` — same. **⚠ pre-commit handlers.**
- [X] T012 [P] [US2] `src/Identity/Infrastructure/Persistence/RegisteredClientRepository.cs` — same. **⚠ pre-commit handlers.**
- [X] T013 [P] [US2] `src/LayoutComposition/Infrastructure/Persistence/LayoutRepository.cs` — same. **⚠ pre-commit handlers.**
- [X] T014 [P] [US2] `src/OverlayDesigner/Infrastructure/Persistence/OverlayRepository.cs` — same. **⚠ pre-commit handlers.**
- [X] T015 [P] [US2] `src/StreamDistribution/Infrastructure/Persistence/StreamRepository.cs` — same. **⚠ pre-commit handlers.**
- [X] T016 [US2] `src/SystemVariables/Infrastructure/Persistence/VariableRepository.cs` — same, and **not [P]**. Its
      `VariableValueChangedDomainEventHandler` is the only handler of the twelve
      that reads as well as publishes (research.md R2), so it is the only one
      whose correctness depends on *when* it runs.
- [X] T017 [US2] Test that `VariableValueChangedDomainEventHandler` still
      resolves the right snapshot when it runs pre-commit — it reads through the
      same `DbContext`, so the change tracker should surface the pending write.
      The one handler where "it should be fine" is not good enough.
- [X] T018 [US2] NetArchTest rule in `tests/Architecture.Tests/`: no type under a
      `Persistence` namespace calls `DbContext.SaveChangesAsync` directly
      (FR-007). A repository added later either goes through the outbox or fails
      the build. Removing the unenrolled path is not available — `IMessageBus`
      is legitimately used by Wolverine's own handlers.
- [X] T019 [US2] Integration case per [quickstart.md](./quickstart.md) step 4 in
      a **non-ingest** context: broker down, register a camera, pending row
      appears in `wolverine_camera_catalog`, drains on recovery (SC-004).
      Testing one path proves the seam, not the coverage.

**Checkpoint**: SC-004 observable. Nine paths covered and the tenth cannot be
added silently.

---

## Phase 5: US3 — the guarantee is written down accurately (P3)

- [X] T020 [US3] Amend `docs/adr/0088-wolverine-defaults.md`: state which
      publishes the outbox covers and which it does not, and how a new write
      path joins. Its consequence line — "transactional outbox guarantees no
      message loss on crash mid-handler" — is true and reads as though it covers
      everything, which is why nobody looked for a year (FR-014, SC-007).
- [X] T021 [P] [US3] Record the pre-commit-handler constraint where a reviewer of
      a *new* domain-event handler will meet it: a handler on this path
      publishes and does nothing else. It cannot be checked mechanically, so it
      has to be written where it will be read.

---

## Phase 6: Visibility

- [X] T022 Expose the pending count and the age of the oldest pending message
      per context, where the contexts already expose health (FR-008, SC-006).
      **An outbox quietly growing looks exactly like an empty one** until the
      disk fills.
- [X] T023 [P] Report repeated delivery failure rather than only retrying it
      (FR-009), and confirm a message that can never be delivered is
      dead-lettered durably and countably (FR-010) rather than retried for ever.

---

## Phase 7: Polish

- [X] T024 Integration case per [quickstart.md](./quickstart.md) step 3: kill the
      service between commit and flush, restart, assert every committed write's
      announcement arrives (SC-002). Note spec 020's finding — the Aspire restart
      command fails on the CI runner, so this likely carries
      `Category=Disruptive` and the same honest note about what that costs.
- [X] T025 Run spec 020's `IngestThroughputMeasurementTests` before and after,
      identical harness, and record both figures (FR-011, FR-012, SC-005). The
      expectation is neutral-to-better — the change removes a synchronous broker
      hop and adds rows to a transaction already open — but **the expectation is
      not the deliverable, the two numbers are**. Watch the row volume: 200
      events per batch becomes 200 event rows plus 200 outbox rows per commit.
- [X] T026 Run `scripts/coverage-check.ps1 -Configuration Release` and confirm
      the gates. **Needs PowerShell 7**; under 5.1 the script fails to parse on
      its own UTF-8 characters — see spec 018's verification note for the BOM
      workaround, which is gitignored.
- [X] T027 Walk [quickstart.md](./quickstart.md) end to end and record the
      observations against the T001 baseline. **"Done" is the observations.**
      Delete T001's reporter once they are on the PR. Steps 2 and 5 are the ones
      that cannot be faked.
- [ ] T028 Close **#1605** with `Closes #1605` in the PR body, and state what
      this feature does **not** do: it does not deduplicate, it does not order,
      and it does not cover a publish made without an accompanying write.

---

## Dependencies

```
Phase 1  (T001)        baseline — record the loss before changing anything
Phase 2  (T002–T003)   the seam — blocks everything
Phase 3  US1 (T004–T008)  🎯 MVP — the path the defect was found on
Phase 4  US2 (T009–T019)  the other eight, plus the guard
Phase 5  US3 (T020–T021)  the record
Phase 6  (T022–T023)   visibility — the feature is invisible without it
Phase 7  (T024–T028)   polish, and the two measurements that gate SC-005
```

- T002 blocks everything. T003 needs T002's shape.
- T004 blocks T005–T008.
- T009–T015 are mutually parallel — different files, identical edit.
- T016 is deliberately **not** parallel with them, and T017 is why.
- T019 needs at least one non-ingest repository (T010) done.
- T025 needs every repository change in, or it measures a half-migrated system.

## Notes

**Why US1 before US2 when US2 is the wider gap.** US1 is one repository and
proves the seam works end to end. Doing the other eight first would be eight
edits against an unproven mechanism.

**Why T016 is not parallel.** Eleven of the twelve domain-event handlers publish
and nothing else, so the ordering inversion is indifferent to them.
`VariableValueChangedDomainEventHandler` reads. It is safe as analysed and it is
the only one where the analysis could be wrong, so it gets its own commit and its
own test rather than being lost in a batch of eight identical diffs.

**Why T006 exists separately from T005.** They look like the same test and are
opposites. T005 proves the message is captured inside the transaction; T006
proves it is discarded when the transaction fails. A fix that passes T005 and
fails T006 has replaced a lost announcement with a false one — consumers acting
on a write that never happened — which is a worse defect than the one being
fixed.
