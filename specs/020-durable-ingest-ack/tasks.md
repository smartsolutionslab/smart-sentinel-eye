# Tasks: An event is never accepted until it is stored

**Input**: Design documents from `/specs/020-durable-ingest-ack/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/ingest.md](./contracts/ingest.md), [quickstart.md](./quickstart.md)

**Tests**: Included, and three of them are the feature rather than evidence for
it — that an outage loses nothing (FR-004), that a kill loses nothing (SC-002),
and that one unstorable delivery does not block the rest (FR-009). None can be
established by reading the code, and the third is how this change could
reintroduce the defect spec 018 fixed.

**Depends on**: nothing outstanding. Specs 018 and 019 are merged.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on incomplete work)
- **[Story]**: US1 (outage), US1b (the sender is told), US2 (kill), US3 (poison escape)
- Exact file paths in every task

---

> **The number that makes this look finished when it is not**
>
> `max_inflight_messages` is unset in `mosquitto.conf`, so it is **20**. Every
> functional step in [quickstart.md](./quickstart.md) passes at 20; only the
> throughput step fails, and only under sustained load. Set it (T004) before
> measuring anything, or T019 will report a number that has nothing to do with
> the code.
>
> **The other way to get this wrong is silent in the opposite direction**:
> setting it to `0` (unlimited) removes the ceiling *and* the backpressure the
> design relies on, moving the unbounded buffer into the broker where nobody
> looks at it.

---

## Phase 1: Setup — reproduce both losses

- [ ] T001 Capture the current behaviour, per [quickstart.md](./quickstart.md) step 0, in **both** forms: (a) pause Postgres, publish ~20 broker events, unpause — record the stored count and the `dispatch faulted` lines; (b) publish a few thousand and `docker kill` event-ingestion mid-drain — record how many arrived versus were sent, **and that nothing at all was logged for them**. **Record both on the PR.** Case (b) is the half the issue does not describe and the half with no evidence of its own.

**Checkpoint**: Both losses documented, with the asymmetry — one complains, one is silent.

---

## Phase 2: Foundational (blocking) — the seam and the ceiling

- [ ] T002 Add a completion signal to the envelope carried by `src/EventIngestion/Application/Ingress/IIngestChannel.cs` — the means to report *stored* or *permanently unstorable* per envelope, per [contracts/ingest.md](./contracts/ingest.md). One signal on the existing channel, **not** a second channel type, so a durable buffer could be substituted later without either ingress noticing.
- [ ] T003 Carry it through `src/EventIngestion/Application/Ingress/BoundedIngestChannel.cs`. Capacity, `FullMode.Wait` and single-reader FIFO are unchanged — only the item is richer.
- [ ] T004 Set `max_inflight_messages` to a stated finite value in `src/AppHost/mosquitto/mosquitto.conf`, with a comment saying why the default of 20 is not survivable once acknowledgement waits for storage (research §R1). **Not `0`.**
- [ ] T005 [P] Add a unit test under `tests/EventIngestion.Infrastructure.Tests/` that the channel preserves per-source FIFO with the completion signal attached — the ordering guarantee (FR-011) is the easiest thing to lose while changing the item type.

**Checkpoint**: The seam exists and the broker can hold a real window. Nothing has changed about when anything is acknowledged.

---

## Phase 3: User Story 1 — A plant's events survive an outage (P1) 🎯 MVP

**Goal**: acknowledge after the write, in batches. **Independent test**: pause storage mid-stream, restore it, compare sent against stored.

- [ ] T006 [US1] In `src/EventIngestion/Infrastructure/Ingress/MqttSubscriberHostedService.cs`, set `AutoAcknowledge = false` and pass the delivery's acknowledgement through the channel as the envelope's completion signal. **Do not acknowledge here** — that line is the defect.
- [ ] T007 [US1] In `src/EventIngestion/Infrastructure/Ingress/PersistenceLoopHostedService.cs`, drain into a **batch**, commit it in one transaction, then acknowledge exactly the envelopes in that batch. A per-message acknowledgement fails FR-010 and a per-batch acknowledgement of the wrong set loses events silently.
- [ ] T008 [US1] Retry a failed batch with bounded backoff instead of dropping it, keeping the envelopes unacknowledged throughout, so an interruption is survived rather than logged (FR-004, FR-005). The bound is a stated number, not a magic one.
- [ ] T009 [US1] Log the interruption, the recovery, **and the count of events affected** in `src/EventIngestion/Infrastructure/Log.cs` (FR-006). A recovery nobody can see afterwards is indistinguishable from a loss.
- [ ] T010 [P] [US1] Unit tests under `tests/EventIngestion.Infrastructure.Tests/`: a batch that fails is retried and not acknowledged; a batch that succeeds acknowledges exactly its own envelopes; backoff is bounded.
- [ ] T011 [US1] Integration case under `tests/Integration.Tests/EventIngestion/` driving [quickstart.md](./quickstart.md) step 1: pause Postgres mid-stream, restore, and assert `count(*) == count(DISTINCT event_id) ==` what was published. **Both equalities** — "all arrived" and "none twice" are different claims now that redelivery is routine.

**Checkpoint**: SC-001 observed. An outage costs nothing but time.

---

## Phase 4: User Story 1b — The sender is told the truth (P1)

**Goal**: direct submissions store before answering. **Independent test**: submit while storage is unavailable and see what the caller is told.

- [ ] T012 [US1b] In `src/EventIngestion/Api/EventsEndpoints.Writes.cs`, persist synchronously in `IngestManual` and answer **201 Created** with `Location: /events/{id}`, replacing the enqueue-and-202. Storage unavailable is **503**, and nothing stored.
- [ ] T013 [US1b] The same for `IngestWebhook` in that file (FR-002) — a partner's retry logic can act on 5xx and cannot act on a 202 followed by silence.
- [ ] T014 [US1b] Add the bounded write limiter that keeps **429** meaningful, per [contracts/ingest.md](./contracts/ingest.md), sized to database write capacity rather than to the old 5 000-slot channel. Without it the endpoint silently becomes "queue and time out" (FR-013).
- [ ] T015 [US1b] Declare **201**, **429** and **503** on both write endpoints in `src/EventIngestion/Api/EventsEndpoints.cs`, and remove the 202 that no longer occurs.
- [ ] T016 [P] [US1b] Integration cases under `tests/Integration.Tests/EventIngestion/` per [quickstart.md](./quickstart.md) step 3: 201 with a `Location` that **actually resolves** — `GET` it, because a 201 pointing at a 404 is the same lie in a better costume — and 5xx with nothing stored while storage is down.

**Checkpoint**: SC-003 observed. No response claims more than the system did.

---

## Phase 5: User Story 2 — A kill loses nothing (P1)

> One task, and it proves the design rather than adding to it.

**Goal**: unacknowledged deliveries come back. **Independent test**: kill mid-burst, restart, count.

- [ ] T017 [US2] Integration case under `tests/Integration.Tests/EventIngestion/` per [quickstart.md](./quickstart.md) step 2: publish a burst, kill the service mid-drain, restart, and assert every published event is stored exactly once. **Nothing is implemented for this story** — it passes because an envelope in the channel is no longer something anyone was promised. If it fails, the acknowledgement is still happening too early somewhere.

**Checkpoint**: SC-002 observed. The in-memory buffer is no longer a hole.

---

## Phase 6: User Story 3 — One bad delivery never blocks the rest (P1)

> The phase that could reintroduce spec 018's defect. Keeping an event until it
> is stored is exactly what turns one unstorable event into an endless retry.

**Goal**: a bounded escape. **Independent test**: one permanently unstorable delivery plus a hundred good ones.

- [ ] T018 [US3] In `src/EventIngestion/Infrastructure/Ingress/PersistenceLoopHostedService.cs`, count attempts per event identifier and, past a stated bound, write the delivery to `dead_letters` with its failure reason and **acknowledge it** so the broker stops (FR-007, FR-008). The counter is in memory and resets on restart — deliberately, per [data-model.md](./data-model.md).
- [ ] T019 [US3] Ensure a failing envelope never blocks the batch behind it (FR-009): isolate the failure to its own envelope rather than failing the whole batch forever. This is the task that decides whether spec 018's defect comes back.
- [ ] T020 [P] [US3] Unit tests under `tests/EventIngestion.Infrastructure.Tests/`: the bound is respected; the dead letter carries the reason; a dead-letter write that itself fails does not lose the loop (the outage case research §R4 admits it cannot cover).
- [ ] T021 [US3] Integration case per [quickstart.md](./quickstart.md) step 4: drop a fab's partition, publish one delivery for it and a hundred for a healthy fab, and assert the hundred all land at the normal rate while the one ends up in `dead_letters` and stops being redelivered.

**Checkpoint**: SC-004 observed. The guard holds.

---

## Phase 7: Polish

- [ ] T022 Measure sustained throughput per [quickstart.md](./quickstart.md) step 5 — 5 000 events/s for 30 s — and compare against the same measurement taken **before** this feature. Record both numbers (SC-005). Confirm `max_inflight_messages` is the value T004 set, or the figure means nothing.
- [ ] T023 Measure arrival-to-visible latency before and after and cite it against the ≤ 200 ms leg of the end-to-end budget (SC-006, constitution §IV). The requirement is the measurement, not the argument that batching is cheap.
- [ ] T024 Confirm per-source ordering is preserved under sustained load (FR-011, SC-005) — it is guaranteed by the single-reader loop today, and batching is where that quietly stops being true.
- [ ] T025 Run `scripts/coverage-check.ps1 -Configuration Release` and confirm the EventIngestion gates. **Needs PowerShell 7**; under 5.1 the script fails to parse on its own UTF-8 characters — see spec 018's verification note for the BOM workaround.
- [ ] T026 Walk [quickstart.md](./quickstart.md) end to end and record the observations on the PR **against the T001 baseline**. **"Done" is the observations.** Steps 4 and 5 are the ones that cannot be faked.
- [ ] T027 Close **#1546** with `Closes #1546` in the PR body, and note in it what this feature does **not** do: the escape cannot record a failure during a total outage, because the dead-letter write fails for the same reason (research §R4).

