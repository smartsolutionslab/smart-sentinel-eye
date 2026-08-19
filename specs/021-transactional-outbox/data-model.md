# Data Model: An integration event is never lost after its write commits

**Feature**: `021-transactional-outbox` · **Phase 1** · 2026-08-19

**No domain model changes. No migrations authored by this feature.** That is the
headline and it is worth stating plainly, because a feature about durability
sounds like it should add a table.

---

## What already exists and is not being created

Wolverine's outbox tables live in a per-context schema — `wolverine_camera_catalog`,
`wolverine_event_ingestion`, and so on — created by
`AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate` in
`WolverineDefaults.cs`. They are provisioned today, in every environment, and
sit empty on the write path because nothing enrols anything into them.

This feature writes to tables that already exist. There is no
`MigrationRunner` work and no EF migration, which is the main reason it touches
nine contexts without nine schema changes.

| Table (per context schema) | Owner | Role here |
|---|---|---|
| `wolverine_outgoing_envelopes` | Wolverine | a message produced by a write and not yet delivered — the durable form of a "pending announcement" |
| `wolverine_incoming_envelopes` | Wolverine | inbound side; untouched by this feature |
| `wolverine_dead_letters` | Wolverine | where a message goes when it can never be delivered (FR-010) |

**Nobody in our code writes these directly.** They are Wolverine's, and reading
them is how FR-008's observability is satisfied.

---

## The entity the spec named

The spec's **pending announcement** — an announcement produced and not yet
delivered — maps to a row in `wolverine_outgoing_envelopes`. It has no
representation in our domain, our Application layer or our contracts, and it
should not acquire one: the moment we model it ourselves we own its delivery,
its retry and its poison handling, which is the hand-rolled outbox R1 rejected.

Its lifecycle, entirely inside Wolverine:

```
produced (inside the write's transaction)
   └─ committed with the domain rows        ← the guarantee: same fate
        └─ released to the sending agent
             ├─ delivered            → row deleted
             └─ undeliverable        → retried, then dead-lettered (FR-010)
```

The only property this feature adds is the first arrow. Everything below it is
machinery ADR-0088 already bought.

---

## What changes shape in our code

Nothing that is persisted. Two seams change behaviour without changing data:

### `IEventBus` (Shared.CQRS) — unchanged interface, new meaning

```csharp
Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
    where TEvent : notnull;
```

The signature stays. What changes is when the message leaves: today,
immediately; afterwards, when the surrounding write commits. **The interface
cannot express that difference**, which is exactly how this defect survived —
a caller cannot tell from the type whether it has a promise.

The contract note (`contracts/event-bus.md`) records the new obligation in
prose, because the type system will not.

### The repositories — one call changes

`dbContext.SaveChangesAsync(ct)` becomes
`outbox.SaveChangesAndFlushMessagesAsync(ct)`, and the dispatch moves above it.
No repository signature changes; `IEventRepository` and its eight siblings keep
their shapes, so no Application or Domain code moves.

---

## Invariants this feature introduces

1. **A domain row and the messages announcing it share a transaction.** Either
   both are committed or neither is.
2. **No message is released to the broker before its transaction commits.**
   Release happens on flush, after commit.
3. **A repository does not call `SaveChangesAsync` directly.** Enforced by
   NetArchTest (FR-007), because this invariant is the one a future write path
   will break by accident.

Invariant 3 is a rule about code rather than data, and it is here rather than in
the plan because it is the one that keeps the other two true after this feature
ships.

---

## Retention and volume

Outbox rows are transient — deleted on successful delivery. The volume question
is the ingest path, where a batch of 200 events becomes 200 domain rows plus 200
outbox rows in one transaction, on the highest-throughput path in the product.

Two things follow, both already in the plan rather than new decisions here:

- the row cost is inside a transaction and round trip that already happen, so it
  is not a new hop (R4);
- a delivery failure means the rows stop being deleted, which is why FR-008 asks
  for the count and the age of the oldest — an outbox quietly growing looks
  exactly like an empty one until it does not.

Dead-lettered messages are retained by Wolverine's own policy. This feature does
not set a retention period for them; if one is wanted, it is a separate decision
and belongs with the operational work, not here.
