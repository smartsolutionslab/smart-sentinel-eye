# ADR-0059: Validation — Hand-Rolled API Validators + Ensure.That() in Value Objects

**Status:** Accepted
**Date:** 2026-05-25

## Context

Validation lives in two places: at the API boundary (incoming
request DTOs) and inside value-object constructors. Yumney uses
**FluentValidation** at the API boundary and an **`Ensure.That(...)`**
fluent chain inside VOs. The hand-written VO choice (ADR-0046) means
we adopt the validator chain pattern regardless.

## Decision

- **Inside value objects:** use the `Ensure.That(...)` fluent chain
  (adopted from Yumney). `Ensure` lives in `Shared.Kernel`:

  ```csharp
  var name = Ensure.That(input).IsNotNullOrWhiteSpace().HasMaxLength(200).AndReturn().Trim();
  ```

  Each predicate throws on failure; the VO constructor catches and
  surfaces via `Result<T, ValidationError>`.

- **At the API boundary:** hand-rolled `IRequestValidator<TRequest>`
  interface in `Shared.Api`. Endpoint filter resolves the validator,
  runs it before handler dispatch, and emits **RFC 7807 Problem
  Details** on failure. **No FluentValidation dependency.**

  ```csharp
  public interface IRequestValidator<T>
  {
      Result<T, ValidationProblem> Validate(T request);
  }
  ```

## Consequences

- **Positive:** consistent validator chain pattern across VO and API
  validation.
- **Positive:** zero external validation framework dependency.
- **Negative:** must build the validator-and-endpoint-filter scaffold
  ourselves once. Acknowledged.

## Alternatives Considered

- **FluentValidation (Yumney's choice)** — mature, well-known; adds
  an external dependency we prefer to avoid for consistency with
  our other "hand-rolled" choices.
- **Data annotations** (`[Required]`, `[MaxLength]`) — too coarse for
  domain validation.
