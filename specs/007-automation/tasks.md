# Tasks: 007 — Automation

**Input:** Design documents at `specs/007-automation/`

**Prerequisites:** [spec.md](./spec.md) (Phase 1 gate approved
2026-05-28), [plan.md](./plan.md) (Phase 2 gate approved
2026-05-28).

**Status:** Draft (Phase 3 — Tasks)

## Format: `[ID] [P?] [Story] Description`

- **[P]** — independent of any task above it in the same phase; safe to parallelise.
- **[Story]** — US1 (Author), US2 (Publish+evaluate), US3 (Archive), US4 (Conflict), US5 (Overlay highlight), AEL, FOUND (foundational), READ (read-API surface), POLISH.

## Path conventions

- Backend: `src/Automation/{Domain,Application,Infrastructure,Api}/`, `src/Shared.Contracts/{SystemVariables,LayoutComposition}/`, `src/MigrationRunner/`, `src/AppHost/`
- LayoutComposition + SystemVariables wire-in: `src/{LayoutComposition,SystemVariables}/`
- ADRs: `docs/adr/0099-hand-rolled-ael.md`
- Tests: `tests/Automation.{Domain,Application,Integration}.Tests/`, `tests/Architecture.Tests/`, `tests/Shared.Contracts.Tests/`
- Frontend: `apps/shared/src/{api,realtime}/`, `apps/management-web/src/features/rules/`, `apps/kiosk-web/src/features/cell/`

Primitives from prior specs (`Option<T>`, `Result<T,E>`, `Ensure`, `AggregateRoot<TId>`, `IValueObject<T>`, `IEventBus`, `IClock`, `AspireFixture`, etc.) are reused — not repeated as tasks.

---

## Phase 1: Foundational — Aspire + V1 contracts + ADR-0099

- [ ] **T001 [FOUND]** Draft **ADR-0099** `docs/adr/0099-hand-rolled-ael.md`: chosen because DynamicExpresso's C#-language surface needs heavy sandboxing and Jint adds a JS interpreter where industrial systems shouldn't have one. Targets ≤ 10 µs/eval; ≥ 100k evals/sec/core.
- [ ] **T002 [P] [FOUND]** Add `automation-db` to `src/AppHost/AppHost.cs`: `var automationDb = postgres.AddDatabase("automation-db");` + wire it into `migrations`.
- [ ] **T003 [FOUND]** Replace the stub `automation` line in AppHost with full wiring: `WithHttpEndpoint().WithReference(automationDb).WithReference(rabbitmq).WithReference(keycloak).WaitForCompletion(migrations)`.
- [ ] **T004 [P] [FOUND]** `Automation.Domain.csproj` mirrors EventIngestion.Domain shape (Shared.Kernel only; no framework refs).
- [ ] **T005 [P] [FOUND]** `Automation.Application.csproj`: Domain + Shared.Kernel + Shared.CQRS + Shared.Contracts + `Microsoft.EntityFrameworkCore` (IQueryable seam) + `Microsoft.Extensions.Logging.Abstractions`.
- [ ] **T006 [P] [FOUND]** `Automation.Infrastructure.csproj`: EFCore + Npgsql + WolverineFx + WolverineFx.RabbitMQ + WolverineFx.EntityFrameworkCore + WolverineFx.Postgresql + `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL` + `Microsoft.AspNetCore.App` framework ref + ServiceDefaults.
- [ ] **T007 [P] [FOUND]** `Automation.Api.csproj`: Infrastructure + ServiceDefaults + `Microsoft.AspNetCore.OpenApi`.
- [ ] **T008 [P] [FOUND]** Add the four `Automation.*` projects to `SmartSentinelEye.slnx`.
- [ ] **T009 [P] [FOUND]** Verify `MigrationRunner.csproj` already refs `Automation.Infrastructure` (it does); add `builder.AddAutomationPersistence();` in `MigrationRunner/Program.cs`.
- [ ] **T010 [P] [FOUND]** `SystemVariableValueRequestedV1` in `src/Shared.Contracts/SystemVariables/SystemVariableValueRequestedV1.cs`: `(string Name, string Value, DateTimeOffset RequestedAt, Guid CausingEventIdentifier) : IIntegrationEvent`.
- [ ] **T011 [P] [FOUND]** `OverlayHighlightRequestedV1` in `src/Shared.Contracts/LayoutComposition/OverlayHighlightRequestedV1.cs`: `(Guid OverlayIdentifier, int DurationMs, DateTimeOffset RequestedAt, Guid CausingEventIdentifier) : IIntegrationEvent`.
- [ ] **T012 [P] [FOUND]** `SystemVariableValueRequestedV1Tests` in `tests/Shared.Contracts.Tests/`: positional ctor, `IIntegrationEvent` marker, equality, JSON round-trip.
- [ ] **T013 [P] [FOUND]** `OverlayHighlightRequestedV1Tests` — same 4 cases.
- [ ] **T014 [FOUND]** Extend `tests/Architecture.Tests/BoundaryTests.cs` with a positive test that `Automation.Domain` has zero framework deps. No new `AllowedCrossContext` entries.

