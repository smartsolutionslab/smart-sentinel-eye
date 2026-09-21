# Tasks — Spec 203, one value, not the whole event

**Spec:** `specs/203-one-value-not-the-whole-event/spec.md` ·
**Plan:** `specs/203-one-value-not-the-whole-event/plan.md` ·
**Issue:** #2427 · **Branch:** `2427-oversized-json-number` · **Base:** `origin/develop` @ `740cc253`

**Phase-4a colour: RED for every task below.** Both stories change behaviour. A
test that arrives green is a phase-4 failure, not a shortcut (ADR-0139, CLAUDE.md
§*Phase 4a has two colours*). `test-writer` runs the tests, returns the
**verbatim** failure output, and that output is quoted in the PR body. The
engineer receives it as its brief and **may not edit the tests to pass.**

**No new ADR, no constitution amendment, no task issues.** Per CLAUDE.md §Workflow
the phase-3 gate is *the feature's issue on Project #13* — #2427 is already there,
status **Todo**, labelled `agent:ready`, no `agent:blocked` (verified at
dispatch). `/speckit-taskstoissues` is **not** to be run.

## Parallelism (ADR-0109)

| Group | Tasks | Why |
|---|---|---|
| A | T001, T003 | Disjoint files: `AelInterpreterTests.cs` + `AelFixtures.cs` vs `DryRunRuleQueryHandler`'s tests in `Queries/RuleQueryHandlerTests.cs`. Safe to run concurrently. |
| B | T002, T005 | **Serialised, not parallel.** Both append to `tests/Automation.Application.Tests/Evaluation/RuleEvaluatorTests.cs`. This shared file is the reason US1 and US2 live on one branch. |
| C | T004, T006, T007 | Implementation. T004 (`AelInterpreter.cs`), T006 (`RuleEvaluator.cs`), T007 (`DryRunRuleQueryHandler.cs`) own disjoint files and are `[P]` **once their red tests exist**. |

**No foundational blocker.** Nothing in `Shared.Kernel`, `Shared.Contracts`,
`AppHost`, or any Aspire resource changes, so there is no gate the rest of the
work waits behind. The only ordering constraints are red-before-green and the
one shared test file.

**Recommended dispatch:** T001 ∥ T003 → T002 → T004 ∥ T007 → T005 → T006 → T008.
US1 (T001, T002, T004) is independently shippable; if the slice has to narrow,
drop US2 and file it as its own issue rather than half-doing it.

---

## US1 (P1) — A number too big for `decimal` makes one field unaddressable, not the event unprocessable

### Phase 4a — RED (`test-writer`)

**[T001] [P] [US1] Interpreter-level tests for the oversized-number boundary**
`tests/Automation.Application.Tests/Ael/AelInterpreterTests.cs` (+ fixtures in
`tests/Automation.Application.Tests/Ael/AelFixtures.cs`)

Add, in the file's existing style (Shouldly, sentence-style names with
underscores, the private `Eval(source, contextJson)` helper at `:7-11`):

