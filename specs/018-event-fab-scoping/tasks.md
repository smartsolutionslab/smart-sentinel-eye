# Tasks: Fab-scope event ingestion

**Input**: Design documents from `/specs/018-event-fab-scoping/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/events-api.md](./contracts/events-api.md)

**Tests**: Included. ADR-0052 mandates TDD for the domain, and three things here are only ever caught by a test: that a refused write ingests **nothing** (FR-007), that an unattributed dead letter reaches nobody (FR-011, invisible when it works), and that the two dead-letter failure modes are not conflated — which would hide the whole list while looking like correct scoping.

**Depends on**: nothing. `FabIdentifier` already exists in this context, and no other context is involved. The first of the six fab features with no cross-context dependency.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on incomplete work)
- **[Story]**: US1–US3 from spec.md
- Exact file paths in every task

---

> **The one file to be careful in**
>
> `EventsEndpoints.Writes.cs` holds **both** `POST /events/manual` and
> `POST /events/webhook/{name}`. They take the same `?fabId=` parameter. The
> webhook already checks it against the caller's own groups and **must not
> change** (FR-014); the manual write does not check it at all and is the
> whole of US2.
>
> That symmetry is presumably how this gap survived five features aimed at it.
> If webhook ingest breaks, the wrong one was edited.

---

## Phase 1: Setup — reproduce the leak

- [x] T001 Capture the current behaviour before changing anything, per [quickstart.md](./quickstart.md) step 0: as `op-dresden@dresden.test`, `GET /events?fabId=munich`, `GET /events/{a munich event}`, `POST /events/manual?fabId=munich`, and `GET /events/dead-letters`. **Record what comes back on the PR.** Every later task asserts a refusal, and a refusal is only evidence if the thing was permitted a moment earlier.

**Checkpoint**: The leak is documented rather than asserted.

---

## Phase 2: Foundational (blocking) — the resolvers

- [x] T002 Add `ResolveReadFabsAsync` and `ResolveWriteFabAsync` to `src/EventIngestion/Api/EventsEndpoints.cs`, binding `FabResolution` to this context's `FabIdentifier`. Mirror `CameraEndpoints`, **including its per-entry parse** of the caller's groups — one unusable group must not fail the whole request. Error code `EVENT_FAB_REQUIRED` for the ambiguity refusal.
- [x] T003 [P] Add fab-resolution unit tests under `tests/EventIngestion.Application.Tests/` for the ADR-0114 table as this context binds it: inferred, named-and-held, named-and-not-held, ambiguous, no-fab-at-all.

**Checkpoint**: The resolvers exist. Nothing uses them, and nothing is scoped.

---

## Phase 3: User Story 2 — An operator cannot inject events into another plant (P1) 🎯 MVP

> **US2 before US1 deliberately.** Reading another plant's data is a
> disclosure; writing into another plant is a **manipulation** — the injected
> event drives that plant's automation rules and changes what its operators
> see. It is the only path in the product by which one fab alters another's
> state, and it is the one to close first.

**Goal**: `POST /events/manual` files events only against fabs the caller holds. **Independent test**: as a Dresden-only operator, submit naming Munich, then check Munich's stream is unchanged.

- [x] T004 [US2] Apply `ResolveWriteFabAsync` to `IngestManual` in `src/EventIngestion/Api/EventsEndpoints.Writes.cs`, replacing the unchecked `[FromQuery] string fabId`. **Resolve before touching the ingest channel** — a refusal that had already enqueued would place a fabricated event in another plant's stream while reporting that it had been stopped (FR-007).
- [x] T005 [US2] Make `fabId` optional on that endpoint so a single-fab operator can omit it, per [contracts/events-api.md](./contracts/events-api.md). This makes a **required** parameter optional; no correctly-behaving client changes.
- [x] T006 [US2] Declare **400** `EVENT_FAB_REQUIRED` and **403** on `POST /events/manual` in `src/EventIngestion/Api/EventsEndpoints.cs`. Both became reachable with this change. Spec 013 shipped this wrong on one endpoint and it took a review to catch.
- [x] T007 [US2] **Do not touch `IngestWebhook`** in the same file. Confirm by diff that the webhook's `"/fabs/" + fabId` check is byte-identical afterwards (FR-014).
- [x] T008 [P] [US2] Add integration cases to `tests/Integration.Tests/EventIngestion/` driving the full write table with real tokens, and — for the 403 case — **assert the event is absent from the target fab's listing afterwards**, not merely that the status was 403. Covers SC-003.

**Checkpoint**: SC-003 observed. One fab can no longer alter another's state.

---

## Phase 4: User Story 1 — An operator reads only their own plant's events (P1)

**Goal**: both event reads are scoped to the caller's fabs. **Independent test**: as a Dresden-only operator, list naming Munich and request a Munich event by identifier.

- [ ] T009 [US1] Widen `Fab` to `Fabs` on `ListEventsQuery` and `GetEventQuery` in `src/EventIngestion/Application/Queries/`.
- [ ] T010 [US1] Change the two predicates in `src/EventIngestion/Application/Queries/Handlers/` from `== query.Fab` to `fabs.Contains(...)`. **Do not touch the sort key**: the cursor pages on `(ingestedAt, eventId)`, which is fab-independent, and adding fab to it would invalidate every issued cursor.
- [ ] T011 [US1] Apply `ResolveReadFabsAsync` to `ListEvents` and `GetEvent` in `src/EventIngestion/Api/EventsEndpoints.Reads.cs`, making `fabId` optional and **checked**.
- [ ] T012 [US1] Return **404** for an event outside the caller's fabs, byte-identical to one that never existed (FR-004). The fab belongs in the lookup, not in a check afterwards — so both cases leave by the same path.
- [ ] T013 [US1] Declare **403** on both reads in `src/EventIngestion/Api/EventsEndpoints.cs`.
- [ ] T014 [P] [US1] Add handler tests under `tests/EventIngestion.Application.Tests/Queries/` for the scoping and the not-found path, including a multi-fab caller seeing both plants.
- [ ] T015 [US1] Add `tests/Integration.Tests/EventIngestion/EventFabScopingIntegrationTests.cs` with `op-dresden@dresden.test` and `op-multi@smart-sentinel-eye.test`: listing scoped, naming an unheld fab 403, another fab's event 404 **compared field by field** with `traceId` and the requested identifier normalised out. Covers SC-001 and SC-002.

**Checkpoint**: SC-001 and SC-002 observed. The event data is closed.

---

## Phase 5: User Story 3 — Failed events are visible only to the plant they came from (P1)

> Every row here carries `rawPayload` — the production data verbatim and
> unvalidated. This is the phase where the two failure modes must stay apart.

**Goal**: the rejected-delivery list is scoped, and an unattributable delivery reaches nobody. **Independent test**: reject one delivery per fab plus one on a malformed topic, then read as each operator.

- [ ] T016 [US3] Add nullable `Fab` to `src/EventIngestion/Domain/DeadLetter/DeadLetter.cs` and to `DeadLetter.Capture`. **Permanently nullable** — a malformed address has no plant, and there will be no follow-up NOT NULL migration ([data-model.md](./data-model.md)).
- [ ] T017 [US3] Pass the parsed fab to `Capture` from `src/EventIngestion/Infrastructure/Ingress/MqttSubscriberHostedService.cs`. **The two failure modes are different**: a malformed *payload* under a well-formed topic has a fab; a malformed *topic* does not. Attempt `FabIdentifier.From` on the second segment and fall back to null — a four-segment topic does not guarantee a legal fab name.
- [ ] T018 [US3] Map the column in `src/EventIngestion/Infrastructure/Persistence/Configurations/DeadLetterConfiguration.cs`: `fab` **nullable**, max length 32, value-converted, plus a plain index `ix_dead_letters_fab`.
- [ ] T019 [US3] Generate the migration under `src/EventIngestion/Infrastructure/Persistence/Migrations/` and hand-correct it to the backfill in [data-model.md](./data-model.md) — `split_part(topic, '/', 2)` guarded by the four-segment shape **and the `FabIdentifier` grammar regex**, so it cannot write a value the domain rejects on read (the defect spec 015 hit). **No `RAISE WARNING`**: nothing is guessed here, unlike specs 015 and 017.
- [ ] T020 [US3] Add `Fabs` to `ListDeadLettersQuery` in `src/EventIngestion/Application/Queries/` and filter on it in the handler. `NULL` satisfies no `IN`, so FR-011 falls out of the query rather than needing a special case.
- [ ] T021 [US3] Apply `ResolveReadFabsAsync` to `ListDeadLetters` in `src/EventIngestion/Api/EventsEndpoints.Reads.cs`, adding an optional `?fabId=`, and declare **403**.
- [ ] T022 [US3] Record a rejected delivery with no establishable fab in `src/EventIngestion/Application/Log.cs` and emit it from the capture path (FR-012). The count and the topic, **never the payload** — invisible is acceptable, invisible and unnoticed is not.
- [ ] T023 [P] [US3] Add domain tests under `tests/EventIngestion.Domain.Tests/` that `Capture` records the fab when given one and leaves it null otherwise.
- [ ] T024 [US3] Add integration cases publishing three deliveries — munich bad payload, dresden bad payload, malformed topic — and assert: dresden sees only its own, multi-fab sees both attributed ones, and **nobody sees the third**. Assert the third's existence directly against the database, or a capture path that nulls every fab would pass by hiding everything. Covers SC-004.

**Checkpoint**: SC-004 observed. The raw payloads are closed.

---

## Phase 6: Polish

- [ ] T025 Verify the backfill on populated data: the count of `fab IS NULL` rows must equal the count of stored topics that do not have the `fab/a/b/c` shape. If every row is null the guard is too strict; if none is, it is too loose. Covers SC-005.
- [ ] T026 Confirm ingest is untouched (SC-006): a well-formed broker delivery and a well-formed webhook call both succeed exactly as before. These are the throughput paths and this feature must not have touched them.
- [ ] T027 Run `scripts/coverage-check.ps1 -Configuration Release` and confirm `EventIngestion.Domain` clears 90% and `Application` 80%.
- [ ] T028 Walk [quickstart.md](./quickstart.md) end to end and record the observations on the PR, **against the T001 baseline**. **"Done" is the observations.** Step 3 is the one that cannot be faked.
- [ ] T029 File the follow-up issue for the webhook integration registry (FR-016), carrying the question as stated: fab-scoped, or a shared template whose entitlement is proven per delivery?
- [ ] T030 Comment on #1155 that every applicable context now applies the guard, and **close it** — this is the last one. **Write `Closes #N`** with the keyword before each number; it fires only on merge to the default branch, and GitHub processes it asynchronously, so verify a minute later rather than immediately.

