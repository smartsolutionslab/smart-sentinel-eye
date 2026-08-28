# ADR-0124: Parallel listeners where order does not matter

**Status:** Accepted
**Date:** 2026-08-28
**Amends:** [ADR-0088](./0088-wolverine-configuration-defaults.md)

## Context

ADR-0088 fixed per-module queue isolation: each consuming context gets
its own queue per event type, so two contexts subscribed to the same
integration event never become competing consumers. It said nothing
about how many listeners drain each of those queues, and Wolverine's
default is one.

That default was never a decision, and spec 009's NFR-001 turned it into
a ceiling. Measured against a run-mode stack (issue 1956):

| achieved | p50 | p99 |
|---|---|---|
| 24 ev/s | 31 ms | 142 ms |
| 48 ev/s | 37 ms | 258–280 ms |
| 68 ev/s | 52 ms | 342 ms |
| 86–95 ev/s | 3 066–4 936 ms | 6 350–6 730 ms |
| 158 ev/s | 9 870 ms | 14 221 ms |

A hard knee between 68 and 86 ev/s, and a drain rate that peaked at
**~100 rows/s** for a single queue — which is exactly the rate NFR-001
demands, and therefore no rate at all.

**The ceiling is the consumer, and it was established rather than
assumed.** Sampled every second through a 158 ev/s burst, the publisher's
`wolverine_outgoing_envelopes` held **zero rows at every sample** while
the audit queue on RabbitMQ backed up to 468 then 643. Messages left the
publisher immediately; the backlog only ever formed in front of the
listener. The run-mode latency histogram is a smooth queueing tail, not a
spike at multiples of any polling interval, which rules out flush cadence
as the mechanism.

So a queue drained at one handler's turnaround no matter how much the
process had left. On the audit path that handler is a single-row
`SaveAsync` — one transaction per message, latency-bound on Postgres, not
CPU-bound. Serialising it wastes almost the whole machine.

## Decision

`AddWolverineForContext` takes an optional `listenerCount`, defaulting to
**1** — every existing context keeps exactly the behaviour it has. A
context that opts in gets that many parallel listeners on each of its
conventionally-routed queues.

```csharp
routing.ConfigureListeners((listener, _) => listener.ListenerCount(listenerCount));
```

**AuditObservability opts in at 4**, which is an upper bound found by
exceeding it rather than a guess. Eight was measured and is worse than
useless on the shared stack: the dev/CI Postgres runs at
`max_connections = 100` for all nine contexts, and eight listeners took
`audit-db` alone to 22 connections and the cluster past its limit. What
failed under that was not audit but **system-variables**, refusing writes
with `53300: sorry, too many clients already`.

That is the real constraint on this knob, and it is worth naming because
it is invisible from the opting-in context: **listeners are paid for out
of a connection budget the whole cluster shares.** A context that raises
its count without re-checking that budget breaks a bystander, and nothing
about the resulting failure points back at a listener count.

### A context may opt in only if delivery order does not change outcomes

This is the whole of the safety argument, and it is a per-context
property, not a global one. Parallel listeners mean two messages from the
same queue can be handled at once and can finish out of order. A context
qualifies when both hold:

1. **Handling is order-independent.** No handler's result depends on
   another message from the same queue having been handled first.
2. **Handling is idempotent**, so a redelivery after a partial failure
   costs nothing.

AuditObservability qualifies on both. An audit row is an independent
observation of something that already happened — its `occurred_at` comes
off the event, so the trail's ordering is the *events'* ordering and
survives whatever order the rows are written in. And the write is
`INSERT … ON CONFLICT (event_identifier) DO NOTHING`, so a redelivery is
a no-op.

**Most contexts do not qualify, and the default protects them.** A
context that folds events into an aggregate, maintains a projection, or
runs a saga has handlers whose outcome depends on order — those keep one
listener until someone shows otherwise for that context specifically.
This ADR is not a licence to widen the default.

### What this does not change

Queue isolation, eager transactions, the Postgres outbox, and the
durable inbox all stay exactly as ADR-0088 left them. This adds
concurrency *within* one context's consumption of its own queue. It does
not make two contexts share a queue, which is the thing ADR-0088 exists
to prevent.

