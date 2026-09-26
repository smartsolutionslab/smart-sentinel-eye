# Tasks: Spec 264, the revoke Keycloak never hears

**Spec:** `spec.md` · **Plan:** `plan.md` · **Issue:** #2206 (defect 2; the PR may close #2206 only if defect 1 is confirmed closed by #2619, **otherwise use "Refs"**) · **Branch:** `fix/2206-webhook-revocation-disables-client`
**Phase-4a colour:** **RED** (behaviour-changing). T008 Fact 1 must be observed failing against the live stack, and its output quoted verbatim in the PR (ADR-0139, constitution §Testing).
**Engineer:** backend only (`test-writer` for 4a, then `backend-engineer` for 4b). No frontend, infra, AppHost or realm change.

`[P]` = disjoint files, may run concurrently (ADR-0109).
**Foundational:** T001 (the `Shared.Contracts` type) blocks every other test and implementation task, because each of them names the type. It is the only foundational task. Nothing in `Shared.Kernel`, AppHost or Aspire resources changes.

---

## Phase 4a: tests first (`test-writer`). Phase 4b may not edit any test file below.

- [ ] **T001 [US1] (foundational)** `src/Shared.Contracts/EventIngestion/WebhookIntegrationRevokedV1.cs`: the record in plan §2, exactly: `(string IntegrationName, DateTimeOffset RevokedAt, EventMetadata Metadata) : IIntegrationEvent`, namespace `SmartSentinelEye.Shared.Contracts.EventIngestion`, with an XML summary in the style of `Shared.Contracts/Identity/WebhookIntegrationRotatedV1.cs`.
  The test-writer creates it because every test below must compile against it. It is a contract, not behaviour, so it has no red of its own.
  - **Depends on:** nothing. **Blocks:** T002–T008, T010–T015.
  - **Expected side effect, to record in the 4a report and not to fix in 4a:** `Architecture.Tests` `Every_integration_event_has_an_audit_handler` and `V1ResourceMap_covers_every_IIntegrationEvent` go red. T014 turns them green.

- [ ] **T002 [P] [US1]** `tests/Shared.Contracts.Tests/EventIngestion/WebhookIntegrationRevokedV1Tests.cs`: mirror `tests/Shared.Contracts.Tests/Identity/WebhookIntegrationRotatedV1Tests.cs`.
  - **Depends on:** T001.

- [ ] **T003 [P] [US1]** `tests/EventIngestion.Domain.Tests/WebhookIntegration/WebhookIntegrationTests.cs`: **add** `Revoke_raises_a_domain_event_carrying_the_integrations_fab` (plan §6.2). **No existing fact changes.** Red: compile (`Fab` absent on the domain event).
  - **Depends on:** T001 (only for build order; it does not use the contract).

- [ ] **T004 [P] [US1]** `tests/EventIngestion.Application.Tests/EventHandlers/WebhookIntegrationRevokedDomainEventHandlerTests.cs`: plan §6.2 row 2, using `Fakes/FakeEventBus.cs`. Red: compile.
  - **Depends on:** T001.

- [ ] **T005 [P] [US1]** `tests/Identity.Application.Tests/Commands/DisableWebhookClientCommandHandlerTests.cs`: mirror `DisableKioskCommandHandlerTests.cs` fact for fact (plan §6.2 row 4), using `Fakes/FakeKeycloakAdminClient.cs` (`Disabled` list, and its throw switch) and `Fakes/InMemoryRegisteredClientRepository.cs`. Red: compile.
  - **Depends on:** T001.

- [ ] **T006 [P] [US1]** `tests/Identity.Application.Tests/EventHandlers/WebhookIntegrationRevokedIntegrationEventHandlerTests.cs`: plan §6.2 row 5, with a recording fake `ICommandHandler<DisableWebhookClientCommand, …>` declared in the test file (or `Fakes/` if a second test needs it). Red: compile.
  - **Depends on:** T001.

- [ ] **T007 [P] [US1]** `tests/Integration.Tests/Identity/WebhookRevocationDisablesClientIntegrationTests.cs`: plan §6.1 Facts 1 and 2. Fact 1 carries the two **controls** (`enabled == true`, grant succeeds) *before* the revoke, so the closing assertion can only fail because of the revoke. Reuse `RealmProbe.AuthorisedAdminClientAsync` + `ReadJsonAsync`, `aspire.CreateKeycloakClient()`, and `aspire.CreateAdminClientAsync("event-ingestion" | "identity")`. Use the 20 s / 500 ms poll idiom from `CrossFabWebhookRotationEffectIntegrationTests.cs:129-134`.
  - **And** add `|FullyQualifiedName~SmartSentinelEye.Integration.Tests.Identity.WebhookRevocationDisablesClientIntegrationTests.` to `tests/Integration.Tests/ci-shards/shard-3.filter` (see `7aab60de` for why).
  - **Depends on:** T001.

