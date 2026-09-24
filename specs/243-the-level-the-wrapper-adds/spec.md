# Spec 243 — The level the wrapper adds

**Issue:** #2496 — *`FabEventIngestedV1Handler.BuildContext` re-parses the payload unguarded: depth-64 nesting and malformed payload strings dead-letter the whole event, and its `JsonDocument` is never disposed*
**Follow-up to:** #2427 / spec 203 (`specs/203-one-value-not-the-whole-event/`)
**Branch:** `fix/2496-buildcontext-dead-letter`
**Phase-4a colour:** **RED overall, with one characterisation-bound story.** US1 (defects 1 + 2) is behaviour-changing → red. US2 (defect 3) is behaviour-preserving → characterisation, observed green before and after, **unmodified**. The split is handled inside one issue by task order (`tasks.md`), not by two issues — see §*Why one issue, two colours*.
**ADRs:** ADR-0042 + ADR-0088 (Wolverine — why an escaping exception dead-letters), ADR-0040 + ADR-0073 (`FabEventIngestedV1` carries primitives at the wire; unchanged, no `V<N>` bump), ADR-0099 (AEL — `EvaluationContext` is its input; unchanged), ADR-0050 (`ILogger<T>` + `[LoggerMessage]`), ADR-0105 (`Ensure.That`), ADR-0139 (new behaviour starts red), ADR-0144 (autonomous lane), ADR-0109 (parallel markers), ADR-0037 (phases).
**Constitution:** §IV (*Event → overlay state ≤ 200 ms* — the leg this handler sits in), §Testing (two obligations).

**No new ADR is required.** All three fixes are local to one private method and its one caller in one Application-layer file. `EvaluationContext` keeps its shape and its non-owning semantics; the disposal contract of its other two construction sites does not change (§*Defect 3*).

## Problem

Line numbers re-read at HEAD `a54b11d0`. Every behavioural claim below was **reproduced locally** against .NET 10 (throwaway console project in the session scratchpad, not committed) — transcripts in §*Reproduced*.

`src/Automation/Application/EventHandlers/FabEventIngestedV1Handler.cs:130-147`:

```csharp
private static EvaluationContext BuildContext(FabEventIngestedV1 message)
{
    StringBuilder builder = new();
    builder.Append("{\"source\":"); ... builder.Append(",\"payload\":");
    builder.Append(message.Payload);
    builder.Append('}');

    JsonDocument doc = JsonDocument.Parse(builder.ToString());   // :145
    return new EvaluationContext(doc.RootElement);               // :146
}
```

called at `:80`, outside the method's only `try` (which guards `FabIdentifier.From`, `:64-78`).

### Defect 1 — the wrapper adds a nesting level the parse does not allow for

`Payload.From(string)` (`src/EventIngestion/Domain/Event/Payload.cs:63-81`) validates with `JsonDocument.Parse(rawJson)` at the default `MaxDepth` of 64. `BuildContext` wraps that payload in one more object and parses again **at the same default**. A payload nested exactly 64 deep is legal to `Payload` and fatal to `BuildContext`: `JsonReaderException` escapes `BuildContext` → `Handle` → Wolverine dead-letters the event (ADR-0042, ADR-0088).

### Defect 2 — nothing on the Automation side re-validates `message.Payload`

`FabEventIngestedV1.Payload` is a bare `string` (`src/Shared.Contracts/EventIngestion/FabEventIngestedV1.cs:22`, primitives at the wire per ADR-0040). The only validation is `Payload.From`, in EventIngestion, before publish. An empty, whitespace, truncated or non-JSON string — or `null`, which `StringBuilder.Append` renders as nothing, producing `"payload":}` — hits the same unguarded parse and dead-letters.

### Defect 3 — the `JsonDocument` is never disposed

`JsonDocument.Parse(string)` transcodes into a buffer rented from `ArrayPool<byte>.Shared` and rents its metadata database the same way; `Dispose` returns both. `BuildContext` never disposes, and cannot with a `using` at `:145` because `EvaluationContext.Root` is read by the evaluator after the method returns.

