# Spec 244 — The number too large to read

**Issue**: #2497 · **Branch**: `fix/2497-ael-lexer-overflow` · **Phase**: 1 (Specify)
**Date**: 2026-09-24 · **Base**: `a54b11d0` (`origin/develop`, fetched 2026-09-24)
**Context**: Automation — `Application/Ael/` (lexer, parser) only. No Domain, Infrastructure, Api,
contract, AppHost or frontend change.
**Engineer**: `backend-engineer` (4b) after `test-writer` (4a) · **Reviewer**: `backend-reviewer`
**Lane**: autonomous (ADR-0144) — issue carries `agent:ready`
**Phase 4a colour**: **red** (§6) — an unhandled 500 becomes a typed 400, and exponent notation gets
its own parse error instead of a misleading one. Behaviour-changing.
**ADRs**: ADR-0099 (AEL grammar — the authority on what a numeric literal is), ADR-0047 / ADR-0089
(`Result<T, Error>` with `ApiError` → the 400), ADR-0037 (phases), ADR-0144 (lane), ADR-0139
(§Testing — new behaviour observed red first), ADR-0036 (smallest change), ADR-0103 (integration via
the Aspire fixture)
**Constitution**: §IV — **N/A**. Rule-authoring endpoint; parsing happens at create time and when a rule
is compiled into the cache, never per event. §II — no domain model touched.
**New ADR needed**: **No** (§5 explains why the exponent decision does not need one).
**Follow-up to**: #2427 / spec 203 (`specs/203-one-value-not-the-whole-event/spec.md` §"Found, not
fixed", item 4).

---

## 1. The premise, re-checked against `a54b11d0`

| # | Issue claim | Status |
|---|---|---|
| 1 | `AelLexer.ParseInt`/`ParseDecimal` throw `OverflowException` on an oversized literal | **Holds.** `AelLexer.cs:163-167` — bare `long.Parse` / `decimal.Parse`. Called only from `AelParser.ParsePrimary` (`AelParser.cs:143, :148`). |
| 2 | `CreateRuleCommandHandler` catches only `AelParseException` | **Holds.** `:38` (predicate) and `:52` (value expression). |
| 3 | …so the create endpoint answers 500 | **Holds, reproduced at the handler (§1.1); the 500 is the consequence, not separately observed.** `Automation/Api/Program.cs:17` `UseExceptionHandler()`; the registered `IExceptionHandler`s (`ServiceDefaults/Authorization/`: `BadHttpRequest…`, `FabAuthorization…`, `UnattributableOperator…`) each decline anything else, so an `OverflowException` becomes the generic 500 problem response — the same path `RulesEndpoints.cs:112-118` records for #1298. |
| 4 | `1e30` in rule source lexes as `1` then identifier `e30` | **Holds exactly** (§1.2). |
| 5 | "…or worse, silently parses as something unintended" | **Does not hold.** Every position was traced and probed: it always fails with an `AelParseException` — but a *misleading* one (§1.2). |

### 1.1 Reproduction — the overflow escapes the handler

A throwaway xUnit theory (deleted afterwards, never committed) drove `CreateRuleCommandHandler` with
`InMemoryRuleRepository`, catching whatever escaped. Verbatim output, 2026-09-24 on `a54b11d0`:

```
ESCAPED System.OverflowException: Value was either too large or too small for an Int64.
ESCAPED System.OverflowException: Value was either too large or too small for a Decimal.
ESCAPED System.OverflowException: Value was either too large or too small for an Int64.
ESCAPED System.OverflowException: Value was either too large or too small for an Int64.
```

