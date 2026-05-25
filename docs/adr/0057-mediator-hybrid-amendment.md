# ADR-0057: Amends ADR-0042 — Hand-Rolled Handler Interfaces + Wolverine Dispatcher

**Status:** Accepted (amends ADR-0042)
**Date:** 2026-05-25

## Context

ADR-0042 originally framed Wolverine as the mediator end-to-end. The
Yumney compare surfaced a cleaner pattern: hand-rolled
`ICommandHandler<T,R>` / `IQueryHandler<T,R>` interfaces in
`Shared.CQRS`, with Wolverine acting as the dispatcher beneath them.

## Decision

Amend ADR-0042. Application code references **hand-rolled interfaces
only**; **Wolverine sits behind them** as the dispatcher providing
middleware. Application code does not import Wolverine types.

See ADR-0042 for the full updated pattern.

## Consequences

- **Positive:** handlers remain greppable and framework-agnostic.
- **Positive:** Wolverine's value (outbox, sagas, retries,
  OpenTelemetry) is preserved.
- **Positive:** the dispatcher is replaceable if Wolverine ever
  becomes inadequate.
- **Negative:** small extra interface layer.

## Alternatives Considered

See ADR-0042's alternatives.