**Stated precisely, because "leak" overstates it:** `JsonDocument` declares **no finalizer** (reflection, below), so the rented arrays are never returned but *are* ordinary garbage once the document is unreachable. This is **pool defeat, not unbounded growth**: every event allocates fresh arrays the pool would otherwise have reused. Measured: **+24.6 KB/event** for a ~10 KB payload, **+65.8 KB/event** for a ~60 KB payload.

### Reachability today — the issue's premise, verified rather than inherited

The issue says defect 1 *"shares #2427's exact blast radius"*. **Through the current ingress it does not — it is latent.** All three producers of `Payload` first deserialize an *envelope* in which the payload is one property deep, with `System.Text.Json`'s default `MaxDepth` of 64:

| Ingress | Envelope type | Where |
|---|---|---|
| `POST /events/manual` | `IngestManualEventRequest(… JsonElement Payload)` | `src/EventIngestion/Api/Requests/IngestManualEventRequest.cs:13` |
| `POST /events/webhook/{integrationName}` | `IngestWebhookEventRequest(… JsonElement Payload)` | `src/EventIngestion/Api/Requests/IngestWebhookEventRequest.cs:13` |
| MQTT | `MqttIngressPayload(… JsonElement Payload)` via `JsonSerializer.Deserialize` | `src/EventIngestion/Infrastructure/Ingress/MqttSubscriberHostedService.cs:216` |

No `MaxDepth` override exists anywhere under `src/` (searched). So a depth-64 payload is already rejected by the envelope parse — the *same* one-level-of-wrapping arithmetic as defect 1, applied one step earlier — and never reaches `Payload.From`, let alone Automation. Reproduced below (`envelope STJ default` / `STJ Web` columns).

Defect 2 is likewise latent: the only production publisher (`src/EventIngestion/Application/EventHandlers/EventIngestedDomainEventHandler.cs`) publishes `Payload.Value`, which is valid by construction.

**Why fix latent defects:** the handler's correctness currently depends on an accident in a *different context's* HTTP/MQTT envelope parsing, not on anything the contract promises. The contract promises "a canonicalised JSON document ≤ 64 KB" — which a depth-64 document is. A new producer (a raw-body MQTT format, an envelope parse whose depth is raised, a replay tool, a bug) turns either defect live with a failure mode that is silent: a dead-letter nobody is alerted on, every rule's effect for the event lost. That is the severity class #2427 fixed; here it is a guard that should exist before the input does, not an outage that exists today. **Phase 5 must say "latent", not "reproduced end to end through ingress"** — see §*Independent end-to-end test procedure*.

### Reproduced

Depth `n` = `[` × n, `1`, `]` × n. `BuildContext` mirrored exactly.

```
depth | Payload.From(raw) | BuildContext               | envelope STJ default          | envelope STJ Web
61    | ok                | ok                         | ok                            | ok
62    | ok                | ok                         | ok                            | ok
63    | ok                | ok                         | ok                            | ok
64    | ok                | THREW JsonReaderException  | THREW JsonException           | THREW JsonException
65    | THREW JsonReader… | THREW JsonReaderException  | THREW JsonException           | THREW JsonException
via envelope depth 63: ok
via envelope depth 64: THREW System.Text.Json.JsonException

--- defect 2: non-JSON strings reaching BuildContext
'':          THREW System.Text.Json.JsonReaderException
'   ':       THREW System.Text.Json.JsonReaderException
'{':         THREW System.Text.Json.JsonReaderException
'not json':  THREW System.Text.Json.JsonReaderException
'{"a":1}}':  THREW System.Text.Json.JsonReaderException
'1,2':       THREW System.Text.Json.JsonReaderException

--- defect 3
JsonDocument declares a finalizer: False
~10 KB payload  dispose=False: 80880 bytes/event   dispose=True: 56256 bytes/event   (stable across 2 rounds)
~60 KB payload  dispose=False: 322480 bytes/event  dispose=True: 256640 bytes/event  (stable across 2 rounds)

--- the fix direction for defect 1
depth 64 inside the wrapper, MaxDepth = 65: ok
depth 65 inside the wrapper, MaxDepth = 65: THREW JsonReaderException : The maximum configured depth of 65 has been exceeded. …
```