**Checkpoint:** `aspire run` brings up `automation` project resource. ADR + V1 contracts merged. Architecture tests still 15/15 + 2 new = 17/17.

---

## Phase 2: User Story 1 — Admin authors a Draft rule (P1)

**Goal:** Authenticated admin creates a `Draft` rule with validated predicate + action expressions.

### Tests first

- [ ] **T015 [P] [US1]** `RuleIdentifierTests`.
- [ ] **T016 [P] [US1]** `RuleNameTests` — grammar `^[a-z][a-z0-9-]{1,62}$`.
- [ ] **T017 [P] [US1]** `RuleStateTests` — three singletons (Draft / Active / Archived) + `From(string)` round-trip + invalid-string rejection.
- [ ] **T018 [P] [US1]** `RuleStateMachineTests` — Draft → Active (Publish); Draft → Archived (cancel); Active → Archived (Archive); illegal transitions reject.
- [ ] **T019 [P] [US1]** `RuleActionTests` — `SetVariableValue` + `HighlightOverlay` constructors, equality, invalid duration (< 500 or > 60_000) rejection.
- [ ] **T020 [P] [US1]** `RulePredicateTests` — `Parse(rawText)` succeeds for valid AEL, throws ArgumentException for malformed.
- [ ] **T021 [P] [US1]** `RuleTests` aggregate-level — `Create` factory produces `Draft` + raises `RuleCreatedDomainEvent`; `Publish` only valid from `Draft`; `Archive` valid from `Draft` and `Active`.
- [ ] **T022 [P] [US1]** `RuleBuilder` fluent test helper (ADR-0054).

### Domain layer

- [ ] **T023 [P] [US1]** `RuleIdentifier` Guid v7 strongly-typed id.
- [ ] **T024 [P] [US1]** `RuleName` StringValueObject.
- [ ] **T025 [P] [US1]** `RuleState` enum-backed VO.
- [ ] **T026 [P] [US1]** `RuleAction` discriminated VO (`SetVariableValue` / `HighlightOverlay`).
- [ ] **T027 [P] [US1]** `RulePredicate` VO (depends on AEL — placeholder for now; replaced in Phase 3).
- [ ] **T028 [P] [US1]** `RuleCreatedDomainEvent` + `RulePublishedDomainEvent` + `RuleArchivedDomainEvent`.
- [ ] **T029 [US1]** `Rule` aggregate root with `Create`, `Publish`, `Archive`.
- [ ] **T030 [P] [US1]** `IRuleRepository` interface.

### Application layer

- [ ] **T031 [P] [US1]** `CreateRuleCommand` + `CreateRuleErrors` (`RuleNameTaken`, `PredicateParseFailed`, `InvalidActionDuration`).
- [ ] **T032 [US1]** `CreateRuleCommandHandler`.
- [ ] **T033 [P] [US1]** `RuleDto` + serialization shape (predicate as string, action as discriminated wire object).

**Checkpoint:** Creating a rule via the application handler produces a `Draft` row in `InMemoryRuleRepository`. Predicate text round-trips. No infra wiring yet.

---

## Phase 3: AEL — automation expression language

**Goal:** Tokenizer + parser + tree-walking interpreter; ≤ 10 µs p99 per eval.

### Tests first

