# ADR-0047: Error Handling — Result&lt;T, Error&gt; for Business Failures, Exceptions for Bugs

**Status:** Accepted (confirmed against Yumney divergence by ADR-0058; augmented by ADR-0089: ApiError record with HTTP status; amended 2026-08-03 — construction ergonomics, see below)
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

## Amendment (2026-08-03): construction ergonomics

The decision above is unchanged. What changes is how a handler *writes*
a result. `Result<TValue, TError>.Success(...)` repeats both type
arguments on every return, which the return type already states:

```csharp
// before
return Result<AuditPageDto, GetResourceTimelineError>.Success(
    new AuditPageDto(dtos, nextCursor));
return Result<RuleDto, GetRuleError>.Failure(
    new GetRuleError.RuleNotFound(query.Name));

// after
return Success(new AuditPageDto(dtos, nextCursor));
return Failure(GetRuleFailures.RuleNotFound(query.Name));
```

**Mechanism.** A non-generic `Result` static class returns a half-built
`SuccessOutcome<TValue>` / `FailureOutcome<TError>`, and
`Result<TValue, TError>` declares an implicit conversion from each. The
outcome names only the half it carries; the return type supplies the
rest. `Result` is imported per project with
`<Using Include="SmartSentinelEye.Shared.Kernel.Result" Static="true" />`,
so handlers write the bare verbs. The struct's own
`Result<T,E>.Success/Failure` remain and are still the primitive.

**Why each error hierarchy also gains a `<Name>Failures` class.** Generic
type parameters are invariant, so a `FailureOutcome` inferred from a
*variant* does not convert to a Result declared over the *base*:

```csharp
Failure(new GetRuleError.RuleNotFound(name))
// FailureOutcome<GetRuleError.RuleNotFound> -> Result<RuleDto, GetRuleError>: CS0029
```

Every failure call site in the codebase constructs a variant, so this is
the normal case rather than an edge one. The fix is a factory that
returns the base, and it cannot live on the hierarchy itself: a static
method may not share a name with a nested type (CS0102), and roughly
forty variant names — `InvalidCursor`, `GridEmpty`, `PageSizeOutOfRange`
— have nothing to strip. So the factories sit in a sibling
`<Name>Failures` static class in the file that already holds the
hierarchy.

The same invariance applies on the success side whenever the expression
is narrower than the declared value type (`int[]` for an
`IReadOnlyList<int>`); there the type argument is named at the call
site, which is rare enough to leave explicit.

**Alternatives considered for the amendment.**

- **Implicit conversion straight from `TValue` / `TError`** — shortest
  of all (`return dto;`), and derived errors convert fine. Rejected: a
  bare DTO return hides that the method returns a Result, and two
  user-defined conversions on one type are fragile if the value and
  error types ever overlap.
- **`Failure<TError>(...)` naming the error base at the call site** —
  needs no factories and no new types. Rejected as the default: it puts
  a type argument back on every failure, which is what this amendment
  set out to remove. It remains available and is used where a value
  needs its type named.
- **Renaming the variants so the base can host the factories** — frees
  the clean name but moves all 108 records and every `ShouldBeOfType`
  that names one. Rejected as disproportionate.
