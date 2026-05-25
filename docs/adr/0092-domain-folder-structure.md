# ADR-0092: Domain Layer Folder Structure — Per Aggregate

**Status:** Accepted
**Date:** 2026-05-25

## Context

Yumney organizes each context's Domain project by **aggregate root**,
colocating the aggregate's value objects, repository interface, and
domain events in one folder per aggregate. This trades off
"package by layer" for "package by feature" inside the Domain layer
and makes domain refactors local.

## Decision

Each bounded context's `Domain` project organizes content **per
aggregate root**, not per pattern (no global `ValueObjects/`,
`Events/`, `Repositories/` folders at the top level).

```
SmartSentinelEye.CameraCatalog.Domain/
  Camera/
    Camera.cs                       ← aggregate root
    CameraIdentifier.cs             ← typed ID (ADR-0090)
    CameraName.cs
    RtspUrl.cs                      ← value objects belonging to Camera
    OnvifProfileToken.cs
    CameraStatus.cs                 ← enum-backed value object
    ICameraRepository.cs            ← repository interface (ADR-0041)
    ICameraCatalogUnitOfWork.cs     ← optional UoW spanning aggregates
    Events/
      CameraRegisteredDomainEvent.cs ← domain events for THIS aggregate
      CameraDecommissionedDomainEvent.cs
      StreamingConfiguredDomainEvent.cs
  CameraGroup/                      ← second aggregate, same shape
    CameraGroup.cs
    CameraGroupIdentifier.cs
    ...
    Events/
      CameraGroupCreatedDomainEvent.cs
```

- **`Events/` subfolder per aggregate** — domain events are local to
  the aggregate that raises them.
- **Domain-event types named `<Aggregate><Verb>DomainEvent`** to
  distinguish them from integration events (`<Aggregate><Verb>V1`,
  ADR-0073).
- Aggregates inherit `AggregateRoot<<Aggregate>Identifier>` from
  `Shared.Kernel`.

## Consequences

- **Positive:** all domain concepts for an aggregate are in one
  folder; refactors stay local.
- **Positive:** matches Yumney verbatim.
- **Positive:** new aggregates are added as new folders without
  reshuffling shared folders.
- **Negative:** value objects shared between aggregates need a
  decision per case. Default: place in the aggregate that owns the
  invariants most strongly; reference from others. Genuine
  cross-cutting types (e.g. `IpAddress` if reused across contexts)
  go in `Shared.Kernel`.

## Alternatives Considered

- **Package by layer** (`ValueObjects/`, `Entities/`, `Events/`,
  `Repositories/`) — easier for newcomers initially; refactors
  scattered.
- **Aggregate folders with shared `_Shared/` for cross-aggregate
  types** — viable but introduces a naming convention we don't need
  yet.
