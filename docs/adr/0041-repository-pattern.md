# ADR-0041: Repository Pattern — Domain Interface, Infrastructure Implementation

**Status:** Accepted
**Date:** 2026-05-25

## Context

ADR-0001 commits us to DDD. ADR-0027 layers each context into
Domain / Application / Infrastructure / Api. We need a consistent
repository pattern that does not leak persistence concerns upward.

## Decision

Repository contracts live in **`<Context>.Domain.Repositories`** (or the
equivalent `Domain/Repositories/` folder). Implementations live in
**`<Context>.Infrastructure.Persistence`**.

- The Domain project has **zero dependency** on EF Core, Marten,
  Npgsql, or any other persistence technology.
- One repository interface per aggregate root (no generic
  `IRepository<T>`).
- Repository methods return aggregates and accept aggregates; they do
  not expose query expressions to callers.
- Variable naming for injections follows the plural convention
  (`ICameraRepository cameras`).

```csharp
// CameraCatalog.Domain/Repositories/ICameraRepository.cs
public interface ICameraRepository
{
    Task<Option<Camera>> GetByIdAsync(CameraId id, CancellationToken ct);
    Task<bool> ExistsByNameAsync(CameraName name, CancellationToken ct);
    void Add(Camera camera);
    Task SaveAsync(CancellationToken ct);
}
```

## Consequences

- **Positive:** Domain code stays pure and unit-testable without any
  framework dependency.
- **Positive:** swapping persistence (Marten ↔ EF Core ↔ Dapper) is a
  per-context, infrastructure-only change.
- **Negative:** small ceremony per aggregate. Mitigated by templating
  during scaffold work.

## Alternatives Considered

- **Generic `IRepository<T>`** — leaky abstraction; LINQ specs end up
  exposing infra detail.
- **Repository in Application** — port-and-adapter purist style. Valid
  but adds a layer we don't need.
- **No repository abstraction** — fine in CRUD-only apps; we have
  enough behavioural aggregates that the indirection earns its keep.
