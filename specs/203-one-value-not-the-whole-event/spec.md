# Spec 203 — One value, not the whole event

**Issue:** #2427 — *An oversized JSON number in an event payload throws `FormatException` out of the interpreter and dead-letters the whole event, silently losing every rule's effect for it*
**Branch:** `2427-oversized-json-number`
**Phase-4a colour:** **RED** (behaviour-changing) — a test that arrives green is a phase-4 failure (ADR-0139, CLAUDE.md §House rules).
**ADRs:** **ADR-0099** (hand-rolled AEL — the component being repaired, the source of the "sandboxed by construction" requirement this fix discharges, and of the NFR-002 ≤ 10 µs p99/eval budget the fix must not erode), ADR-0042 + ADR-0088 (Wolverine — why an escaping exception dead-letters), ADR-0050 (`ILogger<T>` + `[LoggerMessage]` + OTel, the observability decision below), ADR-0047 + ADR-0089 (`Result<T, Error>` / `ApiError` — the dry-run failure path), ADR-0093 (Application layout), ADR-0052 / ADR-0053 / ADR-0054 (xUnit + Shouldly, sentence-style test names, hand-written builders), ADR-0065 (coverage gates), ADR-0105 (`Ensure.That`), ADR-0139 (new behaviour starts red), ADR-0144 (autonomous lane), ADR-0109 (parallel markers), ADR-0037 (phases).
**Constitution:** §IV (latency budget — *Event → overlay state ≤ 200 ms*), §Testing (red for new behaviour).

**Severity: a sustained, silent outage of automation for one trigger, caused by one legal JSON value from one misconfigured device.** Not a 500 on one request — the message dead-letters, every rule's effect for that event is lost, and the only signal is a DLQ depth nobody is alerted on. If the emitting gateway keeps sending the field, it recurs on every event.

**No new ADR is required.** ADR-0099 already requires AEL to be *"sandboxed by construction"* (`docs/adr/0099-hand-rolled-ael.md:31-33`) and to live behind a typed value union. Making the interpreter **total over legal JSON input** is the discharge of that existing requirement, not an amendment to it. Nothing about the grammar, the value union, the layering, or any context boundary changes.

## Problem

Every line number below was re-read in the working tree at HEAD `740cc253`, not
copied from the issue, and every behavioural claim below was **reproduced
locally** (transcripts in §*Reproduced*), not inferred.

### The filed defect

`src/Automation/Application/Ael/AelInterpreter.cs:56-66`:

```csharp
private static AelValue JsonElementToAelValue(JsonElement element) =>
    element.ValueKind switch
    {
        JsonValueKind.True => new AelValue.BoolValue(true),
        JsonValueKind.False => new AelValue.BoolValue(false),
        JsonValueKind.String => new AelValue.StringValue(element.GetString() ?? string.Empty),
        JsonValueKind.Number when element.TryGetInt64(out long integer) => new AelValue.IntValue(integer),
        JsonValueKind.Number => new AelValue.DecimalValue(element.GetDecimal()),   // <-- :63
        JsonValueKind.Null => AelValue.NullValue.Instance,
        _ => AelValue.NullValue.Instance, // arrays / objects are not addressable values in v1
    };
```

`:63` is the **only arm in the switch that calls a throwing accessor.** Every
other arm either constructs from a total conversion or falls to
`NullValue.Instance`. `JsonElement.GetDecimal()` throws `FormatException` for
any JSON number outside `decimal`'s range.

Nothing upstream excludes such a number. `Payload` validates exactly two things
— *parses as JSON* and *≤ 64 KB canonical UTF-8*
(`src/EventIngestion/Domain/Event/Payload.cs:17, 32-37, 62-81`) — and its own doc
comment states the position deliberately: *"Schema is the producer's contract
with downstream consumers; EventIngestion does not look inside"* (`:11-13`).
`1e30` is legal JSON, 10 bytes, and passes.

### Why the whole event is lost, not one rule

`RuleEvaluator`'s two guards enumerate the exceptions they absorb
(`src/Automation/Application/Evaluation/RuleEvaluator.cs:78` and `:100`):

```csharp
catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
```

