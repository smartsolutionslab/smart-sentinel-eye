# Plan 347: The automation context under its limits

**Spec**: [spec.md](./spec.md). **Tasks**: [tasks.md](./tasks.md). **Issue**: #2889.

## 1. Constitution and ADR alignment

| Rule | How this plan holds it |
|---|---|
| ADR-0084 | Clears 14 of 19 hits by shape only; the 5 left are named with reasons (spec §2). |
| ADR-0144 | Phase 4a characterisation, observed green; no gate weakened; no ADR, no constitution edit. |
| §II (no primitives on Domain models) | Domain is not touched. The one new type (J3) is a private struct in Application. |
| §III (no cross-context refs) | No reference added; `Shared.Contracts` untouched. |
| §IV (latency) | *Event → overlay state* touched by three members; FR-004 forbids added allocation, I/O, lock or `await` per event (spec §7). |
| ADR-0099 | AEL grammar, tokens, positions and messages unchanged; pinned by C1-C3. |
| ADR-0050 | `Log.cs` untouched; every log call keeps method, arguments and order. |
| ADR-0088 | Every publish keeps its concrete `PublishAsync<TContract>` type argument (C6 records it). |
| ADR-0141 | Extracted refusal helpers return `Option<TError>` (a `readonly struct`, so no allocation). |
| ADR-0051 | `AddAutomationInfrastructure` keeps its signature; extractions are private static. |
| House rules | Fluent DI chains in extracted registration methods; `var`; one parameter per line when wrapping; handlers keep their deconstruction line as written (`HandlerDeconstructionTests`). |

**Concurrency.** #2887 (spec 345) is merged and touched LayoutComposition only. #2888 (spec 346,
`D:\Github\sse-2888`) touches EventIngestion and adds `EventIngestion*` test files. #2896 (spec
341, merged) touched `src/*/Api/*.cs` and added `RuleActionBuildCharacterisationTests.cs`. This
plan touches no `Api/` file, no EventIngestion or LayoutComposition file, and adds only
`Automation*`-named test files. No file overlap.

## 2. Shape

One bounded context, three layers (Application, Infrastructure; Domain untouched). No new public
type, no new project reference, no migration, no messaging change, no AppHost change.

## 3. Production shape (binding on the engineer)

Every extracted method: **<= 4 parameters (CancellationToken counts), <= 30 lines, complexity
<= 10, nesting <= 3**, verified with the grep in §5 after each commit. Statements move verbatim;
comments move with the code they explain.

### 3.1 `AelInterpreter.EvalBinary` (hit 1) — per-event path

`left` then `right` are still evaluated first, unconditionally (binary operators do not
short-circuit). The switch keeps `Contains`, `Equal`, `NotEqual` and sends everything else to a new
`private static AelValue EvalOrdered(BinaryOperator binaryOperator, AelValue left, AelValue right)`
that holds the two existing `or`-pattern arms (arithmetic -> `EvalArithmetic`, comparison ->
`EvalComparison`) and the existing `_ => throw new InvalidOperationException($"Unhandled
BinaryOperator: {binaryOperator}")` — **same message text**, so an out-of-range enum still reports
"Unhandled BinaryOperator", not the narrower helpers' wording. A static call on values already in
hand: no allocation, no closure. The class name stays (`FoundingDecisionRecordTests`).

### 3.2 `AelLexer.Tokenize` (hits 2-4) — not per event

`Tokenize` keeps the guard, the list, the loop, the whitespace skip and the trailing `EndOfFile`
token; the loop body becomes `i = LexToken(source, i, tokens);`. Branches move **verbatim** into
private static methods, each returning the next position:

- `LexToken(string source, int position, List<AelToken> tokens)` — dispatch in today's order:
  single-character tokens, operators, quotes, digits, letters/underscore, else
  `throw new AelParseException($"unexpected character '{c}'", position)`.
