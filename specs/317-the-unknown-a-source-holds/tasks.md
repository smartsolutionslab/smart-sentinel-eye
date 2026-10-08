# Tasks 317 — The unknown a source holds

**Spec:** `spec.md` · **Plan:** `plan.md` · **Issue:** #2325 (feature-level; on
Project #13 — no per-task issues, per CLAUDE.md's Phase 3 note)
**Engineer:** backend-engineer (one). **Reviewers:** backend-reviewer,
security-reviewer (promotion path + `DELETE` scope).
**Phase 4a colour: RED** (behaviour-changing). Characterisation net and the
sanctioned test edits: `plan.md` §9 — anything else that must change to pass is a
stop, not an adjustment.

Format: `[ID] [P?] [Story] description — files`. `[P]` = disjoint files from every
other `[P]` task in the same group (ADR-0109). `[RED]` = written first by
test-writer, observed failing, failure quoted in the PR.

---

## Phase A — Characterisation (green before anything changes)

- **T001 [Found]** Run the EventIngestion unit suites and the EventIngestion
  integration classes (`StrictSourceIngestIntegrationTests`,
  `EventSourceModeIntegrationTests`, `EventSourceModeAuthorizationIntegrationTests`,
  `DeadLetterReasonIntegrationTests`, `DeadLetterFabScopingIntegrationTests`,
  `EventTypeRegistryIntegrationTests`, `ManualIngestFabScopingIntegrationTests`)
  on the branch base; record pass counts in the PR body. This is the net `plan.md`
  §9 says must stay green unmodified apart from the named edits.

## Phase B — Foundational (blocks everything after it; sequential)

- **T002 [Found] [RED]** Domain tests: `DeadLetterReason` and `HoldState` (closed
  `From`, singletons, throw on unknown); `DeadLetter.Capture` sets `Held`, records
  reason and kind, and refuses `UnknownEventType` without fab or kind;
  `DeliveryTopic.ForEnvelope` composes `event/{fab}/{source}/{device}`.
  — `tests/EventIngestion.Domain.Tests/DeadLetter/*`
- **T003 [Found]** Implement T002: the two VOs, `DeadLetter` properties + capture
  shape (plan §2.1 — choose one signature or per-reason factories, record which),
  `DeliveryTopic.ForEnvelope`, `IDeadLetterRepository.PromoteHeldAsync` signature.
  Update existing `Capture` call sites to compile (`MqttSubscriberHostedService` →
  `ParseFailure`; `PersistenceLoopHostedService.RecordRejectionAsync` → `Refused` +
  kind + `ForEnvelope`) and existing test call sites (plan §9 table).
  — `src/EventIngestion/Domain/DeadLetter/*`, the two Ingress files, test call sites