Rows: predicate `$.payload.v > 999999999999999999999999999999999999` (the issue's own example);
predicate `$.payload.v > 99999999999999999999` (20 digits — just past `long`); predicate
`$.payload.v > 99999999999999999999999999999.5` (past `decimal`); and a valid predicate with value
expression `99999999999999999999`. **All four escape**; both parse sites in the handler are affected,
and both literal kinds.

### 1.2 Exponent notation — what actually happens

The lexer's number arm (`AelLexer.cs:109-134`) takes a digit run and an optional `.digits` fraction,
then stops. `e`/`E` is a letter, so the next iteration starts an identifier (`:136-154`). Probed
(same throwaway harness, `AelLexer.Tokenize` + `AelParser.Parse`):

```
SRC $.payload.v > 1e30
  TOKENS … GreaterThan('>'@12) IntLiteral('1'@14) Identifier('e30'@15) EndOfFile(''@18)
  THREW AelParseException: AEL parse error at position 15: unexpected trailing token 'e30'
SRC $.payload.v > 1e+30
  TOKENS … IntLiteral('1'@14) Identifier('e'@15) Plus('+'@16) IntLiteral('30'@17) EndOfFile(''@19)
  THREW AelParseException: AEL parse error at position 15: unexpected trailing token 'e'
SRC $.payload.v > 1e-3        → … at position 15: unexpected trailing token 'e'
SRC $.payload.v > 1.5e3       → DecimalLiteral('1.5') Identifier('e3') … position 17: unexpected trailing token 'e3'
SRC ($.payload.v > 1e30)      → … at position 16: expected ')'
SRC 1e30 == $.payload.v       → … at position 1: unexpected trailing token 'e30'
SRC $.payload.v > 1e30 || true→ … at position 15: unexpected trailing token 'e30'
SRC -1e3                      → … at position 2: unexpected trailing token 'e3'
SRC $.payload.v * 1e2         → … at position 15: unexpected trailing token 'e2'
SRC 1contains "x"             → PARSED Binary { Contains, IntValue 1, StringValue "x" }
```

**Why it can never silently misparse.** The only grammar position that accepts an `Identifier` token
is directly after a `.` inside a field path (`AelParser.ParseFieldAccess`, ADR-0099
`fieldAccess := '$' ('.' identifier)+`). A number literal is never preceded by that `.` in a valid
parse (`$.a.1e3` already fails: "expected identifier after '.'"). After a literal, every loop checks for
an operator token, and no identifier beginning with `e`/`E` is a keyword (`true`, `false`, `contains`).
So an `e…` identifier after a number always ends the expression early and is reported as a trailing
token, or as `expected ')'` inside parentheses. It is a **clean 400 today — with a message that points
at the wrong problem**. The `1contains "x"` row shows why the fix must be scoped to `e`/`E` and not to
"any letter after a digit": that input is valid today.

## 2. Scope

**In**:
1. An out-of-range numeric literal in rule source is a parse error (`AelParseException`) at the
   literal's position — so both create-handler parse sites return their existing typed 400.
2. A numeric literal immediately followed by `e`/`E` is a parse error that names exponent notation.

**Out** — do not do:
- **Exponent support** (§5).
- **Promoting an over-`long` integer literal to decimal.** Rejected, not promoted — a literal's kind
  decides int-vs-decimal arithmetic, and silently changing it is a semantics decision this issue does
  not ask for. The error message may tell the operator that writing a decimal point is an option.
- **Widening `CreateRuleCommandHandler`'s catch** (§4, the deliberate deviation from the issue text).
- Silent precision loss for many fractional digits (`decimal.Parse` rounds; spec 203 item 5's sibling).
- `long.MinValue` being unwritable (`-9223372036854775808` is unary minus over an out-of-range
  literal) — it becomes a 400 instead of a 500, which is the point; no special case.
- Letters other than `e`/`E` directly after a digit run (`1abc` → trailing-token error, unchanged).
- The duplicated "position N" in the 400's message (`CreateRuleErrors.cs:21-31` prefixes a message
  that `AelParseException` already prefixed) — pre-existing, cosmetic.
- Parser recursion depth (spec 203 item 6).

## 3. User stories

**US1 (P1) — an oversized literal is a typed 400, not a 500.**
As a rule author who types a threshold too large for AEL, I get `RULE_PREDICATE_PARSE_FAILED` (or
`RULE_ACTION_EXPRESSION_PARSE_FAILED`) with the literal's position, so I can see what to fix — instead
of a generic server error that tells me nothing and looks like an outage.

**US2 (P2) — exponent notation is refused by name.**
As a rule author who writes `1e3`, I am told exponent notation is not supported and to write the number
out, instead of being told `e3` is an "unexpected trailing token".

Each story ships and is observable on its own (US1 at the HTTP boundary; US2 at the same boundary with
a different input). One PR carries both: same two files, one engineer.

## 4. Why the parser, not the handler's catch

The issue proposes widening the handler's `catch`. This spec fixes the parser instead, and does **not**
widen the catch:

- `AelParser`'s documented contract (`AelParser.cs:12-13`) is "parse failures throw
  `AelParseException` carrying the position of the failing token". An out-of-range literal *is* a parse
  failure of operator input; `OverflowException` escaping is the parser breaking its own contract.
  Fixing it at the source gives the 400 a **position** (a widened catch has none to give), and covers
  every caller of `AelParser.Parse` — both handler sites and `CompiledRule.From` — at once.
- #2427's "stop enumerating exception types" reasoning was for the **evaluator in a message
  handler**, where an escape dead-letters events and silently stops a rule. Here an escape is a 500 to
  an authenticated operator — which is the *correct* answer for a genuine server bug. A catch widened
  to `not OperationCanceledException` would report a future `NullReferenceException` in the parser as
  "your predicate failed to parse", blaming the operator for our defect. Keeping the catch narrow keeps
  the 400/500 split honest.

## 4a. Acceptance scenarios

```gherkin
Feature: numeric literals AEL cannot represent

  Background:
    Given an operator with sse.rules.write in fab "munich"

  # US1 — happy (the fix)
  Scenario: an oversized integer threshold in a predicate is a typed 400
    When they POST /rules?fabId=munich with predicate "$.payload.v > 999999999999999999999999999999999999"
    Then the response is 400
    And the problem code is "RULE_PREDICATE_PARSE_FAILED"
    And the message names position 14 and says the literal is out of range
    And no rule is stored

  Scenario Outline: every out-of-range literal is a parse error at its own position
    When the AEL source "<source>" is parsed
    Then an AelParseException is thrown at position <position> mentioning "out of range"
    Examples:
      | source                                         | position |
      | 9223372036854775808                            | 0        |
      | $.payload.v > 99999999999999999999             | 14       |
      | $.payload.v > 99999999999999999999999999999.5  | 14       |
      | 1 + 79228162514264337593543950336.0            | 4        |

  Scenario: an oversized literal in a SetVariableValue expression is a typed 400
    When they create a rule whose value expression is "99999999999999999999"
    Then the result is ActionExpressionParseFailed at position 0
    And no rule is stored

  # US1 — boundary (must stay green: characterisation)
  Scenario Outline: the largest representable literals still parse
    When the AEL source "<source>" is parsed
    Then it yields a <kind> literal of <value>
    Examples:
      | source                              | kind    | value                               |
      | 9223372036854775807                 | Int     | 9223372036854775807                 |
      | 79228162514264337593543950335.0     | Decimal | 79228162514264337593543950335       |

  # US2 — exponent
  Scenario Outline: exponent notation is refused by name, at the exponent marker
    When the AEL source "<source>" is tokenized
    Then an AelParseException is thrown at position <position> mentioning "exponent"
    Examples:
      | source               | position |
      | 1e30                 | 1        |
      | $.payload.v > 1E30   | 15       |
      | $.payload.v > 1e+30  | 15       |
      | $.payload.v > 1e-3   | 15       |
      | $.payload.v > 1.5e3  | 17       |
      | 1e                   | 1        |

  Scenario: an exponent in a predicate is a typed 400 naming the problem
    When they create a rule with predicate "$.payload.v > 1e30"
    Then the result is PredicateParseFailed at position 15 whose reason mentions "exponent"

  # Conflict — nothing new; the existing guards still apply first
  Scenario: a name clash is still 409 even when the predicate is also oversized
    Given a non-archived rule "r1" exists in "munich"
    When they create "r1" with predicate "$.payload.v > 99999999999999999999"
    Then the result is RuleNameTaken (409)

  # Must not regress (characterisation)
  Scenario Outline: inputs valid today stay valid
    When the AEL source "<source>" is parsed
    Then it parses without error
    Examples:
      | source                  |
      | 1contains "x"           |
      | $.payload.v1e3 > 1      |
      | $.payload.e30 == 2.5    |

  # Auth — unchanged by this spec, stated so nobody adds a check
  Scenario: a caller without sse.rules.write is refused before parsing
    When a caller holding only sse.rules.read POSTs the oversized predicate
    Then the response is 403 and nothing is parsed
```

The conflict scenario pins an ordering that already exists (`CreateRuleCommandHandler.cs:25-30`
checks the name before parsing) — characterisation, green today. The auth scenario is covered by
existing endpoint tests (`RequireAuthorization(Scope.Sse.Rules.Write)`, `RulesEndpoints.cs:37-39`) and
gets **no new test**.

## 5. The exponent decision — reject clearly, no ADR

**Decision: reject `e`/`E` directly after a numeric literal with a dedicated parse error. Do not add
exponent support.**

- ADR-0099's grammar says `literal := int | decimal | …` and has never included an exponent; the
  lexer matches it. Rejecting clearly **changes no grammar** — every input it affects is already
  refused today (§1.2); only the message and position move. That is within an implementing issue.
- Adding support **would** change the grammar ADR-0099 owns, and forces decisions nobody has made:
  what type `1e3` is (int or decimal?), what `1e30` is when `decimal` tops out at ~7.9e28 (so the
  motivating example would be *rejected anyway* as out of range), and what `1e-40` is (silent
  underflow to `0` — spec 203 item 5). That is an ADR-0099 amendment, and the autonomous lane may not
  write one (ADR-0144). There is also no demand: no issue asks for it, and PLC thresholds are written
  out in full.
- **If exponent support is ever wanted, it needs its own issue and an ADR-0099 amendment first.** This
  spec does not file it; nothing indicates it is wanted.

## 6. Functional requirements

- **FR-001** — `AelParser.Parse` throws `AelParseException`, never `OverflowException`, for an integer
  literal outside `long` or a decimal literal outside `decimal`. Position = the literal token's start.
  Message names the literal (its lexeme) and contains "out of range".
- **FR-002** — `AelLexer.Tokenize` throws `AelParseException` when a digit run (with or without a
  fraction) is immediately followed by `e` or `E`. Position = the index of that `e`/`E`. Message
  contains "exponent" and tells the author to write the number out in full.
- **FR-003** — `CreateRuleCommandHandler` is **not modified**. Its existing catches turn FR-001/FR-002
  into `PredicateParseFailed` / `ActionExpressionParseFailed` (400) with the positions above.
- **FR-004** — Every input that parses today still parses to the identical tree (the full existing
  `AelLexerTests`/`AelParserTests`/`AelInterpreterTests` suites pass **unmodified**).
- **FR-005** — No new exception type, error code, error variant, or `ApiError` subtype.

## 7. Colour and what red looks like (ADR-0139)

**Red.** New-behaviour tests, all failing today:

| Test | Red today because |
|---|---|
| Parser: out-of-range literal → `AelParseException` | `OverflowException` thrown instead (`Should.Throw<AelParseException>` fails on the wrong type). |
| Lexer: exponent → `AelParseException` mentioning "exponent" | `Tokenize` does not throw at all today (the error is raised later, by the parser). |
| Handler: oversized predicate / value expression → typed failure | `OverflowException` escapes `HandleAsync`. |
| Handler: exponent predicate → reason mentions "exponent" | Reason is "unexpected trailing token 'e30'". |
| Integration: POST oversized predicate → 400 `RULE_PREDICATE_PARSE_FAILED` | Answers 500. |

**Green on arrival (characterisation — must not be edited to pass):** the boundary rows
(`long.MaxValue`, `decimal.MaxValue`), the still-valid rows (`1contains "x"`, `$.payload.v1e3 > 1`,
`$.payload.e30 == 2.5`), and the name-clash-first ordering. If any of these arrives red, stop.

## 8. Independent end-to-end test procedure

1. Boot the stack (`AppHost`); mint an operator token with `sse.rules.write` for `munich`.
2. `POST /rules?fabId=munich` with `predicate = "$.payload.v > 999999999999999999999999999999999999"`,
   otherwise the body `RuleLifecycleIntegrationTests.CreateAsync` sends. **Before**: 500. **After**:
   400, problem title/code `RULE_PREDICATE_PARSE_FAILED`, detail naming position 14 and "out of range".
3. Same with `predicate = "$.payload.v > 1e30"`. **Before and after**: 400 — **after**, the detail
   says exponent notation is not supported, at position 15.
4. `GET /rules?fabId=munich` — neither rule exists.
5. The automated form of step 2 is the new fact in `RuleLifecycleIntegrationTests` (T005).

## 9. Spec number

Highest spec directory across `origin/develop`, every local and remote branch, and both other
worktrees, checked 2026-09-24 immediately before writing: **243**
(`243-the-level-the-wrapper-adds`). This is **244**. Re-check before the PR merges (unmerged branches
can claim the same number).
