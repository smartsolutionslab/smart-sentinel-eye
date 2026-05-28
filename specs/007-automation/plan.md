# Implementation Plan: 007 — Automation

**Branch:** `007-automation` | **Date:** 2026-05-28 | **Spec:** [spec.md](./spec.md)

**Status:** Draft (Phase 2 — Plan)

**Input:** Feature specification from `specs/007-automation/spec.md`
(Phase 1, ten Q&A clarifications resolved, zero `[NEEDS CLARIFICATION]`
markers). Phase-1 gate approved 2026-05-28.

## Summary

Lights up the **Automation** bounded context — the rule engine
that closes the camera → event → overlay loop. Consumes
`FabEventIngestedV1` from spec 006, evaluates a declarative,
admin-edited rule set against each event, and emits two new V1
integration events that drive variable updates (spec 005) +
overlay highlights (spec 003/005 SignalR hub).

- **Backend (Automation):** new `Rule` aggregate (CRUD + Draft →
  Active → Archived state machine). Two action shapes:
  `SetVariableValue` (with an AEL value expression) and
  `HighlightOverlay` (overlay id + duration). Three commands
  (`CreateRule`, `PublishRule`, `ArchiveRule`), three queries
  (`GetRule`, `ListRules`, `DryRunRule`). New context DB
  `automation-db`. Wolverine outbox + per-module queue isolation.
- **AEL — automation expression language:** ~400-LOC hand-rolled
  tokenizer + recursive-descent parser + tree-walking
  interpreter in `Automation.Application`. Compiles to an
  immutable expression tree at rule-publish time; the
  interpreter is allocation-free in the hot path. Targets
  ≤ 10 µs/eval; ≥ 100k evals/sec/core (NFR-002).
- **Rule cache:** in-memory
  `ConcurrentDictionary<(Source, Kind), List<CompiledRule>>` —
  process-wide singleton. Seeded on startup by querying the
  `rules` table for `Active` rows. Kept current by subscribing to
  `RulePublishedV1` / `RuleArchivedV1` (in-context integration
  events that ride the bus so multi-instance deployments stay in
  sync; for v1 we run a single instance per fab but the
  contract is forward-compat).
- **Event-driven hot path:** Wolverine subscriber on
  `FabEventIngestedV1` runs through a `RuleEvaluator` that:
  1. Looks up matching rules in the cache via
     `(envelope.Source, envelope.Kind)`.
  2. Evaluates each rule's predicate; collects matches.
  3. Evaluates each match's action expression; produces an
     in-memory action list.
  4. Publishes one `SystemVariableValueRequestedV1` and/or
     `OverlayHighlightRequestedV1` per action via the existing
     Wolverine `IEventBus` (rides the Postgres outbox).
- **Cross-context wire-in:**
  - `SystemVariables.Application` gains a
    `SystemVariableValueRequestedV1Handler` that subscribes to
    the new V1 and dispatches the existing
    `SetVariableValueCommand`. Idempotent on
    `(variableName, causingEventIdentifier)` so Wolverine
    redelivery doesn't double-set.
  - `LayoutComposition.Application` gains a
    `OverlayHighlightRequestedV1Handler` that subscribes and
    calls `ILayoutLifecycleBroadcaster.OverlayHighlightedAsync(...)`,
    a new method on the existing broadcaster bridge. The
    `SignalRLayoutLifecycleBroadcaster` in
    `LayoutComposition.Infrastructure` implements it, pushing
    `OverlayHighlightChanged` on `/hubs/layouts`.
- **Frontend (management-web):** new **Rules** nav entry, new
  `RulesPage` (DataTable with state filter + per-row Publish /
  Archive / Dry-run actions), new `RuleDialog` for Create + Edit
  (form-based: dropdowns for source/kind, textarea for
  predicate + value expression with inline AEL syntax help, and
  a **Dry-run** button that lets the admin paste a sample event
  to see whether the rule matches + what value it would produce).
  New `rulesApi` RTK Query slice in `apps/shared`.
- **Frontend (kiosk-web):** the existing `CellPage` gains a
  `onOverlayHighlightChanged` handler in `useLayoutLifecycle`
  that flips a `ssE-overlay-highlight` CSS class on the
  referenced overlay's container; a client-side timer reverts
  the class after `durationMs`.

## Technical Context

