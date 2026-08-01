# ADR-0043: Aggregate Persistence Concurrency — Optimistic with Explicit Version

**Status:** Accepted
**Date:** 2026-05-25
**Superseded in part by:** [ADR-0113](./0113-optimistic-concurrency-two-layer.md)

> **Read ADR-0113 before acting on this one.** Three statements below are
> superseded: the Marten exemption for Overlays and Automation (both are
> EF Core, so nothing is exempt), the retry-once instruction (harmful —
> it re-applies a mutation over the other writer's work), and the
> implicit assumption that a database concurrency token is sufficient
> (it catches only overlapping transactions, not the cross-request case
> this ADR's own motivating scenario describes). ADR-0113 also records
> that the mechanism specified here was never actually implemented.

## Context

Multiple operators and automation rules can issue commands against the
same aggregate within milliseconds (e.g. two operators both trying to
bind to a kiosk; an automation rule rotating cameras while an operator
manually switches one). Lost updates would silently corrupt state.

## Decision

**Optimistic concurrency with an explicit `Version` field on every
aggregate root.**

```csharp
public abstract class AggregateRoot<TId>
{
    public TId Id { get; protected set; } = default!;
    public int Version { get; protected set; }
    // ... domain events ...
}
```

- Postgres CRUD repositories include `WHERE version = @v` in updates;
  a mismatched version throws `ConcurrencyException`.
- **For Marten event-sourced contexts** (Overlays, Automation per
  ADR-0009), Marten's stream-version mechanism provides the same
  guarantee automatically — we do not duplicate the version field
  inside the event stream.
- Application handlers **retry once** on `ConcurrencyException` and
  then surface a `Result<T, Conflict>` failure to the caller.

## Consequences

- **Positive:** lost-update bugs become impossible to produce
  silently — they fail loudly with a typed error.
- **Positive:** no DB-held locks; throughput stays high.
- **Negative:** every aggregate inherits a base class with the
  `Version` property — minor coupling, justified by ubiquity.

## Alternatives Considered

- **Postgres `xmin`** — leaks DB semantics into the domain.
- **No concurrency control** — silent data loss; unacceptable.
- **Pessimistic `SELECT FOR UPDATE`** — held locks hurt throughput
  and don't scale to the 250-camera concurrent-write profile.
