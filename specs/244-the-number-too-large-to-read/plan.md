# Plan 244 — The number too large to read

**Spec**: [spec.md](spec.md) · **Issue**: #2497 · **Phase**: 2 (Plan)
**Engineer**: `test-writer` (4a) then `backend-engineer` (4b) · **Reviewer**: `backend-reviewer`
**ADRs**: ADR-0099, ADR-0047/0089, ADR-0036, ADR-0139, ADR-0144, ADR-0103. **No new ADR.**

## Context and layers

- **Bounded context**: Automation.
- **Layer**: Application only — `src/Automation/Application/Ael/AelLexer.cs` and `AelParser.cs`.
- **Not touched**: Domain (`RulePredicate` stays a length-capped string VO; AEL validity is an
  Application concern per ADR-0099), `CreateRuleCommandHandler` (FR-003), `CreateRuleErrors`
  (FR-005), Api, Infrastructure, `Shared.*`, AppHost, frontend.
- **No entities, value objects, messages or events.** No domain → integration event change. Boundary
  rules are unaffected (no new references).

## Design

### D1 — out-of-range literals become `AelParseException` (US1, FR-001)

`AelLexer.ParseInt` / `ParseDecimal` (`:163-167`) change from `string lexeme` to `AelToken token`,
use `long.TryParse` / `decimal.TryParse` with the **same** `NumberStyles` and `InvariantCulture`, and on
`false` throw `new AelParseException(<message>, token.Position)`.

