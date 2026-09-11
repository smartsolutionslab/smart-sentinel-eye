# Plan — spec 133

## Shape

Two moves, in this order, because the second is impossible without the first.

1. **Carry the leg's start to where the leg ends.** `EventMetadata.RootIngestedAt`
   already travels from EventIngestion to Automation to `SystemVariableValueRequestedV1`.
   It is dropped at the value write. Thread it the remaining four hops so
   `ResolvedOverlayTextChangedV1` carries it.
2. **Record at the push.** `ResolvedOverlayTextChangedV1Handler` gains
   `ILatencyBudget` and records after `broadcaster.ResolvedOverlayTextChangedAsync`,
   matching `OverlayHighlightRequestedV1Handler.cs:49` line for line.
   `SystemVariableValueRequestedV1Handler` loses the dependency entirely.

## The propagation, hop by hop

| Hop | File | Change |
|---|---|---|
| message → command | `SystemVariables/Application/EventHandlers/SystemVariableValueRequestedV1Handler.cs` | pass `metadata.RootIngestedAt` as `Option<DateTimeOffset>` |
| command | `SystemVariables/Application/Commands/SetVariableValueCommand.cs` | new component `Option<DateTimeOffset> RootIngestedAt` |
| command → aggregate | `SystemVariables/Application/Commands/Handlers/SetVariableValueCommandHandler.cs` | deconstruct it, forward it |
| aggregate → domain event | `SystemVariables/Domain/Variable/Variable.cs` | `SetValue` takes it and stamps it on the raised event |
| domain event | `SystemVariables/Domain/Variable/Events/VariableValueChangedDomainEvent.cs` | new component |
| domain event → integration event | `SystemVariables/Application/EventHandlers/VariableValueChangedDomainEventHandler.cs` | fill `EventMetadata`'s fifth argument |

### Why through the aggregate rather than around it

The three alternatives were considered and rejected:

- **A scoped ambient carrier** set by the message handler and read by the domain
  event handler. Smaller diff, but it is a hidden channel across an aggregate
  boundary whose correctness depends on `DomainEventDispatcher` resolving from
  the same scope — an implementation detail in `ServiceDefaults`. A value that
  travels invisibly is exactly the failure class this issue is fixing.
- **Envelope-level causation propagation** in the event bus, so every outbound
  message inherits the inbound message's root. That is the general answer and it
  is a messaging-infrastructure change serving one leg. Speculative generality.
- **Re-deriving the root downstream** from `CausingEventIdentifier` by querying
  EventIngestion. A cross-context read on the 200 ms path.

Explicit threading is also what the repository already does one hop upstream:
`Automation/…/FabEventIngestedV1Handler.cs:85` forwards `ingestedAt` rather than
re-stamping, and says why in a comment. This is the same move, continued.

`SetValue` already takes `IClock` for no reason other than to stamp the event it
raises. `rootIngestedAt` is the same kind of argument — event-stamping input the
aggregate does not store and no invariant depends on. It ends at four parameters,
which is ADR-0084's limit and not past it.

`Option<DateTimeOffset>` rather than `DateTimeOffset?` inside Domain and
Application (ADR-0141, CLAUDE.md). The value becomes a plain nullable again only
at the `Shared.Contracts` wire boundary, which is where nullable is the native
vocabulary and where `EventMetadata` already declares it.

### What is not touched

- `src/Shared.Contracts/*`, `src/Shared.Kernel/*`, `src/AppHost/AppHost.cs` —
  contention files (ADR-0109). `EventMetadata` already has the field; nothing to add.
- `VariableArchivedDomainEvent` / `Variable.Archive`. An archive has no
  plant-floor root, so there is nothing to carry. Its `ResolvedOverlayTextChangedV1`
  keeps a null root and records nothing, which is FR-004 working.
- `.specify/memory/constitution.md` — blocked outcome (ADR-0144).

## Latency (constitution §IV)

**This is §IV's `event → overlay state` leg (≤ 200 ms), and it is the instrument
for that leg that changes — not the leg.**

- **What the instrument now measures:** plant-floor acceptance
  (`EventMetadata.RootIngestedAt`, stamped by EventIngestion) through to the
  server having pushed the overlay's new state onto `/hubs/layouts`. Identical
  for both effects on this leg.
- **What it measured before, for the variable effect:** acceptance through to the
  value row being written — a prefix of the above, roughly 750 ms short of it
  per #2072.
- **No leg's actual cost changed.** Nothing is published, awaited or ordered
  differently. The added work is one interface call and a subtraction, after the
  broadcast has already returned.
- The 200 ms budget question is #2072's and stays there. An honest instrument
  makes the breach continuously visible; it does not create or settle it.

## Documenting the step change

`ILatencyBudget.RecordEventToOverlayState` and `LatencySegment.EventToOverlayState`
are the two places a reader learns what this number means. Both get a paragraph
saying the measurement point moved, what it used to stop at, and that a figure
from before the change is not comparable with one after it. There is no dashboard
and no retained series to annotate (ADR-0118; spec.md §3).

## Testing — phase 4a colour: RED, behaviour-changing

Three reds, and the first is the load-bearing one because it asserts the **span**.

- **The span ends at the push.** A `ResolvedOverlayTextChangedV1Handler` whose
  broadcaster advances a fake clock by 750 ms during the push must record a
  duration of at least 750 ms. Red today: the handler records nothing.
  **The assertion discriminates the call site**: place the recording before the
  broadcast and the duration is zero and the test fails. That counterfactual is
  run, not asserted in prose.
- **The start survives to the push.** `ResolvedOverlayTextChangedV1.Metadata.RootIngestedAt`
  equals the acceptance moment of the `SystemVariableValueRequestedV1` that
  caused it, end to end through the command handler and the aggregate. Red today:
  null.
- **The write does not end the leg.** `SystemVariableValueRequestedV1Handler`
  reports nothing to the budget. Red today: it reports once.

The existing `An_effect_with_no_plant_floor_root_is_not_timed` (SystemVariables)
moves to LayoutComposition's resolved-text handler unchanged in substance: the
absent moment is still handed to the budget rather than substituted with a zero,
just from the handler that now owns the measurement.

Unit level throughout. No Aspire stack — the span under test is a handler's own
interval and a fake clock proves it more exactly than a wall-clock run would,
and #2072 has already measured the real remainder.

## Verification (phase 5)

`dotnet build -c Release`, then the four affected test projects. `verification.md`
records the red output verbatim, the green, the counterfactual, and states what
the instrument now measures and that no leg's cost moved.
