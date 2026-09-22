# Plan — Spec 213, the reason a rejection already had

**Spec:** `specs/213-the-reason-a-rejection-already-had/spec.md`
**Issue:** #2428
**Bounded context:** `EventIngestion` — **one context, no cross-context traffic.** No `Shared.Contracts` message is added or changed, so the no-cross-context-reference rule (NetArchTest) is satisfied trivially: nothing new crosses a boundary at all.

---

## Layers touched

| Layer | Files | Nature of change |
|---|---|---|
| **Domain** | — | **None.** `RejectionReason`, `DeadLetter`, `DeadLetterConfiguration` are all unchanged. No new value object, no migration, no primitive added to any domain model (constitution §II, `PrimitiveBoundaryTests` unaffected). |
| **Application** | `Commands/IngestEventBatchResult.cs`, new `Commands/RefusedEnvelope.cs`, `Commands/Handlers/IngestEventBatchCommandHandler.cs` | The refusal gains a reason field; `Build` changes return type. |
| **Infrastructure** | `Ingress/PersistenceLoopHostedService.cs`, `Log.cs` | The loop threads the reason from each site to the writer; one log signature. |
| **Api** | — | **None.** `DeadLetterDto` and `GET /dead-letters` are byte-for-byte unchanged. |

ADR-0093 (per-message-kind Application layout) places `RefusedEnvelope` next to `IngestEventBatchResult` in `Application/Commands/` — it is part of a command's result, not a DTO, and `DTOs/` holds the read-side shapes.

---

## The data-shape decision

Three candidate shapes were weighed. The middle one is chosen.

### Rejected — the issue's own suggestion: `IReadOnlyList<(EventEnvelope Envelope, string Code)>`

Works, and is the smallest diff. Rejected for three reasons:

1. **It throws away the `Message`.** `Code` alone gives the row `EVENT_OCCURRED_AT_TOO_FAR_IN_FUTURE` — better than today's lie, but still not *"check the source's clock"*. Substituting a terser token for a richer one that was already in hand is a smaller version of the same defect.
2. **A positional tuple of two references reads badly at the call site** and, unlike a named record, gives the compiler nothing to object to if the pair is ever transposed — the hazard CLAUDE.md's deconstruction rule and `HandlerDeconstructionTests` exist for.
3. **`string Code` is a primitive where a typed error already exists.** `IngestEventError` is the repo's carrier for exactly this (ADR-0047/ADR-0089) and it is already constructed at both discard sites.

### Rejected — a new `DeadLetterReason` value object in Domain

Would give the reason a typed home and enforce the 512 bound at construction. Rejected: `RejectionReason` **is** that value object and already enforces that bound. A second one would be a duplicate with a different name, and ADR-0036 forbids the abstraction that exists for no need.

### Chosen — carry the typed error, then turn it into the domain value object once, at the writer

```csharp
// src/EventIngestion/Application/Commands/RefusedEnvelope.cs  (new)
public sealed record RefusedEnvelope(EventEnvelope Envelope, IngestEventError Reason);

// src/EventIngestion/Application/Commands/IngestEventBatchResult.cs  (widened)
public sealed record IngestEventBatchResult(IReadOnlyList<RefusedEnvelope> Refused);
```

The Application layer speaks in typed errors — its own vocabulary. The Infrastructure loop converts to `RejectionReason` at the single point that writes a row. The domain value object never leaks upward and the application error never leaks into the domain.

---

## Entities, value objects, invariants

No aggregate changes. The invariant this spec adds lives in the loop and is enforced **by construction**, which is the substance of the fix.

### The invariant: a rejection cannot exist without a reason

Today `Outcome` is a bare enum (`PersistenceLoopHostedService.cs:85-95`). `Outcome.Rejected` is representable with no reason attached — which is precisely why a reason had to be invented at the writer. Threading an `Option<RejectionReason>` alongside the enum would fix today's three sites while leaving a fourth site free to reintroduce the defect.

Replace the enum with a private, file-local closed shape:

```csharp
private readonly record struct Ending(Outcome Outcome, Option<RejectionReason> Reason)
{
    public static Ending Stored { get; } = new(Outcome.Stored, Option<RejectionReason>.None);
    public static Ending Failed { get; } = new(Outcome.Failed, Option<RejectionReason>.None);
    public static Ending Rejected(RejectionReason reason) =>
        new(Outcome.Rejected, Option<RejectionReason>.Some(reason));
}
```

`Ending.Rejected` has no parameterless form, so **a rejection with no reason cannot be written.** That is the whole reason this shape is preferred over a second parameter, and it costs about twelve lines. ADR-0036's "smallest possible change" governs *scope* — it does not ask for a shape that leaves the bug representable.

