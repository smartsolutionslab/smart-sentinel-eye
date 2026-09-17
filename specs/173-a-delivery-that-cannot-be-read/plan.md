# Spec 173 — Plan

**Issue**: #2203 · **Branch**: `fix/2203-a-delivery-that-cannot-be-read` · **Phase**: 2 (Plan)

**Spec**: `specs/173-a-delivery-that-cannot-be-read/spec.md`

---

## Bounded context and layers

**One context: `EventIngestion`.** No cross-context reference is added or needed;
nothing in `Shared.Contracts` changes. The three affected files sit in three
layers of that one context:

| Layer | File | Change |
|---|---|---|
| Domain | `src/EventIngestion/Domain/Event/Payload.cs` | New `From(JsonElement)` factory that rejects an absent element |
| Infrastructure | `src/EventIngestion/Infrastructure/Ingress/MqttSubscriberHostedService.cs` | Call site `:228`; visibility seam for the unit red |
| Api | `src/EventIngestion/Api/EventsEndpoints.Writes.cs` | Call sites `:85`, `:162` |

No new project, no new registration, no Aspire resource, no migration, no message
contract. Nothing foundational blocks anything else.

---

## The shape of the fix, and why it is not the obvious one

#2203 proposes widening `:230`'s catch to match `:220`. **Two candidates were
weighed; the plan takes the second.**

### Candidate A — widen the catch

```csharp
catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
```

One line per site, three sites. It reaches the dead-letter path, which is the
issue's core claim. **It fails SC-3.** The reason string an operator then reads in
`dead_letters.error` is `payload rejected: Operation is not valid due to the
current state of the object.` — the BCL's generic message for "this struct has no
parent". The row exists and says nothing. It also fixes each site three times
over, in three catches, leaving the *cause* unguarded in all three.

### Candidate B — the value object rejects the absent element (**chosen**)

```csharp
// Payload.cs
public static Payload From(JsonElement element)
{
    Ensure.That(element.ValueKind != JsonValueKind.Undefined, ...)   // or equivalent
    return From(element.GetRawText());
}
```

and each call site becomes `Payload.From(body.Payload)` /
`Payload.From(payload.Payload)`.

Why this one:

1. **It puts the rule where the rule lives.** `Payload` already decides what a
   valid payload is — valid JSON, ≤ 64 KB — and already has a `From(JsonDocument)`
   overload, so `System.Text.Json` is established in this Domain file (`Payload.cs:2`).
   ADR-0038/0046/0066: the value object validates, the caller does not.
2. **It makes the existing catches correct rather than wider.** An
   `ArgumentException`-family throw is what `:230`, `:87` and `:164` already catch,
   so **no catch changes at all** — the smallest change (ADR-0036), and the
   asymmetry with `:220` becomes *correct* rather than merely tolerated: after the
   fix, the only call in that block provably cannot throw
   `InvalidOperationException`.
3. **One guard, three sites.** Candidate A repeats itself three times and would
   have to be repeated again at the fourth `JsonElement` payload site someone adds.
4. **It authors the operator-facing message once.**

**Constraint on the guard:** it must throw from the `ArgumentException` family
(`Ensure` throws only `ArgumentNullException` / `ArgumentException` —
`src/Shared.Kernel/Ensure.cs`). A guard that throws anything else re-creates the
defect in a new place.

**Constraint on its condition:** `ValueKind == JsonValueKind.Undefined`, and
nothing broader. `Null` and `String` are accepted on `develop` and stay accepted
(SC-4). Do not write `ValueKind != JsonValueKind.Object`.

**`JsonElement` as a Domain method parameter is permitted here.** §II /
`PrimitiveBoundaryTests` bans primitive-typed *state* — the test scans public
properties (`PrimitiveBoundaryTests.cs:180`), not parameters — and
`From(JsonDocument)` is the standing precedent in the same file.

---

## Entities, value objects, invariants

Only one type changes, and only by gaining a factory.

**`Payload`** (`EventIngestion.Domain.Event`, `IValueObject<string>`) —
existing invariants, all unchanged:

- the value is valid JSON (`JsonDocument.Parse`, malformed ⇒ `ArgumentException`);
- canonical-form UTF-8 ≤ 64 KB (`MaximumBytes`), else `ArgumentException`;
- schema is never inspected — EventIngestion does not look inside.

