# ADR-0049: Async + Cancellation Conventions

**Status:** Accepted
**Date:** 2026-05-25

## Context

A 24/7 system under a strict latency SLO (ADR-0015) cannot afford
runaway work surviving past a cancelled request. Async correctness
also matters for thread-pool starvation under 250-camera load.

## Decision

- **Every public async method takes `CancellationToken` as its last
  parameter.** Enforced by a Roslyn analyzer rule (configured in
  `Directory.Build.props`).
- **Tokens chain through every layer** — HTTP request cancellation
  flows down through Api → Application → Domain method (where async)
  → Infrastructure (DB, HTTP, message bus calls).
- **No `ConfigureAwait(false)`** in application code. ASP.NET Core has
  no synchronization context; the framework already resumes on the
  thread pool. `ConfigureAwait` is library-author noise that hurts
  readability without changing behaviour.
- **No sync-over-async.** `.Result`, `.Wait()`,
  `.GetAwaiter().GetResult()` are PR blockers outside `Main`.
  Analyzer-enforced.
- **`Task<Result<T, Error>>` return type** for fallible async handlers;
  never `Task<T>` paired with separate failure exceptions.

```csharp
public Task<Result<CameraId, RegisterCameraError>> HandleAsync(
    RegisterCamera command,
    CancellationToken ct)
{
    // ...
    await repo.SaveAsync(ct);
    return camera.Id;
}
```

## Consequences

- **Positive:** cancellation propagates without thought; abandoned
  requests release DB connections promptly.
- **Positive:** uniform signature shape across every layer.
- **Negative:** boilerplate `ct` parameter on every method — minor.
- **Negative:** developers coming from library work may instinctively
  reach for `ConfigureAwait(false)`. Analyzer catches it.

## Alternatives Considered

- **Optional tokens** — easy to forget; cancellation gaps appear.
- **`ConfigureAwait(false)` everywhere** — library-author noise in
  application code.
- **Globally implicit cancellation via `AsyncLocal`** — magical and
  hard to debug.