The `Outcome` enum itself and its three doc comments stay exactly as they are; `Ending` wraps it.

`Option<T>` rather than `RejectionReason?` (ADR-0141): the file already speaks `Option` at `:179` and `:322`, and the field says what absent *means* — "this ending owes nobody a record".

### The reason, composed once

```csharp
private static RejectionReason Because(IngestEventError error)
{
    string text = $"{error.Code}: {error.Message}";
    return RejectionReason.From(text.Length <= RejectionReason.MaximumLength
        ? text
        : text[..RejectionReason.MaximumLength]);
}
```

The truncation is **argued, not defensive habit**: `RejectionReason.From` runs `HasMaxLength(512)` and throws above it; that throw is caught by `RecordRejectionAsync` (`:310`), returns `false`, and puts the delivery back on `carried` (`:257`) — permanently, since the next attempt composes the same over-long string. Spec 018 FR-008's record-then-release contract would be broken by a value the code itself produced. `error.Message` interpolates caller-supplied data (`IngestEventErrors.cs:29` embeds `occurredAt`), so this is reachable, not theoretical.

---

## Call-site-by-call-site

### `:194` — `StoreArrivalsAsync`, the batch refusal

`HashSet<EventIdentifier> refused` becomes a dictionary, because the identity is no longer the only thing needed:

```csharp
Dictionary<EventIdentifier, IngestEventError> refused =
    result.Value.Refused.ToDictionary(r => r.Envelope.Identifier, r => r.Reason);

foreach (IngestDelivery delivery in arrived)
{
    Ending ending = refused.TryGetValue(delivery.Envelope.Identifier, out IngestEventError? reason)
        ? Ending.Rejected(Because(reason))
        : Ending.Stored;

    await CompleteAsync(delivery, ending, cancellationToken);
}

return arrived.Count - refused.Count;
```

`refused.Count` at `:198` keeps its meaning — a dictionary keyed on the identifier has the same cardinality the `HashSet` had, and the batch handler already guarantees one entry per refused envelope (`seen.Add` dedupes before `refused.Add`).

### `:368` — `StoreOneAsync`, the first-attempt rejection

Return type `Task<Outcome>` → `Task<Ending>`. The three returns become `Ending.Stored`, `Ending.Failed`, and:

```csharp
return result.Error is IngestEventError.EventAlreadyIngested
    ? Ending.Stored
    : Ending.Rejected(Because(result.Error));
```

The `EventAlreadyIngested` branch keeps its exact meaning and keeps its comment — the redelivery **is** stored, and dead-lettering it would be a new defect (spec §Acceptance, conflict scenario). No reason is composed on that branch.

### `:215` — `RetryAsync`, the genuinely exhausted case

```csharp
Ending ending = await StoreOneAsync(delivery, cancellationToken);
if (ending.Outcome == Outcome.Failed && Exhausted(delivery.Envelope.Identifier, window))
{
    ending = Ending.Rejected(RejectionReason.From($"not storable after {window} of retrying"));
}
```

**The sentence moves; it does not change.** Two existing assertions depend on it (spec §Problem, table) and both must stay green *unmodified*. The sentence now lives at the one site where it is true, which is the whole point.

### `RecordRejectionAsync` — the writer

Signature gains the reason and **loses its ability to invent one**:

```csharp
private async Task<bool> RecordRejectionAsync(
    IngestDelivery delivery, RejectionReason reason, CancellationToken cancellationToken)
```

`TimeSpan window = retry.Value.MaximumRetryWindow;` is deleted from this method. That deletion is the fix: after it, no writer can reach `retry.Value` and there is nothing left to fabricate from.

`CompleteAsync` unwraps: `Outcome.Rejected` implies `Reason.HasValue` by `Ending`'s construction, so the call is `RecordRejectionAsync(delivery, ending.Reason.Value, cancellationToken)`.

### `Build` — `IngestEventBatchCommandHandler.cs:133-156`

`Option<EventAggregate>` → `Result<EventAggregate, IngestEventError>` (ADR-0047). The catch constructs the error **once** and uses it twice — log, then return — instead of constructing it to read `.Code` and dropping it:

```csharp
catch (ArgumentException)
{
    IngestEventError reason = IngestEventFailures.OccurredAtTooFarInFuture(envelope.OccurredAt.Value);
    logger.BatchEnvelopeRejected(envelope.Identifier, envelope.Source, envelope.Device, reason.Code);
    return Failure(reason);
}
```

`HandleAsync`'s loop at `:82-92`: `List<EventEnvelope> refused` → `List<RefusedEnvelope> refused`, and the `else` adds `new RefusedEnvelope(envelope, built.Error)`. `logger.BatchEnvelopeRejected` keeps its `string code` parameter — the log line is not part of this change.

### `Log.cs:115` — `IngestAbandoned`