`FormatException` derives from `SystemException`, not from either — verified by
reflection, transcript below. So it escapes `TryEvaluatePredicate` → escapes the
`foreach` in `RuleEvaluator.Evaluate` (`:41-67`) → escapes
`FabEventIngestedV1Handler.Handle`, whose only `try` covers `FabIdentifier.From`
(`src/Automation/Application/EventHandlers/FabEventIngestedV1Handler.cs:64-78`)
and *not* the `evaluator.Evaluate` call at `:81-82`. Wolverine dead-letters it.

This is the gap between what the class promises and what it does.
`RuleEvaluator`'s own doc comment (`:15-20`) says:

> Runtime errors during predicate / action evaluation are logged and skipped per
> rule — **one bad rule does not stop the loop**.

The promise is enforced by a **list of two exception types**. The list has now
been observed to be wrong twice (§*A second, independent instance* below), and a
list that has drifted twice will drift a third time.

### A second, independent instance of the same defect — `OverflowException`

Found while checking whether the issue's fix direction 1 is sufficient. It is
not, because **`OverflowException` reaches the same escape hatch from an
entirely in-range payload**, and no change to `JsonElementToAelValue` touches it.

`AelInterpreter.Arithmetic` (`:183-193`) applies the caller's
`Func<decimal, decimal, decimal>` with no overflow handling. `decimal`
arithmetic throws `OverflowException` on overflow **regardless of
`checked`/`unchecked`**, so:

| Rule fragment | Payload | Result today |
|---|---|---|
| `$.payload.big * 10` | `{"big": 7.9e28}` | `OverflowException` → **event dead-lettered** |
| `$.payload.big + $.payload.big` | `{"big": 7.9e28}` | `OverflowException` → **event dead-lettered** |
| `$.payload.tiny / -1` | `{"tiny": -9223372036854775808}` | `OverflowException` from `DivideInt` (`:171-172`) → **event dead-lettered** |
| `$.payload.tiny % -1` | `{"tiny": -9223372036854775808}` | `OverflowException` from `ModuloInt` (`:177-178`) → **event dead-lettered** |

`7.9e28` is *inside* `decimal`'s range, so the issue's fix direction 1 lets it
through as a `DecimalValue` exactly as it does today, and the multiplication
still throws. The `long.MinValue / -1` row needs no large number at all — it is
the single arithmetic identity the CLR cannot represent, and `DivideInt`'s guard
checks only the divisor for zero.

This matters for the fix decision: **direction 1 alone leaves a live
dead-letter path open**, and it is not the path the issue reported.

### A third instance, in the operator-facing dry run

`DryRunRuleQueryHandler.cs:124` carries a **third** copy of the enumerated
filter (the same two types plus `AelParseException`). Its own comment, ten lines
above at `:106-108`, states the invariant the narrow filter breaks:

> A dry run that disagreed with the live pipeline would be worse than no dry run
> at all.

Today both sides fail, differently: live dead-letters, dry run 500s. After US1
and US2 the live path logs-and-skips, so unless the dry run's filter is widened
in the same change it would 500 on exactly the input the live path now handles
— a *new* disagreement introduced by this fix. Widening it here is therefore
required by that stated invariant, not a drive-by.

### Reproduced

Run locally against .NET 10 (`dotnet run`, transcript verbatim; a throwaway
console project, not committed):

```
--- issue repro 1e30: {"v": 1e30}
    Kind=Number TryInt64=False (i=0)
    TryGetDecimal=False (dec=0)
    GetDecimal() THREW System.FormatException: One of the identified items was in an invalid format.
--- negative 1e30: {"v": -1e30}
    GetDecimal() THREW System.FormatException: ...
--- 1e400 (double inf): {"v": 1e400}
    GetDecimal() THREW System.FormatException: ...
--- many digits: {"v": 999999999999999999999999999999999999...(400 nines)}
    GetDecimal() THREW System.FormatException: ...
--- in range 7.9e28: {"v": 7.9e28}
    TryGetDecimal=True (dec=79000000000000000000000000000)
--- exactly decimal max: {"v": 79228162514264337593543950335}
    TryGetDecimal=True (dec=79228162514264337593543950335)
--- decimal max +1: {"v": 79228162514264337593543950336}
    GetDecimal() THREW System.FormatException: ...
--- long+1: {"v": 9223372036854775808}
    Kind=Number TryInt64=False  TryGetDecimal=True (dec=9223372036854775808)
```