- [ ] **T034 [P] [AEL]** `AelFixtures` — shared expression strings + expected token streams + expected parse trees.
- [ ] **T035 [P] [AEL]** `AelLexerTests` — all token kinds (literals int/decimal/string/bool, identifiers, operators, parentheses), whitespace handling, error positions on bad chars.
- [ ] **T036 [P] [AEL]** `AelParserTests` — round-trip the fixtures into the expected `AelExpression` tree; precedence tests (unary > mul > add > comparison > equality > logical-and > logical-or); error positions for bad syntax.
- [ ] **T037 [P] [AEL]** `AelInterpreterTests` — field access (`$.payload.x`, envelope fields), arithmetic + boolean ops, string `contains`, type coercion rules, runtime errors (null deref, type mismatch).
- [ ] **T038 [P] [AEL]** `AelInterpreterBenchmarkTests` — 100 000 evals of a representative predicate complete in ≤ 1 second wall-clock (≤ 10 µs/eval mean).

### AEL implementation

- [ ] **T039 [P] [AEL]** `AelValue` discriminated runtime value (`IntValue`, `DecimalValue`, `StringValue`, `BoolValue`, `NullValue`).
- [ ] **T040 [P] [AEL]** `AelExpression` discriminated tree (`Literal`, `FieldAccess`, `Binary`, `Logical`, `Unary`).
- [ ] **T041 [P] [AEL]** `AelLexer` — span-based tokenizer over `ReadOnlySpan<char>`; emits a `Token[]`.
- [ ] **T042 [US1]** `AelParser` — recursive descent over the token stream; produces an `AelExpression` tree.
- [ ] **T043 [AEL]** `AelInterpreter` — tree-walk evaluator; uses a pooled `Span<AelValue>` evaluation stack; no allocations in the hot path.
- [ ] **T044 [P] [AEL]** Update `RulePredicate.Parse` (T027) to call into the real AEL parser.

**Checkpoint:** `AelInterpreterBenchmarkTests` passes ≤ 1 s for 100 000 evals on the dev hardware envelope.

---

## Phase 4: User Story 2 — Publish + evaluate (P1)

**Goal:** Publishing a rule wires it into the in-memory cache; matching events trigger `SystemVariableValueRequestedV1`.

### Tests first

- [ ] **T045 [P] [US2]** `PublishRuleCommandHandlerTests` — happy path; unknown rule → `RuleNotFound`; already-archived → `RuleAlreadyArchived`; already-active is idempotent.
- [ ] **T046 [P] [US2]** `EvaluationContextTests` — payload + envelope JsonElement access; `$.kind`, `$.source`, `$.device`, `$.payload.x` resolve correctly.
- [ ] **T047 [P] [US2]** `RuleEvaluatorTests` — matches by (source, kind); predicate eval; action eval; publishes the right V1 events through `FakeEventBus`.
- [ ] **T048 [P] [US2]** `FabEventIngestedV1HandlerTests` — Wolverine subscriber path: receives V1, runs evaluator, publishes downstream V1 events. Idempotent on `causingEventIdentifier`.

### Application layer

- [ ] **T049 [P] [US2]** `PublishRuleCommand` + `PublishRuleErrors`.
- [ ] **T050 [US2]** `PublishRuleCommandHandler`.
- [ ] **T051 [P] [US2]** `EvaluationContext` — wraps `(envelope, payload)` JsonElements.
- [ ] **T052 [P] [US2]** `IRuleCache` interface — `LookupActive(source, kind)` returns `IReadOnlyList<CompiledRule>`.
- [ ] **T053 [P] [US2]** `CompiledRule` — cached rule with parsed predicate + action expressions.
- [ ] **T054 [US2]** `RuleEvaluator` — runs predicate + action against a `FabEventIngestedV1`. Emits in-memory action list.
- [ ] **T055 [US2]** `FabEventIngestedV1Handler` — the Wolverine subscriber; uses `IRuleCache` + `RuleEvaluator`; publishes downstream V1 events via `IEventBus`.

### Infrastructure layer