---

## Dependencies

```text
Phase 1 (T001)            baseline — both halves, or the evidence is thin
      ↓
Phase 2 (T002–T005)       BLOCKING: the completion seam and the broker window
      ↓
Phase 3 US1  (T006–T011)  🎯 MVP — acknowledge after the write
Phase 4 US1b (T012–T016)  the sender is told the truth
Phase 6 US3  (T018–T021)  the guard on US1's mechanism
      ↓
Phase 5 US2  (T017)       verification only; needs US1 to mean anything
      ↓
Phase 7 (T022–T027)       polish, and the two measurements that gate the claim
```

**US1b is independent of US1** and could ship alone: persisting before answering
needs neither the batch nor the broker window. **US3 is not independent** — it
guards the mechanism US1 introduces, and shipping US1 without it reintroduces
the defect spec 018 fixed.

## Parallel opportunities

- **Phase 2**: T005 alongside T002–T004.
- **Phase 3**: T010 alongside T006–T009.
- **Phase 4**: the whole phase alongside Phase 3 — different files, different path.
- **Phase 6**: T020 alongside T018–T019.

## Implementation strategy

**MVP is Phases 1–3 plus Phase 6.** Not Phases 1–3 alone: US1 without US3 is a
system that survives outages by retrying forever, including on the one delivery
that will never succeed. The guard is not polish.

**Do T001 before anything else**, and do both halves of it. The kill case is the
one nobody has numbers for — it logs nothing today — and after this change it
cannot be reproduced without undoing the fix.

**Set T004 before measuring anything.** At the default of 20 the feature works
correctly and slowly, which is the most expensive kind of wrong to discover
late.

**The diff is medium and concentrated.** Two ingress paths, one loop, one config
line. No schema change, no new project, no new table — the same shape as spec
019, and for a related reason: the defect is in *when* the system speaks, not in
what it stores.
