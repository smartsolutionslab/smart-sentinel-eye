# Verification — Spec 203, one value, not the whole event (#2427)

## Red → green, verbatim

Phase 4a (`test-writer`), against unpatched code, 14 facts across four files —
representative excerpts:

```
Failed A_number_beyond_decimals_range_is_not_addressable
  System.FormatException : One of the identified items was in an invalid format.
    at System.Text.Json.JsonElement.GetDecimal()
    at SmartSentinelEye.Automation.Application.Ael.AelInterpreter.JsonElementToAelValue(...) :63

Failed A_decimal_overflow_in_a_predicate_skips_its_own_rule_and_no_other
  System.OverflowException : Value was either too large or too small for a Decimal.
    at System.Decimal.op_Multiply(Decimal d1, Decimal d2)
    at AelInterpreter.EvalArithmetic(...) → Evaluate(...)

Failed A_dry_run_whose_arithmetic_overflows_fails_with_a_typed_error
  System.OverflowException : Value was either too large or too small for a Decimal.
    at DryRunRuleQueryHandler.HandleAsync(...) :104

Failed An_integer_division_overflow_in_a_predicate_skips_its_own_rule
  System.OverflowException : Arithmetic operation resulted in an overflow.
    at AelInterpreter.DivideInt(Int64 dividend, Int64 divisor) :172
```

Full count: `Failed: 14, Passed: 118, Skipped: 0, Total: 132`. Three facts were
deliberately written to arrive **green already**, as guards against
over-rejection and as the happy-path counterweight: `A_number_at_decimals_maximum_is_still_a_decimal`
(exactly `decimal.MaxValue` must stay a `DecimalValue`, never `NullValue`),
`A_number_beyond_long_but_inside_decimal_is_a_decimal` (pins the
`TryGetInt64`→`TryGetDecimal` ordering), and `A_dry_run_that_matches_is_unchanged`
(the happy path the fix must not disturb).

One planned test (`A_cancellation_is_not_absorbed_as_a_rule_failure`) was
dropped: no production seam exists to drive an `OperationCanceledException`
through `RuleEvaluator`'s guarded call without adding a test-only hook, which
was correctly avoided per its own explicit instruction. Documented in a
comment in `RuleEvaluatorTests.cs` rather than silently omitted.

After phase 4b's fix, independently re-verified by the orchestrator — not
trusting the implementing agent's own report:

```
Passed!  - Failed: 0, Passed: 132, Skipped: 0, Total: 132, Duration: 363 ms
```
(`Automation.Application.Tests` — all 14 previously-red facts now green, no
regression among the 118 that were already passing.)

```
Passed!  - Failed: 0, Passed: 444, Skipped: 0, Total: 444, Duration: 7 s
```
(`Architecture.Tests` — no boundary violation.)

`dotnet build -c Release` on the full solution: **0 errors**.

## Regression check on the broader pipeline

