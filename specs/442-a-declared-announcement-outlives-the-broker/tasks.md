# Tasks 442: A declared announcement outlives the broker

**Spec:** [spec.md](spec.md) · **Plan:** [plan.md](plan.md) · **Issue:** #3102 (feature-level, on Project #13)
**Slice:** 1 of 1. **Engineer:** backend-engineer only (no AppHost, CI-workflow or Helm change; infra-engineer not
needed unless T002's failure injection needs a fixture change). **Colour:** red, plus characterisation (plan §"Phase 4a").
**Reviewers:** backend-reviewer, **security-reviewer (mandatory, ADR-0185 decision 6)**.

Format: `[ID] [P?] [Story] description — files`

## Phase 4a-1 — characterisation, captured green first (test-writer)

- [ ] T000 [US1] Run and quote green, unmodified: `OutboxEventBusTests`, `OutboxBacklogHealthCheckTests`,
      `OutboxBacklogAssessmentCharacterisationTests` (ServiceDefaults.Tests), `ExecutionStrategyCharacterisationTests`
      (Architecture.Tests), `OutboxSharesTheWritesFateTests`, `OutboxBacklogIsVisibleTests` (Integration.Tests, CI),
      `e2e/kiosk-live-updates.spec.ts` (CI) — no file edited

## Phase 4a-2 — new behaviour, observed red (test-writer; blocks 4b)

- [ ] T001 [US1] **First test.** Docker-free routing facts with test-only probe types: declared → `IsDurable`/
      `Mode=Durable`; undeclared → Inline; local route stays `BufferedInMemory`; a second `ConfigureSending`
      replaces the first — `tests/ServiceDefaults.Tests/DurableOutboxRoutingTests.cs` (+ probe types in the same
      project). If not buildable without Docker (plan R1), say so with the verbatim error and move the facts to T002.
- [ ] T002 [US1] Aspire persistence facts: row committed before `CommitAsync` returns (trigger or hold-back, no
      polling race); send failure keeps the durable row and delivers it later, inline probe discarded; publish with no
      EF write commits a row — `tests/Integration.Tests/ServiceDefaults/DurableOutboxMechanismTests.cs` + `ci-shards`
      entry
- [ ] T003 [P] [US1] Per-type durable routing, one case per listed type, driven by each module's real list —
      preferably one theory over the six modules in `tests/Architecture.Tests/Messaging/DurableOutboxListTests.cs`
      (`PostgresComposition` precedent; CameraCatalog and OverlayDesigner have no `*.Infrastructure.Tests` project,
      and two new projects for one test each is not justified)
- [ ] T004 [US1] `OutboxCoversEveryContextTests`: observe a non-zero `CameraRegisteredV1` outgoing write before the
      drain — `tests/Integration.Tests/CameraCatalog/OutboxCoversEveryContextTests.cs`
- [ ] T005 [P] [US1] Hot-path guard: every D3 type resolves a non-durable sender in its own module (characterisation;
      green now, proven able to fail by counterfactual 4) — same files as T003
- [ ] T006 [US1] Retry precondition observed: a probe DbContext with `EnableRetryOnFailure` makes the durable publish
      throw — in `DurableOutboxMechanismTests.cs`
- [ ] T007 [US1] `OutboxSurvivesAKillTests` re-pointed at `WebhookIntegrationRevokedV1` (create + revoke a webhook
      integration), row pending across the restart, delivered after; stays `Category=Disruptive`, run locally —
      `tests/Integration.Tests/EventIngestion/OutboxSurvivesAKillTests.cs`
- [ ] T009 [US1] Race-path characterisation (plan R2): `WebhookClientRaceDisableFailed` from the
      `DbUpdateException` branch with a healthy connection still commits and is announced — existing
      `RotateWebhookClientCommandHandler` integration suite, Aspire

## Phase 4b — implementation (backend-engineer; may not edit T001-T009)

- [ ] T010 [US1] `durableOutboxMessageTypes` + one `ConfigureSending` + comments — `src/ServiceDefaults/WolverineDefaults.cs`
      (depends T001/T002 red). **Blocks T011-T016.**
- [ ] T011 [P] [US1] Identity list (4) — `src/Identity/Infrastructure/IdentityInfrastructureModule.cs`
- [ ] T012 [P] [US1] EventIngestion list (1) — `src/EventIngestion/Infrastructure/EventIngestionInfrastructureModule.cs`
- [ ] T013 [P] [US1] CameraCatalog list (4) — `src/CameraCatalog/Infrastructure/CameraCatalogInfrastructureModule.cs`
- [ ] T014 [P] [US1] SystemVariables list (2) — `src/SystemVariables/Infrastructure/SystemVariablesInfrastructureModule.cs`
- [ ] T015 [P] [US1] OverlayDesigner list (2) — `src/OverlayDesigner/Infrastructure/OverlayDesignerInfrastructureModule.cs`
- [ ] T016 [P] [US1] LayoutComposition list (3) — `src/LayoutComposition/Infrastructure/LayoutCompositionInfrastructureModule.cs`
- [ ] T017 [P] [US1] Doc corrections (ADR-0185 item 7) — `src/ServiceDefaults/OutboxTransactionalCommit.cs`,
      `src/ServiceDefaults/OutboxEventBus.cs`, `src/Shared.CQRS/IEventBus.cs`
- [ ] T018 [P] [US1] Drain-before-rollback procedure (item 8) — `src/ServiceDefaults/OutboxBacklogHealthCheck.cs`
- [ ] T019 [US1] Run the seven counterfactuals (plan §Counterfactuals), quote each failure

## Phase 5 — verify

- [ ] T020 Spec §"Independent end-to-end test procedure" step 4 (RabbitMQ stopped, camera registered, row pending,
      drains on restart), observed on a stack booted from this branch's binaries (check the AppHost PID start time)
- [ ] T021 CI on the tip: integration shards + full-stack e2e green; `kiosk-live-updates` latency assertions pass
      unmodified (download the log before reading the run as green)

## Phase 6 — review

- [ ] T022 backend-reviewer
- [ ] T023 **security-reviewer**: decision 4 per listed type (rows 7, 9 same-database condition); no open
      transaction across a disclosing `await`; every durable publish followed by a commit; plan R2 race path

## After merge

- Human: ADR-0088 body edits and CLAUDE.md *Wolverine defaults* row (ADR-0185 *Record edits*); file the follow-up
  issue for the two event-less credential paths if not yet filed. #3016 slice A then adds `CameraAddressReadV1`.