The boundary is `decimal`'s exact range, not a loose "~7.9e28": `decimal.MaxValue`
itself is fine, `+1` throws. `TryGetDecimal` returns `false` for every throwing
case, so it is a complete, allocation-free substitute.

Arithmetic, driven through `Func<>` delegates so the compiler cannot constant-fold:

```
decimal 7.9e28 * 10                        THREW System.OverflowException
decimal 7.9e28 + 7.9e28                    THREW System.OverflowException
decimal.MaxValue + 1                       THREW System.OverflowException
long.MaxValue + 1 (interpreter path)       = -9223372036854775808     <- wraps, no throw
long.MinValue / -1 (interpreter path)      THREW System.OverflowException
long.MinValue % -1 (interpreter path)      THREW System.OverflowException
unary negate long.MinValue                 = -9223372036854775808     <- wraps, no throw

FormatException   : System.SystemException   (IsA ArgumentException=False, IsA InvalidOperationException=False)
OverflowException : System.ArithmeticException -> System.SystemException (IsA ArgumentException=False)
```

Both new classes sit outside both enumerated filters. Confirmed, not assumed.

### Why no existing test could have caught this

`tests/Automation.Application.Tests/Ael/AelInterpreterTests.cs` has no number
outside `long`/`decimal` range anywhere — the only numeric fixtures are `27`,
`1 + 2 * 3`, `1 + 2.5`, `42`. `AelFixtures.cs:36-52` carries two payload
contexts, both small integers.
`RuleEvaluatorTests.Predicate_runtime_failure_on_one_rule_skips_just_that_rule`
(`:202-224`) exercises the "one bad rule" promise — but with
`predicate: "1 + $.payload.cycleTime"`, a **non-bool result**, which throws
`InvalidOperationException` and is therefore *inside* the enumerated filter. The
test proves the promise holds for the exceptions the filter already lists, which
is precisely the case that was never in doubt.

## Fix direction — the decision, and why neither candidate alone is enough

The issue offers two and leaves the choice open. Both are taken, in priority
order, because each covers a failure the other does not.

### Direction 1 (US1) — the interpreter fails closed per value

`element.TryGetDecimal(out decimal d) ? new AelValue.DecimalValue(d) : AelValue.NullValue.Instance`.

**This is not a new semantic.** `NullValue.Instance` is already the interpreter's
sentinel for *"a legal JSON thing that AEL v1 cannot address as a value"* — it
is returned for a missing object (`:43`), a missing property (`:48`), a JSON
`null` (`:64`), and, with the comment *"arrays / objects are not addressable
values in v1"*, for every array and object (`:65`). A number too wide for
`decimal` joins a category that already exists.

**Every downstream path was traced; none of them throws anything new.** Read
against `AelInterpreter.cs` and `AelValue.cs` at HEAD:

| Where a `NullValue` lands | What happens | Net effect |
|---|---|---|
| `Compare` (`<`,`<=`,`>`,`>=`) `:195-200` | `ToDecimal` `:210` throws `InvalidOperationException("cannot coerce NullValue to a number")` | **already inside** the existing filter → logged, rule skipped |
| `Arithmetic` `:183-193` | same `ToDecimal` throw | already inside the filter |
| `AreEqual` (`==`, `!=`) `:214-225` | falls to `_ => false`, **no throw** | `== x` → false, `!= x` → true; predicate completes normally |
| `EvalContains` `:139-148` | `as StringValue` is null → `InvalidOperationException` | already inside the filter |
| unary `-` / `!` `:73-85` | `InvalidOperationException` | already inside the filter |
| logical operand `:90-117` | `InvalidOperationException` | already inside the filter |
| value expression → `ToWireString()` `AelValue.cs:52` | `NullValue => string.Empty`, **no throw** | effect emitted with `""` |

