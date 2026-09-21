# Plan — Spec 203, one value, not the whole event

**Spec:** `specs/203-one-value-not-the-whole-event/spec.md` · **Issue:** #2427 ·
**Branch:** `2427-oversized-json-number` · **Base:** `origin/develop` @ `740cc253`

## Context and layers

One bounded context, one layer. Nothing crosses a boundary.

| Context | Layer | Files |
|---|---|---|
| **Automation** | **Application** | `Ael/AelInterpreter.cs` (US1), `Evaluation/RuleEvaluator.cs` (US2), `Queries/Handlers/DryRunRuleQueryHandler.cs` (US2) |

**Domain is untouched.** No aggregate, value object, invariant, repository,
migration, or EF mapping changes. `RulePredicate`, `Rule`, `RuleIdentifier`,
`CompiledRule` are read, not modified.

**No new integration event, no contract change.** `Shared.Contracts` is not
touched, so no `V<N>` bump and no consumer migration (ADR-0040, ADR-0073). The
domain-event → integration-event flow is unchanged; the change is entirely
*inside* the `FabEventIngestedV1` subscriber's evaluation step.

**Boundary rules hold trivially.** No cross-context project reference is added;
NetArchTest's rules are unaffected. `AelInterpreter` remains a dependency-free
`static` class (ADR-0099's sandboxing requirement) — the plan deliberately does
**not** give it a logger, see spec §*Observability*.

**Entities / value objects / invariants.** None added. The one invariant being
repaired is a *class* invariant stated in a doc comment and not enforced:

> `RuleEvaluator.cs:15-20` — "Runtime errors during predicate / action
> evaluation are logged and skipped per rule — one bad rule does not stop the
> loop."

US2 makes the code say what the comment says. US1 removes the most likely reason
to need it.

## US1 — the interpreter becomes total over legal JSON

### The change

`AelInterpreter.cs:62-63`, one arm:

```csharp
JsonValueKind.Number when element.TryGetInt64(out long integer) => new AelValue.IntValue(integer),
JsonValueKind.Number when element.TryGetDecimal(out decimal fraction) => new AelValue.DecimalValue(fraction),
JsonValueKind.Number => AelValue.NullValue.Instance,
```

Three lines where there were two. The final `Number` arm needs to be explicit
rather than folded into the `_` discard below it, because the `_` arm carries the
comment *"arrays / objects are not addressable values in v1"* and a number that
does not fit is a **different** reason to be unaddressable. A reader who finds
`1e30` falling through an arm labelled "arrays / objects" learns the wrong thing.
A short comment on the new arm names `decimal`'s range and points at #2427.

### Why `TryGetDecimal`, and why not the alternatives

| Candidate | Rejected because |
|---|---|
| `GetDouble()` and add a `DoubleValue` variant | Widens `AelValue`, changes AEL's numeric tower, and needs a grammar/ADR discussion. `1e400` is still `∞` (reproduced), so it does not even close the class. |
| `StringValue(element.GetRawText())` | Makes `$.payload.v == "1e30"` true and `$.payload.v > 100` a coercion failure — an inconsistent, surprising third semantics for one kind of number. |
| `throw` a typed AEL exception | Moves the problem to "which filter catches it", which is the defect US2 exists to stop re-litigating. |
| Clamp to `decimal.MaxValue` | Silently answers a comparison **wrongly**. `1e30 > 100` would become `true` — plausible here, catastrophically wrong for `< threshold` rules. Failing closed is the only safe direction for an automation trigger. |
| `TryGetDecimal` → `NullValue.Instance` | **Chosen.** Reuses the sentinel the switch already returns three times, allocates nothing (`AelValue.cs:27` singleton), and is total. |

### Why `NullValue` is safe here — the trace, not the hope

The full downstream table is in spec §*Fix direction / Direction 1*. The
structural point for the implementer: **every** operator that cannot consume a
`NullValue` raises `InvalidOperationException` from `ToDecimal`
(`AelInterpreter.cs:210-211`) or from its own `as`-cast guard, and
`InvalidOperationException` is **already** the first entry in both existing catch
filters. So US1 requires no change to `RuleEvaluator` to be safe. It ships
standalone.

The two throw-free paths are the interesting ones and both are intentional:
`AreEqual` falls to `_ => false` (`:224`), so `== x` is a clean `false`; and
`ToWireString()` renders `NullValue` as `string.Empty` (`AelValue.cs:52`), so a
value expression writes `""`. Both are **exactly** what a missing field has
always done, which is the whole argument for this shape.

### Hot path

`TryGetDecimal` and `GetDecimal` perform the same parse; one returns `bool`, the
other throws. No allocation is added. The `when` clause adds one predicate
evaluation on the `Number` path, which already evaluated one (`TryGetInt64`).
`AelInterpreterBenchmarkTests`' median-batch gate is the evidence and must be
quoted at phase 5 (spec §*Latency-budget impact*).

## US2 — the filter stops enumerating

### The change

Three call sites, the same edit:

| File | Line today |
|---|---|
| `Evaluation/RuleEvaluator.cs` | `:78` `catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)` |
| `Evaluation/RuleEvaluator.cs` | `:100` same |
| `Queries/Handlers/DryRunRuleQueryHandler.cs` | `:124` same **plus** `or AelParseException` |

becomes, at each:

```csharp
catch (Exception exception) when (exception is not OperationCanceledException)
```

The dry-run site keeps returning `DryRunRuleFailures.EvaluationFailed(ex.Message)`
→ `RULE_DRY_RUN_EVALUATION_FAILED` / **400** (`DryRunRuleErrors.cs:41-45`).
`AelParseException` is a subclass of `Exception` and stays caught; dropping it
from the filter list loses nothing.

### Why the filter stops enumerating

This is the judgement call in the spec and it should be argued, not asserted.

1. **The list is the defect.** It was written for the exceptions AEL threw when
   AEL was written. `FormatException` (from `GetDecimal`) and `OverflowException`
   (from `decimal` arithmetic and `long.MinValue / -1`) were both outside it, both
   reachable, and both reproduced. Extending it to four types leaves the same
   mechanism in place for the fifth. This repository has a documented history of
   exactly this shape — CLAUDE.md records §II drifting twice on a partial
   enumeration and §IV recording a leg wrongly because nobody re-checked a list.
2. **The guarded region has no exception worth propagating.** Each `try` wraps a
   single call to `AelInterpreter.Evaluate` (plus `ToWireString()`), which is
   `static`, synchronous, takes no `CancellationToken`, performs no I/O, no
   reflection, no allocation of consequence, and touches only an already-parsed
   `JsonElement` and an in-memory expression tree. There is no database error, no
   HTTP failure, no transient fault in scope that a caller could usefully
   distinguish.
3. **Nothing is swallowed.** Karpathy §6 blocks *silent* absorption. Every
   absorbed exception goes to `logger.PredicateEvaluationFailed(ex, ...)` /
   `ValueExpressionEvaluationFailed(ex, ...)` (`Log.cs:31-35`) — the full
   exception object, `Warning` level, with the `RuleIdentifier` — or, for the
   dry run, into the 400's `Reason`. The fix is *more* observable than today,
   where these two classes produce no log line at all.
4. **The alternative is worse by a wide margin.** The current failure mode is a
   dead-lettered message and a silent, sustained outage of every rule on the
   trigger. Absorbing an unanticipated exception costs one rule and a `Warning`.

### Why `OperationCanceledException` is the one carve-out

It cannot currently reach these blocks — the guarded calls are synchronous and
token-free — so this is the carve-out's only justification and it should be
stated in the code comment rather than left to look like prophylaxis: it is the
single exception whose absorption is *never* correct, the carve-out costs one
token, and it marks the boundary as deliberate rather than accidental for the
next person who makes something in this path async. If phase 6 prefers the bare
`catch (Exception exception)`, that is an acceptable outcome; what is **not**
acceptable is going back to an enumerated list.

`OutOfMemoryException` is knowingly inside the filter. It would be logged, the
loop would continue, and the next allocation would almost certainly fail anyway
— treating it specially buys nothing real.

### Why the dry run is in this change and not deferred

`DryRunRuleQueryHandler.cs:106-108` states: *"A dry run that disagreed with the
live pipeline would be worse than no dry run at all."* US1 + US2 change what the
live pipeline does with these inputs. Leaving `:124` narrow would make the dry
run 500 on precisely the input the live path now handles cleanly — a **new**
disagreement created by this fix. It is one line in the same change, and skipping
it would be the drive-by, not including it.

## Messaging

No domain event, no integration event, no Wolverine configuration change.

The only messaging-adjacent fact worth writing down is the one being removed:
`WolverineDefaults` has no error policy for this exception, so an escaping throw
dead-letters (ADR-0042, ADR-0088). **That default stays.** A dead letter remains
the right answer for a message that genuinely cannot be processed — a corrupt
envelope, an unreachable database. The fix is that a legal-JSON payload value
stops being such a message. Adding a `.OnException<FormatException>()` policy was
considered and rejected: it would hide the *next* real defect behind a retry
instead of charging the failure to the rule that caused it.

## Testing strategy

**Phase 4a colour: RED.** Both stories change behaviour. A test that arrives
green is a phase-4 failure (ADR-0139). The `test-writer` returns verbatim output;
the engineer may not edit the tests.

Expected red, per story — the implementer should be able to predict the failure
text before running:

- **US1 interpreter tests** — `System.FormatException: One of the identified
  items was in an invalid format.` escaping `AelInterpreter.Evaluate`.
- **US1 evaluator tests** — the same `FormatException` escaping
  `RuleEvaluator.Evaluate`, so the assertion on the surviving rule's effect never
  runs.
- **US2 tests** — `System.OverflowException` escaping `RuleEvaluator.Evaluate`
  and `DryRunRuleQueryHandler.Handle`.

Unit only. No Aspire-fixture integration test is added: the change is a pure
function and a `catch` filter, both fully reachable from
`Automation.Application.Tests`, and the end-to-end evidence is phase 5's manual
procedure against the real stack (spec §*Independent end-to-end test procedure*),
which is stronger than a fixture test for "the message did not dead-letter".

**Logging must be asserted, not assumed** (memory: *self-review catches
contradictions, never omissions*; *an assertion must not check its own input*).
`RuleEvaluatorTests` passes `NullLogger<RuleEvaluator>.Instance` today, which
records nothing. **Reuse the fake that already exists** —
`tests/Automation.Application.Tests/Fakes/CapturingLogger.cs`, a
`CapturingLogger<T> : ILogger<T>` exposing
`IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> Entries`,
written for precisely this situation (its own comment: *"for the cases where the
log **is** the behaviour"*). Do not write a new one.

**Assert the `RuleIdentifier` and the exception type**, not merely "something was
logged". `Message` is the formatted string, so it carries the rendered
identifier; `Exception` carries the real object. A scenario with two rules must
assert the entry names the **failing** rule and not the surviving one — an
assertion that cannot tell them apart is not an assertion, and one that only
checks "no exception was thrown" also passes against `catch { }`.

**Coverage** (ADR-0065): Application ≥ 80 %. Both touched files gain branches
that the new tests cover directly, so the gate should move up, not down.

**Conventions:** Shouldly, sentence-style names with underscores (ADR-0053),
existing `RuleBuilder` / `InMemoryRuleCache` / `FakeClock` fakes (ADR-0054) — no
AutoFixture, no new builder.

## Alternatives considered (and rejected)

1. **Validate numeric range in `Payload`.** Would mean EventIngestion parsing and
   walking every payload to reject values a *downstream* context cannot
   represent. `Payload`'s doc comment (`:11-13`) states the opposite position on
   purpose — the schema is the producer's contract. It would also 4xx an event
   that is perfectly valid for every consumer except AEL, and it would need a
   contract change and an ADR.
2. **Catch at `FabEventIngestedV1Handler.Handle`.** Broader than the defect. A
   `try` around `evaluator.Evaluate` would also absorb genuine infrastructure
   failures from the `events.PublishAsync` calls if the block were drawn wide, and
   even drawn narrowly it charges the failure to the event rather than to the
   rule — the opposite of what `RuleEvaluator`'s promise says. (A **narrow** catch
   around `BuildContext` at `:145` is a different and genuinely needed change —
   spec §*Out of scope* items 1-3, separate issue.)
3. **A Wolverine error policy for these exception types.** Trades a dead letter
   for a discarded message and hides the next real defect. Rejected above.
4. **Extending the filter list to four types.** The cheapest diff and the one
   that guarantees a fourth visit. Rejected in §*Why the filter stops
   enumerating*.

## Review focus for phase 6

1. Is the bare-ish `catch` defensible at these three sites, given each `try`
   wraps exactly one pure synchronous call and every absorbed exception is logged
   with its rule identifier? (The deliberate question — challenge it here, not
   after merge.)
2. Does the new `JsonValueKind.Number` arm sit **above** the `_` discard and carry
   its own reason, rather than falling through the "arrays / objects" comment?
3. Do the tests assert the *surviving* rule's effect and the *logged rule's*
   identifier — not merely "no exception was thrown"? A test that only asserts
   absence of a throw passes against a `catch { }`.
4. Was the red output quoted verbatim in the PR body (ADR-0139)?
5. Does the benchmark median appear in the verification note, from **two** runs?
6. Is anything from spec §*Out of scope* silently fixed? A tidy-up of
   `BuildContext` in this PR is scope creep and should be bounced.
