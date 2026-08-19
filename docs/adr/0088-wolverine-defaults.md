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

## Amendment (2026-08-19, spec 021) — what the outbox actually covers

The consequence below used to read *"transactional outbox guarantees no
message loss on crash mid-handler"*. That sentence is true and it was
read, for a year, as a guarantee about every message this system
publishes. It is not, and issue #1605 is what that cost.

`AutoApplyTransactions` enrols messages published **from inside a
Wolverine message handler** — a context reacting to an integration event
it received. Those are covered, and always were.

**A write that originates anywhere else was not.** An HTTP endpoint or a
hosted service calling a repository is not a Wolverine handler, so
nothing enrolled its publishes: the repository committed its rows and
then announced them, and a failure in between left the row durable and
the announcement gone, with nothing holding a copy. Nine repositories,
one or two per bounded context, every one of them.

Spec 021 closed it by reaching the outbox that this ADR had already paid
for, rather than by building anything:

- `IEventBus` is implemented by `OutboxEventBus<TDbContext>`, which
  captures into the `IDbContextOutbox` bound to the calling context's
  `DbContext` instead of publishing immediately.
- A repository commits through `ITransactionalCommit`, whose
  implementation calls `SaveChangesAndFlushMessagesAsync` — the rows and
  the messages in one transaction.
- The dispatch happens **before** the commit, so the message is captured
  inside the transaction. A domain-event handler therefore runs before
  its write is durable, and one that throws fails the write. That is
  acceptable only while every such handler publishes and does nothing
  else.
- `OutboxCommitTests` fails the build if a repository calls
  `SaveChangesAsync` directly, because that is what a new repository does
  by default and the failure is silent.

**How to tell whether a new write path is covered.** If it goes through a
repository that commits via `ITransactionalCommit`, it is. If it calls
`SaveChangesAsync` itself, it is not — and the architecture test will say
so. If it publishes without an accompanying write, there is no
transaction to join and this guarantee does not apply to it.

## Consequences

- **Positive:** queue isolation prevents subtle "missed messages"
  bugs that come from competing consumers.
- **Positive:** the transactional outbox prevents message loss for
  messages published inside a Wolverine handler **and**, since spec 021,
  for integration events announced by a repository write. See the
  amendment above for what remains outside it.
- **Positive:** Postgres-backed outbox is durable; no external
  dependency beyond the database we already need.
- **Negative:** more RabbitMQ queues per cluster (one per
  context × event type). Acceptable; modern RabbitMQ handles tens of
  thousands.
- **Negative:** a write now costs its outbox rows in the same
  transaction — on the ingest path, a batch of 200 events writes 200
  event rows and 200 outbox rows together. Measured rather than assumed
  (spec 021 SC-005); the rows are short-lived and deleted on delivery.

## Alternatives Considered

- **Lazy transaction mode** — handler manages its own transaction
  lifetime. More control, more places to forget.
- **No queue prefix (default routing)** — competing-consumers
  race conditions.
- **Different message store (Redis, EventStore)** — extra
  infrastructure; rejected in favour of Postgres-only operational
  surface.