- [ ] **T056 [P] [US2]** `AutomationDbContext` + `RuleConfiguration` (table `rules`, columns mirroring the aggregate).
- [ ] **T057 [P] [US2]** Initial migration via `dotnet ef migrations add InitialAutomation`.
- [ ] **T058 [P] [US2]** `RuleRepository`.
- [ ] **T059 [P] [US2]** `RuleQuerySource`.
- [ ] **T060 [P] [US2]** `DesignTimeDbContextFactory` + `AutomationMigrator`.
- [ ] **T061 [P] [US2]** `AutomationPersistenceModule` slim composition for MigrationRunner.
- [ ] **T062 [US2]** `InMemoryRuleCache` (singleton) — `ConcurrentDictionary<(Source, Kind), List<CompiledRule>>`.
- [ ] **T063 [US2]** `RuleCacheSeederHostedService` — on startup, query `Active` rules + populate the cache. Kept fresh by `RulePublishedV1`/`RuleArchivedV1` subscribers (deferred to spec 007a if needed; for v1 single-instance is fine and a process restart re-seeds).
- [ ] **T064 [US2]** `AutomationInfrastructureModule.AddAutomationInfrastructure` registers DbContext, repos, query sources, cache, seeder, evaluator, hosted services, `IEventBus`, `AddWolverineForContext`.

### API layer

- [ ] **T065 [P] [US2]** `CreateRuleRequest` + endpoint `POST /rules` (admin only).
- [ ] **T066 [P] [US2]** `POST /rules/{name}/publish` (admin only).
- [ ] **T067 [P] [US2]** `AutomationApiModule.AddAutomationApi` + `Program.cs` wiring (defaults, bearer auth, infrastructure, endpoints).

### Cross-context wire-in — SystemVariables

- [ ] **T068 [P] [US2]** `SystemVariableValueRequestedV1Handler` in `SystemVariables.Application.EventHandlers` — looks up the variable, dispatches `SetVariableValueCommand`. Idempotency: dedup on `(variableName, causingEventIdentifier)` via a `system_variable_value_requests` table with 7-day TTL.
- [ ] **T069 [P] [US2]** `SystemVariableValueRequestedV1HandlerTests` — happy path; dedup on second delivery.
- [ ] **T070 [P] [US2]** Add migration to SystemVariables: `system_variable_value_requests` table with `(variableName, causingEventIdentifier)` PK + `seenAt timestamptz` for TTL housekeeping.
- [ ] **T071 [P] [US2]** Wire the handler in `SystemVariablesInfrastructureModule`.

**Checkpoint:** Publish a rule via API; publish a matching MQTT event via spec 006; observe the `oeeLine1` SystemVariable update on the kiosk.

---

## Phase 5: User Story 3 — Archive (P1)

- [ ] **T072 [P] [US3]** `ArchiveRuleCommandHandlerTests` — happy path; double-archive is idempotent; unknown returns `RuleNotFound`.
- [ ] **T073 [P] [US3]** `ArchiveRuleCommand` + `ArchiveRuleErrors`.
- [ ] **T074 [US3]** `ArchiveRuleCommandHandler`.
- [ ] **T075 [P] [US3]** `POST /rules/{name}/archive` endpoint (admin only).
- [ ] **T076 [P] [US3]** Update `IRuleCache` to invalidate on archive (the seeder handles cold-start; live invalidation = remove from the dictionary).

---

## Phase 6: User Story 4 — Conflict resolution (P2)

- [ ] **T077 [P] [US4]** `RuleEvaluatorTests.Two_rules_writing_the_same_variable_pick_the_later_declared` — exercises the `createdAt` ordering + last-write-wins.
- [ ] **T078 [P] [US4]** `RuleEvaluatorTests.Two_rules_writing_different_variables_both_fire` — independence.
- [ ] **T079 [P] [US4]** `RuleEvaluatorTests.Two_highlight_actions_on_the_same_overlay_both_publish` — both V1s ride the outbox; kiosk handles the OR.

---

## Phase 7: User Story 5 — Overlay highlight + LayoutComposition wire-in (P1)

**Goal:** Highlight action fires `OverlayHighlightRequestedV1`; LayoutComposition pushes `OverlayHighlightChanged` on `/hubs/layouts`; kiosk applies + auto-reverts CSS class.

### Backend cross-context