The last row is the one consequence worth stating out loud: a
`SetVariableValue` action whose value expression resolves *directly* to an
oversized number now writes `""` instead of dead-lettering. **That is exactly
what a missing field already does today** — `$.payload.nonexistent` has always
produced `NullValue` → `""`. Making an unrepresentable number behave identically
to an absent field is the intent, and US1's scenarios pin it so it is an
observed decision rather than an accident. Changing what `NullValue` writes to
the wire is a different question about a pre-existing behaviour and is **out of
scope** (§*Out of scope*).

**The issue's own framing of direction 2 is slightly off, and this is why
direction 1 still earns its place.** The issue says widening the catch *"keeps
the failure at whole event lost"*. It does not — the throw originates inside the
two `try` blocks, so widening does narrow it to one rule. The real distinction is
one step finer: widening makes the outcome *"one rule skipped with an error
log"*; direction 1 makes it *"one field is unaddressable, and the rule evaluates
anyway"*. For `$.payload.v == 42` that is the difference between an error log
plus a skipped rule and a correct, quiet `false`.

### Direction 2 (US2) — the promise stops being a list

Widen the filter in `RuleEvaluator.TryEvaluatePredicate` (`:78`),
`RuleEvaluator.TryEvaluateValueExpression` (`:100`), and
`DryRunRuleQueryHandler` (`:124`).

Direction 1 cannot address `OverflowException` — that throw happens in
`Arithmetic`/`DivideInt`/`ModuloInt`, from values that are perfectly
representable. Widening is the only thing that closes it, and it closes the
whole class rather than the two members of it found so far.

The **shape** of the widening is settled in `plan.md` §*Why the filter stops
enumerating*: a catch-all with one carve-out, not a longer list. The short
version is that a list which has already been wrong about `FormatException` and
`OverflowException` will be wrong about the third one too, the guarded call is a
single pure synchronous function over in-memory data with no I/O failure worth
propagating, and nothing is swallowed — every absorbed exception is logged in
full with its rule identifier.

## Observability — what is in scope, and what is deliberately not

The issue's sharpest sentence is *"nothing in the rule fan-out sees or logs which
value caused it"*. The answer has two halves and the boundary between them is a
decision, stated here rather than left to phase 4.

**In scope — the failure becomes a logged, attributable, per-rule event.**
Today both new failure classes produce **no log line at all**; the dead-letter is
the entire signal, and a DLQ has no alert. US2's widened filter makes them flow
through the two `[LoggerMessage]` definitions that already exist
(`src/Automation/Application/Log.cs:31-35`):

```
Warning  Predicate evaluation failed on rule {Rule}; skipping rule.
Warning  Value-expression evaluation failed on rule {Rule}; skipping action.
```

Both already take the `Exception` and the `RuleIdentifier`, so the exception type
and message are captured structurally with zero new logging code. **Which event**
is answerable without plumbing `eventIdentifier` into `RuleEvaluator`: the log is
emitted inside the Wolverine message activity, so OTel stamps it with the
`TraceId`/`SpanId` of the `FabEventIngestedV1` consume span (ADR-0050). US1's
scenarios and the phase-5 verification both assert a log line was written, so
this is *observed*, not asserted from the source.