| Concern | Decision | Source |
|---|---|---|
| Backend language | C# / .NET 10 | ADR-0001, ADR-0024 |
| Frontend language | TypeScript / React 19 | ADR-0074 |
| Persistence | EF Core on Postgres (per-context DB `automation-db`) | ADR-0009, ADR-0071 |
| Messaging | RabbitMQ via Wolverine; per-module queue isolation; Postgres outbox | ADR-0042, ADR-0088 |
| Expression engine | **Hand-rolled AEL** (~400 LOC; tokenizer + recursive-descent parser + tree-walking interpreter); zero external deps | spec FR-013 + ADR-0099 (drafted in PR A) |
| Identity | Keycloak; `AdminPolicy` on writes; authenticated reads | ADR-0007 + spec FR-027 |
| API style | Minimal APIs only | ADR-0070 |
| Errors | `Result<T, ApiError>` with sealed-record error hierarchies | ADR-0047, ADR-0089 |
| Frontend state | RTK Query — new `rulesApi` slice in shared | ADR-0075 |
| Real-time push | **Existing** `/hubs/layouts` SignalR hub. New typed-client method `OverlayHighlightChanged`. No new hub. | spec FR-019 + ADR-0076 |
| Tests | xUnit + Shouldly + Moq + Testcontainers via `AspireFixture` | ADR-0052, ADR-0068 |
| Performance | Rule eval ≤ 100 ms p95 from `FabEventIngestedV1` consume to action V1 published (NFR-001). AEL ≤ 10 µs p99 / eval (NFR-002). | spec NFR-001 / NFR-002 |
| Scale | 1 000 Active rules per fab; 100 matches/sec sustained worst-case (NFR-004). | spec |

## Constitution Check

| Principle | Check | Status |
|---|---|---|
| §I On-prem first | Automation runs on the same fab host. No cloud calls. | ✅ |
| §II DDD + VOs | `RuleIdentifier`, `RuleName`, `RulePredicate` (carries the parsed expression tree), `RuleAction` (discriminated VO: `SetVariableValue` / `HighlightOverlay`), `RuleState` (enum-backed VO). Maximalist hand-written; `IValueObject<T>` markers. | ✅ |
| §III Bounded-context isolation | All new code in `SmartSentinelEye.Automation.*`. Cross-context contracts via `Shared.Contracts/SystemVariables/SystemVariableValueRequestedV1.cs` + `Shared.Contracts/LayoutComposition/OverlayHighlightRequestedV1.cs`. **No new `AllowedCrossContext` entries.** The existing `OverlayDesigner` allow-rule for the broadcaster bridge gets a new method (additive). | ✅ |
| §IV Latency budget | ≤ 100 ms p95 for the Automation leg. With spec 006 (50 ms ingest) and spec 005 (50 ms resolve + 50 ms broadcast) the total is ≤ 200 ms p95 to overlay state-change — a strict subset of the 200 ms `event → overlay state` leg of the 800 ms NFR. | ✅ |
| §V Spec-driven | Spec gate approved 2026-05-28. This plan. Tasks follow via `/speckit-tasks`. | ✅ |
| §VI Aspire composition root | New AppHost resources: `automation` (.NET Aspire project), `automation-db` (`postgres.AddDatabase`). Connection strings flow via Aspire references. | ✅ |
| §VII No event sourcing without justification | Rules are CRUD aggregates in Postgres. The in-memory rule cache is a derived read projection rebuilt from `RulePublishedV1` / `RuleArchivedV1` on start — same pattern as every other read projection. | ✅ |
| §VIII Safe at trust boundaries | Admin policy on writes; AEL parser rejects malformed predicates at the API edge. Action evaluation in the hot path catches any runtime AEL error and logs + drops the action (never crashes the consumer loop). | ✅ |
| §IX Forward-compatible interfaces | `SystemVariableValueRequestedV1` and `OverlayHighlightRequestedV1` are V1 — additive evolution only. The AEL grammar is documented + bounded; v2 can add function-calls / array indexing without invalidating v1 expressions. | ✅ |

**Result:** No violations. No Complexity Tracking entries.

**Tech-stack additions requiring ADR before Phase 4:** none. The
hand-rolled AEL is intentionally an in-context choice (not a
shared library decision), but we'll record it in ADR-0099 as part
of PR A for traceability.

## Project Structure

### Documentation