The residual 56 KB / 256 KB per event after disposal is the `StringBuilder` and its `ToString()` (a UTF-16 copy of the whole composed document). That is a separate, larger optimisation — avoiding the re-parse altogether — and is **out of scope** (§*Out of scope*).

## How this mirrors #2427 — and where it deliberately does not

#2427's principle: *a malformed value should cost the smallest unit it can, not the whole event, and the cost must be logged.* Spec 203 applied it twice — the interpreter made a bad value *unaddressable* (US1 there), and the evaluator's catch made a bad rule *skipped* (US2 there).

Applied here:

| Defect | Smallest unit that can honestly absorb it | Mechanism |
|---|---|---|
| 1 — depth 64 | **None needs to.** The payload is legal; it should be evaluated. | `JsonDocumentOptions { MaxDepth = 64 + 1 }` — the wrapper allows for exactly the level it adds. |
| 2 — not JSON | **This one event's evaluation.** There is no field or rule to narrow to: no rule can evaluate against a context that does not exist. | Narrow `catch (JsonException)` around the parse → `Warning` log → `return` (message acknowledged, not dead-lettered). |
| 3 — undisposed | n/a — no behaviour | The caller owns the document for the duration of `Evaluate` (`using`), exactly as `DryRunRuleQueryHandler` already does. |

**Deliberate divergence from the issue text:** the issue proposes *"one narrow try/catch closes defects 1 and 2 together"*. It would — but by **skipping** a legal depth-64 payload rather than evaluating it, which is a smaller loss than a dead-letter but still a loss, and spec 203's own reasoning (US1 there: "the rule evaluates anyway" beats "an error log plus a skipped rule") argues against it. The `MaxDepth` fix costs one options value.

**Deliberate divergence from spec 203's catch shape:** spec 203 replaced an enumerated filter with `catch (Exception) when (exception is not OperationCanceledException)`, because it guarded an interpreter with many throw sites and a list that had drifted twice. Here the guarded call is **one BCL method** whose documented failure for bad input is `JsonException` (`JsonReaderException` derives from it). Its only other throw — `ArgumentOutOfRangeException` for a negative `MaxDepth` — is caused by our constant, not the input, and should crash loudly in tests. The closer precedent is the **same handler's own** `FabIdentifier` guard at `:64-78`: narrow type, named log, `return`. That is the shape mirrored.

## Locked technical choices

| Concern | Choice | Source |
|---|---|---|
| Messaging | Wolverine; dead-letter policy **not** changed | ADR-0042, ADR-0088 |
| Contract | `FabEventIngestedV1` unchanged — no `V<N>` bump | ADR-0040, ADR-0073 |
| Evaluation input | `EvaluationContext` (readonly record struct over a `JsonElement`) unchanged, stays non-owning | ADR-0099 |
| Logging | one new `[LoggerMessage]` in `src/Automation/Application/Log.cs` | ADR-0050 |
| Guards | `Ensure.That(...)` — unchanged, none added | ADR-0105 |
| Tests | xUnit + Shouldly, sentence-style names, existing `InMemoryRuleCache` / `FakeEventBus` / `CapturingLogger` fakes | ADR-0052, ADR-0053, ADR-0054 |
| Coverage | Application ≥ 80 % | ADR-0065 |

## Latency-budget impact

**Leg: *Event → overlay state (RabbitMQ + projection) ≤ 200 ms* (constitution §IV).** The handler's context build runs on the projection half of that leg for every ingested event.

