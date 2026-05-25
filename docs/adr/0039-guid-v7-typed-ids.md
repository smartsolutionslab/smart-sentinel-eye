# ADR-0039: Aggregate IDs — Guid v7 in Strongly-Typed Records

**Status:** Superseded by ADR-0090 (Identifier suffix replaces Id suffix)
**Date:** 2026-05-25

## Context

ADR-0006 (on-prem-first + cloud-ready) requires idempotent IDs so a
future cloud sync layer can replicate writes without server-side
allocation. ADR-0038 (maximalist VOs) wraps all primitives — IDs in
particular.

## Decision

All aggregate identifiers use **Guid v7** wrapped in strongly-typed
records.

```csharp
public readonly record struct CameraId(Guid Value)
    : IValueObject<Guid>
{
    public static CameraId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}
```

- Persisted in Postgres as native `uuid`.
- Sortable (v7 encodes time in the high bits → DB-index-friendly).
- Client-generatable (satisfies ADR-0006 idempotency).
- Pattern repeated for every aggregate root: `CameraId`, `LayoutId`,
  `OverlayId`, `OperatorId`, etc.

## Consequences

- **Positive:** strong typing eliminates ID mix-ups at compile time.
- **Positive:** sortable IDs reduce B-tree index fragmentation in
  Postgres.
- **Positive:** no central ID generator service required.
- **Negative:** ID-as-string display is 36 chars (no compaction).
  Acceptable for an internal industrial system; reconsider if user-
  facing URL ergonomics matter.

## Alternatives Considered

- **ULID** — sortable, shorter base32 display, but adds a third-party
  NuGet and manual Postgres mapping.
- **Database-generated `bigint`** — conflicts with idempotency.
- **Plain Guid v4** — not sortable; worse DB-index behaviour.