- Messages (exact wording is the engineer's; tests assert only the fragment "out of range" and that the
  lexeme appears): integer — `integer literal '<lexeme>' is out of range`; the engineer may add that a
  decimal point allows values up to `decimal`'s range. Decimal — `decimal literal '<lexeme>' is out of
  range`.
- `TryParse` returns `false` only for overflow here: the lexer guarantees the lexeme is `[0-9]+` or
  `[0-9]+\.[0-9]+`, so format failure is impossible. No second branch for "format".
- `AelParser.ParsePrimary` (`:143, :148`) passes `token` instead of `token.Lexeme`. Both helpers are
  `internal` and have no other caller (`grep -rn "ParseInt\|ParseDecimal" src tests` — only the parser).

Keeping the helpers in `AelLexer` (not moving them to the parser) is the smallest change; they already
live there and own the numeric format.

### D2 — exponent notation refused in the lexer (US2, FR-002)

In the number arm (`AelLexer.cs:109-134`), after the digit run and optional fraction and **before**
adding the token:

```csharp
if (i < source.Length && source[i] is 'e' or 'E')
{
    throw new AelParseException(
        "exponent notation is not supported; write the number out in full", i);
}
```

(Engineer: mind precedence — `source[i] is 'e' or 'E'` binds as intended; `i < source.Length && …`
must guard the index.)

- Scoped to `e`/`E` only — `1contains "x"` is valid today (spec §1.2) and must stay valid.
- Cannot reject a valid input: an identifier immediately after a digit run is never accepted by the
  grammar (spec §1.2), so every affected source already fails today.
- Placed in the lexer because that is where the character is seen; the parser never sees `1e30` as one
  thing.

### Why not widen the handler's catch

Spec §4. In short: the parser's contract is `AelParseException`; fixing it at the source gives the 400
a position and gives `CompiledRule.From`'s callers a consistent `AelParseException` type too, instead of
a mixed `OverflowException`/`AelParseException` situation; widening the catch would misreport real
server bugs as operator parse errors.

### Error flow (unchanged plumbing)

`AelParseException(message, position)` → `CreateRuleCommandHandler` catch (`:38` / `:52`) →
`CreateRuleError.PredicateParseFailed` / `ActionExpressionParseFailed` (`HttpStatusCode.BadRequest`)
→ `error.ToProblem()` in `RulesEndpoints.Create` → 400 problem, `RULE_PREDICATE_PARSE_FAILED` /
`RULE_ACTION_EXPRESSION_PARSE_FAILED`.

`DryRunRuleQueryHandler`'s reason mapping lists `AelParseException` (`:137`), but its call to
`CompiledRule.From` (`:101`) sits **outside** its own `try` block (which starts at `:103` and wraps only
`AelInterpreter.Evaluate`) — so an `AelParseException` from that call site would still escape uncaught
today. That gap is pre-existing and out of scope: `CompiledRule.From` over a *stored* rule can never
meet an oversized literal, because create validates before persisting (it 500'd before `rules.Add`), so
the gap has no reachable trigger today and this PR does not need to close it.

## Tests (4a — `test-writer`)

| File | New | Colour |
|---|---|---|
| `tests/Automation.Application.Tests/Ael/AelParserTests.cs` | Theory: out-of-range literals → `AelParseException` at position, "out of range" (4 rows, spec §4a). Theory: `long.MaxValue` / `decimal.MaxValue` boundary parse. Theory: still-valid rows (`1contains "x"`, `$.payload.v1e3 > 1`, `$.payload.e30 == 2.5`). | red / green / green |
| `tests/Automation.Application.Tests/Ael/AelLexerTests.cs` | Theory: 6 exponent rows → `Tokenize` throws `AelParseException` at the `e`/`E` position, message contains "exponent". | red |
| `tests/Automation.Application.Tests/Commands/CreateRuleCommandHandlerTests.cs` | Oversized predicate → `PredicateParseFailed` with `Position == 14`, repo empty. Oversized value expression → `ActionExpressionParseFailed` with `Position == 0`, repo empty. Exponent predicate → `PredicateParseFailed`, `Position == 15`, `Reason` contains "exponent". Name clash + oversized predicate → `RuleNameTaken`. | red ×3 / green |
| `tests/Integration.Tests/Automation/RuleLifecycleIntegrationTests.cs` | One `[Fact]`: POST oversized predicate → 400, body code `RULE_PREDICATE_PARSE_FAILED`. Add a `predicate` parameter (default today's value) to the private `CreateAsync` rather than a second helper. | red (500 today) |

Asserting `Position` on the typed error, not just its type, is what makes the handler tests unable to
pass vacuously — the existing `Malformed_predicate…` test asserts type only.

**Integration shard**: the fact goes into an **existing** class, so its shard filter
(`tests/Integration.Tests/ci-shards/`) already covers it — **no shard file change**. Do not create a
new test class.

The red output for the integration fact may be captured on CI rather than locally if the stack cannot
be booted (one machine, one Aspire stack); the unit reds must be captured locally, verbatim.

## Commit sequence (each builds and passes on its own — ADR-0087)

1. `docs(244): specify, plan and task the out-of-range and exponent literal fixes` — this directory.
2. `test(automation): pin out-of-range and exponent literals as typed parse failures, red first` —
   all four test files. **Builds**; the new red facts fail, everything else green. (The repo accepts a
   red-test commit on a fix branch — see `f2cb3b39` for #2427.)
3. `fix(automation): report out-of-range and exponent literals as AEL parse errors` — `AelLexer.cs`,
   `AelParser.cs`. All green.

## Verification (phase 5)

- `dotnet test tests/Automation.Application.Tests` — all green, the new facts included, existing
  AEL suites unmodified (FR-004: `git diff --stat` on the three existing AEL test files shows additions
  only).
- `dotnet test tests/Architecture.Tests` — green (no boundary change expected).
- `AelInterpreterBenchmarkTests` run **twice** in Release — median in line with spec 203's recorded
  23.9–25.9 ms. The lexer change adds one bounds-checked char compare per numeric literal at parse
  time; the benchmark evaluates, it does not parse per iteration, so no movement is expected — measure
  anyway rather than assert it.
- Integration fact green (CI or local); step 2–4 of spec §8 by `curl` if the stack is up.

## Latency

**N/A.** No event→overlay leg touched. Parsing happens at rule create and when the rule cache compiles
a rule, never per event (ADR-0099 "Parse tree built once at rule-publish time").

## Risks

| Risk | Mitigation |
|---|---|
| An input valid today becomes invalid | Proved impossible in spec §1.2; pinned by the still-valid characterisation rows. |
| `TryParse` accepts something `Parse` rejected (or vice versa) | Same `NumberStyles` and culture; the lexer constrains the lexeme to digits and one `.`. |
| .NET `decimal.Parse` rounds instead of throwing near `MaxValue` | Boundary rows: `…335.0` parses; `…336.0` throws (observed on .NET Framework 4.8 via PowerShell; test-writer observes on .NET 10). If .NET 10 disagrees, adjust the **row**, report it, do not weaken the assertion. |
| Parallel Automation work | Open PR `fix/2496-buildcontext-dead-letter` touches Automation's event handler/evaluator, not `Ael/` or `CreateRuleCommandHandler`. Rebase after it merges. |
