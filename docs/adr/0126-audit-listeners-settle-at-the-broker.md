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

> **What this ADR does *not* claim, because an earlier draft did and was
> wrong.** Switching mode does **not** stop Postgres being written per
> message. Measured after the change, with every audit endpoint verified
> as `Mode=NativeAck`, the incoming-envelopes table still gains **one row
> per event** — `status=Handled`, `attempts=0`, with a `keep_until`
> retention stamp. Those are dedup tombstones, not the two-phase
> Incoming→Handled work the durable inbox does.
>
> The first draft asserted the inbox was bypassed, on the strength of the
> table *shrinking* during a run. That inference was unsound: the
> durability agent sweeps old rows, so a net decrease is consistent with
> writes continuing. The claim is withdrawn; the latency numbers below
> stand, but the mechanism behind them is not established here.

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

**The evidence the change is live** is the endpoint mode read from the
running service, not an inference from row counts: a temporary startup
diagnostic logged every audit queue as `Mode=NativeAck ListenerCount=4`,
which is what settled it after the row-count reading proved unsound.

**The mechanism is not established.** The listeners are demonstrably in
`NativeAck`, and p99 demonstrably improved, but Postgres is still written
once per message (the `Handled` tombstone above), so "the inbox write was
removed" cannot be the explanation. The plausible reading is that the
tombstone is written off the handler's critical path where the durable
inbox's Incoming→Handled pair is not — plausible is all it is, and
nothing here tested it.

### One trap, found by breaking it

`ConfigureListeners` **replaces** a previous registration rather than
composing with it. Adding this as a second `ConfigureListeners` call
silently reverted `ListenerCount` to Wolverine's default — the queue
reported `"consumers":1` while the code plainly asked for four, and
nothing failed. Both settings now live in one call.

### Nothing guards this, and that is stated rather than papered over

A first attempt at a guard asserted the inbox gains no rows per event.
It failed in CI, correctly — the assertion was false, and finding out why
is what produced the correction above. It has been removed rather than
loosened into something that passes without meaning anything.

What would actually guard the change is the endpoint's mode, and that is
not observable from a test process: it lives in the audit service's
Wolverine runtime. A permanent startup log of endpoint modes would make
it visible without making it assertable. Left undone deliberately —
recorded here so the gap is a known one rather than an assumed guard.

## Consequences

- **Positive:** p99 at 100 ev/s roughly halved, with no durability given
  up.
- **Neutral, and contrary to this ADR's first draft:** the incoming
  envelopes table still gains one `Handled` tombstone per message. The
  work removed is whatever the durable inbox does *beyond* that, which
  this ADR does not quantify.
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
