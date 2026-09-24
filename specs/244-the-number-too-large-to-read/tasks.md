# Tasks 244 — The number too large to read

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Issue**: #2497 · **Phase**: 3 (Tasks)
**Colour**: **red** for T001–T005's new-behaviour facts; the boundary, still-valid and name-clash
facts are characterisation (green on arrival, never edited).
**Engineer**: `test-writer` (4a) then `backend-engineer` (4b) · **Reviewer**: `backend-reviewer`
**Tracking**: feature-level issue #2497 (already `agent:ready` on Project #13). No per-task issues.

Format: `[ID] [P?] [Story] description`. **No foundational work** (no Shared.Kernel / Contracts /
AppHost / Aspire resource). `[P]` = disjoint files (ADR-0109).

## Phase 4a — tests, red first (test-writer)

- [ ] **T001 [P] [US1]** `tests/Automation.Application.Tests/Ael/AelParserTests.cs`:
  - Theory `An_out_of_range_numeric_literal_is_a_parse_error_at_its_position` — rows
    (`9223372036854775808`, 0), (`$.payload.v > 99999999999999999999`, 14),
    (`$.payload.v > 99999999999999999999999999999.5`, 14), (`1 + 79228162514264337593543950336.0`, 4).
    Assert `Should.Throw<AelParseException>`, `.Position` equals the row, message contains
    "out of range". **Red** (throws `OverflowException`).
  - Theory `The_largest_representable_literals_still_parse` — `9223372036854775807` → `IntValue`
    `long.MaxValue`; `79228162514264337593543950335.0` → `DecimalValue` `decimal.MaxValue`. **Green.**
  - Theory `Sources_valid_today_with_letters_after_digits_still_parse` — `1contains "x"`,
    `$.payload.v1e3 > 1`, `$.payload.e30 == 2.5`. **Green.**
- [ ] **T002 [P] [US2]** `tests/Automation.Application.Tests/Ael/AelLexerTests.cs`: Theory
  `Exponent_notation_is_refused_at_the_exponent_marker` — rows (`1e30`, 1), (`$.payload.v > 1E30`, 15),
  (`$.payload.v > 1e+30`, 15), (`$.payload.v > 1e-3`, 15), (`$.payload.v > 1.5e3`, 17), (`1e`, 1).
  Call `AelLexer.Tokenize` (not the parser); assert `AelParseException`, `.Position`, message contains
  "exponent". **Red** (`Tokenize` does not throw today).
- [ ] **T003 [P] [US1, US2]** `tests/Automation.Application.Tests/Commands/CreateRuleCommandHandlerTests.cs`
  (reuse `HappyCommand`):
  - `An_oversized_literal_in_the_predicate_is_PredicateParseFailed_at_its_position` — predicate
    `$.payload.v > 999999999999999999999999999999999999`; `PredicateParseFailed`, `Position == 14`,
    `repo.Rules` empty. **Red.**
  - `An_oversized_literal_in_the_value_expression_is_ActionExpressionParseFailed` — value expression
    `99999999999999999999`; `ActionExpressionParseFailed`, `Position == 0`, repo empty. **Red.**
  - `An_exponent_in_the_predicate_is_refused_by_name` — `$.payload.v > 1e30`; `PredicateParseFailed`,
    `Position == 15`, `Reason` contains "exponent". **Red** (reason is "unexpected trailing token").
  - `A_name_clash_is_reported_before_an_oversized_predicate` — create once validly, then same name with
    `$.payload.v > 99999999999999999999`; `RuleNameTaken`. **Green.**
- [ ] **T004 [P] [US1]** `tests/Integration.Tests/Automation/RuleLifecycleIntegrationTests.cs`: give
  the private `CreateAsync` an optional `predicate` parameter defaulting to today's
  `"$.payload.cycleTime <= 30"`; add `[Fact] An_oversized_literal_in_a_predicate_is_a_400_not_a_500`
  — POST `$.payload.v > 999999999999999999999999999999999999`; assert `400` with `DiagnoseAsync`, and
  the problem body names `RULE_PREDICATE_PARSE_FAILED`. **Red (500).** Existing class only — no shard
  filter change. If the stack cannot be booted locally, say so and let CI show the red.
- [ ] **T005 [US1, US2]** Run `dotnet test tests/Automation.Application.Tests` and capture the output
  **verbatim**. Required pattern: exactly the red facts listed above fail, with `OverflowException`
  (T001, T003 oversized rows), "no exception thrown" (T002), wrong reason (T003 exponent); every
  green-marked fact and every pre-existing fact passes. Any other pattern → stop and report. Commit
  `test(automation): pin out-of-range and exponent literals as typed parse failures, red first`.

Depends: T001–T004 in parallel (disjoint files) → T005.

## Phase 4b — implement (backend-engineer; may not edit any test)

- [ ] **T006 [US1]** `src/Automation/Application/Ael/AelLexer.cs`: `ParseInt` / `ParseDecimal` take
  `AelToken`, use `TryParse` with unchanged `NumberStyles`/`InvariantCulture`, throw
  `AelParseException` (lexeme in message, "out of range", `token.Position`) on failure (plan D1).
- [ ] **T007 [US1]** `src/Automation/Application/Ael/AelParser.cs:143, :148`: pass `token`.
- [ ] **T008 [US2]** `src/Automation/Application/Ael/AelLexer.cs` number arm: reject `e`/`E`
  immediately after the literal, position = that index (plan D2). `e`/`E` only.
- [ ] **T009** Run `dotnet test tests/Automation.Application.Tests` and `tests/Architecture.Tests` —
  all green; confirm the existing `AelLexerTests`/`AelParserTests`/`AelInterpreterTests` bodies are
  unmodified. Release build clean (analyzers). Commit
  `fix(automation): report out-of-range and exponent literals as AEL parse errors` (T006–T008).

Depends: T005 → T006 → T007 → T008 → T009 (T006 and T008 share `AelLexer.cs`; no `[P]`).
`CreateRuleCommandHandler.cs` is **not** in the change set (FR-003) — a diff there is a review finding.

## Phase 5 — verify

- [ ] **T010** `AelInterpreterBenchmarkTests` twice in Release (median vs spec 203's 23.9–25.9 ms);
  the integration fact green; spec §8 steps 2–4 against a live stack if one can be booted. Write
  `verification.md` with every figure as observed.