- `TryLexSingleCharacter(char c, int position, List<AelToken> tokens)` -> `bool` — the existing
  nine-case `switch` (`$ . ( ) + - * / %`).
- `LexComparison(string source, int position, List<AelToken> tokens)` — `<`, `>`, `=`, `!` with
  today's lexemes and the `"expected '==' (single '=' is not an operator)"` throw.
- `LexLogical(string source, int position, List<AelToken> tokens)` — `&`, `|` with their throws.
- A private `static bool NextIs(string source, int position, char expected)` may replace the
  repeated `i + 1 < source.Length && source[i + 1] == X` — a pure restatement of the same test.
- `LexString(string source, int position, List<AelToken> tokens)` — **keeps `StringBuilder`** and
  the `"unterminated string literal (started at {start})"` message at `start`.
- `LexNumber(string source, int position, List<AelToken> tokens)` plus
  `SkipDigits(string source, int position)` -> `int` — the fraction rule (a `.` counts only when a
  digit follows), then the exponent refusal at the `e`/`E` position, then the token at `start`.
  This removes the S134 nesting.
- `LexWord(string source, int position, List<AelToken> tokens)` — identifier scan and the
  `true` / `false` / `contains` keyword switch (case-sensitive).

No rewrite of the scanning logic (no regex, no `Span`, no `ReadOnlySpan` slicing, no switch to
`Substring` for the string literal). `ParseInt` / `ParseDecimal` are untouched. If C1 or C2 would
need any literal changed, stop: that is a behaviour change, not this issue.

### 3.3 `AelParser.ParsePrimary` (hit 5) — not per event

