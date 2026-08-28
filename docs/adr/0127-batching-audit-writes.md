# ADR-0127: Batching audit writes

**Status:** Rejected for NFR-001 — measured, and it makes the target
metric worse. See "What it actually cost".
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

## What it actually cost

Measured as a matched A/B — same box, same session, same protocol, three
runs each with a discarded warm-up and a drain between:

**Sustained ~100 ev/s, which is the rate NFR-001 names:**

| | p50 | p99 |
|---|---|---|
| without batching | **23.1–29.5 ms** | **95.1–128.1 ms** |
| with batching | 36.5–44.4 ms | 100.3–162.5 ms |

**Worse on both.** p50 rises by ~13 ms — the 10 ms window plus change,
which is the arithmetic at the top of this ADR arriving exactly as
written. p99 does not improve to pay for it.

**Flat-out burst:**

| | achieved | p50 | p99 | transactions for 1 280 rows |
|---|---|---|---|---|
| without batching | 232 ev/s | 2 319 ms | 2 846 ms | **1 280** (one row each) |
| with batching | 190 ev/s | **708 ms** | **2 243 ms** | **192** (max 16 rows) |

Under backlog it is a large win — 6.7× fewer transactions and p50 3.3×
better — because batches finally fill by size rather than by clock. The
two runs pushed different rates (190 vs 232 ev/s), so the latency halves
of that row are not a controlled comparison; the transaction counts are.

## Decision, revised by the measurement

**Not adopted.** NFR-001 is a latency requirement at a sustained rate,
and at that rate this makes latency worse. The lever is real but it is
aimed at a different problem: it buys burst absorption, and ADR-0124 and
ADR-0126 already moved the collapse point well past 100 ev/s, so there is
no backlog left for it to help with.

Keeping it would trade a measured regression at the operating point for a
gain in a regime the system is no longer in. The code is preserved on
`perf/1956-batch-audit-writes` and this ADR records what it does, so
adopting it later is a revert away — the right shape if sustained load
ever climbs past what ADR-0124's ceiling absorbs.

**The prediction held, and that is worth one line given the record.**
Reasoning about this pipeline was wrong twice before: ADR-0124
mischaracterised the durable-inbox trade, and ADR-0126's first draft
claimed an inbox write had been removed when it had not. This time the
arithmetic written before the code — *N/R*, ~10 ms of window, no fill at
100 ev/s — is what the numbers say. Which is an argument for doing the
arithmetic, not for trusting the next prediction.

## Consequences

**Of not adopting:**

- The per-message transaction stays. That cost is real and unaddressed;
  what this ADR establishes is that *batching* cannot remove it without
  charging more in window latency than it saves.
- Burst absorption stays where ADR-0124 and ADR-0126 left it. Should
  sustained load ever approach that ceiling, this is the measured lever
  to reach for, and the branch is the implementation.

**What adopting would have cost, recorded because it is the reason:**

- Up to `TriggerTime` of latency on any event arriving into an empty
  batch — paid at every rate, and paid most visibly when traffic is
  sparse and there is nothing to batch it with.
- One event kind flowing differently from the other nineteen, with a
  singular overload that must stay absent or the batch silently stops.
- `ExcludeTypes(IsArray)` reaching every context rather than just audit
  — a no-op for the other eight today, and one that would stop working
  silently if a context ever routed an array message deliberately.

## Alternatives Considered

- **Batch all twenty kinds.** Symmetry for its own sake, and it would
  add `TriggerTime` to nineteen kinds that have shown no need for it.
- **A longer `TriggerTime`.** Fills batches properly and cannot meet a
  50 ms p99 while doing so. That is the whole tension.
- **Drop `AutoApplyTransactions` for audit endpoints.** Audit publishes
  nothing, so the transaction pairing ADR-0088 fixed buys it little, and
  removing it attacks the same per-message cost with no window latency
  at all. Not tried; arguably should have been tried first.