---

## Dependencies

```text
Phase 1 (T001)           baseline — do it FIRST or the evidence is weaker
      ↓
Phase 2 (T002–T003)      BLOCKING: the resolvers all three stories use
      ↓
Phase 3 US2 (T004–T008)  🎯 MVP — the manipulation
Phase 4 US1 (T009–T015)  the disclosure
Phase 5 US3 (T016–T024)  the raw payloads
      ↓
Phase 6 (T025–T030)      polish
```

**US1, US2 and US3 are mutually independent** once Phase 2 lands — a rarity in
this programme, and a consequence of the context already modelling its fab.
They touch different files: US2 `Writes.cs`, US1 `Reads.cs` + the two event
handlers, US3 the `DeadLetter` aggregate + its own query. Only
`EventsEndpoints.cs` (status declarations) is shared, and each story adds its
own lines.

## Parallel opportunities

- **Phase 2**: T003 alongside T002.
- **Phase 3**: T008 alongside T004–T007.
- **Phase 4**: T014 alongside T009–T013.
- **Phase 5**: T023 alongside T016–T022.
- **Across phases**: all three stories can proceed concurrently after Phase 2.

## Implementation strategy

**MVP is Phases 1–3.** The write leak is closed, and with it the only path by
which one fab can alter another's state.

**Do T001 before anything else.** Every assertion in this feature is that
something is now refused, and a refusal proves nothing unless it was permitted
first. It is also the only chance to record what the leak actually returned.

**The diff will be small.** Two predicates, three endpoint resolutions, one
nullable column. That is the expected shape — the context already models its
fab and already filters on it, and the missing piece was always a few lines at
the boundary. A small diff here is not a sign the feature is underscoped.
