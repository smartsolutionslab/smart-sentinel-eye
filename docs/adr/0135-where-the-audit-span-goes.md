# ADR-0135: Where the audit ingest span goes

**Status:** Accepted
**Date:** 2026-08-31
**Supersedes:** (none)
**Superseded by:** (none)

## Context

NFR-001 (spec 009) requires p99 ≤ 50 ms from RabbitMQ deliver-ack to audit row
committed, under sustained 100 ev/s. The best figure ever observed is 85 ms p99,
in run mode.

Three levers have been applied or rejected against that gap, each with its own
ADR and its own measurements — parallel listeners (ADR-0124, adopted), settling
audit deliveries at the broker (ADR-0126, adopted), batching audit writes
(ADR-0127, built, measured, rejected). All three changed the **consumer** side.

Both prior conclusions ended in the same place: what remains is production
topology, which does not exist (ADR-0130), or moving the requirement. **Neither
was ever tested against a breakdown of where the time actually goes**, because no
breakdown existed. This ADR records one.

Two things made the existing figure unable to answer the question:

- **It is one number for a span nobody had divided.** `received_at - occurred_at`
  is stamped either side of the whole pipeline.
- **It is not the requirement's span.** `occurred_at` is stamped by the publisher
  as the aggregate mutates, so the figure is a **superset** of NFR-001's named leg
  at the front, and `received_at` is stamped *before* `SaveAsync`, so it is
  **short** of it at the back. Three ADRs have treated the two as
  interchangeable.

## Decision

**We record the following measurement, and nothing follows from it here.**
Whether the requirement moves, and whether a fourth lever is worth building, are
decisions with their own evidence and their own authors.

Two nullable timestamps were added to the audit row behind a switch that
**defaults to off** — handler entry, by the consumer's clock, and row commit, by
the database's. Together with the existing pair they divide the span into parts
that sum to it.

### The breakdown

Three paced runs, 1000 events each, at an achieved 98.7 / 99.0 / 98.8 ev/s
against a target of 100. Medians. Aspire test fixture, service logging at
`Warning`.

| Part | Run 1 | Run 2 | Run 3 |
|---|---|---|---|
| Observed span (occurred → received) | 1634.9 ms | 2642.5 ms | 1376.8 ms |
| — before handler entry | 1634.9 ms | 2642.4 ms | 1376.8 ms |
| — inside the handler | 0.0 ms | 0.0 ms | 0.0 ms |
| — **unattributed** | **0.0 ms** | **0.0 ms** | **−0.0 ms** |
| Write (after the observed span ends) | 8.5 ms | 10.1 ms | 9.0 ms |
| Tail band (rows ≥ p99 of total) | 3145.7 ms | 3530.9 ms | 3026.5 ms |

**Essentially the whole span precedes the audit handler.** The handler's own work
is 0.0 ms and the write is 8.5–10.1 ms.

### The two spans, stated separately

The requirement's span (broker hand-over → row committed) cannot be given as a
figure, because the hand-over falls *inside* "before handler" and no
publisher-side stamp exists to separate it. What can be stated is a range:

| | Run 1 | Run 2 | Run 3 |
|---|---|---|---|
| Requirement span, floor | 8.5 ms | 10.2 ms | 9.0 ms |
| Requirement span, ceiling | 1643.4 ms | 2652.6 ms | 1385.8 ms |
| Front overhang — in the observed figure, outside the requirement | 1634.9 ms | 2642.4 ms | 1376.8 ms |
| Back shortfall — in the requirement, outside the observed figure | 8.5 ms | 10.1 ms | 9.0 ms |

**The width of that range is the cost of not having a publisher-side stamp**, and
it is wide enough that the requirement's own leg cannot be quoted as a number at
all. This feature deliberately did not add that stamp; adding one is a change to
the publish path of every context, not a measurement.

### That the breakdown is stable

The shape holds across conditions that move the span it divides by two orders of
magnitude:

| Load | Achieved | Observed span | Before handler | In handler | Write |
|---|---|---|---|---|---|
| 1 writer, unpaced | 15.6 ev/s | 11.9 ms | 11.9 ms | 0.0 ms | 3.0 ms |
| 8 writers, unpaced | 48.5 ev/s | 82.3 ms | 82.3 ms | 0.0 ms | 2.9 ms |
| 25 writers, unpaced | 60.7 ev/s | 195.2 ms | 195.2 ms | 0.0 ms | 9.2 ms |
| 50 writers, unpaced | 79.1 ev/s | 999.5 ms | 999.4 ms | 0.0 ms | 11.3 ms |
| 50 writers, unpaced, quiet logs | 244.4 ev/s | 5521.7 ms | 5521.7 ms | 0.0 ms | 9.2 ms |
| 50 writers, **paced to 100** | 98.7–99.0 ev/s | 1376.8–2642.5 ms | same | 0.0 ms | 8.5–10.1 ms |