**Out of scope, deliberately — naming the offending JSON value or field path.**
Doing so means giving `AelInterpreter` a logger. It is a `static` class with no
constructor and no dependencies by design (ADR-0099: *"sandboxed by
construction — no file IO, no network, no reflection"*), sitting on the NFR-002
hot path of ≤ 10 µs p99/eval, inside the projection half of constitution §IV's
*Event → overlay state ≤ 200 ms* leg. Threading an `ILogger` through
`Eval`/`ResolveField`/`JsonElementToAelValue` is a design change that deserves
its own discussion, not a rider on a correctness fix.

There is a second reason, which is the stronger one: after US1 an
unrepresentable number is *by construction* indistinguishable from a missing
field, and that indistinguishability is the fix. A log that named the value would
have to be added for missing fields too, or the two cases would diverge again in
exactly the way this spec is closing. Recorded as a follow-up candidate, not
smuggled in here.

## Locked technical choices

| Concern | Choice | Source |
|---|---|---|
| Expression engine | hand-rolled AEL, `Automation.Application.Ael` | ADR-0099 |
| Value union | `AelValue` closed record hierarchy; `NullValue.Instance` is the singleton "unaddressable" sentinel | `AelValue.cs:9-28` |
| Logging | existing `[LoggerMessage]` partials in `Automation/Application/Log.cs`; no new definitions | ADR-0050 |
| Error surface (dry run) | `Result<T, Error>` → `DryRunRuleFailures.EvaluationFailed` | ADR-0047, ADR-0089 |
| Guards | `Ensure.That(...)` — unchanged, none added | ADR-0105 |
| Tests | xUnit + Shouldly, sentence-style names, existing `RuleBuilder` / `InMemoryRuleCache` fakes | ADR-0052, ADR-0053, ADR-0054 |
| Coverage | Application ≥ 80 % — the touched files are Application-layer | ADR-0065 |
| Messaging | Wolverine; the dead-letter this removes is Wolverine's default policy, which is **not** changed | ADR-0042, ADR-0088 |

## Latency-budget impact

**Leg: *Event → overlay state (RabbitMQ + projection) ≤ 200 ms* (constitution §IV).**
The rule fan-out runs inside the projection half of that leg.

**Expected effect: non-negative, i.e. no erosion and a large improvement in the
failing case.**

- `TryGetDecimal` performs the same parse as `GetDecimal` and returns a `bool`
  instead of throwing. On the success path the work is identical — one branch
  replaces one throw-site. No allocation is added (`NullValue.Instance` is a
  singleton, `AelValue.cs:27`).
- The filter widening changes nothing on the success path; a `when` clause is
  evaluated only when an exception is already in flight.
- On the *failing* path the change removes a thrown exception, a full unwind
  through the Wolverine handler, and a dead-letter round trip, and replaces them
  with a branch and one `Warning`. The current cost of this case is unbounded —
  the event never produces an overlay at all.

**Evidence required at phase 5:** `AelInterpreterBenchmarkTests` (median-batch
gate, threshold 500 ms per 10 000 evals, last recorded medians 20.7 / 26.7 / 25.4
/ 23.4 ms — `AelInterpreterBenchmarkTests.cs:28-50`) must stay green, and the
observed median must be quoted in the verification note. Per the memory note
*"measurement runs need repeating"*, run it twice.

## User stories

### US1 (P1) — A number too big for `decimal` makes one field unaddressable, not the event unprocessable

**As** a fab operator whose PLC gateway has been misconfigured to emit `1e30`,
**I want** the rules that do not depend on that field to keep firing,
**so that** one bad sensor reading does not silence automation for the trigger.

Independently shippable: US1 alone fixes the filed issue and can merge without US2.

**Acceptance:** the exact payload from the issue (`{"v": 1e30}`) reaches the
interpreter, no exception escapes `RuleEvaluator.Evaluate`, an unrelated rule on
the same trigger still produces its effect, and the rule that touched the field
is skipped with a `Warning` naming it.

### US2 (P2) — The evaluator's promise survives an exception type nobody listed

**As** the maintainer of a rule that multiplies a large-but-valid payload number,
**I want** the failure charged to my rule,
**so that** the class of "an exception the filter did not anticipate" stops
costing the whole event.

Independently shippable: US2 alone is a valid change that closes the
`OverflowException` path, and can merge without US1.

**Acceptance:** `$.payload.big * 10` with `big = 7.9e28`, and
`$.payload.tiny / -1` with `tiny = long.MinValue`, are each contained to their
own rule and logged; a second rule on the same event still fires; the dry-run
endpoint answers **400 `RULE_DRY_RUN_EVALUATION_FAILED`** rather than 500 for the
same inputs.

## Acceptance scenarios

Gherkin. Every `Given` payload below is legal JSON that passes `Payload`'s ≤ 64 KB
and parse checks unchanged.

### US1 — happy path (the field is simply usable)

```gherkin
Scenario: A number that fits decimal is still a DecimalValue
  Given a payload {"payload": {"v": 7.9e28}}
  When the expression "$.payload.v" is evaluated
  Then the result is a DecimalValue of 79000000000000000000000000000
  And no exception is thrown

Scenario: decimal.MaxValue exactly is not treated as oversized
  Given a payload {"payload": {"v": 79228162514264337593543950335}}
  When the expression "$.payload.v" is evaluated
  Then the result is a DecimalValue of 79228162514264337593543950335
```

### US1 — the filed defect (the empirical repro, verbatim)

```gherkin
Scenario: The exact reported value no longer throws out of the interpreter
  Given a payload {"payload": {"v": 1e30}}
  When the expression "$.payload.v" is evaluated
  Then the result is AelValue.NullValue.Instance
  And no FormatException is thrown

Scenario Outline: Every number outside decimal's range is unaddressable, not fatal
  Given a payload {"payload": {"v": <value>}}
  When the expression "$.payload.v" is evaluated
  Then the result is AelValue.NullValue.Instance
  Examples:
    | value                                  |
    | 1e30                                   |
    | -1e30                                  |
    | 1e400                                  |
    | 79228162514264337593543950336          |
    | <400 nines>                            |
```

### US1 — conflict: one bad field must not take the other rules with it

```gherkin
Scenario: An oversized number skips its own rule and no other
  Given an active rule "alarm"  on (munich, plc, PlcCycleStart) with predicate "$.payload.v > 100"
  And   an active rule "healthy" on (munich, plc, PlcCycleStart) with predicate "$.payload.cycleTime <= 30"
        and action SetVariableValue("oeeLine1", "99")
  And   an event whose payload is {"v": 1e30, "cycleTime": 27}
  When the RuleEvaluator evaluates the event
  Then exactly one effect is returned: SetVariableValue("oeeLine1", "99")
  And  no exception escapes Evaluate
  And  a Warning "Predicate evaluation failed on rule {Rule}; skipping rule." is logged for "alarm"

Scenario: Equality against an oversized number is answered, not skipped
  Given an active rule with predicate "$.payload.v == 42"
  And   an event whose payload is {"v": 1e30}
  When the RuleEvaluator evaluates the event
  Then no effect is returned
  And  no warning is logged, because nothing failed — the comparison is simply false

Scenario: An oversized number in a value expression behaves as a missing field does
  Given an active rule with predicate "$.payload.cycleTime <= 30"
        and action SetVariableValue("x", "$.payload.v")
  And   an event whose payload is {"v": 1e30, "cycleTime": 27}
  When the RuleEvaluator evaluates the event
  Then one effect SetVariableValue("x", "") is returned
  And  the same rule against payload {"cycleTime": 27} with no "v" at all yields the identical effect
```

### US1 — bad request: `Payload` validation is unchanged

```gherkin
Scenario: The fix does not start rejecting payloads at ingestion
  Given a request body whose payload is {"v": 1e30}
  When POST /events is called with a valid token
  Then the response is 202 Accepted, exactly as before
  And  Payload.From accepts the value, because "is it valid JSON and ≤ 64 KB" is unchanged
```

### US2 — the overflow class

```gherkin
Scenario: Decimal arithmetic overflow is charged to its rule, not the event
  Given an active rule "mul"     with predicate "$.payload.big * 10 > 0"
  And   an active rule "healthy" with predicate "$.payload.cycleTime <= 30"
        and action SetVariableValue("oeeLine1", "99")
  And   an event whose payload is {"big": 7.9e28, "cycleTime": 27}
  When the RuleEvaluator evaluates the event
  Then exactly one effect is returned: SetVariableValue("oeeLine1", "99")
  And  no OverflowException escapes Evaluate
  And  a Warning naming rule "mul" is logged

Scenario: Integer division overflow is charged to its rule
  Given an active rule with predicate "$.payload.tiny / -1 > 0"
  And   an event whose payload is {"tiny": -9223372036854775808}
  When the RuleEvaluator evaluates the event
  Then no effect is returned, no exception escapes, and a Warning names the rule

Scenario: An overflow in a value expression skips the action, not the event
  Given an active rule with predicate "$.payload.cycleTime <= 30"
        and action SetVariableValue("x", "$.payload.big * 10")
  And   an event whose payload is {"big": 7.9e28, "cycleTime": 27}
  When the RuleEvaluator evaluates the event
  Then no effect is returned
  And  a Warning "Value-expression evaluation failed on rule {Rule}; skipping action." is logged

Scenario: Cancellation is never absorbed by the widened filter
  Given a guarded call that throws OperationCanceledException
  When the RuleEvaluator evaluates
  Then the exception propagates unchanged and nothing is logged as a rule failure
```

### US2 — the dry run agrees with the live pipeline

```gherkin
Scenario: Dry-run returns a typed failure where the live path skips a rule
  Given a published rule with predicate "$.payload.big * 10 > 0"
  When POST /rules/{name}/dry-run is called with sample {"payload": {"big": 7.9e28}}
       and a token carrying the rule-read scope
  Then the response is 400 RULE_DRY_RUN_EVALUATION_FAILED
  And  it is not a 500

Scenario Outline: Dry-run auth is unchanged by this spec
  Given the dry-run endpoint
  When it is called <auth>
  Then the response is <status>
  Examples:
    | auth                          | status |
    | with no bearer token          | 401    |
    | with a token lacking the scope| 403    |
```

## Independent end-to-end test procedure

Observable by a person, against the real stack — not a test run (ADR-0103, phase 5).

1. Boot the Aspire AppHost (one stack per machine; stop any running one first).
2. Mint a token from Aspire's **proxied** Keycloak endpoint, not the container's
   mapped port.
3. `POST /rules` twice on trigger `(munich, plc, PlcCycleStart)`:
   - **A** — predicate `$.payload.v > 100`, action `SetVariableValue("spec203Alarm","1")`
   - **B** — predicate `$.payload.cycleTime <= 30`, action `SetVariableValue("spec203Healthy","99")`
   Publish both.
4. **Record the current dead-letter depth** for the Automation queue before step 5.
5. `POST /events` with payload `{"v": 1e30, "cycleTime": 27}`.
6. Observe, in order:
   - `GET /system-variables/spec203Healthy` → `"99"`. **Rule B fired.** On
     `develop` today it does not, because the event never reaches evaluation.
   - `GET /system-variables/spec203Alarm` → unchanged/absent. Rule A correctly
     did not fire.
   - The dead-letter depth is **unchanged from step 4**.
   - One `Warning` in the Aspire dashboard structured logs: *"Predicate
     evaluation failed on rule {Rule}; skipping rule."* with rule A's identifier,
     an `InvalidOperationException` naming `NullValue`, and a `TraceId` that
     resolves to the `FabEventIngestedV1` consume span. Create the event and look
     — do not search trace history (`list_traces` ignores `search`).
7. Repeat step 5 **three times**. Depth still unchanged, `spec203Healthy` still
   `"99"`: the sustained-outage scenario is closed, not merely survived once.
8. `POST /rules/{name}/dry-run` with `{"payload": {"big": 7.9e28}}` on a rule whose
   predicate is `$.payload.big * 10 > 0` → **400 `RULE_DRY_RUN_EVALUATION_FAILED`**,
   not 500.
9. Run `AelInterpreterBenchmarkTests` twice in Release; quote both medians.

**Before/after is the point of step 6.** The note must state that the same
procedure on `develop` produces a dead-letter and no `spec203Healthy`.

## Out of scope

Each of these was found while investigating; each is recorded so it is a
decision, not an omission.

1. **`BuildContext` can exceed the JSON depth limit at exactly payload depth 64
   — a second uncaught dead-letter path.** `Payload.From` validates with
   `JsonDocument.Parse` at the default `MaxDepth` of 64 and **accepts** a payload
   nested 64 deep. `FabEventIngestedV1Handler.BuildContext` (`:130-147`) wraps
   that payload in **one more object level** and re-parses at `:145` with **no
   `try`/`catch`**; at 65 the parse throws `JsonReaderException`, which escapes
   `Handle` and dead-letters the event. Blast radius identical to #2427.
   **Reproduced:** depths 61-63 pass both; **depth 64 passes `Payload` and fails
   `BuildContext`**; depth 65 is rejected by `Payload`. Different file, different
   fix, and a payload nested exactly 64 deep needs an adversarial or unusual
   producer — so it is a separate issue, not a rider. **Recommend filing.**
2. **`BuildContext` assumes `message.Payload` is valid JSON text.**
   `FabEventIngestedV1.Payload` is a bare `string` on the contract
   (`Shared.Contracts/EventIngestion/FabEventIngestedV1.cs:22`) — no value object
   crosses the boundary, so nothing re-validates it on the Automation side. Any
   message whose `Payload` is empty or malformed (a different publisher, a
   replayed or hand-edited message) throws out of `Handle` the same way. Same
   follow-up issue as (1); one `try` around `BuildContext` closes both.
3. **`BuildContext`'s `JsonDocument` is never disposed** (`:145`).
   `JsonDocument.Parse(string)` rents from `ArrayPool`; the buffer is never
   returned, so every ingested event leaks a pooled array until GC. Not a
   correctness defect, and it **cannot** be fixed with a `using` — the
   `RootElement` outlives the method — so it needs a scope change. Note on the
   same follow-up.
4. **A literal wider than `long`/`decimal` in a rule's own source 500s the create
   endpoint.** `AelLexer.ParseInt`/`ParseDecimal` (`:163-167`) call
   `long.Parse`/`decimal.Parse`, which throw `OverflowException`;
   `CreateRuleCommandHandler` (`:38, :52`) catches only `AelParseException`. So
   `$.payload.v > 99999999999999999999999` yields a 500 instead of a typed
   `PredicateParseFailed` 400. This is an **operator-input** boundary, not the
   external-payload one #2427 is about, and its severity is "wrong status code",
   not "silent outage". US2 does not reach it — the parse happens at create and
   publish time, outside both guarded blocks. Separate issue. Note also that the
   lexer has **no exponent support** (`:109-132`), so `1e30` in a *rule source*
   lexes as `1` followed by identifier `e30` — an unrelated grammar gap, also
   not fixed here.
5. **Silent precision loss on underflow.** `1e-40` makes `TryGetDecimal` succeed
   and yield `0` (reproduced), so `$.payload.v > 0` is `false` for a genuinely
   positive value. No exception, no log, pre-existing, and arguably correct for a
   `decimal`-typed language. Unchanged by this spec.
6. **`AelParser` is recursive-descent with no depth guard** (`AelParser.cs`, no
   `depth`/`MaxDepth` anywhere). A deeply-parenthesised predicate recurses once
   per level, and `StackOverflowException` is **uncatchable** — no filter
   widening helps. Bounded in practice: `RulePredicate` caps the source at 4096
   characters (`RulePredicate.cs:19`) and predicates arrive through an
   authenticated operator endpoint, not a payload. Low severity; recorded, not
   fixed.
7. **What `NullValue` writes to the wire.** `ToWireString()` renders it as `""`
   (`AelValue.cs:52`). This spec makes an oversized number reach that path; it
   does **not** change what the path does, because a missing field has always
   reached it and changing it would alter existing behaviour for every rule.
8. **Wolverine's error policy.** `WolverineDefaults` gains no retry or
   dead-letter rule. The fix is that the exception stops escaping, not that the
   escape is handled differently.
9. **Naming the offending value in a log** — see §*Observability*.
10. **`Option<T>` migration** of any signature touched here (ADR-0141 is
    advisory; existing signatures are not a defect).

## File contention

All inside `Automation`. No cross-context project reference is added or needed
(NetArchTest unaffected); `Shared.Contracts` is untouched.

| File | Story |
|---|---|
| `src/Automation/Application/Ael/AelInterpreter.cs` | US1 |
| `src/Automation/Application/Evaluation/RuleEvaluator.cs` | US2 |
| `src/Automation/Application/Queries/Handlers/DryRunRuleQueryHandler.cs` | US2 |
| `tests/Automation.Application.Tests/Ael/AelInterpreterTests.cs` | US1 |
| `tests/Automation.Application.Tests/Ael/AelFixtures.cs` | US1 |
| `tests/Automation.Application.Tests/Evaluation/RuleEvaluatorTests.cs` | US1 + US2 — **shared, serialise** |
| `tests/Automation.Application.Tests/Queries/RuleQueryHandlerTests.cs` — the dry-run cases live here; there is no separate `DryRunRuleQueryHandlerTests` | US2 |

`RuleEvaluatorTests.cs` is the one file both stories append to. It is the reason
US1 and US2 are one branch rather than two, and the reason their phase-4a tasks
are **not** marked `[P]` against each other (ADR-0109).