```
specs/007-automation/
├── spec.md          ← Phase 1 (approved 2026-05-28)
├── plan.md          ← this file (Phase 2)
└── tasks.md         ← Phase 3 (next; created by /speckit-tasks)
```

### Source code — files added / modified

```
src/Automation/Domain/                              ← scaffold exists; populated here
└── Rule/
    ├── Rule.cs                                     ← aggregate root (state machine)
    ├── RuleIdentifier.cs                           ← Guid v7 strongly-typed id
    ├── RuleName.cs                                 ← StringValueObject; `^[a-z][a-z0-9-]{1,62}$`
    ├── RulePredicate.cs                            ← VO wrapping raw text + parsed expression tree
    ├── RuleAction.cs                               ← discriminated VO (SetVariableValue | HighlightOverlay)
    ├── RuleState.cs                                ← enum-backed VO (Draft | Active | Archived)
    ├── IRuleRepository.cs
    └── Events/
        ├── RuleCreatedDomainEvent.cs
        ├── RulePublishedDomainEvent.cs
        └── RuleArchivedDomainEvent.cs

src/Automation/Application/
├── Commands/
│   ├── CreateRuleCommand.cs                        ← + CreateRuleErrors.cs
│   ├── PublishRuleCommand.cs                       ← + PublishRuleErrors.cs
│   ├── ArchiveRuleCommand.cs                       ← + ArchiveRuleErrors.cs
│   └── Handlers/
│       ├── CreateRuleCommandHandler.cs
│       ├── PublishRuleCommandHandler.cs
│       └── ArchiveRuleCommandHandler.cs
├── Queries/
│   ├── GetRuleQuery.cs                             ← + GetRuleErrors.cs
│   ├── ListRulesQuery.cs                           ← + ListRulesErrors.cs
│   ├── DryRunRuleQuery.cs                          ← + DryRunRuleErrors.cs
│   ├── IRuleQuerySource.cs
│   └── Handlers/
│       ├── GetRuleQueryHandler.cs
│       ├── ListRulesQueryHandler.cs
│       └── DryRunRuleQueryHandler.cs
├── DTOs/
│   ├── RuleDto.cs
│   └── DryRunResultDto.cs
├── EventHandlers/
│   └── FabEventIngestedV1Handler.cs                ← Wolverine subscriber; the hot path
├── Evaluation/
│   ├── IRuleCache.cs                               ← cache facade (Application-side seam)
│   ├── CompiledRule.cs                             ← cached rule with parsed expressions
│   ├── RuleEvaluator.cs                            ← runs predicate + action against an event
│   └── EvaluationContext.cs                        ← envelope + payload accessor
└── Ael/                                            ← Automation Expression Language
    ├── AelLexer.cs
    ├── AelParser.cs
    ├── AelExpression.cs                            ← discriminated tree (Literal | FieldAccess | Binary | Logical | Unary)
    ├── AelInterpreter.cs                           ← tree-walking evaluator (allocation-free hot path)
    └── AelValue.cs                                 ← discriminated runtime value (Int | Decimal | String | Bool)

src/Automation/Infrastructure/
├── Persistence/
│   ├── AutomationDbContext.cs                      ← DbSet<Rule>
│   ├── Configurations/RuleConfiguration.cs
│   ├── RuleRepository.cs
│   ├── RuleQuerySource.cs
│   ├── DesignTimeDbContextFactory.cs
│   ├── AutomationMigrator.cs
│   └── Migrations/
│       └── 2026xxxx_InitialAutomationSchema.cs
├── Cache/
│   ├── InMemoryRuleCache.cs                        ← singleton; ConcurrentDictionary
│   └── RuleCacheSeederHostedService.cs             ← cold-start seeding
└── AutomationInfrastructureModule.cs               ← Add Automation{Infrastructure,Api}() per ADR-0051

src/Automation/Api/
├── RulesEndpoints.cs                               ← POST / GET / publish / archive / dry-run
├── Requests/
│   ├── CreateRuleRequest.cs
│   └── DryRunRuleRequest.cs
└── AutomationApiModule.cs

src/Shared.Contracts/
├── SystemVariables/
│   └── SystemVariableValueRequestedV1.cs           ← NEW v1 contract
└── LayoutComposition/
    └── OverlayHighlightRequestedV1.cs              ← NEW v1 contract

src/SystemVariables/Application/EventHandlers/
└── SystemVariableValueRequestedV1Handler.cs        ← NEW; idempotent on (variableName, causingEventIdentifier)

src/LayoutComposition/Domain/Layout/
└── ILayoutLifecycleBroadcaster.cs                  ← NEW method OverlayHighlightedAsync(...)

src/LayoutComposition/Application/EventHandlers/
└── OverlayHighlightRequestedV1Handler.cs           ← NEW; calls broadcaster

src/LayoutComposition/Infrastructure/
└── Realtime/SignalRLayoutLifecycleBroadcaster.cs   ← MODIFIED — adds OverlayHighlightChanged push

src/AppHost/AppHost.cs                              ← adds automation project + automation-db

apps/shared/src/api/
└── rules.api.ts                                    ← RTK Query slice
apps/shared/src/realtime/
└── layoutHub.ts                                    ← MODIFIED — onOverlayHighlightChanged callback

apps/management-web/src/features/rules/
├── RulesPage.tsx
├── RulesPage.test.tsx
├── RuleDialog.tsx
├── RuleDialog.test.tsx
├── DryRunPanel.tsx
└── AelHelpPanel.tsx                                ← inline syntax reference for admins

apps/kiosk-web/src/features/cell/
└── CellPage.tsx                                    ← MODIFIED — adds onOverlayHighlightChanged

tests/Automation.Domain.Tests/                      ← new test project
├── Rule/
│   ├── RuleTests.cs
│   ├── RuleNameTests.cs
│   ├── RulePredicateTests.cs
│   ├── RuleActionTests.cs
│   ├── RuleStateTests.cs
│   ├── RuleIdentifierTests.cs
│   ├── RuleStateMachineTests.cs
│   └── RuleBuilder.cs                              ← fluent test helper

tests/Automation.Application.Tests/                 ← new test project
├── Commands/
│   ├── CreateRuleCommandHandlerTests.cs
│   ├── PublishRuleCommandHandlerTests.cs
│   └── ArchiveRuleCommandHandlerTests.cs
├── Queries/
│   ├── GetRuleQueryHandlerTests.cs
│   ├── ListRulesQueryHandlerTests.cs
│   └── DryRunRuleQueryHandlerTests.cs
├── Evaluation/
│   ├── RuleEvaluatorTests.cs
│   └── EvaluationContextTests.cs
├── Ael/
│   ├── AelLexerTests.cs
│   ├── AelParserTests.cs
│   ├── AelInterpreterTests.cs
│   └── AelFixtures.cs                              ← reusable expression strings + expected trees
├── EventHandlers/
│   └── FabEventIngestedV1HandlerTests.cs
└── Fakes/
    ├── InMemoryRuleRepository.cs
    ├── FakeRuleCache.cs
    ├── FakeEventBus.cs
    ├── FakeClock.cs
    └── TestAsyncQueryable.cs                       ← same pattern as spec 006

tests/Shared.Contracts.Tests/
├── SystemVariableValueRequestedV1Tests.cs
└── OverlayHighlightRequestedV1Tests.cs

tests/Architecture.Tests/
└── BoundaryTests.cs                                ← MODIFIED — Automation.Domain framework-free check

docs/adr/
└── 0099-hand-rolled-ael.md                         ← captures the expression-engine choice
```

