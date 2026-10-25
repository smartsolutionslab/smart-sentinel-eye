# Tasks 458: Dead letters age out after 14 days

**Spec:** [spec.md](spec.md) · **Plan:** [plan.md](plan.md) · **Issue:** #3149 (feature-level, on Project #13)
**Engineers:** infra-engineer (AppHost broker policy, runbook) + backend-engineer (ServiceDefaults expiry + depth
check). Disjoint files; parallel after Phase 4a-2.
**Colour:** RED (behaviour-changing), with a declared characterisation set (plan §"Phase 4a").
**Reviewers:** infra-reviewer, backend-reviewer, security-reviewer (credential-bearing payload retention, ADR-0179/0184).
**New ADR:** none. Every decision traces to ADR-0189 (accepted, rulings 1-3), ADR-0154, ADR-0088, ADR-0118.

Format: `[ID] [P?] [Story] description — files`

## Foundational (blocks everything after it)

- [ ] T001 [US1] **Observe N1 on the pinned image** (ADR-0189 "Not verified"): boot `rabbitmq:4-management-alpine`
      with a draft `definitions.json` + `load_definitions`, the AppHost's password env; record whether the default
      user still authenticates. Write the result and the chosen mechanism (primary mount vs sanctioned management-API
      fallback) into plan.md §1 "Decision point" and the PR body. No `src/` change — infra-engineer
- T002 *(intentionally empty)* — `definitions.json` is **not** created ahead of the tests: it lands in T008, so
      R2/R3 fail on a real assertion ("definitions file carries no dead-letter policy"), not on a scaffold. The
      tests must report a missing file as that assertion failure, not as an unhandled `FileNotFoundException`.

No Shared.Kernel/Contracts or new Aspire resource is touched.

## Phase 4a-1 — characterisation, captured green first (test-writer)

- [ ] T003 [US1][US2] Run and quote green, unmodified: `OutboxBacklogHealthCheckTests`,
      `OutboxBacklogAssessmentCharacterisationTests`, `HealthCheckFailureStatusGuardTests`,
      `ServedHealthHostCompletenessTests`, `AuditObservabilityHealthCheckRegistrationTests`,
      `AppHostCompositionCharacterisationTests`, `AppHostStackPersistenceTests` — no file edited

## Phase 4a-2 — new behaviour, observed red (test-writer)

- [ ] T004 [P] [US1] R3 + R4 — `tests/ServiceDefaults.Tests/DeadLetterRetentionAgreementTests.cs`,
      `tests/ServiceDefaults.Tests/WolverineDeadLetterExpiryTests.cs` (new)
- [ ] T005 [P] [US2] R6 — `tests/ServiceDefaults.Tests/DeadLetterDepthHealthCheckTests.cs` (new)
- [ ] T006 [P] [US2] R8 — `tests/Architecture.Tests/HealthChecks/DeadLetterCheckRegistrationTests.cs` (new)
- [ ] T007 [US1][US2] R1, R2, R5, R7 — `tests/Integration.Tests/Messaging/DeadLetterAgeOutPolicyTests.cs`,
      `PostgresDeadLetterExpiryTests.cs`, `DeadLetterDepthReadinessTests.cs` (new; reuse
      `AuditQueueProbe.ClientAsync` for management credentials) + one entry per class in
      `tests/Integration.Tests/ci-shards/shard-N.filter`. Sequential with itself (one Aspire stack per machine).
      Every test cleans up broker state it creates.

Hand back the verbatim red output of T004-T007.

## Phase 4b — implementation

- [ ] T008 [P] [US1] Broker policy per T001's outcome: `src/AppHost/Resources/rabbitmq/definitions.json` (+
      `90-definitions.conf` and `WithBindMount`s in `src/AppHost/AppHost.cs` for the primary; **or**
      `src/AppHost/RabbitMqDeadLetterPolicy.cs` + its one call in `AppHost.cs` for the fallback). Turns R1, R2, R3
      (file half) green — infra-engineer
- [ ] T009 [P] [US1] `WolverineDefaults.DeadLetterRetention` + `Durability.DeadLetterQueueExpiration[Enabled]` —
      `src/ServiceDefaults/WolverineDefaults.cs`. Turns R3, R4, R5 green — backend-engineer
- [ ] T010 [US2] `DeadLetterDepthHealthCheck` + `[LoggerMessage]` + registration in `AddWolverineForContext` +
      `Extensions.cs` check-set comment — `src/ServiceDefaults/DeadLetterDepthHealthCheck.cs`,
      `src/ServiceDefaults/WolverineDefaults.cs`, `src/ServiceDefaults/Extensions.cs`. Turns R6, R7, R8 green.
      Depends on T009 (same file) — backend-engineer
- [ ] T011 [P] [US1][US2] Runbook section "Dead letters and the outbox backlog" (spec FR-008, assumption A2) —
      `docs/runbooks/audit-observability.md` — infra-engineer

## Phase 5 — verify

- [ ] T012 [US1][US2] Spec §"Independent end-to-end test procedure" steps 1-6 on a booted stack; quote the
      management-API JSON (`effective_policy_definition`), the `/health` Degraded→Healthy transition and the
      `\d` output in the PR. Also boot once against a **persistent** broker that already has the queue: no
      "inequivalent arg" startup failure (Gherkin "Conflict — existing broker")
- [ ] T013 [US1] ADR-0189 N2-N4: N2 from T012's JSON; N3 observed once (pre-existing message expires after policy
      set; policy survives broker restart) and written down, FIFO stated as assumed if not observed; N4 read-only
      review of `CameraRegisteredV1`/`CameraAddressChangedV1` handlers' exception messages — a URL echo is filed as a
      new issue, not fixed here

## Dependencies

T001 → T008. T003 → T004-T007 → T008-T011. T009 → T010 (shared file). T008, T009, T011 are `[P]` (disjoint files:
AppHost / ServiceDefaults / docs). T012-T013 after all of 4b. Coverage gates: ServiceDefaults counts as Shared (≥ 90%).
