# ADR-0127: Batching audit writes

**Status:** Proposed — the latency measurement that decides it is outstanding
**Date:** 2026-08-28
**Follows:** [ADR-0124](./0124-parallel-listeners-where-order-does-not-matter.md),
[ADR-0126](./0126-audit-listeners-settle-at-the-broker.md)

## Context

The third lever named on issue 1956. Each audit message costs its own
transaction: `AuditingMessageHandler` adds one row and calls
`SaveAsync` immediately, so a hundred events are a hundred commits.
Batching lets one commit carry many rows.

**The arithmetic is against it at the rate NFR-001 names, and that was
known before a line was written.** Wolverine assembles a batch by size
*or* by time: it fires when `BatchSize` fills, or when `TriggerTime`
elapses with whatever has arrived. To collect *N* messages at rate *R*
the window must be *N/R*:

| window | collected at 100 ev/s | latency it adds |
|---|---|---|
| 10 ms | ~1 | ~10 ms |
| 50 ms | ~5 | the whole p99 budget |
| 250 ms (Wolverine's default) | ~25 | 5× the budget |

So batching is a **throughput** lever, and NFR-001 is a **latency**
requirement. It fills by size only under backlog — which is exactly the
state ADR-0124 and ADR-0126 removed.

## Decision

Batch `SystemVariableValueChangedV1` only, at `BatchSize` 100 and
`TriggerTime` **10 ms** — two orders below Wolverine's default, chosen
against the arithmetic above rather than by feel.

One event kind, not all twenty: batching earns its place per kind on
evidence, and this is the kind the NFR-001 measurement drives.

`AuditingMessageHandler.HandleBatchAsync` adds every row and commits
once. The singular path is untouched and still serves the other
nineteen.

## Two things that had to be discovered by breaking them

**A batch definition is silently inert while a singular handler
exists.** With both `Handle(T)` and `BatchMessagesOf<T>` registered,
Wolverine picks the singular one. Every event was still audited one at a
time, the batch log never fired, nothing failed, and every row landed —
the change did nothing and looked exactly like working. The singular
overload for the batched kind is therefore **deliberately absent**, with
a comment saying so, because re-adding it would quietly undo this.

`Every_integration_event_has_an_audit_handler` now accepts `Handle(T[])`
as coverage for `T`. It reads the first parameter type and unwraps
arrays, so the rule stays "every V1 reaches the audit handler somehow"
without caring which shape does it.

**Conventional routing claims `T[]` and sends batches through the
broker.** ADR-0088's convention names a queue for every message type,
and an assembled batch *is* a message type. Left alone it gave
`…SystemVariableValueChangedV1[]` its own RabbitMQ queue, so a batch
assembled locally was published to the broker and read back — a round
trip added by a convention that only ever meant to name listeners.
`routing.ExcludeTypes(messageType => messageType.IsArray)` stops it.
Arrays are never integration events here, so the exclusion costs nothing
else.

## Evidence that it engages

Not from logs. The Aspire structured-log search returned zero hits for
the batch log *and* zero for message kinds whose rows were demonstrably
landing, so absence there means nothing — a trap this project has
recorded before about `list_traces` and which applies here too.

The signal that cannot lie is Postgres' own: rows committed in one
transaction share an `xmin`. Over 320 events at ~40–100 ev/s:

| rows per transaction | transactions |
|---|---|
| 7 | 3 |
| 6 | 9 |
| 5 | 12 |
| 4 | 10 |
| 3 | 18 |
| 2 | 21 |
| 1 | 49 |

**122 transactions for 320 rows — 2.6× fewer.** And the shape is the
arithmetic above, observed rather than predicted: a 10 ms window at that
rate collects one to seven messages, mostly one to three. Nowhere near
the 100 the batch size permits, because the rate never fills it.

## What is not yet known

**Whether it helps or hurts p99.** 2.6× fewer transactions is real, and
so is up to 10 ms of added window. Which dominates at ~100 ev/s, and
what happens under burst where batches fill by size, is the measurement
this ADR is waiting on. Against ADR-0126's baseline of p50 26–35 ms /
p99 85–236 ms.

Until that exists this ADR is **Proposed**, and the prediction — a
modest cost at steady state, a real gain under backlog — is a
prediction. Reasoning about this pipeline has been wrong twice already:
ADR-0124 mischaracterised the durable-inbox trade, and ADR-0126's first
draft claimed an inbox write had been removed when it had not.

## Consequences

- **Positive:** 2.6× fewer transactions at the measured rate, more under
  load, and the ceiling rises where ADR-0124 and ADR-0126 raised it.
- **Negative:** up to `TriggerTime` of latency added to any event
  arriving into an empty batch — paid at every rate, and paid most
  visibly when traffic is sparse and there is nothing to batch it with.
- **Negative:** one event kind now flows differently from the other
  nineteen. `A_batch_commits_every_row_in_one_transaction` guards the
  commit boundary and the batched row is asserted identical to the
  singular one, but the asymmetry is real and is the price of deciding
  per kind on evidence.
- **Negative:** `ExcludeTypes(IsArray)` applies to every context, not
  just audit. It is a no-op for the other eight today and would stop
  working silently if one ever routed an array message deliberately.

## Alternatives Considered

- **Batch all twenty kinds.** Symmetry for its own sake, and it would
  add `TriggerTime` to nineteen kinds that have shown no need for it.
- **A longer `TriggerTime`.** Fills batches properly and cannot meet a
  50 ms p99 while doing so. That is the whole tension.
- **Drop `AutoApplyTransactions` for audit endpoints.** Audit publishes
  nothing, so the transaction pairing ADR-0088 fixed buys it little, and
  removing it attacks the same per-message cost with no window latency
  at all. Not tried; arguably should have been tried first.
