# ADR-0044: Bounded-Context Relationships — Published Language + ACL

**Status:** Accepted
**Date:** 2026-05-25

## Context

The 9 bounded contexts (ADR-0016) must integrate without becoming a
distributed ball of mud. External systems (MES, SCADA, PLC gateways)
also feed events into Event Ingestion with vendor-specific shapes.

## Decision

Inter-context coupling uses two DDD patterns:

- **Published Language** — versioned integration events in
  `Shared.Contracts` (ADR-0040, ADR-0073). Each context publishes its
  own events; consumers subscribe via Wolverine + RabbitMQ. No
  cross-context database joins. No shared aggregates.
- **Anti-Corruption Layer (ACL)** — at every boundary where an
  upstream shape differs from internal shape, the consuming context
  builds an explicit translator. Most relevant for Event Ingestion
  consuming external MES/SCADA event payloads.

`Shared.Kernel` is the **shared-kernel exception**, strictly limited to
language-level types: value-object base, `Result<T, Error>`,
`Option<T>`, `IStronglyTypedId<TValue>`, `IValueObject<TValue>`,
`Ensure`. No domain concepts.

## Consequences

- **Positive:** each context can refactor internals freely without
  breaking consumers.
- **Positive:** explicit ACLs make external integration drift visible
  and testable.
- **Negative:** more translation code than a shared-everything model.
  Pays back on the first upstream change.

## Alternatives Considered

- **Conformist** (just adopt upstream shapes) — couples internal model
  to upstream stability. Valid as a per-integration tactical choice
  but not as a default.
- **Shared aggregates across contexts** — gives up the entire DDD
  benefit. Rejected.
