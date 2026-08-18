# Tasks: A plant that exists can store its events

**Input**: Design documents from `/specs/019-fab-event-partitions/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/provisioning.md](./contracts/provisioning.md), [quickstart.md](./quickstart.md)

**Tests**: Included. Three things here are only ever caught by a test — that a
refused write enqueues **nothing** (FR-007), that an unreachable realm fails
instead of provisioning nothing (FR-011), and that removing a fab group does
not drop its partition (FR-006). The third would be discovered in production by
losing a plant's history.

**Depends on**: nothing outstanding. Spec 018 is merged; `FabIdentifier`,
`EventPartitionRolloverMigrator` and Identity's Keycloak admin client all exist.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on incomplete work)
- **[Story]**: US1–US3 from spec.md
- Exact file paths in every task

---

> **The one file to be careful in**
>
> `EventPartitionRolloverMigrator.cs` gains a step and keeps everything else.
> Two ways to get it wrong, both silent:
>
> **Order.** Provisioning must run **before** discovery. A fab partition with
> no monthly child beneath it stores exactly as little as no partition at all,
> so provisioning after the rollover leaves a new fab broken until the *next*
> run — the same bug with a longer fuse.
>
> **The S2077 comment.** It currently argues the interpolated table names are
> safe because they come from `pg_class`. After this feature they come from a
> Keycloak group, and that argument is false. It must be rewritten, not left:
> the next reader will trust it (research §R3).

---

## Phase 1: Setup — reproduce the loss

- [X] T001 Capture the current behaviour before changing anything, per [quickstart.md](./quickstart.md) step 0: add a `/fabs/berlin` group and an operator in it, `POST /events/manual` as them, then `SELECT count(*) FROM events WHERE fab_id = 'berlin'` and read the persistence-loop log line. **Record all four observations on the PR** — the 202, the empty listing, the zero count, and the log line that does not say why. Every later task asserts this is fixed, and none of them proves anything if the loss was not real first.

**Checkpoint**: The loss is documented rather than asserted.

---

## Phase 2: Foundational (blocking) — the ports, the realm read, the credential

- [X] T002 [P] Add `IProvisionedFabSource` to `src/EventIngestion/Application/Ingress/`, returning `IReadOnlyList<FabIdentifier>` per [contracts/provisioning.md](./contracts/provisioning.md). Port only — EventIngestion declares it and never implements it.
- [X] T003 [P] Add `IFabStorageReadiness` to `src/EventIngestion/Application/Ingress/`, per the same contract.
- [X] T004 Add a group-listing read to `src/Identity/Infrastructure/KeycloakAdmin/IKeycloakAdminClient.cs` and `HttpKeycloakAdminClient.cs` — sub-groups of a given path (`/fabs`). Identity's own surface growing by one read; **no other context may call it**.
- [X] T005 [P] Add a Keycloak client for the migration job to `src/AppHost/Realms/smart-sentinel-eye-realm.json` holding **`query-groups` and nothing else** (research §R2). Not `identity-admin`, which also holds `manage-users` and `manage-clients`.
- [X] T006 Wire it in `src/AppHost/AppHost.cs`: pass the new client's credentials and the Keycloak URL to the `migrations` resource, and add **`.WaitFor(keycloak)`** — it currently waits only for the nine databases (FR-012).
- [X] T007 [P] Extend `tests/Architecture.Tests/BoundaryTests.cs` to assert EventIngestion still references no other context **and** that the Keycloak-backed implementation lives in MigrationRunner. `AllowedCrossContext` must stay empty; if this feature ever needs an entry there, the design is wrong.

**Checkpoint**: The seams exist and the realm can be read. Nothing is provisioned and nothing is refused.

---

## Phase 3: User Story 1 — A new plant can store events from the moment it exists (P1) 🎯 MVP

**Goal**: adding a fab group is sufficient. **Independent test**: create a fab that has never existed, run the migration job, and confirm it can store and return an event.

- [X] T008 [US1] Add `src/MigrationRunner/KeycloakProvisionedFabSource.cs` implementing `IProvisionedFabSource` over Identity's admin client. **Parse each group name through `FabIdentifier.From` and skip what fails** (FR-005) — one unusable name must not stop a new fab getting its storage. **Throw** when the realm is unreachable or yields nothing usable; never return empty (FR-011).
- [X] T009 [US1] Add `src/EventIngestion/Infrastructure/Persistence/FabPartitionProvisioner.cs` issuing `CREATE TABLE IF NOT EXISTS events_<fab> PARTITION OF events FOR VALUES IN ('<fab>') PARTITION BY RANGE (ingested_at)`. The name shape is a **contract with the rollover's discovery**, not a convention ([data-model.md](./data-model.md)).
- [X] T010 [US1] Call the provisioner from `src/EventIngestion/Infrastructure/Persistence/EventPartitionRolloverMigrator.cs` **before** `DiscoverFabPartitionsAsync`, so a new fab gets its months in the same pass (FR-004), and **rewrite the S2077 comment** to argue validation rather than provenance (research §R3).
- [X] T011 [US1] Register the adapter in `src/MigrationRunner/Program.cs` alongside the existing per-context registrations.
- [X] T012 [P] [US1] Add unit tests under `tests/EventIngestion.Application.Tests/` against a fake `IProvisionedFabSource`: an unusable name is skipped and the rest still provisioned; an empty result throws rather than provisioning nothing; a repeat run issues the same idempotent statements.
- [X] T013 [US1] Add an integration case under `tests/Integration.Tests/EventIngestion/` driving [quickstart.md](./quickstart.md) step 1: a fab group with no partition gains one **and its current and next month**, a second run changes nothing, and an event for that fab then lands and reads back.

**Checkpoint**: SC-001 and SC-002 observed. Adding a plant is one action.

---

## Phase 4: User Story 2 — An event that cannot be stored is never reported as accepted (P1)

**Goal**: the silence ends. **Independent test**: present an event for a fab with no partition and confirm it is refused and nothing is enqueued.

- [X] T014 [US2] Add `src/EventIngestion/Infrastructure/Persistence/CatalogFabStorageReadiness.cs` implementing `IFabStorageReadiness` against the Postgres catalog, cached with a short TTL. **Re-read before answering false**, so a fab provisioned a minute ago is not refused by a stale cache. A database error **throws** — it must never be reported as "not provisioned", which would blame a provisioning gap that does not exist.
- [X] T015 [US2] Apply it to `IngestManual` in `src/EventIngestion/Api/EventsEndpoints.Writes.cs`, **after fab resolution and before `channel.TryWrite`** — the ordering is the requirement (FR-007). Refuse with **503 `EVENT_FAB_NOT_PROVISIONED`**.
- [X] T016 [US2] Apply the same to `IngestWebhook` in the same file (FR-009), after the integration's own fab is established by the spec 018 amendment — readiness is asked about a fab the caller is entitled to, never one they merely named.
- [X] T017 [US2] Declare **503** on both write endpoints in `src/EventIngestion/Api/EventsEndpoints.cs`.
- [X] T018 [US2] In `src/EventIngestion/Infrastructure/Ingress/PersistenceLoopHostedService.cs`, distinguish `23514` from an arbitrary dispatch fault and log it naming the fab and the missing partition (FR-008). **The envelope is still dropped** — that is #1546 and is deliberately not fixed here ([contracts/provisioning.md](./contracts/provisioning.md)).
- [X] T019 [P] [US2] Add unit tests under `tests/EventIngestion.Application.Tests/` for the readiness contract: cache miss triggers a re-read before refusing; a database failure surfaces as an exception rather than a false.
- [X] T020 [US2] Add integration cases under `tests/Integration.Tests/EventIngestion/` per [quickstart.md](./quickstart.md) step 3: **503** for a fab with no partition on both write paths, an untouched fab still **202**, and — the assertion that matters — **zero rows for that fab afterwards**. A 503 that had already enqueued is the same defect with a better error message.

**Checkpoint**: SC-003 and SC-004 observed. Nothing is accepted that cannot be stored.

---

## Phase 5: User Story 3 — Removing a plant never destroys its events (P1)

> Short phase, and the only one whose failure is unrecoverable.

**Goal**: provisioning is additive, forever. **Independent test**: remove a fab group, re-run, confirm the events are still there.

- [X] T021 [US3] Confirm by inspection **and by test** that no code path in `FabPartitionProvisioner` or `EventPartitionRolloverMigrator` issues `DROP`, `DETACH` or `TRUNCATE` against any partition (FR-006). Assert on the statements issued, not on the outcome — an outcome test passes for a fab that simply had nothing to lose.
- [X] T022 [US3] Add an integration case under `tests/Integration.Tests/EventIngestion/` per [quickstart.md](./quickstart.md) step 5: seed events for a fab, remove its group from the realm, re-run provisioning, and assert the partition and every event survive.

**Checkpoint**: SC-006 observed. Decommissioning a plant cannot delete its history.

---

## Phase 6: Polish

- [X] T023 Verify the unreachable-realm behaviour per [quickstart.md](./quickstart.md) step 6: stop Keycloak, run the migration job, and confirm it **fails visibly** and never logs "no fabs found" (FR-011). This is the one that reintroduces the silence if it regresses.
- [X] T024 Confirm ingest is untouched (SC-007): a well-formed broker delivery and a well-formed webhook call for a provisioned fab both behave exactly as before. The readiness check must not appear in an ingest measurement.
- [X] T025 Run `scripts/coverage-check.ps1 -Configuration Release` and confirm `EventIngestion.Domain` clears 90% and `Application` 80%. **Needs PowerShell 7**; under Windows PowerShell 5.1 the script fails to parse on its own UTF-8 characters — see spec 018's verification note for the BOM workaround.
- [X] T026 Walk [quickstart.md](./quickstart.md) end to end and record the observations on the PR, **against the T001 baseline**. **"Done" is the observations.** Step 3 is the one that cannot be faked; step 4 records a residue rather than a failure.
- [ ] T027 Close **#1547** with `Closes #1547` in the PR body, and comment on **#1546** naming precisely which half of it this feature did and did not address — the cause is now rare and legible, the general drop is untouched.