**New invariant, stated precisely:** *a `Payload` cannot be built from a
`JsonElement` that is not there.* `JsonValueKind.Undefined` is the absence of a
JSON value, not a JSON value; every other `ValueKind` — including `Null` — is a
value and remains acceptable.

No aggregate changes. `EventEnvelope`'s construction is untouched. No new value
object is introduced.

---

## Messaging

**Nothing.** No domain event, no integration event, no contract version. The
change is entirely on the *rejection* path, which by design fans out nothing —
spec 006 FR-015 records dead letters as audit-only. `EventIngestedV1` is published
only for deliveries that parse, and which deliveries parse does not change: a
message missing `payload` was never going to become an event. It changes from
*never answered* to *recorded and released*.

---

## Behaviour after the fix, stated as the thing the tests assert

This is #2203's "check what `:220` actually does before copying it", written down
as the target rather than assumed. Traced through
`MqttSubscriberHostedService.OnMessageReceived`:

1. `TryParseEnvelope` returns `ParseResult(null, "<reason>")` — no throw.
2. `result.Envelope is null` ⇒ `CaptureDeadLetterAsync(topic, body, reason)`.
3. That logs `RejectingMqttDelivery`, resolves the fab from the topic (or counts
   it unattributable — FR-012), writes one `dead_letters` row and saves.
4. **Only if the save succeeded** (`return true`) is `args.AcknowledgeAsync` called.

So the asserted behaviour is: **one dead-letter row, then one acknowledgement, and
no redelivery** — with the deliberate exception that a *failed capture* leaves the
delivery unacknowledged so the broker brings it back and the capture is retried
(`:139-144`). That exception is correct and is **not** what this defect is: a
database outage is transient and the retry ends; an unparseable payload is
permanent and the retry does not. Phase 4 must not "simplify" it away.

The HTTP sites have no dead letter — their equivalent is
`Results.Problem(title: "EVENT_INVALID_INPUT", …, 400)`, already written at
`EventsEndpoints.Writes.cs:87-91` and `:164-168` (ADR-0047/0089).

---

## Test seam for the unit red

`TryParseEnvelope` and its `ParseResult` are `private static` inside the hosted
service, and the hosted service takes a concrete `MosquittoConnectionFactory` that
builds a real `MqttClient` — so the handler cannot be driven from a unit test as
it stands.

**Seam: change `private static` to `internal static` on `TryParseEnvelope` and on
the nested `ParseResult` record.** `InternalsVisibleTo` for
`EventIngestion.Infrastructure.Tests` already exists
(`SmartSentinelEye.EventIngestion.Infrastructure.csproj:8`), and
`MqttConnectionLoop` is already `internal` and tested that way.

This is a **visibility-only change with no behaviour**, made by the test-writer in
phase 4a so the red can be observed. It is called out here so review does not read
it as the engineer editing production code inside the test commit. **No other
production edit belongs in the 4a commit.**

Do **not** introduce an interface over `MosquittoConnectionFactory` to make the
hosted service injectable — that is speculative generality (ADR-0036) for one
test, and the integration test covers the ACK behaviour the seam cannot.

---

## Boundary rules

- No cross-context project reference is added; `Shared.Contracts` is untouched.
- Domain gains no I/O and no framework reference — `System.Text.Json` is already
  referenced by `Payload.cs`.
- Api and Infrastructure both call the Domain factory; neither reimplements the
  guard.
- NetArchTest, `PrimitiveBoundaryTests` and `HandlerDeconstructionTests` are
  unaffected (no new handler, no new domain property).

---

## Risks

| Risk | Handling |
|---|---|
| The MQTT red leaves an un-ACKed delivery in the broker's persistent session | Known and bounded — the fix drains it on the next redelivery (spec step 3). Do not clear the Mosquitto volume to "clean up"; that would erase the evidence and is not needed |
| An over-broad guard rejects `payload: null` or a scalar payload | SC-4 pins both green, before and after. The condition is `Undefined` and nothing else |
| Widening a catch "while in there" | Explicitly not done. If candidate B is implemented, no catch changes; a diff that touches `:220`, `:230`, `:87`, `:164` or `:252` is out of scope |
| The 500-vs-400 premise (A1) proves wrong | Phase 4a observes the real status and reports it; the spec is corrected, the test is not bent |
| The dead-letter reason needs to change meaning, not just exist | That would be a design decision — **stop and escalate**, do not decide it (spec, Why) |
