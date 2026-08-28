# ADR-0126: Audit listeners settle at the broker, not in the inbox

**Status:** Accepted
**Date:** 2026-08-28
**Amends:** [ADR-0088](./0088-wolverine-configuration-defaults.md)
**Follows:** [ADR-0124](./0124-parallel-listeners-where-order-does-not-matter.md)
(and corrects its account of this option)

## Context

ADR-0124 raised the audit queues to four listeners and moved p99 at
100 ev/s from 6 350–6 730 ms to 214–420 ms. It left NFR-001's 50 ms
unmet and named a second lever: drop the durable inbox.

The audit listeners were durable — confirmed, not assumed:
`wolverine_audit.wolverine_incoming_envelopes` held **9 303 rows, every
one `Handled`**. So each message cost an inbox INSERT, a status UPDATE
and an eventual DELETE, on top of the audit row's own INSERT.

**That inbox buys deduplication, and the destination already provides
it.** `AuditEventRepository.SaveAsync` writes
`INSERT … ON CONFLICT (event_identifier, occurred_at) DO NOTHING`, whose
own documentation says it exists so that "Wolverine at-least-once
redeliveries are absorbed silently". Two mechanisms, one job.

## Decision

Audit listeners use Wolverine's `ProcessInParallelWithNativeAcks()`,
exposed as an opt-in `useNativeAcks` on `AddWolverineForContext`,
**defaulting to false** so every other context is untouched.

Wolverine's own description: *hold each broker delivery unacknowledged
while it flows through an in-memory execution block, and settle it
natively when the handler finishes.*

### This is not a durability trade, and that was measured

ADR-0124 listed this alternative as trading "crash-safety for
throughput", and **that was wrong**. The delivery stays unacknowledged on
RabbitMQ for the whole handler, so the broker holds exactly what the
inbox was holding.

Tested by killing the audit service outright — `taskkill /F`, no
graceful shutdown — five seconds into a 640-event burst:

| | |
|---|---|
| events written | 640 |
| audit rows before restart | **0** |
| RabbitMQ queue, service down | **640 `messages_ready`**, 0 unacknowledged, 0 consumers |
| audit rows after restart | **640** |

Every in-flight delivery went back to `ready` when the connection
dropped, and all 640 were redelivered and audited. Nothing was lost.

What is actually given up is the inbox's **hard** deduplication. A
redelivery is now handled again rather than recognised and skipped — and
lands on `ON CONFLICT … DO NOTHING`, which is why this context can afford
it. A context without an idempotent write cannot, which is the same
per-context test ADR-0124 applies to parallel listeners.

`WithInMemoryIdempotency(...)` exists for a best-effort in-memory guard.
Deliberately not used: it is per-process and forgotten on restart, and it
would add a second half-measure in front of a destination that already
answers the question properly.

## What it bought, measured

Six runs at 99–113 ev/s against three baseline runs at 99–115 ev/s, same
stack, same generator:

| | p50 | p99 |
|---|---|---|
| durable inbox (ADR-0124) | 29–63 ms | **283–333 ms** |
| native acks (this) | 26–35 ms | **85–236 ms** |

No overlap on p99 — every native-ack run beat every durable run. p50 is
roughly unchanged and tighter.

**NFR-001 is still not met.** The best p99 observed is 85 ms against a
50 ms budget. Taken with ADR-0124 the gap has gone from ~130× to under
2× at its best and ~4.7× at its worst, which is a different conversation
from where issue 1956 started but not a closed one.

The inbox itself is the cleanest evidence the change is live: across
2 000 events the table **shrank** from 3 685 to 3 155 rows as old entries
aged out, instead of gaining 2 000.

### One trap, found by breaking it

`ConfigureListeners` **replaces** a previous registration rather than
composing with it. Adding this as a second `ConfigureListeners` call
silently reverted `ListenerCount` to Wolverine's default — the queue
reported `"consumers":1` while the code plainly asked for four, and
nothing failed. Both settings now live in one call, and
`AuditListenersBypassTheInboxTests` guards the outcome.

## Consequences

- **Positive:** roughly a third of the per-message database work removed,
  with p99 cut by more than half and no durability given up.
- **Positive:** the audit inbox stops accumulating rows that a durability
  agent then has to sweep.
- **Negative:** deduplication now rests entirely on one unique index. If
  that index or the `ON CONFLICT` clause is ever changed, duplicate audit
  rows become possible and nothing else will catch it — the repository's
  own documentation is the warning, and it predates this ADR.
- **Negative:** the guarantee now depends on a transport property
  (individual, out-of-order settlement) rather than on our own store.
  RabbitMQ has it; Wolverine refuses to bootstrap an endpoint whose
  transport does not, so a future transport change fails loudly rather
  than quietly losing messages.

## Alternatives Considered

- **`BufferedInMemory()`** — moves deliveries into an in-memory queue and
  settles them immediately. *This* is the option that trades crash-safety
  for throughput, and it is the one ADR-0124 was describing. Rejected: an
  audit trail losing in-flight events is the one failure it cannot
  report.
- **`ProcessInline()`** — also settles after the handler, so equally
  safe, but Wolverine normalises `MaxDegreeOfParallelism` to 1 for an
  inline endpoint, which would undo ADR-0124.
- **Keep the durable inbox and drop the `ON CONFLICT`** — the mirror
  image. Rejected: the inbox is per-endpoint bookkeeping, while the
  unique index protects the table against every writer, including a
  replay or a backfill.
- **Batch handler (`MessageBatchSize`)** — still available, and now the
  obvious next lever if the remaining gap has to close. It makes each
  message's latency depend on its batch filling, which is the wrong shape
  for a p99 budget, so it was not reached for first.