## Domain Model

### Rule (aggregate root)

```csharp
public sealed class Rule : AggregateRoot<RuleIdentifier>
{
    public FabIdentifier Fab { get; private set; }
    public RuleName Name { get; private set; }
    public Source TriggerSource { get; private set; }
    public Kind TriggerKind { get; private set; }
    public RulePredicate Predicate { get; private set; }
    public RuleAction Action { get; private set; }
    public RuleState State { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public OperatorIdentifier CreatedBy { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public DateTimeOffset? ArchivedAt { get; private set; }

    public static Rule Create(...);          // → Draft; raises RuleCreatedDomainEvent
    public void Publish(IClock clock);       // Draft → Active; raises RulePublishedDomainEvent
    public void Archive(IClock clock);       // Draft|Active → Archived; raises RuleArchivedDomainEvent
}
```

### RulePredicate / RuleAction

```csharp
public sealed record RulePredicate(string SourceText, AelExpression Compiled)
    : IValueObject<string>
{
    public string Value => SourceText;
    public static RulePredicate Parse(string raw); // throws ArgumentException on parse fail
}

public abstract record RuleAction
{
    public sealed record SetVariableValue(VariableName Target, AelExpression ValueExpression) : RuleAction;
    public sealed record HighlightOverlay(OverlayIdentifier Overlay, int DurationMs) : RuleAction;
}
```