1. `A_number_beyond_decimals_range_is_not_addressable` — context
   `{"payload": {"v": 1e30}}` (**the exact value from #2427's repro**), expression
   `$.payload.v`, expect `AelValue.NullValue.Instance`.
2. `Every_number_outside_decimals_range_is_not_addressable` — `[Theory]`/
   `[InlineData]` over `1e30`, `-1e30`, `1e400`,
   `79228162514264337593543950336` (`decimal.MaxValue + 1`), and a 400-digit
   integer. Each → `NullValue`.
3. `A_number_at_decimals_maximum_is_still_a_decimal` —
   `79228162514264337593543950335` → `DecimalValue` with that exact value. **This
   is the guard against a fix that over-rejects**; without it, returning
   `NullValue` for every non-`long` number would pass tests 1 and 2.
4. `A_number_beyond_long_but_inside_decimal_is_a_decimal` —
   `9223372036854775808` (`long.MaxValue + 1`) → `DecimalValue`. Pins the
   `TryGetInt64` → `TryGetDecimal` ordering.
5. `A_number_beyond_decimals_range_compares_as_a_missing_field_does` —
   `$.payload.v == 42` with `v = 1e30` → `BoolValue(false)`, **no exception**.
   Assert the same expression against a context with no `v` at all gives the
   identical result; equivalence-to-absence is the semantic being locked in.

**Expected red:** `System.FormatException: One of the identified items was in an
invalid format.` from `JsonElement.GetDecimal()`, escaping
`AelInterpreter.Evaluate`. Tests 3 and 4 should pass already — say so in the
report; a task whose every test is red would mean the boundary cases are not
covered.

**[T002] [US1] Evaluator-level tests: one bad field, one skipped rule**
`tests/Automation.Application.Tests/Evaluation/RuleEvaluatorTests.cs`
**Blocks on nothing, but must not run concurrently with T005 — same file.**

Use the existing `ActiveRule(...)` / `BuildRule(...)` helpers (`:32-51`) and
`Context(json)` (`:53-54`). Replace `NullLogger<RuleEvaluator>.Instance` **in the
new tests only** with `Fakes.CapturingLogger<RuleEvaluator>` — the fake already
exists and was written for exactly this; do not add another.

1. `An_oversized_payload_number_skips_its_own_rule_and_no_other` — two active
   rules on `(munich, plc, PlcCycleStart)`: `"alarm"` with predicate
   `$.payload.v > 100`, `"healthy"` with the default predicate and action
   `SetVariableValue("oeeLine1","99")`. Context payload
   `{"v": 1e30, "cycleTime": 27}`. Assert **exactly one** effect,
   `SetVariableValue("oeeLine1","99")`. Then assert the captured log has one
   `Warning` entry whose `Message` names **`"alarm"`'s** `RuleIdentifier` and
   whose `Exception` is an `InvalidOperationException`. Asserting the surviving
   effect *and* the identity of the skipped rule is what separates this from the
   existing `Predicate_runtime_failure_on_one_rule_skips_just_that_rule`
   (`:202-224`), which uses a non-bool predicate already inside the filter.
2. `An_oversized_number_in_a_value_expression_writes_what_a_missing_field_writes`
   — one rule, predicate `$.payload.cycleTime <= 30`, action
   `SetVariableValue("x", "$.payload.v")`, payload `{"v": 1e30, "cycleTime": 27}`
   → one effect with `Value == ""`; and the same rule against
   `{"cycleTime": 27}` (no `v`) yields the identical effect. **Pins the
   consequence spec §*Fix direction* calls out**, so it is an observed decision.
3. `An_equality_test_against_an_oversized_number_logs_nothing` — predicate
   `$.payload.v == 42`, payload `{"v": 1e30}` → no effect **and zero log
   entries**. Nothing failed; the comparison is simply false.

**Expected red:** tests 1 and 2 fail with `System.FormatException` escaping
`RuleEvaluator.Evaluate` before any assertion runs; test 3 the same.

### Phase 4b — GREEN (`backend-engineer`)

**[T004] [P] [US1] Make `JsonElementToAelValue` total over `JsonValueKind.Number`**
`src/Automation/Application/Ael/AelInterpreter.cs:62-63`

Replace the single throwing arm with two:

```csharp
JsonValueKind.Number when element.TryGetDecimal(out decimal fraction) => new AelValue.DecimalValue(fraction),
JsonValueKind.Number => AelValue.NullValue.Instance,
```

- The explicit `Number` arm **must sit above the `_` discard**, not fold into it:
  the discard's comment says *"arrays / objects are not addressable values in
  v1"*, which is the wrong reason for this case (plan §*The change*).
- One short comment on the new arm: a JSON number outside `decimal`'s range is
  unaddressable rather than fatal, why (`GetDecimal()` throws `FormatException`),
  and `#2427`. Say *why*, not *what* (CLAUDE.md §*No drive-by comments*).
- **Nothing else in the file changes.** No logger, no new `AelValue` variant, no
  `Ensure.That`, no reformatting of untouched lines.
- Do not touch `RuleEvaluator` in this task — US1 is safe without it, and mixing
  the two makes the red evidence unattributable.

Done when T001 and T002 pass **unmodified**, `dotnet format` and the analyzers
are clean, and the Release build has no new warning (metrics are advisory,
ADR-0084, but collection-expression and NRT rules are not).

---

## US2 (P2) — The evaluator's promise survives an exception type nobody listed

### Phase 4a — RED (`test-writer`)

**[T003] [P] [US2] Dry-run returns a typed failure, not a 500, on arithmetic overflow**
`tests/Automation.Application.Tests/Queries/RuleQueryHandlerTests.cs` (the
dry-run cases already live here; there is no separate
`DryRunRuleQueryHandlerTests`)

Mirror the existing dry-run tests' arrangement.

1. `A_dry_run_whose_arithmetic_overflows_fails_with_a_typed_error` — a published
   rule with predicate `$.payload.big * 10 > 0`, sample
   `{"payload": {"big": 7.9e28}}`. Expect a failure `Result` carrying
   `DryRunRuleError.EvaluationFailed` (code `RULE_DRY_RUN_EVALUATION_FAILED`,
   `HttpStatusCode.BadRequest`).
2. `A_dry_run_that_matches_is_unchanged` — one existing-shaped happy-path
   assertion re-stated here, so the widened filter is shown not to have turned a
   match into a failure. This one arrives **green**; report it as such.

**Expected red on 1:** `System.OverflowException` escaping the handler.

**[T005] [US2] Evaluator tests for the overflow class**
`tests/Automation.Application.Tests/Evaluation/RuleEvaluatorTests.cs`
**Runs after T002 — same file, serialise.** Same `CapturingLogger<RuleEvaluator>`.

1. `A_decimal_overflow_in_a_predicate_skips_its_own_rule_and_no_other` — rules
   `"mul"` (predicate `$.payload.big * 10 > 0`) and `"healthy"`; payload
   `{"big": 7.9e28, "cycleTime": 27}`. Exactly one effect
   (`SetVariableValue("oeeLine1","99")`); one `Warning` naming `"mul"` with an
   `OverflowException`. **`7.9e28` is inside `decimal`'s range** — the point of
   this test is that US1 does not and cannot fix it.
2. `An_integer_division_overflow_in_a_predicate_skips_its_own_rule` — predicate
   `$.payload.tiny / -1 > 0`, payload `{"tiny": -9223372036854775808}`. No
   effect, no escaping exception, one `Warning` with an `OverflowException` from
   `DivideInt`. No large number is involved at all.
3. `A_decimal_overflow_in_a_value_expression_skips_the_action_not_the_event` —
   predicate `$.payload.cycleTime <= 30`, action
   `SetVariableValue("x", "$.payload.big * 10")`, payload
   `{"big": 7.9e28, "cycleTime": 27}` → no effect, one `Warning` whose message is
   the *value-expression* one (`"…skipping action."`), not the predicate one.
4. `A_cancellation_is_not_absorbed_as_a_rule_failure` — drive an
   `OperationCanceledException` through the guarded call (a `CompiledRule` whose
   expression throws it, or the narrowest seam available) and assert it
   **propagates** and logs nothing. Proves the carve-out by counterfactual
   rather than by reading the filter. If no seam exists without changing
   production code, say so in the report and drop the test — **do not** add a
   production hook to make it reachable.

**Expected red on 1-3:** `System.OverflowException` escaping
`RuleEvaluator.Evaluate`. Test 4 arrives green against today's narrow filter
(`OperationCanceledException` is outside it too) — report that honestly; its
value is as a **regression guard on the widening**, which is exactly why it must
be written before T006 and must still pass after.

### Phase 4b — GREEN (`backend-engineer`)

**[T006] [P] [US2] Stop enumerating in `RuleEvaluator`**
`src/Automation/Application/Evaluation/RuleEvaluator.cs:78` and `:100`

Both filters become:

```csharp
catch (Exception exception) when (exception is not OperationCanceledException)
```

- Keep both bodies exactly as they are — the existing `[LoggerMessage]` calls are
  the observability deliverable (spec §*Observability*); add no new log
  definition.
- Add a comment above **one** of the two (not both — CLAUDE.md §*No drive-by
  comments*) recording the reasoning from plan §*Why the filter stops
  enumerating*, compressed: the enumerated list missed `FormatException` (#2427)
  and `OverflowException`, the guarded call is one pure synchronous function over
  in-memory data, nothing is swallowed because every exception is logged with its
  rule, and `OperationCanceledException` is the one absorption that is never
  correct.
- The class doc comment at `:15-20` claims *"one bad rule does not stop the
  loop"*. It is now true. **Leave the words alone** unless they are wrong —
  rewriting a correct comment is churn.
- **No `ILogger` is added to `AelInterpreter`.** Out of scope and argued in the
  spec; a reviewer should bounce it.

**[T007] [P] [US2] Stop enumerating in the dry run**
`src/Automation/Application/Queries/Handlers/DryRunRuleQueryHandler.cs:124`

Same filter. `AelParseException` drops out of the list and stays caught (it
derives from `Exception`). The body is unchanged —
`DryRunRuleFailures.EvaluationFailed(ex.Message)` → 400
`RULE_DRY_RUN_EVALUATION_FAILED`. The justification belongs at this site and is
one line: the comment ten lines above (`:106-108`) says a dry run that disagreed
with the live pipeline would be worse than no dry run, and US1 + T006 changed
what the live pipeline does.

Done when T003 and T005 pass **unmodified**, plus the full
`Automation.Application.Tests` suite and `AelInterpreterBenchmarkTests`.

---

## Phase 5 — Verify (`/verify`)

**[T008] Observe it end to end against the real Aspire stack**

Execute spec §*Independent end-to-end test procedure* in full and write
`specs/203-one-value-not-the-whole-event/verification.md`. Non-negotiable
contents:

- The dead-letter depth **before and after**, and after **three** repeats of the
  same event — the issue's severity claim is *sustained* outage, so surviving it
  once proves less than the claim.
- `spec203Healthy == "99"` read back from `GET /system-variables/…`, quoted. This
  is the observable that is **false on `develop` today**; the note must say so
  explicitly, because a value that was always there proves nothing.
- The `Warning` line from the Aspire dashboard, with its `TraceId`, and the
  `FabEventIngestedV1` span that `TraceId` resolves to.
- The dry-run response body: 400 `RULE_DRY_RUN_EVALUATION_FAILED`.
- **`AelInterpreterBenchmarkTests` median, from two Release runs** (constitution
  §IV, *Event → overlay state ≤ 200 ms*; memory: *measurement runs need
  repeating* — the first run after machine churn reads exactly like a
  regression). Compare against the recorded 20.7 / 26.7 / 25.4 / 23.4 ms and the
  500 ms gate.

Operational reminders, each of which has cost this repo a session before: one
Aspire stack per machine — stop any running one first; stop the stack before
building or MSB3027 reads as a broken build; mint tokens from Aspire's **proxied**
Keycloak endpoint, not the container's mapped port; create the event and look,
do not hunt trace history (`list_traces` ignores `search`).

## Phase 6 — QA

**[T009] `/code-review`** against plan §*Review focus for phase 6*. The
deliberate question is item 1 — the shape of the widened filter. Raise it there,
not after merge.

**[T010] `/security-review` — not required.** No auth, scope, token, secret,
trust-boundary, or authorization path is touched; the dry-run endpoint's
authentication and `RequireScope` are unchanged. Record the skip with its reason
in the PR body rather than running it for form.

## Phase 7 — PR

**[T011] Open the PR** — `gh pr create --base develop` (mandatory flag; CLAUDE.md
§Branching). Body must carry:

- `Closes #2427` — a closing keyword, and **check the issue state after the
  merge**; a bare mention closes it roughly one time in three.
- The **verbatim red output** from T001/T002/T003/T005 (ADR-0139). This is the
  only form of the phase-4 evidence a later reader can check.
- The phase-5 figures from T008, including both benchmark medians.
- `Phase 6: /security-review skipped — no auth or trust-boundary surface.`
- The **out-of-scope findings** from spec §*Out of scope* items 1-4, named, so
  the follow-up issues can be filed against a written record rather than
  rediscovered.
- No `Co-Authored-By` footer (ADR-0086); the Claude Code line stays.

Each commit must build **on its own** — rebase-merge lands them individually and
a commit that compiles only with its successor breaks `git bisect` forever
(ADR-0087). Verify per commit, not per branch. Conventional Commits (ADR-0030).

**[T012] Follow-up issues to file** (not part of this PR's diff):

1. `BuildContext` dead-letters on a payload nested exactly 64 deep, and on any
   `FabEventIngestedV1.Payload` that is not valid JSON text — spec §*Out of
   scope* 1-3, including the undisposed `JsonDocument` at
   `FabEventIngestedV1Handler.cs:145`. One narrow `try` around `BuildContext`
   closes all three. **Same blast radius as #2427; reproduced.**
2. A numeric literal wider than `long`/`decimal` in a **rule's own source**
   returns 500 instead of a typed `PredicateParseFailed` 400 —
   `AelLexer.ParseInt`/`ParseDecimal` (`:163-167`) vs
   `CreateRuleCommandHandler` (`:38, :52`). Operator-input boundary, lower
   severity. Mention the lexer's missing exponent support (`:109-132`) in the
   same issue.