- **Success path: non-negative.** Passing `JsonDocumentOptions` changes no parsing work. `using` returns two pooled arrays per event instead of abandoning them: measured **−24.6 KB/event** (10 KB payload) to **−65.8 KB/event** (60 KB payload) allocated, which reduces GC pressure on this path. No new allocation is introduced (a `try` block costs nothing until something throws).
- **Failure path:** a thrown exception, a full unwind, Wolverine's retry/dead-letter round-trip are replaced by one caught exception and one `Warning`. Today that path produces no overlay at all.
- **Evidence at phase 5:** the existing `AelInterpreterBenchmarkTests` does not cover this method, so it proves nothing here and must **not** be quoted as evidence for it. The allocation figures above are the evidence; phase 5 re-runs the scratch measurement (twice — memory note *measurement runs need repeating*) against the built handler path or states it was not re-run.

## User stories

### US1 (P1) — A payload that cannot become an evaluation context costs that event's evaluation, not a dead-letter; a legal one always becomes one

**As** a fab operator,
**I want** a payload the evaluator cannot read to be logged and skipped, and a payload EventIngestion accepted to always be evaluated,
**so that** a contract violation is visible in the log rather than silently draining into a DLQ nobody watches, and a legal payload never trips over a limit the handler itself introduced.

Behaviour-changing → **red**. Independently shippable.

### US2 (P2) — The evaluation context's document is returned to the pool once evaluation is done

**As** the operator of a 250-camera site with a steady event stream,
**I want** each event's parsed context to return its pooled buffers,
**so that** the rule fan-out does not allocate ~25–66 KB per event that the pool exists to avoid.

Behaviour-preserving → **characterisation, observed green before the change, unmodified after**. Independently shippable.

## Acceptance scenarios

Handler-level (`FabEventIngestedV1Handler.Handle` with the real `RuleEvaluator`, `InMemoryRuleCache`, `FakeEventBus`, `CapturingLogger`). Fab `munich`, source `plc`, kind `PlcCycleStart` unless stated.

### US1 — happy path

```gherkin
Scenario: A payload nested 64 deep is evaluated, not dead-lettered
  Given an active rule on (munich, plc, PlcCycleStart) with predicate '$.source == "plc"'
        and action SetVariableValue("oeeLine1", "1")
  And   an event whose Payload is 64 nested arrays around the number 1
  When  the handler handles the event
  Then  no exception escapes Handle
  And   exactly one SystemVariableValueRequestedV1 ("oeeLine1", "1") is published
  And   no Warning is logged

Scenario: An ordinary payload is unaffected
  Given the existing FabEventIngestedV1HandlerTests suite
  Then  every test passes unmodified
```

### US1 — the defect (bad request: not a context)

```gherkin
Scenario Outline: A payload that is not JSON is logged and skipped, not dead-lettered
  Given an active rule on (munich, plc, PlcCycleStart) with predicate '$.source == "plc"'
  And   an event whose Payload is <payload>
  When  the handler handles the event
  Then  no exception escapes Handle
  And   nothing is published
  And   exactly one Warning is logged naming the event identifier
  And   the logged exception is a JsonException
  Examples:
    | payload                                   |
    | ""                                        |
    | "   "                                     |
    | "{"                                       |
    | "not json"                                |
    | "{\"a\":1}}"                              |
    | null (contract violation — null! in test) |

Scenario: One level deeper than Payload allows is a malformed context, not a crash
  Given an event whose Payload is 65 nested arrays around the number 1
  When  the handler handles the event
  Then  no exception escapes Handle, nothing is published, and one Warning is logged
```

### US1 — conflict: the skip is distinguishable from the other skips

```gherkin
Scenario: An unreadable payload is logged distinctly from an absent or unparseable fab
  Given the same event identifier on three events: fab "", fab "NotAFab", and a valid fab with Payload "not json"
  When  each is handled by its own handler instance with its own CapturingLogger
  Then  the three rendered Warning messages are pairwise different

Scenario: An unreadable payload does not log the payload text
  Given an event whose Payload is "not json" padded to 60 000 characters
  When  the handler handles the event
  Then  the Warning's rendered message does not contain the payload text
  And   it does contain the payload's length
```

### US1 — auth

N/A, stated rather than omitted: the handler is a Wolverine subscriber on an internal queue with no caller identity (ADR-0088). No endpoint, scope or token is added, changed or reachable by this spec.

