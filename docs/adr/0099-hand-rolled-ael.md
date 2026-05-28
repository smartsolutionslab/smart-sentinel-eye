# ADR-0099: Hand-rolled Automation Expression Language (AEL)

**Status:** Accepted
**Date:** 2026-05-28
**Supersedes:** —
**Superseded by:** —

## Context

Spec 007 (Automation) introduces declarative, admin-authored
rules. Each rule carries a **predicate** that decides whether the
rule fires on a given `FabEventIngestedV1`, and (for
`SetVariableValue` actions) a **value expression** that computes
the new value. Both strings need a tightly-scoped expression
language: JSONPath-style field access against the event payload,
arithmetic + comparison + boolean ops, and a `contains` operator.

Phase-1 Q&A locked the engine choice:

- **Hand-rolled mini-interpreter** (~400 LOC). **CHOSEN.**
- DynamicExpresso (Apache-2). Rejected.
- Jint (BSD-2). Rejected.

Constraints:

- ≤ 10 µs p99 / eval; ≥ 100 000 evals/sec/core on dev hardware.
- Lives in `Automation.Application` so the Domain layer stays
  expression-engine-free.
- No external deps (constitution §II tech-stack guard).
- Sandboxed by construction — no file IO, no network, no
  reflection, no dynamic types beyond the AEL value union.
- Forward-compat: a future v2 may add function calls + array
  indexing without invalidating v1 expressions.

## Decision

**AEL is a hand-rolled tokenizer + recursive-descent parser +
tree-walking interpreter**, owned by us, lives in
`Automation.Application.Ael`.

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

### Runtime value union

```csharp
public abstract record AelValue
{
    public sealed record IntValue(long Value)       : AelValue;
    public sealed record DecimalValue(decimal Value) : AelValue;
    public sealed record StringValue(string Value)   : AelValue;
    public sealed record BoolValue(bool Value)       : AelValue;
    public sealed record NullValue                   : AelValue;
}
```

### Performance shape

- Tokenization over `ReadOnlySpan<char>` (no string slicing).
- Parse tree built once at **rule-publish time** and cached
  inside `CompiledRule`.
- Interpreter uses a **pooled `Span<AelValue>` evaluation stack**
  — zero allocations in the hot path.
- Field access uses pre-computed `JsonPath` accessors compiled
  from the parse tree.

## Consequences

**Positive:**

- Zero external deps; trivial security review.
- Predictable, bounded performance (no JIT surprises, no GC in
  the hot path).
- Owned grammar — extensions ship on our timeline.
- Easy to test: every node type gets unit tests; benchmark test
  asserts the per-eval budget.
- Fits within `Automation.Application` (~400 LOC); doesn't bloat
  the project.

**Negative:**

- We own the maintenance forever — parser bugs, error messages,
  grammar evolution.
- ~1 week of careful engineering to nail edge cases (operator
  precedence, escape sequences in strings, decimal vs int
  promotion, null propagation).
- Admins can't paste C# / JS expressions; the syntax is its own
  thing. Mitigated by the inline AEL help panel in the
  management UI + the `/rules/{name}/dry-run` endpoint.

## Alternatives Considered

**DynamicExpresso (Apache-2) — REJECTED.** Mature C#-syntax
expression evaluator. Pros: works tomorrow, familiar syntax,
battle-tested. Cons: exposes the **entire C# language surface**
including `typeof`, `new`, method calls, indexer access. Locking
it down to a safe subset means wrapping every node in a
reference-restriction visitor — ends up the same engineering
effort as rolling our own without the security clarity. ~100
µs/eval — fine at 1 k/s but tighter than we'd like at burst
rates.

**Jint (BSD-2) — REJECTED.** Embedded JavaScript engine. Pros:
huge ecosystem familiarity; JS is a known quantity. Cons: ships
a full JS interpreter in a fab-floor system that probably
shouldn't have one; ~200 µs/eval starts to erode the 150 ms
Automation latency budget; massive sandboxing surface
(prototypes, eval, Function constructor) — too much rope.

**Roslyn scripting / `CSharpScript`. REJECTED.** Same C#-surface
problem as DynamicExpresso, plus the per-script JIT cost
(~1 second cold-compile) is incompatible with the rule-cache
seeder.

**JSONLogic — REJECTED.** Existing rule-engine library used in
admin tooling elsewhere. Pros: declarative JSON structure
matches our rule storage. Cons: the .NET ecosystem ports are
unmaintained; we'd end up forking and owning the maintenance
anyway.

## Implementation Notes

- AEL files land in `src/Automation/Application/Ael/`:
  `AelLexer.cs`, `AelParser.cs`, `AelExpression.cs`,
  `AelInterpreter.cs`, `AelValue.cs`.
- `RulePredicate.Parse(string raw)` (in Domain) calls into the
  parser; on parse failure it throws `ArgumentException` carrying
  the lexer's error position.
- Tests live in `tests/Automation.Application.Tests/Ael/`:
  `AelLexerTests.cs`, `AelParserTests.cs`,
  `AelInterpreterTests.cs`, `AelInterpreterBenchmarkTests.cs`,
  plus a shared `AelFixtures.cs` with expression strings used
  across all three.
- Benchmark target: 100 000 evals of a representative predicate
  complete in ≤ 1 second wall-clock on the dev hardware.