### State machine

```
Draft ──Publish──▶ Active ──Archive──▶ Archived
  │
  └────Archive(cancel)────▶ Archived
```

No `Active → Draft` (no de-publish; the only path back is to
clone). No `Archived → *`.

## AEL — automation expression language

### Grammar (EBNF)

```
expression  := orExpr
orExpr      := andExpr ('||' andExpr)*
andExpr     := notExpr ('&&' notExpr)*
notExpr     := '!' notExpr
             | comparison
comparison  := additive ( ('==' | '!=' | '<' | '<=' | '>' | '>=' | 'contains') additive )?
additive    := multiplicative ( ('+' | '-') multiplicative )*
multiplicative := unary ( ('*' | '/' | '%') unary )*
unary       := '-' unary
             | primary
primary     := literal
             | fieldAccess
             | '(' expression ')'
literal     := int | decimal | string | 'true' | 'false'
fieldAccess := '$' ('.' identifier)+
identifier  := [a-zA-Z_][a-zA-Z0-9_]*
```

### Runtime values

```csharp
public abstract record AelValue
{
    public sealed record IntValue(long Value) : AelValue;
    public sealed record DecimalValue(decimal Value) : AelValue;
    public sealed record StringValue(string Value) : AelValue;
    public sealed record BoolValue(bool Value) : AelValue;
    public sealed record NullValue : AelValue;
}
```

### Evaluation contract

```csharp
public sealed class AelInterpreter
{
    public AelValue Evaluate(AelExpression expression, EvaluationContext context);
}

public sealed record EvaluationContext(
    JsonElement EventEnvelope,    // $.source, $.kind, $.device, $.occurredAt
    JsonElement Payload);          // $.payload.*
```

### Performance characteristics

- The tree is built once at rule-publish time and cached
  inside `CompiledRule`. The interpreter walks the tree with
  zero allocations (uses a pooled `Span<AelValue>` evaluation
  stack).
- Field access uses pre-computed `JsonPath` accessors compiled
  from the parse tree.
- Target: ≤ 10 µs p99 per eval; ≥ 100k evals/sec/core (NFR-002).

## Cross-context wire-in — surfaces created or extended

### New V1 contracts

```csharp
// In Shared.Contracts/SystemVariables/SystemVariableValueRequestedV1.cs
public sealed record SystemVariableValueRequestedV1(
    string Name,
    string Value,
    DateTimeOffset RequestedAt,
    Guid CausingEventIdentifier) : IIntegrationEvent;

// In Shared.Contracts/LayoutComposition/OverlayHighlightRequestedV1.cs
public sealed record OverlayHighlightRequestedV1(
    Guid OverlayIdentifier,
    int DurationMs,
    DateTimeOffset RequestedAt,
    Guid CausingEventIdentifier) : IIntegrationEvent;
```

### SystemVariables subscriber

A new handler in `SystemVariables.Application.EventHandlers`
subscribes to `SystemVariableValueRequestedV1`, looks up the
variable, dispatches the existing `SetVariableValueCommand`.
Idempotency: dedup table `system_variable_value_requests`
storing `(variableName, causingEventIdentifier)` with a 7-day
TTL. Duplicate within TTL → no-op (Wolverine outbox redelivery
safety).

### LayoutComposition broadcaster extension

`ILayoutLifecycleBroadcaster` gains:

```csharp
Task OverlayHighlightedAsync(OverlayHighlightedNotification notification, CancellationToken cancellationToken);
public sealed record OverlayHighlightedNotification(Guid OverlayIdentifier, int DurationMs);
```

`SignalRLayoutLifecycleBroadcaster` pushes
`OverlayHighlightChanged` on the existing
`/hubs/layouts` hub. Kiosk-web's `useLayoutLifecycle` adds an
`onOverlayHighlightChanged` callback that applies the
`ssE-overlay-highlight` class for `durationMs`.

## Performance Validation (NFR-001 = ≤ 100 ms p95)

Plan-phase commitment: `NFR001_RuleEvaluationLatencyTests`
asserts p95 ≤ 100 ms over a warm 100-iteration run on a clean
Testcontainers Postgres + RabbitMQ + the spec 005 stack. The
breakdown is monitored:

| Span | Budget | Approach to hit it |
|---|---|---|
| `automation.wolverine-deserialise` | ≤ 5 ms | System.Text.Json source-generated parser on the V1 record |
| `automation.cache-lookup` | ≤ 2 ms | `ConcurrentDictionary<(Source, Kind), List<CompiledRule>>` |
| `automation.predicate-eval` | ≤ 30 ms | AEL tree-walking; ≤ 10 µs p99 per eval × up to 100 matching rules |
| `automation.action-eval` | ≤ 30 ms | Same AEL interpreter for value expressions |
| `automation.outbox-dispatch` | ≤ 25 ms | Wolverine async dispatcher reads outbox row → RabbitMQ |
| headroom | ≤ 8 ms | absorb GC pauses, network jitter |

## Out of Scope (deferred — re-stated for the plan)

- **AEL function calls + array indexing** — spec 008+ when there
  are concrete use cases.
- **Stateful rules / sliding windows / debounce / escalation** —
  spec 008 (Automation v2).
- **Notification + recording actions** — separate context(s);
  spec 008+.
- **Rule revisions / versioned audit history** — two-state
  lifecycle is enough for v1.
- **Multi-action rules** — author multiple rules with the same
  trigger.
- **Rule priorities** — `createdAt` ordering only.

## PR shape (Phase 7 preview — drives the task breakdown)

Six PRs against `develop`, in dependency order:

| PR | Title | Scope | Gate |
|---|---|---|---|
| A | `feat(automation): scaffold + Aspire + ADR-0099 + V1 contracts` | Empty projects, Aspire wiring, ADR-0099 (hand-rolled AEL), the two new V1 contracts + their Shared.Contracts tests, architecture-test extension. | Aspire boots; `automation` project resource green; V1 contract tests pass |
| B | `feat(automation): Domain — Rule aggregate + state machine + value objects` | All Domain VOs, Rule aggregate, state machine, domain tests. | Domain tests ≥ 90% coverage |
| C | `feat(automation): AEL parser + interpreter` | `Ael/*`. ≥ 95% coverage on AEL classes. ≤ 10 µs/eval benchmark in unit test. | Application coverage ≥ 80% (with the AEL slice); AEL benchmark passes |
| D | `feat(automation): Application — commands, queries, evaluator, fakes` | Rule CRUD handlers, DryRun handler, RuleEvaluator, FabEventIngestedV1Handler, in-memory fakes for tests. | Application tests ≥ 80% coverage |
| E | `feat(automation): Infrastructure + API + Wolverine wiring` | EF DbContext + migration, RuleRepository + QuerySource, InMemoryRuleCache + seeder hosted service, RulesEndpoints, AutomationApiModule, Program.cs, AppHost.cs additions. | Integration test: create + publish a rule → MQTT event → variable update visible |
| F | `feat(automation): cross-context subscribers + frontend + polish` | SystemVariableValueRequestedV1Handler in SystemVariables; OverlayHighlightRequestedV1Handler in LayoutComposition + broadcaster extension; RulesPage + RuleDialog + DryRunPanel in management-web; kiosk-web onOverlayHighlightChanged; coverage gates; README quickstart; NFR-001 latency test. | All coverage gates pass; latency p95 ≤ 100 ms |

Phase-5 manual verification (publish a real PLC event → see the
overlay value update + (separately) see an overlay highlight)
is the spec-007 release verification note, not a PR gate.

## Gate (Phase 2 → Phase 3)

This plan is ready for the Tasks phase once the architect lead
confirms:

1. The hand-rolled AEL choice is locked (ADR-0099 drafted in
   PR A).
2. The two new V1 contracts (`SystemVariableValueRequestedV1`,
   `OverlayHighlightRequestedV1`) are acceptable as the
   automation→consumer wire-out.
3. The cross-context wire-in on the SystemVariables +
   LayoutComposition side fits the existing broadcaster pattern.
4. The PR shape above (A–F) matches the team's preferred review
   cadence.
5. The ≤ 100 ms p95 NFR-001 budget breakdown is plausible to
   verify in CI under Testcontainers.

When the gate is approved, Phase 3 (`/speckit-tasks`) decomposes
this plan into atomic tasks (~80–100 tasks, one per file /
handler / test / migration), each ≤ 30 minutes of work, with
`[P]` markers on parallelizable tasks and `[US-N]` cross-
references back to the spec's user stories.