- [ ] **T008 [US1]** Run the suites and return **verbatim** output:
  - `dotnet test tests/Shared.Contracts.Tests tests/EventIngestion.Domain.Tests tests/EventIngestion.Application.Tests tests/Identity.Application.Tests`: expected **build failure** naming the missing `Fab`, `WebhookIntegrationRevokedDomainEventHandler`, `DisableWebhookClientCommand*` and `WebhookIntegrationRevokedIntegrationEventHandler`. That is the compile red. T002's facts, if they compile in isolation, are expected green (contract only).
  - `dotnet test tests/Integration.Tests --filter "FullyQualifiedName~WebhookRevocationDisablesClientIntegrationTests"` (Aspire fixture; **one stack per machine**; stop any running AppHost first). `Integration.Tests` references no unit-test project and T007 uses only HTTP, so it builds while the unit projects do not. Quote **Fact 1 red on `enabled` still `true`**, with **Fact 2 green** as the declared control. This is the load-bearing red.
  - `dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~BoundaryTests"`: expected red on the two audit-coverage facts only (T001's side effect).
  - Any red not listed here is an escalation, not a step.
  - **Depends on:** T002–T007.

## Phase 4b: implementation (`backend-engineer`). No test file in the diff after T008.

- [ ] **T009 [P] [US1]** `src/EventIngestion/Domain/WebhookIntegration/Events/WebhookIntegrationRevokedDomainEvent.cs`: add `FabIdentifier Fab` between `Name` and `RevokedAt` (plan §3). `src/EventIngestion/Domain/WebhookIntegration/WebhookIntegration.cs:103`: pass `Fab`.
  - **Depends on:** T008. **Blocks:** T010.

- [ ] **T010 [US1]** `src/EventIngestion/Application/EventHandlers/WebhookIntegrationRevokedDomainEventHandler.cs` (plan §5.1) + one `[LoggerMessage]` in `src/EventIngestion/Application/Log.cs` + the registration in `src/EventIngestion/Infrastructure/EventIngestionInfrastructureModule.cs` after `:72` (plan §4).
  - **Depends on:** T009. Same context as T009, so not `[P]` with it.

- [ ] **T011 [P] [US1]** `src/Identity/Application/Commands/DisableWebhookClientCommand.cs` (command + `DisableWebhookClientError` + `DisableWebhookClientFailures`) and `src/Identity/Application/Commands/Handlers/DisableWebhookClientCommandHandler.cs`: a line-for-line mirror of `DisableKioskCommand.cs` / `DisableKioskCommandHandler.cs` (plan §5.2). `DisabledWebhookClient` log message in `src/Identity/Application/Log.cs`.
  - **Depends on:** T008. `[P]` with T009/T010 (other context).

- [ ] **T012 [US1]** `src/Identity/Application/EventHandlers/WebhookIntegrationRevokedIntegrationEventHandler.cs` (plan §5.3) + its log messages in `src/Identity/Application/Log.cs`. **No Wolverine configuration**: discovery is by convention (plan §4). A hand-written listener or route is a review blocker.
  - **Depends on:** T011 (consumes its command type; same `Log.cs`).

- [ ] **T013 [US1]** `src/Identity/Infrastructure/IdentityInfrastructureModule.cs`: register `DisableWebhookClientCommandHandler`, concrete and `ICommandHandler<…>`, after `:81`, in the DisableDevice pair's shape. Do not register the Wolverine handler class.
  - **Depends on:** T011.

- [ ] **T014 [P] [US1]** `src/AuditObservability/Application/EventHandlers/IntegrationEventAuditHandler.cs`: add `Handle(WebhookIntegrationRevokedV1 …)` beside `WebhookIntegrationRotatedV1`'s. `src/AuditObservability/Application/EventHandlers/V1ResourceMap.Conventions.cs:63`: add `Add<WebhookIntegrationRevokedV1>(map, DomainResourceKind.WebhookIntegration, revoked => revoked.IntegrationName);`, with the Identity-block comment widened to name it. Add the `using …Shared.Contracts.EventIngestion;` if it is absent.
  - **Depends on:** T008. `[P]` with T009–T013 (third context, disjoint files).

- [ ] **T015** Re-run all of T008's commands. Everything is green, including Fact 1, `BoundaryTests` and `HandlerDeconstructionTests`. **Confirm `git diff --stat <T008 commit>.. -- tests/` shows no test file changes.** Build Release (`TreatWarningsAsErrors`) and `dotnet format --verify-no-changes` on every touched project.
  - **Depends on:** T009–T014.

## Phase 5: verification (`/verify`)

- [ ] **T016** Spec §7 steps 1–9 on a fresh `aspire run`. Record the Keycloak `enabled` flip time (revoke `200`, then first `false` read), the exact grant-refusal status and `error` code **as observed**, the Identity log line, the audit row, and the empty RabbitMQ error queue for the never-rotated case, all in `verification.md`. Record the first figure *as observed*. Do not call it a budget: no SLO exists for it.
- [ ] **T017** Latency: **N/A** (spec §5). State it in the verification note and claim no measurement.

## Phase 6: QA

- [ ] **T018** `/code-review`.
- [ ] **T019** `/security-review`: **mandatory** (credential lifecycle, cross-context trust). Ask specifically:
  (a) Can a crafted `WebhookIntegrationRevokedV1` disable another fab's client? It should not, because of `GetWithinFabAsync` plus the Kind gate.
  (b) Is any Keycloak failure swallowed rather than retried?
  (c) Is the "rotate after revoke" gap (spec §3) correctly deferred, and is a follow-up issue filed?

## Gate notes for the reviewer (not tasks)

- **Two stated assumptions to accept or reject at the Phase-1 gate** (spec §3): no
  back-fill for integrations revoked before this lands, and "rotate after revoke"
  deferred to a follow-up issue.
- **Dependency graph:** T001 → {T002, T003, T004, T005, T006, T007} (all `[P]`) → T008 → {T009→T010} ∥ {T011→{T012, T013}} ∥ T014 → T015 → T016–T019.