```csharp
IngestAbandoned(ILogger, EventIdentifier, FabIdentifier, TimeSpan window)
```

The log is the operator's *other* post-mortem surface, and it currently repeats the same fabrication: every abandonment is logged with the window whether or not one elapsed. Change the parameter to the reason:

```csharp
IngestAbandoned(ILogger, EventIdentifier, FabIdentifier, string reason)
```

In scope because it is the same defect in the same method, on the same value, and fixing the row while leaving the log lying would leave a reviewer two contradictory records of one event. `logger.IngestAbandoned(envelope.Identifier, envelope.Fab, reason.Value)` at `:307`.

---

## Messaging — domain → integration event

**None.** No domain event is raised, no integration event is published, no `Shared.Contracts` type is touched. `DeadLetter` is explicitly audit-only with no fan-out (`DeadLetter.cs:7-8`), and the persistence loop's messaging surface is unchanged.

Consequently: no Wolverine queue, no outbox row, no consumer to version, no `V<N>` contract bump.

---

## Boundary rules

| Rule | How this plan satisfies it |
|---|---|
| No cross-context project references | Nothing outside `src/EventIngestion` is read or written. |
| Communication between contexts only via `Shared.Contracts` | No inter-context communication exists here. |
| Domain has no I/O and no framework refs | Domain is untouched. |
| Application does not reference Infrastructure | `RefusedEnvelope` and `IngestEventBatchResult` live in Application and name only `EventEnvelope` (Application) and `IngestEventError` (Application). The Infrastructure loop depends downward on them, as it already does. |
| No primitive on a domain model (§II, `PrimitiveBoundaryTests`) | The only `string` introduced is a local inside `Because`, and `Ending` is a private Infrastructure struct, not a domain model. |
| `Ensure.That` for argument guards (ADR-0105) | `HandleAsync`'s existing guard is untouched; no new public entry point is added, so no new guard is owed. |

`tests/Architecture.Tests` needs no new rule and should require no change. If `OutboxCommitTests` reacts to the `IngestEventBatchResult` shape, that is a signal to investigate, not to suppress.

---

## US2 (P2) — the exhausted row names its last failure

Separable, and deliberately planned but not merged into US1.

`failingSince` (`:76`) is `Dictionary<EventIdentifier, DateTimeOffset>`. Widen the value:

```csharp
private readonly Dictionary<EventIdentifier, IngestFailure> failingSince = [];
private readonly record struct IngestFailure(DateTimeOffset Since, string LastError);
```

`NoteFailure(identifier)` gains the exception and records `ex.Message` (not `ToString()` — a stack trace would crowd a 512-char column; recorded as an assumption in the spec), overwriting `LastError` each time while keeping the original `Since`. `Exhausted` reads `.Since`. The `:215` reason becomes:

```csharp
RejectionReason.From($"not storable after {window} of retrying; last failure: {lastError}")
```

composed through the same truncating helper. Both existing `not storable` assertions use `ShouldContain`, so **appending keeps them green** — which is exactly why US2 is an append and not a rewrite.

`NoteFailure` is called from one place (`:372`), so the signature change is local.

---

## Risks

| Risk | Handling |
|---|---|
| An implementer "fixes" `IngestEventBatchCommandHandlerTests.cs:115` by weakening it | Spec calls it out as mechanical-only; tasks pin the exact allowed edit. A weakened gate is a blocked outcome (ADR-0144). |
| The `:215` sentence changes by accident while its call site moves | Two existing assertions (`ShouldContain`) cover it and must pass **unmodified**. Named in the tasks as a standing check, not a hoped-for side effect. |
| An implementer dead-letters `EventAlreadyIngested` while threading the reason | Its own Gherkin scenario and its own RED test (spec §Acceptance, conflict). |
| The reason exceeds the column and strands the delivery forever | `Because` truncates; its own RED test with a synthetic over-long error. |
| Someone concludes the reason is always `EVENT_OCCURRED_AT_TOO_FAR_IN_FUTURE` and hard-codes it | Spec §Narrowing records that it is today's only reachable value and why that must not shorten the mechanism. |

---

## Definition of done

1. A dead letter from `:194` and one from `:368` each carry their typed code and message; one from `:215` still carries the window sentence — **all three observable in one listing**.
2. The two existing `not storable` assertions pass **unmodified**.
3. `IngestEventBatchCommandHandlerTests.cs:115` gained `.Envelope` and a `.Reason` claim, and lost nothing.
4. `RecordRejectionAsync` contains no reference to `retry.Value`.
5. Format and analyzers clean; Domain ≥ 90% / Application ≥ 80% coverage gates hold (ADR-0065).
6. The end-to-end procedure in the spec observed by a person, including its step-7 counterfactual.