---

## Dependencies

```text
Phase 1 (T001)           baseline — do it FIRST or the evidence is weaker
      ↓
Phase 2 (T002–T007)      BLOCKING: ports, the realm read, the credential
      ↓
Phase 3 US1 (T008–T013)  🎯 MVP — provisioning follows the realm
Phase 4 US2 (T014–T020)  the silence ends
Phase 5 US3 (T021–T022)  the data-loss guard
      ↓
Phase 6 (T023–T027)      polish
```

**US2 does not depend on US1**, and that is worth exploiting: the readiness
check and the distinguishable log are valuable on their own, against today's
hand-provisioned partitions. Shipping US2 first would already convert silent
loss into a visible refusal.

**US3 depends on US1** — there is nothing to guard until provisioning derives
from a list that a fab can leave.

## Parallel opportunities

- **Phase 2**: T002, T003, T005 and T007 are four different files; only T004 and T006 are sequential with anything.
- **Phase 3**: T012 alongside T008–T011.
- **Phase 4**: T019 alongside T014–T018.
- **Across phases**: US1 and US2 can proceed concurrently once Phase 2 lands.

## Implementation strategy

**MVP is Phases 1–3** — adding a fab group becomes sufficient, which is the
issue's headline. But if only one phase can ship, **consider Phase 4 instead**:
US1 removes today's cause, US2 removes the property that let the cause survive
from spec 006 to spec 018 unnoticed, and the second is what protects against
the causes nobody has thought of yet.

**Do T001 before anything else.** Every assertion in this feature is that
something is now stored or now refused, and neither means much unless the loss
was observed first. It is also the last chance to see it: after Phase 3 the
berlin case cannot be reproduced without dismantling the fix.

**The diff is medium and lopsided.** Two ports, one adapter, one provisioner,
one call site reordered, one check on two endpoints, one log line made
specific. The largest single piece of work is the AppHost and realm wiring in
Phase 2, which is configuration rather than logic — and the riskiest is T010,
which is four lines in the file the callout above is about.
