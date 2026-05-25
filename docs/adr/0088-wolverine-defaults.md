# ADR-0088: Wolverine Configuration Defaults

**Status:** Accepted
**Date:** 2026-05-25

## Context

ADR-0042 + ADR-0057 commit to Wolverine as the dispatcher beneath
hand-rolled handler interfaces. Yumney's Wolverine setup
(`src/Yumney.Shared.Events.Wolverine/WolverineEventBusExtensions.cs`)
captures two implementation details worth adopting as defaults:
per-module queue isolation and eager transaction mode.

## Decision

Bake the following Wolverine defaults into the shared bootstrapping
helper (likely `Shared.CQRS` or `ServiceDefaults`):

### Per-module queue isolation

Every event consumer's RabbitMQ queue is prefixed by the consuming
context name:

```csharp
opts.UseRabbitMq(new Uri(rabbitConnection))
    .AutoProvision()
    .UseConventionalRouting(routing =>
        routing.QueueNameForListener(eventType =>
            $"{moduleQueuePrefix}.{eventType.FullName}"));
```

This prevents two contexts subscribing to the same integration event
from becoming **competing consumers** — each gets its own queue and
its own copy of every message.

### Eager transaction mode

```csharp
opts.UseEntityFrameworkCoreTransactions(TransactionMiddlewareMode.Eager);
// or, for Marten contexts:
opts.UseMartenTransactions(TransactionMiddlewareMode.Eager);
```

Wraps every handler in an EF Core or Marten transaction
automatically — pairs with the Postgres-backed outbox for exactly-
once delivery semantics.

### Outbox persistence

```csharp
opts.PersistMessagesWithPostgresql(connectionString, schema);
opts.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
```

Per-context schema (e.g. `wolverine_camera_catalog`) so each
context's outbox tables are isolated.

## Consequences

- **Positive:** queue isolation prevents subtle "missed messages"
  bugs that come from competing consumers.
- **Positive:** transactional outbox guarantees no message loss on
  crash mid-handler.
- **Positive:** Postgres-backed outbox is durable; no external
  dependency beyond the database we already need.
- **Negative:** more RabbitMQ queues per cluster (one per
  context × event type). Acceptable; modern RabbitMQ handles tens of
  thousands.

## Alternatives Considered

- **Lazy transaction mode** — handler manages its own transaction
  lifetime. More control, more places to forget.
- **No queue prefix (default routing)** — competing-consumers
  race conditions.
- **Different message store (Redis, EventStore)** — extra
  infrastructure; rejected in favour of Postgres-only operational
  surface.
