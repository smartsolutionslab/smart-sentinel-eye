# Plan — Spec 243, the level the wrapper adds

**Spec:** `specs/243-the-level-the-wrapper-adds/spec.md` · **Issue:** #2496 ·
**Branch:** `fix/2496-buildcontext-dead-letter` · **Base:** `origin/develop` @ `a54b11d0`

## Context and layers

One bounded context, one layer, two files of production code.

| Context | Layer | Files |
|---|---|---|
| **Automation** | **Application** | `EventHandlers/FabEventIngestedV1Handler.cs` (US1 + US2), `Log.cs` (US1 — one new `[LoggerMessage]`) |

- **Domain untouched.** No aggregate, value object, invariant, repository, migration or EF mapping changes.
- **`Shared.Contracts` untouched.** `FabEventIngestedV1` keeps `string Payload` — the wire carries primitives by design (ADR-0040); no `V<N>` bump (ADR-0073).
- **`EvaluationContext`, `AelInterpreter`, `RuleEvaluator`, `DryRunRuleQueryHandler` untouched.**
- **Boundary rules hold trivially.** No project reference added. In particular, Automation does **not** reference `EventIngestion.Domain.Event.Payload` to learn its depth limit — that would be a cross-context reference (NetArchTest-enforced). The limit is restated as a named constant with a comment saying whose limit it mirrors.
- **Messaging:** no domain event, no integration event added. The change removes a path by which an integration event is dead-lettered (ADR-0042/0088); Wolverine's policy is not changed.

## The shape after the change

```csharp
// Handle, replacing :80-82
JsonDocument document;
try
{
    document = ParseContext(message);
}
catch (JsonException exception)
{
    logger.SkippedEventWithUnparseablePayload(exception, eventIdentifier, message.Payload?.Length ?? 0);
    return;
}

IReadOnlyList<RuleActionEffect> effects;
using (document)
{
    effects = evaluator.Evaluate(parsedFab, source, kind, new EvaluationContext(document.RootElement));
}
// … publish loop unchanged, outside the using
```

```csharp
// BuildContext → ParseContext: returns the document, not a view of it
private static JsonDocument ParseContext(FabEventIngestedV1 message)
{
    … StringBuilder composition unchanged …
    return JsonDocument.Parse(builder.ToString(), ContextParseOptions);
}

// The payload was validated by EventIngestion at System.Text.Json's default depth (64);
// the envelope object this method wraps around it is one level more.
private const int PayloadMaximumDepth = 64;
private static readonly JsonDocumentOptions ContextParseOptions = new() { MaxDepth = PayloadMaximumDepth + 1 };
```

The exact names are the engineer's to choose within house rules (ADR-0091: no abbreviations; no leading underscore; `Ensure.That` unchanged). The `payload length` log field: `message.Payload` is `string` under NRT but can be `null` from a violating producer — the length expression must not itself throw, or the guard dead-letters on the very input it guards. Whether to read `Payload` through the existing deconstruction (`var (eventIdentifier, fab, source, _, kind, _, ingestedAt, payload, _) = message;` — the handler already deconstructs, CLAUDE.md §*Handlers destructure their input first*) is preferred over `message.Payload`; `HandlerDeconstructionTests` will check the local's name.

## Defect 1 — why `MaxDepth = 64 + 1`, and why not a bare catch