Extract the `LeftParen` case to `private static AelExpression ParseParenthesised(IReadOnlyList<AelToken> tokens, ref int cursor)`
(consume `(`, `ParseOr`, the `"expected ')'"` throw at the current token's position, consume `)`),
and the five literal cases to `private static AelExpression.Literal ParseLiteral(AelToken token)`
only if still over 30 lines. Cursor increments stay exactly where they are today relative to each
parse call, because error positions depend on them.

### 3.4 `CreateRuleCommandHandler.HandleAsync` (hit 6)

Extract both parse checks to
`private static Option<CreateRuleError> RefuseUnparseable(RulePredicate predicate, RuleAction action)`.
Order kept: name taken -> predicate parse -> value-expression parse -> create -> add -> save ->
log. The deconstruction line stays as written.

### 3.5 `CompiledRule` constructor (hit 7, J2) — not per event

`private CompiledRule(Rule rule, AelExpression compiledPredicate, AelExpression? compiledValueExpression)`
assigning `Identifier = rule.Id`, `Fab = rule.Fab`, `TriggerSource`, `TriggerKind`,
`CreatedAt = rule.Creation.At`, `Action = rule.Action` (same instance) and the two expressions.
`From` keeps `Ensure.That(rule)`, parses the predicate first, then the value expression, then
constructs. Public properties unchanged.

### 3.6 `RuleEvaluator.Evaluate` (hit 8) — per-event path

Move the `switch (rule.Action)` block verbatim into
`private void AddEffect(CompiledRule rule, EvaluationContext context, List<RuleActionEffect> effects)`.
`Evaluate` keeps the guard, the cache lookup, the `Array.Empty` early return, the
`new(candidates.Count)` capacity, the loop and the `TryEvaluatePredicate` skip. `void`, no
closure, no new collection, no `Option`/nullable result object: **no allocation added per rule**.
The default arm still logs `UnhandledRuleActionCase(rule.Action.GetType().Name, rule.Identifier)`.

### 3.7 `FabEventIngestedV1Handler.Handle` (hit 9, J3) — per-event path

Keep the guard and the deconstruction line as written. Then:

- `private bool TryParseFab(Guid eventIdentifier, string? fab, out FabIdentifier parsedFab)` — the
  whitespace check (logs `SkippedEventWithoutFab`) and the `ArgumentException` catch (logs
  `SkippedEventWithUnparseableFab` with the raw value). Order kept.
- `private bool TryEvaluate(FabEventIngestedV1 message, FabIdentifier parsedFab, out IReadOnlyList<RuleActionEffect> effects)`
  — or equivalent locals-based signature within 4 parameters. The `try` wraps **only**
  `ParseContext(message)`, exactly as today; `evaluator.Evaluate` stays outside it, inside the
  `using (document)`. Logs `SkippedEventWithUnparseablePayload(exception, eventIdentifier, payload?.Length ?? 0)`.
- `private readonly record struct EffectCausation(DateTimeOffset RequestedAt, Guid CausingEvent, string Fab, DateTimeOffset? RootIngestedAt)`
  (use the real types of `ingestedAt` and `fab` from the contract), constructed **by name**, once,
  after `effects.Count == 0` returns, with `RequestedAt: clock.UtcNow` read exactly once as today.
- `private Task PublishEffectAsync(RuleActionEffect effect, EffectCausation causation, CancellationToken cancellationToken)`
  — **not `async`**: a `switch` expression whose arms return `events.PublishAsync(new <Contract>(...), cancellationToken)`
  with the contract constructed exactly as today (same argument order, the raw `fab` string in
  `EventMetadata`, `null` correlation, `RootIngestedAt` = `ingestedAt`, a fresh
  `Guid.CreateVersion7()` per message) and `_ => Task.CompletedTask` for an unmatched effect (today
  the statement `switch` has no default, so nothing happens). Each arm's `new` keeps its concrete
  type so `PublishAsync<TEvent>` binds to the contract type, never `object`. `Handle` keeps one
  `await` per effect in its `foreach`: no extra state machine, no extra `await`.
- `logger.FannedOutActions(effects.Count, eventIdentifier, source, kind)` stays in `Handle`, after
  the loop.

### 3.8 `DryRunRuleQueryHandler.HandleAsync` (hits 12, 13)

Extract (a) name parse + fab-scoped lookup + not-found + ambiguity into
`private async Task<Result<Rule, DryRunRuleError>> FindRuleAsync(IReadOnlyCollection<FabIdentifier> fabs, string name, CancellationToken cancellationToken)`
(use the query's real fab collection type; keep every comment with its statement), and (b) the
`using (sample)` evaluation block with its `catch` into
`private Result<DryRunResultDto, DryRunRuleError> Evaluate(Rule rule, JsonDocument sample)` (keeps the
`DryRunEvaluationFailed` log and the three-type reason filter). Order kept: name parse -> lookup ->
not found -> ambiguous -> sample parse -> compile -> predicate -> value expression. The sample is
still parsed **after** the lookup.

### 3.9 `ListRulesQueryHandler.HandleAsync` (hit 14)

Extract the state filter to
`private static Result<IQueryable<Rule>, ListRulesError> FilterByState(IQueryable<Rule> rules, string? state)`
and the two trigger filters to
`private static Option<IQueryable<Rule>> FilterByTrigger(IQueryable<Rule> rules, string? triggerSource, string? triggerKind)`
(None = a filter value that could never match, answered as an empty success). The fab scope stays
first in `HandleAsync`; order kept: fab scope -> state -> source -> kind -> order by
`Creation.At` descending -> map.

### 3.10 `AutomationInfrastructureModule.AddAutomationInfrastructure` (hit 16)

Keep `AddAutomationPersistence()` and `EnrichPostgresDbContext<AutomationDbContext>()` first and
the `AddWolverineForContext` call last. Extract private static groups in today's order:
`AddPersistenceServices` (repository, dispatcher, clock, `TimeProvider.System`, idempotency store,
sweep), `AddRuleEvaluation` (cache, evaluator, seeder, `FabEventIngestedV1Handler`),
`AddCommandHandlers`, `AddQueryHandlers` (query source + three handlers). Fluent chains inside each
group. Registration **order and lifetimes are unchanged** (C8).

### 3.11 `RuleConfiguration.Configure` (hit 17)

A sequence of private static methods in today's order: table + key + identity, scalar columns
(fab, name, trigger source/kind, predicate, action, state), creation (owned type + required
navigation), timestamps + version, indexes + `Ignore(PendingEvents)`. Builder calls keep their
order and arguments. `RuleActionColumnConverter` untouched.

## 4. Characterisation tests (phase 4a, test-writer, before any production edit)

Coverage that already exists and is relied on (must pass unmodified):

| Member | Existing tests |
|---|---|
| `AelLexer.Tokenize` | `AelLexerTests` (9 facts; kinds mostly, messages by `ShouldContain`, few positions) |
| `AelParser.ParsePrimary` | `AelParserTests` (14) |
| `AelInterpreter.EvalBinary` | `AelInterpreterTests` (21), `AelInterpreterBenchmarkTests` (NFR-002) |
| `CompiledRule` | indirectly via `RuleEvaluatorTests`, `RuleQueryHandlerTests` dry runs, `InMemoryRuleCacheTests` |
| `RuleEvaluator.Evaluate` | `RuleEvaluatorTests` (19) |
| `FabEventIngestedV1Handler.Handle` | `FabEventIngestedV1HandlerTests` (21 facts) |
| `CreateRuleCommandHandler` | `CreateRuleCommandHandlerTests` (12) |
| `DryRunRuleQueryHandler`, `ListRulesQueryHandler` | `RuleQueryHandlerTests` (38) |
| Infrastructure module | `Architecture.Tests` via `PostgresComposition` (idempotency sweep, health, pool bound, outbox) |
| `RuleConfiguration` | `RuleActionColumnConverterTests` (converter only); mapping in Integration.Tests (CI only) |

Gaps, pinned by **new files only** (no existing test edited). Every expected value is a literal
captured by running against unrefactored `d259668e`; exceptions are pinned by type, **full**
`Message` and `Position`, never by `ShouldContain`.

| ID | File | Pins |
|---|---|---|
| C1 | `tests/Automation.Application.Tests/Ael/AelLexerCorpusCharacterisationTests.cs` | `Tokenize` over a corpus, each rendered as `Kind 'lexeme' @position` per token or `AelParseException @position: <Message>`. Must include: every token kind; each keyword and a case variant (`True`, `Contains`, `true1`); identifiers with `_` and digits (`_a1`, `a_b`); field paths; empty source and whitespace only (EOF position); tabs, `\r\n`, NBSP `\u00A0` and `\u2003` as whitespace; strings in both quotes, empty, containing the other quote, a backslash (`"a\nb"` with a literal backslash), a newline, non-ASCII; unterminated in each quote and after leading text; integers (`0`, `007`, `9223372036854775808`), decimals (`3.14`, `12.5.3`), `1.` and `.5` and `1..2`, `1.e5` (Int, Dot, Identifier — no throw), `12abc`, `1_000`; exponents `1e5`, `1E`, `1.5e3`, `1.5E`; every operator plus adjacent forms (`<=`, `<==`, `!==`, `&&&`, `|||`, `= =`, `a =`, `==`), single `=`, `&`, `|`; unknown characters `@`, `#`, `^`, `~`, `` ` ``, `é`, `٣` (Arabic-Indic digit), an emoji (surrogate pair: message carries the high surrogate). A realistic multi-line predicate. |
| C2 | `tests/Automation.Application.Tests/Ael/AelParserCorpusCharacterisationTests.cs` | `Parse` over a corpus, the tree rendered by a test-local S-expression printer (literal kind + value, field segments, operator names), or the exception's type, full message and position. Must include: each literal kind incl. `true`/`false`/string; `(1)`, `((1))`, `(1`, `)`, `()`, empty source, `1 2` (trailing token), `$`, `$.`, `$.1`, `$.a.`, `$a`, `- -1`, `!!true`, `1 contains 2`, precedence chains, `9223372036854775808` and an over-range decimal (full messages from `ParseInt`/`ParseDecimal`). |
| C3 | `tests/Automation.Application.Tests/Ael/AelBinaryOperatorCharacterisationTests.cs` | Every `BinaryOperator` over int/int, int/decimal, decimal/int, numeric string vs int, bool/bool, string/string, null/null and mismatched pairs: result type and value, or exception type and full message (`contains` sides, coercion, division/modulo by zero). Both operands evaluated before dispatch: `1 == 1 / 0` and `"a" contains (1 / 0)` throw division by zero. |
| C4 | `tests/Automation.Application.Tests/Evaluation/CompiledRuleCharacterisationTests.cs` | `CompiledRule.From`: `Identifier`, `Fab`, `TriggerSource`, `TriggerKind`, `CreatedAt == rule.Creation.At`, `Action` same instance, `CompiledValueExpression` null for highlight and switch, non-null for set-variable; a rule whose predicate **and** value expression are both malformed throws the predicate's exception (message and position). |
| C5 | `tests/Automation.Application.Tests/Evaluation/RuleEvaluatorEffectOrderCharacterisationTests.cs` | Mixed candidates (highlight, set-variable, a set-variable whose value expression throws, switch-Next, switch-Layout) in cache order -> exactly the effects in that order with every field; the throwing one logs `ValueExpressionEvaluationFailed` (exact text) and is skipped; a test-local `RuleAction` subclass hits the default arm: exact `UnhandledRuleActionCase` text, no effect, loop continues. |
| C6 | `tests/Automation.Application.Tests/EventHandlers/FabEventFanOutCharacterisationTests.cs` | With a test-local `IEventBus` that records `(typeof(TEvent), message)` per call: one event matching highlight + set-variable + switch rules publishes three messages in effect order, with type arguments `OverlayHighlightRequestedV1`, `SystemVariableValueRequestedV1`, `WallSceneSwitchRequestedV1`; all share `RequestedAt` (the fake clock's value) and `CausingEventIdentifier`; `Metadata.EventIdentifier` distinct and non-empty; `Metadata.Fab` is the event's fab string; `Metadata.RootIngestedAt` is the event's `IngestedAt`; the debug log reads exactly `Fanned out 3 action(s) for <guid> (<source>/<kind>).`; with zero effects nothing is published and no fan-out log is written. |
| C7 | `tests/Automation.Application.Tests/Commands/CreateRuleRefusalOrderCharacterisationTests.cs` | Bad predicate + bad value expression -> `PredicateParseFailed` with the predicate's reason and position; any refusal adds nothing and does not save; success logs exactly `Created rule <id> '<name>' (<source>/<kind>) by <operator>.` |
| C7b | `tests/Automation.Application.Tests/Queries/RuleQueryRefusalOrderCharacterisationTests.cs` | List: invalid state + invalid trigger source -> `InvalidState`; valid state + invalid trigger kind -> empty success; whitespace filters ignored. Dry run: unknown name + non-JSON sample -> `RuleNotFound`; illegal `RuleName` (empty / over-length) -> `RuleNotFound` naming it; ambiguous + non-JSON -> `FabAmbiguous`; null sample -> `SampleEventNotJson`; predicate false + throwing value expression -> `Matched: false`, no failure, no log; a non-operator-facing exception yields reason `the rule could not be evaluated against this sample` and logs `DryRunEvaluationFailed` (exact text). Add only the cases `RuleQueryHandlerTests` does not already pin exactly. |
| C8 | `tests/Architecture.Tests/Persistence/AutomationRegistrationCharacterisationTests.cs` | Mirror `LayoutCompositionRegistrationCharacterisationTests` on develop: via `PostgresComposition.Compose("automation", ...)`, the ordered descriptors mentioning a `SmartSentinelEye.Automation.*` type (recursively through generic arguments) plus the shared services the module registers (`IClock`, `IDomainEventDispatcher`, `TimeProvider`), rendered `"{Lifetime} {Service} -> {Implementation or factory/instance}"`, equal a literal list. No total descriptor count. |
| C9 | `tests/Automation.Infrastructure.Tests/AutomationModelCharacterisationTests.cs` | Mirror `LayoutCompositionModelCharacterisationTests` on develop: `AutomationDbContext` built with `UseNpgsql("Host=unused")`, its design-time model read through the **public metadata API** (per entity: table, schema, owned, key; per property: column, store type, CLR type, nullability, max length, converter provider type, default, concurrency token; foreign keys; indexes with filter and uniqueness; navigations), equal a literal table. **Not** `Model.ToDebugString()`. |

A characterisation test that arrives red on the unrefactored code is a test defect and is fixed in
the test, never by a production change in this spec. None of these classes is in
`Integration.Tests`, so no `ci-shards/*.filter` entry is needed.

## 5. Measurement and run commands (before and after, quoted in the PR)

```sh
dotnet build src/Automation/Infrastructure/SmartSentinelEye.Automation.Infrastructure.csproj \
  -c Release --no-incremental -p:TreatWarningsAsErrors=false 2>&1 \
  | grep -oE '[^ (]+\([0-9]+,[0-9]+\): warning (S104|S107|S138|S1541|S134)' | sort -u \
  | grep -vE 'Api[\\/]' | grep Automation
```

Before (`d259668e`): 19 lines (spec §2). After: the 5 "Leave" rows exactly (`Log.cs:47`,
`Log.cs:50`, `Rule.cs:58`, the two migrations). Compare by member; lines of left hits do not move
because their files are untouched.

```sh
dotnet test tests/Automation.Domain.Tests -c Release          # 132 passed on d259668e
dotnet test tests/Automation.Application.Tests -c Release     # 174 passed on d259668e
dotnet test tests/Automation.Infrastructure.Tests -c Release  # 16 passed on d259668e
dotnet test tests/Architecture.Tests -c Release               # PrimitiveBoundaryTests, HandlerDeconstructionTests, BoundaryTests, FoundingDecisionRecordTests, PostgresComposition suites
dotnet test tests/Automation.Application.Tests -c Release --filter "FullyQualifiedName~AelInterpreterBenchmarkTests"
dotnet format src/Automation/Application/SmartSentinelEye.Automation.Application.csproj --verify-no-changes
dotnet format src/Automation/Infrastructure/SmartSentinelEye.Automation.Infrastructure.csproj --verify-no-changes
```

Integration.Tests (`Automation/*`, incl. `AcceptToDecideLatencyTests`): CI.

## 6. Risks

| Risk | Mitigation |
|---|---|
| Lexer split alters a token, a position or a message | Verbatim moves (§3.2), C1 complete-stream corpus, C2 through the parser |
| Parser cursor moved relative to a throw | §3.3 rule + C2 positions |
| Binary dispatch loses "both operands evaluated" or the unhandled-operator text | §3.1 + C3 |
| Per-event allocation or extra `await` added on the rule path | FR-004; `PublishEffectAsync` non-`async`; `AddEffect` `void`; `EffectCausation` a struct; benchmark + `AcceptToDecideLatencyTests` |
| `PublishAsync<object>` instead of the contract type (Wolverine routes by type) | §3.7 + C6 records `typeof(TEvent)` |
| `try` widened to catch a `JsonException` from evaluation | §3.7 binding: `try` around `ParseContext` only |
| `parsedFab.Value` substituted for the raw fab in metadata | §3.7 + C6 |
| Refusal order drift in create / list / dry run | C7, C7b |
| DI order change altering last-wins resolution | C8 ordered list |
| EF config reorder altering the model | C9 logical model table |
| Collision with #2888 / merged #2887, #2896 | No `Api/`, EventIngestion or LayoutComposition file; distinct test file names |