- [ ] **T080 [P] [US5]** Extend `ILayoutLifecycleBroadcaster` with `OverlayHighlightedAsync(OverlayHighlightedNotification, ct)` + the notification record.
- [ ] **T081 [P] [US5]** Implement the new method on `SignalRLayoutLifecycleBroadcaster` in `LayoutComposition.Infrastructure` — pushes `OverlayHighlightChanged` on `/hubs/layouts`.
- [ ] **T082 [P] [US5]** `OverlayHighlightRequestedV1Handler` in `LayoutComposition.Application.EventHandlers` — calls the broadcaster.
- [ ] **T083 [P] [US5]** Wire the handler in `LayoutCompositionInfrastructureModule`.
- [ ] **T084 [P] [US5]** `OverlayHighlightRequestedV1HandlerTests`.
- [ ] **T085 [P] [US5]** Architecture test: `LayoutComposition.Application` still has zero new cross-context refs (the broadcaster extension is additive on the existing allow-rule).

### Frontend

- [ ] **T086 [P] [US5]** Add `onOverlayHighlightChanged` to `apps/shared/src/realtime/layoutHub.ts` typed-client.
- [ ] **T087 [US5]** Kiosk `CellPage.tsx` — apply `ssE-overlay-highlight` CSS class on the affected overlay's container; auto-revert via `setTimeout(durationMs)`.
- [ ] **T088 [P] [US5]** Define `.ssE-overlay-highlight` styling tokens in `apps/shared/src/ui/tokens/`.

---

## Phase 8: Polish — read API, frontend, coverage, latency, README

### Read API + dry-run

- [ ] **T089 [P] [READ]** `GetRuleQuery` + handler; `ListRulesQuery` (state + triggerSource + triggerKind filters) + handler; `DryRunRuleQuery` + handler (parses + evaluates without persisting; returns `DryRunResultDto { matched: bool, evaluatedValue: string? }`).
- [ ] **T090 [P] [READ]** `GET /rules`, `GET /rules/{name}`, `POST /rules/{name}/dry-run` endpoints (admin only).
- [ ] **T091 [P] [READ]** Application tests for all three query handlers.

### Frontend

- [ ] **T092 [P] [POLISH]** `rulesApi` RTK Query slice in `apps/shared/src/api/rules.api.ts`.
- [ ] **T093 [P] [POLISH]** `RulesPage.tsx` + `RulesPage.test.tsx` — DataTable with state filter + per-row actions.
- [ ] **T094 [P] [POLISH]** `RuleDialog.tsx` + `RuleDialog.test.tsx` — form-based editor.
- [ ] **T095 [P] [POLISH]** `DryRunPanel.tsx` — paste sample event JSON, see match + value.
- [ ] **T096 [P] [POLISH]** `AelHelpPanel.tsx` — inline AEL syntax reference.
- [ ] **T097 [P] [POLISH]** Add **Rules** nav entry to management-web sidebar.

### Coverage + latency + ops

- [ ] **T098 [POLISH]** Extend `scripts/coverage-check.ps1` with `Automation.Domain >= 90` and `Automation.Application >= 80`.
- [ ] **T099 [POLISH]** `NFR001_RuleEvaluationLatencyTests` integration test: warm 20 iters + measure 100 iters; assert p95 ≤ 100 ms `FabEventIngestedV1` consume → action V1 published.
- [ ] **T100 [P] [POLISH]** README "Author and publish a rule" quickstart section.
- [ ] **T101 [P] [POLISH]** Phase-5 manual verification: `aspire run`, create + publish a rule, publish a matching MQTT event, observe variable update + (separately) overlay highlight.

---

## Dependencies between phases

```
Phase 1 (Foundational)
   │
   ▼
Phase 2 (US1: Author Draft)
   │
   ▼
Phase 3 (AEL)
   │
   ▼
Phase 4 (US2: Publish + evaluate) ──────┐
   │                                    │
   ▼                                    │
Phase 5 (US3: Archive)                  │
                                        │
   ┌────────────────────────────────────┘
   ▼
Phase 6 (US4: Conflict) — tests only
Phase 7 (US5: Highlight) — backend + frontend
   │
   ▼
Phase 8 (Polish + NFR-001 + frontend)
```

## Estimation

- 101 atomic tasks. **[P] = 73** (≈ 72%) parallelizable within their phase.
- Target PR cadence (per plan): 6 PRs (A–F) mapping roughly to Phases 1 / 2 / 3 / 4-5 / 6-7 / 8.
- Walking-skeleton-style critical path: T001 → T029 → T042 → T054 → T055 → T064 → T068 (publish rule → MQTT event → variable update visible).
