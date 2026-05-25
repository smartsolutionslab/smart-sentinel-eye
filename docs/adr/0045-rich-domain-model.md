# ADR-0045: Domain Model Style — Rich

**Status:** Accepted
**Date:** 2026-05-25

## Context

DDD-shaped codebases can drift toward an **anemic** model (data
containers + service classes that operate on them) or stay **rich**
(aggregates and value objects own their behaviour). The constitution
implicitly assumes rich; this ADR makes it explicit and gives
reviewers a concrete rule to apply.

## Decision

Aggregates and value objects are **rich**: they expose behaviour
methods that validate invariants and raise domain events; state has
private setters.

```csharp
public sealed class Camera : AggregateRoot<CameraId>
{
    public CameraName Name { get; private set; } = default!;
    public CameraStatus Status { get; private set; } = default!;

    public Result<Unit, ConfigureStreamingError> ConfigureStreaming(
        RtspUrl url,
        Option<OnvifProfileToken> profile)
    {
        if (Status != CameraStatus.Registered)
            return new ConfigureStreamingError.WrongState(Status);

        // mutate, raise event
        Raise(new StreamingConfigured(Id, url, profile));
        return Unit.Value;
    }
}
```

- **Application layer orchestrates only** — it loads an aggregate,
  calls a behaviour method, saves the aggregate. It does not contain
  domain logic.
- **POCOs with public setters are allowed only for read models, DTOs,
  and projection records.** Never for aggregates.

## Consequences

- **Positive:** invariants live next to the data they guard.
- **Positive:** unit tests target methods on the aggregate, not service
  classes that orchestrate primitive state mutations.
- **Negative:** more friction than anemic when domain logic is
  truly trivial. Trade-off accepted for consistency.

## Alternatives Considered

- **Anemic** — Application "services" do the work. Scales poorly;
  drifts into transaction scripts. Rejected.
- **Pragmatic mix** — rich for invariant-heavy aggregates, anemic for
  thin ones. Risk of inconsistent reasoning across contexts. Stay
  rich; if an aggregate is genuinely a value container, it's
  probably a read model.
