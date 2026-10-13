# Spec 347: The automation context under its limits

**Feature Branch**: `refactor/2889-automation-metrics` (cut from `origin/develop` at `d259668e`)

**Created**: 2026-10-13

**Status**: Ready for the Phase 1 gate. No `[NEEDS CLARIFICATION]` remains. Three judgement calls
are marked explicitly (§6).

**Input**: Issue [#2889](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2889),
*Clear the ADR-0084 metric warnings in Automation's Domain, Application and Infrastructure (19)*.

**Lane.** Autonomous (ADR-0144). Behaviour-preserving: Phase 4a is **characterisation, observed
green** (constitution §Testing; CLAUDE.md "Refactors stay green").

**Spec number.** 347. Checked on 2026-10-13: `origin/develop` holds 341, 342 and 345 (#2902 merged
since this branch was cut, LayoutComposition only); no other remote branch carries a spec above
342 (`git ls-tree` over every head); the `sse-2888` worktree holds unpushed 346; open PRs (#2811,
#2837) carry no spec. Highest visible is 346. Re-check before the PR is opened.

**ADRs.** ADR-0084 (code metrics, amended 2026-09-17: S138 <= 30 lines, S107 <= 4 parameters,
S1541 <= 10, S134 <= 3, advisory `warning`), ADR-0144 (autonomous lane; phase 4a colours; the lane
may not weaken a gate), ADR-0099 (AEL), ADR-0036 (smallest change), ADR-0141 (`Option<T>` for
absences in Application), ADR-0050 (`[LoggerMessage]` fields come from the template), ADR-0051
(per-context DI module), ADR-0088 (Wolverine publish through the message context).

---

## 1. What was measured (premise verified, not assumed)

The issue's command does not run as written: `src/Automation` holds no project file. The
equivalent, which compiles Domain and Application transitively:

```sh
dotnet build src/Automation/Infrastructure/SmartSentinelEye.Automation.Infrastructure.csproj \
  -c Release --no-incremental -p:TreatWarningsAsErrors=false 2>&1 \
  | grep -oE '[^ (]+\([0-9]+,[0-9]+\): warning (S104|S107|S138|S1541|S134)' | sort -u \
  | grep -vE 'Api[\\/]' | grep Automation
```

Result on `d259668e`: 0 errors, **19 hits, exactly the 19 the issue lists, at the same lines. None
is stale.** `git diff d259668e origin/develop -- src/Automation` is empty as of writing, so the
figure holds on the current `develop` too. Measured values: `Tokenize` 185 lines / complexity 57;
`EvalBinary` complexity 14; `DryRunRuleQueryHandler.HandleAsync` 63 lines / complexity 11;
`FabEventIngestedV1Handler.Handle` 70; `RuleConfiguration.Configure` 74;
`ListRulesQueryHandler.HandleAsync` 48; `AddAutomationInfrastructure` 43; `RuleEvaluator.Evaluate`
39; `ParsePrimary` 36; `CreateRuleCommandHandler.HandleAsync` 34; `CompiledRule` constructor 8
parameters; `Rule.Create` 8; `Log.CreatedRule` 6; `Log.FannedOutActions` 5.

## 2. Decision per hit

| # | File:line | Member | Rule | Decision |
|---|---|---|---|---|
| 1 | `Application/Ael/AelInterpreter.cs:121` | `EvalBinary` | S1541 | **Clear** by extraction (plan §3.1) |
| 2 | `Application/Ael/AelLexer.cs:15` | `Tokenize` | S138 | **Clear** by extraction, behind the lexer corpus pin C1 (plan §3.2) |
| 3 | same | `Tokenize` | S1541 | **Clear** by the same extraction |
| 4 | `Application/Ael/AelLexer.cs:169` | `Tokenize` (fraction loop) | S134 | **Clear** by the same extraction |
| 5 | `Application/Ael/AelParser.cs:135` | `ParsePrimary` | S138 | **Clear** by extraction (plan §3.3) |
| 6 | `Application/Commands/Handlers/CreateRuleCommandHandler.cs:15` | `HandleAsync` | S138 | **Clear** by extraction |
| 7 | `Application/Evaluation/CompiledRule.cs:40` | private constructor (8) | S107 | **Clear**: the constructor takes the `Rule` it is compiled from plus the two parsed expressions (J2) |
| 8 | `Application/Evaluation/RuleEvaluator.cs:26` | `Evaluate` | S138 | **Clear** by extraction, no allocation added (plan §3.6) |
| 9 | `Application/EventHandlers/FabEventIngestedV1Handler.cs:47` | `Handle` | S138 | **Clear** by extraction, no allocation or await added (plan §3.7) |
| 10 | `Application/Log.cs:47` | `CreatedRule` `[LoggerMessage]` (6) | S107 | **Leave**: parameters are the structured log template's fields (ADR-0050) |
| 11 | `Application/Log.cs:50` | `FannedOutActions` `[LoggerMessage]` (5) | S107 | **Leave**: same reason |
| 12 | `Application/Queries/Handlers/DryRunRuleQueryHandler.cs:23` | `HandleAsync` | S138 | **Clear** by extraction |
| 13 | same | `HandleAsync` | S1541 | **Clear** by the same extraction |
| 14 | `Application/Queries/Handlers/ListRulesQueryHandler.cs:12` | `HandleAsync` | S138 | **Clear** by extraction |
| 15 | `Domain/Rule/Rule.cs:58` | `Rule.Create` (public, 8) | S107 | **Leave**: public Domain factory called by `CreateRuleCommandHandler`, `RuleBuilder` (Domain.Tests) and `CrossFabEvaluationIntegrationTests` (twice); any parameter object changes every caller, tests included, so "tests unmodified" cannot hold |
| 16 | `Infrastructure/AutomationInfrastructureModule.cs:33` | `AddAutomationInfrastructure` | S138 | **Clear** by extraction (public signature unchanged) |
| 17 | `Infrastructure/Persistence/Configurations/RuleConfiguration.cs:23` | `Configure` | S138 | **Clear** by extraction |
| 18 | `Infrastructure/Persistence/Migrations/20260528192601_InitialAutomation.cs:12` | generated `Up` | S138 | **Leave**: generated, already applied; the issue forbids touching migrations |
| 19 | `Infrastructure/Persistence/Migrations/20260803080508_FabScopeRules.cs:54` | generated `Up` | S138 | **Leave**: same reason |

**Cleared: 14** (9 S138, 3 S1541, 1 S134, 1 S107: hits 1-9, 12-14, 16, 17). **Left: 5** (3 S107:
2 `[LoggerMessage]` and 1 public Domain factory; 2 S138 in migrations).
Expected after-count: **5**, every one named in the PR body with its reason.

## 3. User story

### US1 (P1): Automation's non-Api layers meet ADR-0084 wherever that is mechanical

As a maintainer, I want the 14 clearable hits gone, each by an extraction (or, for the private
`CompiledRule` constructor, a narrower parameter list) **without** any change an HTTP caller, a rule
author, another context, a log reader, the database, or the event-to-overlay latency could observe.

**Why P1 / independently shippable.** It is the whole issue: one context, no contract, no
migration, no runtime resource, no Api file.

#### Acceptance scenarios

Characterisation (all must hold **before** the refactor and pass **unmodified** after):

```gherkin
Scenario: Every existing Automation test passes unmodified (happy path)
  Given Automation.Domain.Tests (132), Application.Tests (174), Infrastructure.Tests (16) and Architecture.Tests
  When the refactor commits land
  Then all pass and `git diff <last 4a commit>..HEAD -- tests/` is empty

Scenario: The lexer produces exactly today's token stream (happy path)
  Given every source in the lexer corpus (plan §4, C1)
  When it is tokenized
  Then each token's kind, lexeme and position equal the literal captured on d259668e

Scenario: The lexer refuses exactly today's inputs, at today's positions, with today's words (bad request)
  Given a single '=', a lone '&' or '|', an unterminated string, an exponent, an unknown or non-ASCII character
  When it is tokenized
  Then an AelParseException is thrown whose full Message and Position equal the captured literal

Scenario: The parser builds today's tree and refuses with today's message (bad request)
  Given the parser corpus (C2), including "(1", ")", "", "$", "$.", "$.1" and out-of-range literals
  Then the rendered tree, or the exception's full Message and Position, equal the captured literal

Scenario: Binary operators evaluate both sides, then dispatch as today
  Given every BinaryOperator over int/int, int/decimal, string-number and non-numeric operands
  Then the result value and type, or the exception type and full message, equal the captured literal
  And "1 == 1/0" throws division by zero (both operands are evaluated before dispatch)

Scenario: Rule refusal order is unchanged (conflict)
  Given a create with a bad predicate and a bad value expression
  Then the answer is PredicateParseFailed at the predicate's position
  Given a list with an invalid state and an invalid trigger source
  Then the answer is InvalidState
  Given a dry run of an unknown or illegal name with a sample that is not JSON
  Then the answer is RuleNotFound, not SampleEventNotJson

Scenario: An ingested event fans out in effect order, with today's message types (happy path)
  Given one event matching a highlight, a set-variable and a wall-switch rule
  Then three messages are published in that order, each via PublishAsync<TConcreteContract>
  And they share one RequestedAt and the causing event identifier, with distinct Metadata.EventIdentifier
  And Metadata.Fab is the event's fab string and RootIngestedAt its IngestedAt
  And the debug log reads exactly "Fanned out 3 action(s) for <event> (<source>/<kind>)."

Scenario: The DI composition and the EF model are unchanged
  Then AddAutomationInfrastructure registers the same Automation-owned services, in the same order, with the same lifetimes
  And AutomationDbContext's logical model (tables, columns, store types, converters, keys, indexes, owned types) equals the captured table
```

**Auth scenario: N/A.** No authorisation boundary moves. Fab scoping on reads (spec 013 FR-005/
FR-006) and on evaluation (FR-002) stays where it is and is pinned above, not changed.

#### Independent test procedure (end to end)

1. The `dotnet test` runs of plan §5, green before and after.
2. The grep of §1 prints exactly the 5 "Leave" rows after the change.
3. `AelInterpreterBenchmarkTests` green (NFR-002 coarse gate on the hot path).
4. CI's Integration.Tests (`Automation/*`, including `AcceptToDecideLatencyTests`,
   `EventReachesItsEffectsTests`, `CrossFabEvaluationIntegrationTests`) green: they boot the real
   stack and cover the EF mapping, the DI module and the event path against Postgres and RabbitMQ.
   Docker-only, so observed in CI, not locally.

## 4. Functional requirements

- **FR-001** The 14 "Clear" hits of §2 report nothing after the change. If an extraction leaves a
  hit, the PR says so with the measured value; it is never suppressed.
- **FR-002** No public or `protected` signature changes: `AelLexer.Tokenize`, `AelParser.Parse`,
  `AelInterpreter.Evaluate`, `CompiledRule.From` and its properties, `RuleEvaluator.Evaluate`,
  `FabEventIngestedV1Handler.Handle`, every `ICommandHandler`/`IQueryHandler`,
  `AddAutomationInfrastructure`, `RuleConfiguration.Configure`, `Rule.Create`, `Shared.Contracts`
  and `Log.cs`. Class names stay (`FoundingDecisionRecordTests` reads `AelInterpreter` from source).
- **FR-003** For every input: the token stream, every `AelParseException` message and position,
  every interpreter exception type and message, every check order and error value, every log call
  (method, arguments, order), every published message (generic type argument, order, fields) and
  every repository call are identical. Lexer statements move verbatim; no "while I'm here" edits.
- **FR-004** Per-event path (constitution §IV, *Event → overlay state*): the changes to
  `AelInterpreter`, `RuleEvaluator` and `FabEventIngestedV1Handler` add **no heap allocation, no
  I/O, no lock and no `await`** per event, per rule or per effect. Extracted helpers there are
  non-`async` (a `Task` returned directly where one is needed), take no closures, and return
  `void`, `bool`, a value already being produced, or a `readonly record struct`.
- **FR-005** No new primitive on a Domain type (constitution §II): Domain is not changed.
- **FR-006** DI registrations keep their order and lifetimes; EF configuration produces the same
  model.
- **FR-007** No suppression, `#pragma`, threshold, `.editorconfig` or `SonarLint.xml` change; no
  guard edited; no file under any `Api/` or `Migrations/` directory touched; `Log.cs` untouched.

## 5. Success criteria

- **SC-001** Before/after hit lists quoted in the PR body; after = the 5 "Leave" rows.
- **SC-002** Characterisation observed green on unrefactored code, green and unmodified after; both
  outputs quoted.
- **SC-003** `dotnet build -c Release` 0 errors; `dotnet format --verify-no-changes` clean on the
  three touched projects; coverage gates hold (Domain >= 90 %, Application >= 80 %).
- **SC-004** `AelInterpreterBenchmarkTests` green after; latency leg cited in the PR body as
  *Event → overlay state*, unchanged by construction (FR-004), with Integration.Tests'
  `AcceptToDecideLatencyTests` green in CI as the observed evidence.

## 6. Judgement calls (marked)

- **J1. The lexer is cleared, not left.** The issue's special care asks whether `Tokenize` can be
  split without risking tokenisation, error positions or messages. It can: every branch is selected
  by the current character alone and the branches are mutually exclusive, so moving each branch
  verbatim into its own method preserves the dispatch exactly. The risk is mitigated, not assumed
  away, by C1 — a corpus pinning the complete token stream and every exception's full message and
  position, captured on `d259668e` before any edit. If any C1 or C2 literal has to change, the
  refactor stops (block, do not adjust).
- **J2. `CompiledRule`'s private constructor takes the `Rule`.** The eight parameters are seven
  facts of one rule plus two parsed expressions. The constructor is private and called once, from
  `CompiledRule.From`, which already holds the `Rule`; the new signature
  `(Rule rule, AelExpression compiledPredicate, AelExpression? compiledValueExpression)` names that
  concept and needs no new type. `From` keeps parsing the predicate before the value expression.
  Built at publish / cache-seed time, not per event. The nullable private parameter mirrors the
  nullable property it sets (ADR-0141 is advisory; persisted absences stay nullable).
- **J3. One private parameter struct in `FabEventIngestedV1Handler`.** Publishing an effect needs
  the effect, the request time, the causing event, the raw fab string and the root ingest time:
  five values plus the token. The last four are one concept — the causation every fanned-out
  message carries — so they become a private `readonly record struct` built once per event, by
  name, only after `effects.Count > 0`. It is in Application and private, outside §II's Domain
  scope. A struct, so no heap allocation is added on the hot path.

No defect was found while reading the touched members, so this is not a refactor-plus-fix. Two
oddities are recorded and **not** changed: `AelInterpreter`'s summary claims the interpreter is
"allocation-free per eval" though it allocates an `AelValue` record per node; and the lexer's
summary says "no escape sequences", which C1 pins as backslashes kept literally. If either is
thought wrong, that is a separate issue.

## 7. Latency budget impact (constitution §IV)

| Member | Runs | §IV leg |
|---|---|---|
| `FabEventIngestedV1Handler.Handle` | per ingested event | **Event → overlay state** (≤ 200 ms) — start of the rule-driven chain to `OverlayHighlightRequestedV1` / `SystemVariableValueRequestedV1` |
| `RuleEvaluator.Evaluate` | per event | same leg |
| `AelInterpreter.EvalBinary` | per binary node, per rule, per event | same leg (NFR-002: ≈ 10 µs p99 per eval) |
| `AelLexer`, `AelParser`, `CompiledRule` | at create, publish, cache seed, dry run | none — not on the per-event path |
| Command/query handlers, DI module, EF configuration | per HTTP request or at boot | none |

The leg is touched, and held unchanged by construction: FR-004 forbids any allocation, I/O, lock or
`await` added per event; the extracted helpers are static or instance calls on values already in
hand. Evidence: `AelInterpreterBenchmarkTests` locally and `AcceptToDecideLatencyTests` in CI.

## 8. Out of scope

Every `Api/` file, migrations, tests' own metrics, the `ServiceDefaults` hits the same build
prints, `Log.cs`, `Rule.Create`, and the two oddities in §6.

## 9. Locked tech choices

xUnit + Shouldly + hand-written fakes (ADR-0052), sentence-style names (ADR-0053), `Ensure.That`
guards (ADR-0105), `Option<T>` (a `readonly struct`) for an absent refusal (ADR-0141), `var`
wherever it compiles, collection expressions, fluent DI chains, one parameter per line when
wrapping, no leading underscores (CLAUDE.md house rules). No Testcontainers (ADR-0103).