- **T004 [Found] [RED]** Infrastructure/integration test: after migration, a
  pre-seeded `event/…` row reads `Refused`, a `fab/…` row reads `ParseFailure`,
  both `Held`, kind null; a seeded `source_modes` `discovery` row is gone and a
  `strict` row survives at its version. (Seed via the fixture's database
  connection before the migration runs, or assert on the migration's SQL against a
  scratch schema — engineer's choice; record which.)
  — new `tests/Integration.Tests/EventIngestion/DeadLetterHoldMigrationIntegrationTests.cs`
  + shard-filter entry
- **T005 [Found]** EF configuration + `AddDeadLetterHoldState` migration (plan §4,
  steps 1–5), Designer + snapshot generated. Verify with `git diff --stat` on
  `Migrations/`.
  — `Infrastructure/Persistence/Configurations/DeadLetterConfiguration.cs`,
  `Infrastructure/Persistence/Migrations/*`

**Gate after T005:** the solution builds in Release; T001's net is still green.
Tracks A, B and C below may then run in parallel.

---

## Phase C — US1 (P1): a declared discovery pair holds an unknown kind — Track A

- **T006 [P] [US1] [RED]** Unit tests in `EventTypeAdmissionTests`:
  undeclared pair admits an unregistered kind (existing, unchanged);
  **declared discovery holds** an unregistered kind (the inverted
  `A_discovery_source_admits_an_unregistered_kind`, renamed); declared discovery
  admits a registered kind; strict still refuses and does not hold; a retired kind
  is held under discovery; call counts: default case = 1 declared-pairs call, 0
  registry calls; one registry call per fab with any declared pair, not per
  envelope; empty batch = 0/0. Update the fakes to `DeclaredSourceModesAsync`.
  — `tests/EventIngestion.Application.Tests/Ingress/EventTypeAdmissionTests.cs`,
  `tests/*/Fakes/*EventTypeAdmissionSource.cs`
- **T007 [US1] [RED]** Handler unit tests: single handler — a held envelope writes
  one dead letter (`UnknownEventType`, kind, `ForEnvelope` topic, payload verbatim),
  stores no event, records no volume, returns `EventTypeHeld`; redelivery and
  future skew outrank the hold; strict refusal unchanged. Batch handler — a mixed
  batch stores the registered envelope, holds the unknown one in the same commit,
  and does **not** list the held one in `Refused`.
  — `tests/EventIngestion.Application.Tests/Commands/IngestEventQuarantineTests.cs` (new)
- **T008 [US1]** Implement T006–T007: port `DeclaredSourceModesAsync` +
  `EventTypeAdmissionSource`; `EventTypeVerdicts.Holds`; `EventTypeHeld` error;
  both handlers write holds (plan §6.1–6.3).
  — `Application/Ingress/*`, `Application/Commands/IngestEventErrors.cs`,
  `Application/Commands/Handlers/IngestEvent*CommandHandler.cs`,
  `Infrastructure/Persistence/EventTypeAdmissionSource.cs`
- **T009 [US1] [RED]** `PersistenceLoopHostedServiceTests`: a slow-path
  `EventTypeHeld` ends `Stored` and writes no second dead letter; a refusal still
  dead-letters with reason `Refused` and the envelope's kind.
  — `tests/EventIngestion.Infrastructure.Tests/PersistenceLoopHostedServiceTests.cs`
- **T010 [US1]** Implement T009 (`StoreOneAsync` branch) and the write endpoints'
  `EventTypeHeld` → `202` mapping + `.Produces(202)`; verify `ExecuteCreateAsync`'s
  handling of the failure branch (plan §5) and record the finding.
  — `Infrastructure/Ingress/PersistenceLoopHostedService.cs`,
  `Api/EventsEndpoints.Writes.cs`
- **T011 [US1] [RED]** Integration, Aspire fixture:
  `DiscoverySourceHoldsIntegrationTests` — HTTP manual: declared discovery + unknown
  kind → 202, absent from `/events`, one held row; registered kind → 201.
  MQTT fast path: mixed burst on a declared-discovery `inference` pair → one stored,
  one held, delivery acknowledged. **Undeclared default** (`dresden, manual`):
  unknown kind → 201, no dead letter. Invert the explicit-discovery case in
  `StrictSourceIngestIntegrationTests` (~line 156) to 202 + held. Every test
  undeclares what it declared in a `finally`.
  — new `tests/Integration.Tests/EventIngestion/DiscoverySourceHoldsIntegrationTests.cs`,
  `StrictSourceIngestIntegrationTests.cs`, shard-filter entry
  **Depends on T018** (the `UndeclareAsync` helper) — sequence T011 after Track C's
  T018, or write the `finally` against the helper's signature and let it go red
  for that reason only until T018 lands.

## Phase D — US2 (P1) + US3 (P2): listing and promotion — Track B

- **T012 [P] [US2] [RED]** `ListDeadLettersQueryHandlerTests`: filter by reason,
  by state, by both, neither (unchanged); DTO carries fab, reason, kind, state.
  — `tests/EventIngestion.Application.Tests/Queries/ListDeadLettersQueryHandlerTests.cs`,
  `DTOs/DtoSmokeTests.cs`
- **T013 [US2]** Implement T012 + the endpoint's `reason`/`state` parsing →
  `400 DEAD_LETTER_INVALID_FILTER`; summary text.
  — `Application/Queries/ListDeadLetters*`, `Application/DTOs/DeadLetterDto.cs`,
  `Api/EventsEndpoints.Reads.cs`
- **T014 [P] [US3] [RED]** `RegisterEventTypeCommandHandlerTests`: success calls
  `PromoteHeldAsync(fab, kind)` once after save; the already-registered path also
  calls it, then returns 409; a fake repository records the calls.
  Integration `HeldEventTypePromotionIntegrationTests`: three held rows in berlin +
  one in munich → register in berlin → berlin rows `Promoted`, munich `Held`, a
  `Refused` row with the same `(fab, kind)` stays `Held` (predicate test); next
  event of that kind → 201; a lingering `Held` row for an already-registered kind
  is promoted by a 409 registration; `GET …?reason=UnknownEventType&state=Held`
  excludes promoted rows; bad filter → 400; a caller with `sse.events.write` only
  → 403 on `POST /event-types` and rows unchanged (event-source-shaped client,
  spec 143 FR-010's gotcha); a `dresden`-only operator sees no other fab's rows.
  — `tests/EventIngestion.Application.Tests/Commands/RegisterEventTypeCommandHandlerTests.cs`,
  new `tests/Integration.Tests/EventIngestion/HeldEventTypePromotionIntegrationTests.cs`,
  shard-filter entry
- **T015 [US3]** Implement T014: `DeadLetterRepository.PromoteHeldAsync`
  (`ExecuteUpdateAsync`, plan §7 incl. fallback), handler both branches, log
  message, `POST /event-types` summary sentence.
  — `Infrastructure/Persistence/DeadLetterRepository.cs`,
  `Application/Commands/Handlers/RegisterEventTypeCommandHandler.cs`,
  `Infrastructure/Log.cs` or the handler's log partial, `Api/EventTypesEndpoints.cs`

## Phase E — US4 (P2): undeclare — Track C

- **T016 [P] [US4] [RED]** Domain: `SourceMode.Undeclare` raises
  `SourceModeUndeclaredDomainEvent` with fab, source, prior mode, actor, time.
  Application: `UndeclareSourceModeCommandHandler` — 404 undeclared, 409 stale,
  success removes and saves.
  — `tests/EventIngestion.Domain.Tests/SourceMode/SourceModeTests.cs`,
  `tests/EventIngestion.Application.Tests/Commands/SourceModeCommandHandlerTests.cs`
- **T017 [US4]** Implement T016 + `DELETE /event-sources/{source}` (plan §5:
  428 → 400 → fab → handler; 204/404/409; scope), DI registration,
  `GET /event-sources` summary + doc-comment text, `EndpointScopeDeclarationTests`
  69 → 70 (re-read on develop first).
  — `Domain/SourceMode/*`, `Application/Commands/UndeclareSourceMode*`,
  `Application/Commands/Handlers/UndeclareSourceModeCommandHandler.cs`,
  `Infrastructure/Persistence/SourceModeRepository.cs`,
  `Infrastructure/EventIngestionInfrastructureModule.cs`,
  `Api/EventSourcesEndpoints.cs`, `tests/Architecture.Tests/EndpointScopeDeclarationTests.cs`
- **T018 [US4] [RED→]** Integration: extend `EventSourceModeIntegrationTests` /
  `EventSourceModeAuthorizationIntegrationTests` with the spec §4 undeclare block
  (204 then open again; 428; 409; 404; 400; 403 for an event-source client).
  Replace `EventSourceModeApi.RestoreDiscoveryAsync` with `UndeclareAsync` and
  move every call site (FR-015: `StrictSourceIngestIntegrationTests`,
  `IdempotencyKeyReuseEventIngestionIntegrationTests`,
  `EventSourceModeIntegrationTests`). Pairs these tests *set* to `discovery` as a
  step of the test (not as cleanup) keep doing so and undeclare in `finally`.
  — `tests/Integration.Tests/EventIngestion/EventSourceMode*.cs`, the two call-site files

---

## Phase F — Close-out (sequential, after A–C)

- **T019 [Polish]** Phase 5 measurement (plan §11): `IngestThroughputMeasurementTests`
  ×2 develop, ×2 branch undeclared, ×2 branch declared discovery with kinds
  registered; admission span duration; written into the verification note as
  observed. §IV untouched.
- **T020 [Polish]** Run spec §6 end to end on the dev stack; record each step's
  observed status in the PR's verification note (steps 7 and 12 explicitly).
- **T021 [Polish]** File the constitution §VIII correction as a `documentation`
  issue for a human, with plan §10's drafted text, and link it from the PR body.
  **Do not edit `.specify/memory/constitution.md`** (ADR-0144; spec FR-012).
- **T022 [Polish]** PR body: quote every `[RED]` failure verbatim; list each
  sanctioned test edit from plan §9 with its reason; state "Phase 4a: RED";
  `Closes #2325`; name #2780 as the follow-on.

---

## Dependencies and parallelism

```
T001 ─▶ T002 ─▶ T003 ─▶ T004 ─▶ T005 ─┬─▶ Track A: T006 ─▶ T007 ─▶ T008 ─▶ T009 ─▶ T010 ─▶ T011*
                                      ├─▶ Track B: T012 ─▶ T013 ; T014 ─▶ T015
                                      └─▶ Track C: T016 ─▶ T017 ─▶ T018
                                                       (* T011 needs T018's helper)
A, B, C done ─▶ T019 ─▶ T020 ─▶ T021 ─▶ T022
```

- **Foundational: T002–T005** (Domain VOs, `DeadLetter` shape, EF config,
  migration) block everything; nothing fans out before the T005 gate.
- **Fan-out after T005:** T006, T012, T014, T016 are `[P]` — disjoint files
  (plan §8). Within a track, order is test → implementation.
- **Contention files, single owner:** `EventIngestionInfrastructureModule.cs` and
  `EndpointScopeDeclarationTests.cs` (Track C); each new integration class adds
  its own `ci-shards/shard-N.filter` line — serialise those edits or let the last
  merge resolve them.
- **One engineer, one PR.** The tracks are parallel in the ADR-0109 sense (could be
  split across worktrees) but one branch has one index (memory); an orchestrator
  fanning out should give each track its own worktree and cherry-pick in order
  A → B → C, each commit building on its own (CLAUDE.md, stacked-commit rule).

## Out of this task list

The inspector UI (#2780), registry/source-mode UI, replay on promotion, hold
de-duplication, schema validation (#2326), any constitution edit.
