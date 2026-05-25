# ADR-0047: Error Handling — Result&lt;T, Error&gt; for Business Failures, Exceptions for Bugs

**Status:** Accepted (confirmed against Yumney divergence by ADR-0058; augmented by ADR-0089: ApiError record with HTTP status)
**Date:** 2026-05-25

## Context

A domain-rich codebase has two failure categories: expected
**business failures** (validation, conflict, not-found, authorization
denied) that callers should handle, and unexpected **bugs / infra
failures** (NullReferenceException, DB unreachable) that should
bubble to middleware. Conflating them — throwing for both — makes
control flow opaque and invites swallowed-exception bugs.

## Decision

- **Expected business failures use `Result<T, Error>`** where `Error`
  is a sealed-record hierarchy specific to the operation. Application
  handlers and domain methods return `Result<T, Error>`.
- **Exceptions are reserved for programmer errors and infrastructure
  failures.** Wolverine middleware catches and translates to 5xx.
- **API layer pattern-matches `Result.Failure`** to the appropriate
  HTTP status. Pattern-matching on the sealed `Error` hierarchy is
  exhaustive — the compiler warns if a new case is added without an
  arm.
- **No swallowed exceptions, ever.** `try { ... } catch { }` is a PR
  blocker.

```csharp
public abstract record RegisterCameraError
{
    public sealed record NameAlreadyTaken : RegisterCameraError;
    public sealed record InvalidUrl(string Url) : RegisterCameraError;
}

public Task<Result<CameraId, RegisterCameraError>> HandleAsync(
    RegisterCamera command, CancellationToken ct);
```

## Consequences

- **Positive:** business failure shapes are visible in handler
  signatures; reviewers see the failure surface at a glance.
- **Positive:** API layer maps cases exhaustively, no `default: 500`
  fallback masking unhandled domain failures.
- **Negative:** more boilerplate per handler — explicit error
  hierarchies. Acceptable; better than string-coded errors.
- **Negative:** small impedance mismatch with frameworks that expect
  exceptions (e.g. ASP.NET Core model binding). Acceptable.

## Alternatives Considered

- **Single-generic `Result<T>` + string code** (Yumney pattern) —
  considered and rejected in favour of exhaustive typed errors.
- **Exceptions everywhere** — couples domain logic to framework
  middleware; harder to reason about.
- **Result everywhere including infra** — verbose; .NET BCL throws,
  forcing wrappers at every framework seam.