### US2 — characterisation (observed green on HEAD `a54b11d0` before any change, unmodified after)

```gherkin
Scenario: A numeric value computed from the payload survives disposal
  Given the existing test Matching_event_publishes_SystemVariableValueRequestedV1_with_the_causing_event_id
  Then  it publishes Value "46" before and after the change, unmodified

Scenario: A string read out of the payload survives disposal
  Given an active rule with predicate '$.payload.cycleTime <= 30' and value expression '$.payload.station'
  And   an event whose Payload is {"cycleTime":27,"station":"station-4-east"}
  When  the handler handles the event
  Then  exactly one SystemVariableValueRequestedV1 with Value "station-4-east" is published
  # Written first, observed GREEN on HEAD. This is the case a wrong disposal
  # boundary would break (ObjectDisposedException, or a value read after the
  # buffer returned to the pool), so it is the characterisation that matters.
```

## Independent end-to-end test procedure

**Honest limit, stated up front:** no current ingress can produce either US1 input (§*Reachability today*), so `POST /events/manual` cannot exercise this change. Phase 5 observes it one hop later, at the Automation queue, which is the boundary the defect lives on.

1. Boot the Aspire AppHost (one stack per machine). RabbitMQ runs with the management plugin (`src/AppHost/AppHost.cs:120-122`).
2. Mint a token from Aspire's **proxied** Keycloak endpoint. Create and publish rule **A** on `(munich, plc, PlcCycleStart)`, predicate `$.source == "plc"`, action `SetVariableValue("spec243", "1")`.
3. Record the Automation dead-letter queue depth.
4. Via the RabbitMQ management HTTP API, publish to the exchange Automation's `FabEventIngestedV1` listener is bound to a message carrying Wolverine's message-type header for `FabEventIngestedV1` and a body with a valid envelope and `"Payload": "not json"`. (If Wolverine's envelope headers cannot be reproduced by hand in reasonable time, fall back to step 4′ and say so in the note.)
   4′. Fallback: the handler-level tests from `tasks.md`, quoted verbatim, labelled *not observed on the stack*.
5. Observe: DLQ depth unchanged; one `Warning` "…payload of {PayloadLength} characters that could not be parsed (not valid JSON, or nested too deeply); no rule evaluated." in the Automation service's structured logs carrying the event identifier.
6. Repeat step 4 with a 64-deep payload: DLQ unchanged; `spec243` becomes `1` (read it from the SystemVariables list endpoint, or from the `SystemVariableValueRequestedV1` trace — memory note: create the event rather than hunt `list_traces`).
7. Repeat with an ordinary payload: `spec243` still updates — the success path is unbroken.

## Out of scope

- **Removing the re-parse** (building the envelope view without composing and re-parsing a string). It is the larger allocation (~56–256 KB/event residual) but needs `EvaluationContext` / `AelInterpreter.ResolveField` to resolve `$.payload.*` against a separate document — an AEL input-shape change under ADR-0099. Candidate follow-up issue, not a rider.
- **Raising the envelope `MaxDepth` at ingress** so a 64-deep payload can actually arrive. That is EventIngestion's contract decision, not Automation's.
- **Logging the payload text.** Up to 64 KB of producer data; the exception's line/byte position plus the length is the diagnostic.
- **`EvaluationContext` owning its document.** Rejected in `plan.md` §*Defect 3 — the decision*.

## Why one issue, two colours

CLAUDE.md: *"A refactor that is also a bug fix is two issues, because characterisation would otherwise encode the bug as the safety net."* That risk does not arise here, and the reason is checkable: the characterisation set for US2 (the existing `FabEventIngestedV1HandlerTests` plus the one string-value test) contains **no malformed or over-deep payload** — `grep -n "Payload" tests/Automation.Application.Tests/EventHandlers/FabEventIngestedV1HandlerTests.cs` shows one fixture, `{"cycleTime":27}`. Nothing in it asserts that a bad payload throws, so nothing in it encodes defects 1 or 2. The two stories touch the same method, so they ship together; `tasks.md` orders them so each colour's evidence is captured independently.