**The 15.6 ev/s row is the one that matters most.** There the pipeline is idle —
an 11.9 ms span, no backlog — and before-handler is still the whole of it. Without
that row the paced result would prove itself trivially: a backlogged queue spends
its time waiting, by definition.

### The clocks

`occurred_at` and `received_at` are stamped in **different processes**, so some
unknown fraction of the span could be clock disagreement attributed to a
component. Each process's offset from the shared Postgres server was measured by
bracketing the reading and halving the round trip; the residual is reported with
it.

| | Run 1 | Run 2 | Run 3 |
|---|---|---|---|
| Offset from the shared server | −1.96 ms | +1.27 ms | −7.20 ms |
| Residual | ± 0.95 ms | ± 1.03 ms | ± 1.01 ms |

Worst case 8.2 ms, against a 10 ms threshold. **The run-to-run spread (8.5 ms)
is far wider than any single reading's residual (~1 ms)** — the readings are more
precise than they are accurate, and the spread is the honest figure.

The clock question was this feature's headline risk and turns out to be
immaterial *at these magnitudes*: 8 ms of uncertainty against a ~1500 ms span is
0.3%. It would not be immaterial against the 85 ms this work set out to divide,
which is one more reason the two must not be conflated.

### The apparatus' own cost

Same run shape, switch off then on. Each run reports the switch state it ran
under, read off its own rows, so the pairing is verified rather than remembered.

| | p50 | p99 | max | Rows stamped |
|---|---|---|---|---|
| Off | 17.2 ms | 4389.6 ms | 4519.7 ms | 0 of 1000 |
| On | 18.7 ms | 3691.7 ms | 3995.9 ms | 1000 of 1000 |

**About 1.5 ms at p50.** At p99 the switched-on run was faster, so the cost is
below run-to-run variance at the tail rather than negative.

## Consequences

**What this establishes.** At every load measured, from idle to saturated, the
audit pipeline's span is spent before the audit handler is entered. The handler's
own work is unmeasurable at millisecond resolution and the write is single-digit
to low-teens milliseconds.

**What it does not establish, and these are not caveats but limits on use:**

- **It does not say which of four things spends the time.** "Before handler" is
  one interval covering the publisher's own transaction, the outbox hop, the
  broker, and Wolverine's dispatch to the handler. Separating them needs a
  publisher-side stamp, which was deliberately not added. *It isn't the handler
  or the write* is the finding; *it's the broker hop* is not.
- **It does not divide the recorded 85 ms.** These are Aspire-fixture figures. At
  a sustained 100 ev/s the fixture runs 1376–2642 ms where run mode recorded
  85–236 ms p99 — an order of magnitude worse, and in backlog where run mode was
  not. Attributing the recorded figure needs this apparatus run against run mode,
  which needs a paced load driver the repository does not have.
- **It says nothing about production.** There is no production deployment
  (ADR-0130).
- **It does not evaluate any lever**, including the obvious one this breakdown
  suggests.

**A measurement default now sits on a production write path.** It is off unless
configured, and that default is asserted on a row written through the ordinary
path rather than read back off the option.

**Debug logging is not free and is on by default in Development.** Both the
publisher and the audit service set `"Default": "Debug"`, which logs every SQL
command. Turning it down raised achieved throughput from 79 to 244 ev/s in an
otherwise identical run. Any figure taken on a dev stack without checking this
carries it.

## Alternatives Considered

**OpenTelemetry spans read off the Aspire dashboard.** Idiomatic, and the
dashboard is already the dev sink (ADR-0118). Rejected because the trace list is
effectively unsearchable, so the approach depends on hunting history rather than
provoking a known event; and because spans do not survive a run in a form the
same SQL that produces the percentiles can read.

**Re-sourcing `occurred_at` or `received_at` from the database** to remove the
cross-process clock question entirely. Rejected: it changes production behaviour
to suit a measurement, and breaks comparison with every figure already recorded.

**Taking the p99 of each part separately.** Rejected because three independent
p99s belong to three different events and adding them divides nothing. The tail
band selects *rows* at or above the p99 of the total, so each row's parts still
sum to that row's own span.

**Reaching 100 ev/s by adding writers.** Rejected once it was measured: unpaced,
50 writers reached 244 ev/s and a 5.5 s span. "Sustained 100 ev/s" is a rate, and
running flat out measures overload. The run is now paced to the rate and asserts
it landed within ±15%.

## Implementation Notes

The measurement test stays **excluded from CI** (`Category!=Measurement`), and
NFR-001's budget stays at 50 ms. Nothing here was tuned to make anything pass.