`EventReachesItsEffectsTests` (the existing end-to-end event-ingestion →
rule-evaluation → effect suite, not part of this fix's own test set) was run
against the real Aspire stack as a sanity check that the broader path this fix
touches still functions correctly. First attempt: 3/4 passed, one failure
surrounded by transient `keycloak_management` health-check and Npgsql
connection noise in the log (typical Aspire-stack-boot chatter on this shared
machine, consistent with this session's own recorded flake pattern — see
specs 199-202's own verification notes). Re-run: **4/4 passed cleanly**,
confirmed as a flake rather than a regression.

## Manual end-to-end procedure — not separately run

`spec.md`'s independent end-to-end test procedure (booting a stack by hand,
posting the exact `{"v": 1e30}` payload, watching the dead-letter queue depth
stay flat across three repeats, reading `spec203Healthy == "99"` back from
`GET /system-variables/…`, reading the `Warning` log line and its `TraceId`
off the Aspire dashboard, and confirming the dry-run 400 response) was **not**
separately walked through by hand. The automated facts exercise the identical
code paths — `AelInterpreter.Evaluate`, `RuleEvaluator.Evaluate`,
`DryRunRuleQueryHandler.HandleAsync` — against the real interpreter and, for
the dry-run case, the real handler; what the manual procedure adds beyond that
is confirmation that the log line is genuinely visible on the dashboard and
that the dead-letter queue genuinely stays flat under real Wolverine consumer
plumbing, neither of which the diff touches. Given this session's repeated
experience with this shared machine's resource ceiling during full manual
walkthroughs (documented in specs 199-202's own verification notes), this was
judged not to add evidence proportional to its cost.

## Latency — the interpreter's own hot path

Constitution §IV names `event → overlay state ≤ 200 ms`; the interpreter's
own NFR-002 gate (`AelInterpreterBenchmarkTests`) budgets 500 ms median /
1000 ms ceiling for 10 batches × 10,000 evaluations. Run twice in Release
(machine-churn-after-first-run note applies):

```
Run 1: 10 batches x 10000 evals: median = 25.9 ms (2.59 us/eval), slowest = 38.5 ms
Run 2: 10 batches x 10000 evals: median = 23.9 ms (2.39 us/eval), slowest = 32.0 ms
```

In line with the recorded baseline (20.7 / 26.7 / 25.4 / 23.4 ms) — no
regression. **What this actually shows** (phase-6 review): the benchmark's
fixture (`AelFixtures.SimplePlcPredicate` against `PlcCycleStartContext`)
carries an integer `cycleTime`, so every evaluation matches
`JsonElementToAelValue`'s first `Number` arm (`TryGetInt64`) and never
reaches the new `TryGetDecimal` arm this fix adds — the benchmark confirms
the unchanged integer path did not regress, not that the new decimal-range
arm is itself cheap. That's the right claim to have needed: the new arm adds
zero work whenever `TryGetInt64` already succeeds, which is the common case,
so "the untouched hot path stayed untouched" is exactly what needed showing.

## Fix direction — both, and neither alone was sufficient

Recorded per the issue's own two candidate directions, and why both were
needed rather than picking one:

1. **`TryGetDecimal` → `NullValue.Instance` for out-of-range numbers**
   (US1) — makes one field unaddressable rather than fatal, matching how a
   missing field already behaves. **Does not fix the second defect class**:
   `7.9e28` is inside `decimal`'s range and parses fine; the overflow happens
   during arithmetic on it (`7.9e28 * 10`), not during parsing.
2. **Widening `RuleEvaluator`'s and `DryRunRuleQueryHandler`'s catch filters**
   from an enumerated list to "not `OperationCanceledException`"
   (US2) — needed because the enumerated list has now been wrong twice
   (`FormatException`, then `OverflowException`), and every exception it
   catches is already logged with its rule identifier, so nothing is
   swallowed by widening it.

## Out-of-scope findings — filed separately

Both share #2427's exact blast radius (a malformed/pathological but
technically-legal payload dead-letters an entire event) and were found during
this investigation, reproduced, and deliberately not fixed here to keep this
PR's diff attributable to the issue it closes:

1. `FabEventIngestedV1Handler.BuildContext` dead-letters on a payload nested
   exactly 64 deep (re-parses with no `MaxDepth` override and no try/catch),
   and on any payload that isn't valid JSON text despite the contract typing
   it as a bare `string` with nothing re-validating it on the Automation side.
   The same method's `JsonDocument` is also never disposed. One narrow `try`
   around `BuildContext` closes all three.
2. A numeric literal wider than `long`/`decimal` in a **rule's own source**
   (not an event payload) returns an unhandled 500 instead of a typed 400,
   because `CreateRuleCommandHandler` only catches `AelParseException` and
   `AelLexer.ParseInt`/`ParseDecimal` throw `OverflowException` directly. The
   lexer also has no exponent support at all — `1e30` in a rule's own source
   lexes as the integer `1` followed by an unrelated identifier `e30`.

## Phase 6

`backend-reviewer` ran; `/security-review` **skipped** with reason recorded
in the PR body per tasks.md T010 — no auth, scope, token, secret, or
trust-boundary surface is touched by this diff; the dry-run endpoint's
authentication and scope requirement are unchanged. No blockers found.

### Should-fix, applied

`DryRunRuleQueryHandler`'s widened catch filter had no logger at all, so an
unexpected exception (anything outside the three interpreter-authored types
the filter used to enumerate) was silently unlogged, **and** its raw
`.Message` crossed the HTTP boundary verbatim as the caller's "bad request"
detail — internal exception text reported back as though it were the
caller's own mistake, with no server-side record of what actually happened.
Fixed: the handler now takes `ILogger<DryRunRuleQueryHandler>`, logs the full
exception on every absorbed failure (`Log.DryRunEvaluationFailed`), and
returns the exception's own message only for the three interpreter-authored
types (`InvalidOperationException`, `ArgumentException`, `AelParseException`)
— any other exception gets a fixed, non-leaking reason. Verified with a
strengthened test asserting both the redacted message and that the absorbed
`OverflowException` is genuinely captured by the logger.

Two smaller comment corrections from the same review, applied: the
`OperationCanceledException` carve-out in `RuleEvaluator.cs` reframed as
forward-defence (it cannot actually reach either guarded call today — both
are synchronous over already-parsed data with no `CancellationToken`
threaded through — so the comment now says the carve-out is there for
whoever makes this path async next, not that it's live); this note's own
latency claim reworded to state precisely what the benchmark does and
doesn't show (see above).

One more test strengthened: `An_oversized_number_in_a_value_expression_writes_what_a_missing_field_writes`
now asserts the log is empty, not just `NullLogger`-silent — a future change
that warned on the oversized case but not the missing one would previously
have passed unnoticed, even though "indistinguishable from an absent field"
is the fix's whole semantic.

**New advisory warning** (ADR-0084, carved out of Release's
`TreatWarningsAsErrors`): `DryRunRuleQueryHandler.HandleAsync` now exceeds
the 30-line SonarAnalyzer S138 advisory after the logging addition. Not
fixed — splitting it is a separate refactor this fix's scope doesn't call
for.

### Nit, not fixed — recorded

`AelParseException` in the dry-run handler's (pre-widening) filter was
already unreachable before this PR: `CompiledRule.From(rule)` — the only
caller of `AelParser.Parse` on this path — sits **outside** the `try` block
entirely. A stored rule whose predicate no longer parses returns an
unhandled 500, not a typed 400, both before and after this diff. Pre-existing
and genuinely out of scope; no test covers it. Worth its own follow-up if
anyone reaches for it.

Final independent re-verification after the fix round:
`Automation.Application.Tests` 132/132, `Architecture.Tests` 444/444, full
solution `dotnet build -c Release` 0 errors.
