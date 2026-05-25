# ADR-0071: CQRS Read Model Strategy — Marten Event Streams + Inline Projections

**Status:** Accepted
**Date:** 2026-05-25

## Context

ADR-0009 reserves event sourcing for contexts where invariants justify
it — initially Overlays and Automation. Event sourcing buys
auditability and replay, but reads off the event stream are slow.
A read model layer is required.

## Decision

For event-sourced contexts:

- **Marten stores the event stream** (source of truth) in Postgres.
- **`ProjectionLifecycle.Inline` projections** write denormalized
  Postgres tables in the **same transaction** as the event append.
- **Read-side handlers query the projected table** directly via
  Marten's session — no event replay at query time.

```csharp
// OverlayDesigner.Infrastructure/Marten/MartenOverlayDesignerConfig.cs
opts.Projections.Add<OverlayDetailsProjection>(ProjectionLifecycle.Inline);
opts.Projections.Add<OverlaysListProjection>(ProjectionLifecycle.Inline);

// OverlayDesigner.Application/Queries/GetOverlayDetailsHandler.cs
var overlay = await session.LoadAsync<OverlayDetailsReadModel>(query.OverlayId, ct);
```

- **Async projections** (`AsyncProjection`) are reserved for
  analytics-style read models that tolerate lag.

## Consequences

- **Positive:** event stream gives audit + replayability.
- **Positive:** reads are simple SELECTs against a denormalized table
  — no read-after-write surprises.
- **Positive:** no separate read store to operate.
- **Negative:** inline projections add to the command-path latency.
  Acceptable; measured per leg against the latency budget (ADR-0015).

## Alternatives Considered

- **Async projections only** — read-after-write surprises; bad for
  operator UX.
- **No CQRS in event-sourced contexts** — reads off the event stream
  scale poorly.
- **Separate read store (Redis, Elasticsearch)** — extra operational
  burden; not justified at our scale.