- The wrapper adds **exactly one** object level (`{"source":…,"payload":<payload>}`), so the parse must allow **exactly one** more than `Payload` allows. Reproduced: 64-deep inside the wrapper parses at 65; 65-deep still throws (spec §*Reproduced*). So legal payloads are always evaluated, and one level beyond legal still goes to the defect-2 path — the limit is tight, not raised.
- A bare catch (the issue's proposal) would log-and-skip a **legal** payload. That is strictly worse than evaluating it, and spec 203 made the same argument for its US1 ("the rule evaluates anyway" over "an error log plus a skipped rule").
- **Not speculative generality:** it is one options value matching an existing contract, not a knob. No config binding, no parameter.
- **AEL is unaffected by depth.** `AelInterpreter.ResolveField` walks field-path segments iteratively (`AelInterpreter.cs:36-54`); nothing recurses over the document.

## Defect 2 — the catch

- **Type:** `JsonException`, narrow. `JsonDocument.Parse(string, JsonDocumentOptions)` throws `JsonException` (incl. `JsonReaderException`) for input that is not a JSON value; `ArgumentOutOfRangeException` only for invalid options (our constant — must crash in tests, not be absorbed). `null` payload: `StringBuilder.Append(string?)` appends nothing → `"payload":}` → `JsonException`; covered without a special case. Spec 203's catch-all-minus-OCE is **not** the right shape here — it guarded an interpreter with many throw sites; this guards one BCL call with a documented failure type (spec §*How this mirrors #2427*).
- **Scope of the `try`:** the parse only. `Evaluate` must stay outside it — `RuleEvaluator` already contains its own per-rule failures (spec 203), and a `JsonException` from anywhere else is a different defect that must not be silently relabelled "unparseable payload".
- **Outcome:** `return` without publishing; Wolverine acks. Matches the fab guard at `:64-78` exactly (fail closed, log, return).
- **Log:** new `Log.SkippedEventWithUnparseablePayload(this ILogger, Exception, Guid @event, int payloadLength)`, `Warning`, message along the lines of *"Ingested event {Event} carried a payload of {PayloadLength} characters that is not valid JSON; no rule evaluated."* Distinct from `SkippedEventWithoutFab` and `SkippedEventWithUnparseableFab` (spec scenario pins pairwise distinctness). **The payload text is not logged**: up to 64 KB of producer data; the exception carries line and byte position. XML doc comment on the partial states *why* it names the length and not the value — the opposite choice from the fab log, which does name its value, and a reader will ask.

## Defect 3 — the decision

**Chosen: the caller owns the document; `EvaluationContext` stays a non-owning view.** `ParseContext` returns the `JsonDocument`; `Handle` wraps the one synchronous `Evaluate` call in `using (document)`.

**Why this is safe — the lifetime was traced, not assumed:**

| Thing that could outlive the `using` | Holds a `JsonElement`? | Evidence |
|---|---|---|
| `EvaluationContext` | yes — but it is constructed inside the `using` and not stored | local only |
| `RuleEvaluator.Evaluate` | no — synchronous, returns a materialised `IReadOnlyList` | `RuleEvaluator.cs:26-69` |
| `RuleActionEffect.SetVariableValue(string Name, string Value)` | no — `Value` is `AelValue.ToWireString()`, a new `string` | `RuleActionEffect.cs:10`, `RuleEvaluator.cs:108` |
| `RuleActionEffect.HighlightOverlay(Guid, int)` | no | `RuleActionEffect.cs:12` |
| `AelValue.StringValue` | no — built from `element.GetString()`, a copy | `AelInterpreter.cs:61` |
| the publish loop (`await`s) | reads only `effects`, strings, `Guid`s, `DateTimeOffset`s | `FabEventIngestedV1Handler.cs:89-118` |

So the document can be disposed **before** the first `await`, which keeps the pooled buffers out of the async state machine.

**Alternatives rejected:**

| Alternative | Rejected because |
|---|---|
| `EvaluationContext : IDisposable`, owning the `JsonDocument` | It is a `readonly record struct` (`AelInterpreter.cs:236`) passed **by value** through `RuleEvaluator` and every `AelInterpreter.Eval*` overload. A disposable struct copied by value gives every copy the power to dispose the shared document — the classic value-type-disposal hazard. Making it a class changes AEL's input type (ADR-0099 territory) and its other two construction sites (`DryRunRuleQueryHandler.cs:100`, test fixtures `AelFixtures.cs`) — **a wider change for no gain**, since the handler is the only site that leaks. |
| `JsonElement.Clone()` then dispose the document | `Clone()` copies into an unpooled array — trades a pooled buffer for an unpooled copy of the same size. Measurably no better than today. |
| Parse the payload alone and compose the envelope in `EvaluationContext` | Removes the re-parse (the larger win), but changes how `$.payload.*` resolves — AEL input shape, ADR-0099. Out of scope; candidate follow-up (spec §*Out of scope*). |

**Precedent:** `DryRunRuleQueryHandler.cs:98-100` already does exactly this — `using (sample) { EvaluationContext context = new(sample.RootElement); … }`. The fix makes the live path agree with the dry-run path, which that file's own comment names as an invariant.

**No new ADR.** The disposal contract of `EvaluationContext` is unchanged at every site; one private method's return type changes. This is squarely an implementation choice.

## Test plan

All in `tests/Automation.Application.Tests/EventHandlers/FabEventIngestedV1HandlerTests.cs` (existing file, existing fakes: `InMemoryRuleCache`, `FakeEventBus`, `FakeClock`, `CapturingLogger<T>`, `RuleBuilder`). No new fake, no new builder.

| Test | Story | Colour | Observed |
|---|---|---|---|
| existing 10 test methods (12 cases) in the file | US2 | characterisation | green on HEAD, green after, **unmodified** |
| `A_string_read_from_the_payload_is_published_intact` | US2 | characterisation | written first, **green on HEAD**, green after, unmodified |
| `A_payload_nested_to_the_depth_ingestion_accepts_is_evaluated` (64) | US1 | red | **red on HEAD** (`JsonReaderException` escapes `Handle`) |
| `A_payload_that_is_not_json_is_logged_and_skipped` (Theory: `""`, `"   "`, `"{"`, `"not json"`, `"{\"a\":1}}"`) | US1 | red | **red on HEAD** |
| `A_null_payload_is_logged_and_skipped` (`null!`) | US1 | red | **red on HEAD** |
| `A_payload_one_level_deeper_than_ingestion_accepts_is_logged_and_skipped` (65) | US1 | red | **red on HEAD** |
| `An_unparseable_payload_is_logged_distinctly_from_both_fab_failures` | US1 | red | red on HEAD — **must not compile-fail as its red**: write it so it fails on assertion (no log entry / exception escapes), not on a missing symbol |
| `An_unparseable_payload_log_carries_its_length_not_its_text` | US1 | red | red on HEAD |

Red-quality note for the test-writer: every US1 red must fail **for the reason in the spec** (an exception escaping `Handle`, or an absent `Warning`) — quote the failure. A red caused by a missing `Log` method or a compile error proves nothing (memory: *the wrong red matches the filed number*). The depth-64 test's red must be `JsonReaderException`, and after the fix it must **publish** — a fix that only adds the catch will leave it red, which is the intended discriminator between this plan and the issue's bare-catch proposal.

The 64/65 fixtures are built programmatically (`new string('[', n) + "1" + new string(']', n)`), with the depth a named local — not a literal string.

**Coverage:** Application ≥ 80 % (ADR-0065). The new catch arm and the `using` are both covered by the table above. `Log.cs` is `[ExcludeFromCodeCoverage]`.

**Code metrics (ADR-0084, advisory):** `Handle` is already over the 30-LOC advisory; this adds ~10 lines. The engineer may extract the parse-and-evaluate step into a private method returning the effects list — but only if it stays behaviour-preserving under the characterisation set. Not required.

## Verification (phase 5)

Per spec §*Independent end-to-end test procedure* — including its honest limit: no ingress produces these inputs today, so observation is at the Automation queue via the RabbitMQ management API, or explicitly labelled as not observed on the stack. Allocation figures re-measured twice or declared not re-run.

## Constitution / ADR check

| Check | Result |
|---|---|
| §II primitives on domain models | n/a — Application handler, contract DTO exempt |
| §IV latency | leg *Event → overlay state*; non-negative (spec §*Latency-budget impact*) |
| §Testing two obligations | honoured by story split and task order |
| ADR-0105 guards | unchanged |
| ADR-0141 `Option<T>` | no new nullable parameter in Application |
| ADR-0050 logging | `[LoggerMessage]` source-gen, structured fields |
| Cross-context references | none added |
| New ADR needed | **no** |
