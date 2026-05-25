# ADR-0042: Mediator / Handler Dispatch — Hand-rolled Interfaces, Wolverine Dispatcher

**Status:** Accepted (amended by Round 5 Yumney compare, ADR-0057)
**Date:** 2026-05-25

## Context

The Application layer in each context needs a uniform way to dispatch
commands and queries to their handlers, with middleware for retries,
OpenTelemetry instrumentation, outbox transactionality, and saga
support. MediatR became commercial in 2024; Wolverine is opinionated
and ergonomic but framework-magic-heavy. Yumney's pattern (sister repo
in the same org) keeps hand-rolled handler interfaces and uses
Wolverine only for the bus.

## Decision

Use **hand-rolled handler interfaces** in `Shared.CQRS` for the
Application-facing contract, and **Wolverine as the dispatcher** that
wires them at runtime.

```csharp
// Shared.CQRS
public interface ICommandHandler<TCommand, TResult>
{
    Task<TResult> HandleAsync(TCommand command, CancellationToken ct = default);
}

public interface IQueryHandler<TQuery, TResult>
{
    Task<TResult> HandleAsync(TQuery query, CancellationToken ct = default);
}
```

- **Application code references only `ICommandHandler<,>` /
  `IQueryHandler<,>`.** It does not import Wolverine types.
- **Wolverine sits behind the interfaces** — it wires handler
  invocation, supplies free middleware (retries, OpenTelemetry,
  Postgres outbox transactionality, sagas per ADR-0072), and adopts
  the conventions for routing.
- **Logging + validation decorators** are explicit `ICommandHandler<,>`
  wrappers in `Shared.CQRS`.

## Consequences

- **Positive:** handler code stays greppable, refactorable, and free
  of framework-specific attributes.
- **Positive:** Wolverine still earns its keep on the bus side
  (outbox, sagas, retries) without leaking into business logic.
- **Positive:** the abstraction is replaceable — if Wolverine ever
  becomes a poor fit, swap the dispatcher without touching handlers.
- **Negative:** one extra interface layer between framework and
  handler. Minor.

## Alternatives Considered

- **MediatR** — commercial license cost, narrower scope.
- **Wolverine everywhere with no abstraction** — couples Application
  to Wolverine; harder to test without bringing the framework along.
- **Hand-rolled dispatcher** — re-implements middleware we'd get free.