## What it bought, measured

Same generator, same run-mode stack, before and after:

| achieved | p50 (1 listener) | p50 (4) | p99 (1 listener) | p99 (4) |
|---|---|---|---|---|
| ~30 ev/s | 31 ms | **10 ms** | 142 ms | **58 ms** |
| ~50–58 ev/s | 37 ms | **15 ms** | 258–280 ms | **62 ms** |
| ~85–115 ev/s | 3 066–4 936 ms | **34–66 ms** typical | 6 350–6 730 ms | **214–420 ms** typical |

Peak drain went from ~100 to **270 rows/s**, and the knee where latency
runs away moved from ~75 ev/s to past 110.

**Two of six runs at ~100 ev/s spiked** to p50 494 / 2 353 ms and p99
2 119 / 3 786 ms; the other four sat in the range tabled above. That
scatter is the honest picture — ~100 ev/s is close enough to the new knee
that a run's outcome depends on what else the box is doing. Even the
excursions stay well inside what a single listener produced *every* time.

**NFR-001 is still not met, and this ADR does not claim it is.** p99 ≤ 50
ms is approached but not held even at 30 ev/s (58 ms), and at the NFR's
100 ev/s the typical p99 is 214–420 ms. What changed is that 100 ev/s is
survivable and draining instead of collapsing to six or seven seconds —
the remaining gap is ~5×, where it was ~130×. Whether it closes via the
alternatives below, via production topology, or by moving NFR-001 stays
open on issue 1956.

The measurement's own caveat, and it is not a small one: one 8-core
workstation running all nine services, Postgres, RabbitMQ, Keycloak,
MediaMTX and the load generator together. Production gives audit its own
pod and its own database node, so these are a floor rather than a
verdict.

### The connection budget had to move first

The first attempt to re-measure this change appeared to show it barely
helping — p50 6 458 ms at 108 ev/s. It was not the listeners. The shared
Postgres was at **97 of its 100 connections before any load**, nine
contexts' pools having grown into the whole budget, so under load the
cluster simply ran out. `AppHost` now starts Postgres with
`max_connections=400`, and the numbers above are from after that.

Worth recording because of how it presents: the service that fails is
whichever one asks for a connection next, not the one that consumed the
budget. That is what makes a shared limit expensive to diagnose, and it
is the same reason the listener count is capped at 4.

## Consequences

- **Positive:** audit ingest is no longer capped at roughly the rate its
  own NFR demands. See the table above, spec 009's `tasks.md`, and
  `NFR001_AuditIngestLatencyTests`.
- **Positive:** the knob is opt-in and per context, so a context that
  cannot tolerate reordering cannot acquire it by accident.
- **Negative:** listeners are paid for out of a **cluster-wide** Postgres
  connection budget, so this knob has a blast radius outside the context
  that turns it up — established by turning it up (see the Decision).
- **Negative:** an opted-in context loses per-queue ordering, and nothing
  in the build enforces the two conditions above. They are stated here
  and argued at each opt-in site rather than checked mechanically — the
  same footing as ADR-0088's own "publish through the ambient context"
  rule before `OutboxCommitTests` existed.

## Alternatives Considered

- **Drop the durable inbox for audit queues.** The audit row's own
  `ON CONFLICT (event_identifier)` already gives the dedup the inbox is
  paying for, so this removes duplicated work per message rather than
  adding parallelism. Rejected as the first move because it trades
  crash-safety for throughput on an audit trail, where losing in-flight
  messages is the one failure the trail cannot report. Still available if
  parallel listeners prove insufficient.
- **Batch the audit inserts** via a Wolverine batch handler. Bigger
  change to the handler and the repository, and it makes each message's
  latency depend on the batch filling — the wrong shape for a p99 budget.
- **Move NFR-001 to what the pipeline already does.** Honest, and it was
  on the table. Rejected because the ceiling turned out to be a default
  nobody chose rather than a cost of the design.
- **Raise the default `listenerCount` for every context.** Would have
  fixed audit and quietly reordered message handling in eight contexts
  that were never assessed for it.
