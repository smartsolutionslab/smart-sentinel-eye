# ADR-0136: Where the audit ingest span goes in run mode

**Status:** Accepted
**Date:** 2026-09-01
**Supersedes:** (none — extends ADR-0135)
**Superseded by:** (none)

## Context

ADR-0135 divided the audit ingest span and named its own gap in its Consequences:
the figures were taken on the Aspire test fixture, and the p99 the open latency
decision is about — 85 ms — was recorded in **run mode**. At a sustained 100 ev/s
the fixture ran 1376–2642 ms. So the breakdown that existed divided a span an
order of magnitude larger than the one anybody was arguing over.

This records the same division, of the same span, at the same paced rate, taken
against a running run-mode stack.

**The apparatus was not the gap.** The stamps, the switch, the division, the
clock probe and the conditions guards all came from spec 053 unchanged. What was
missing was an environment and a driver: every run-mode figure in the record was
produced ad hoc in an earlier session and thrown away.

## Decision

**We record the following measurement, and nothing follows from it here.**
Whether NFR-001 moves, and whether a fourth lever is worth building, are
decisions with their own evidence and their own authors.

### The breakdown

Three paced runs, 1000 events each, against run mode. Medians. Service logging at
`Warning`, measurement switch on, every row stamped, per-row residual **0.000 ms**
on every band of every run.

| | Run 1 | Run 2 | Run 3 |
|---|---|---|---|
| Achieved rate | 97.2 ev/s | 99.6 ev/s | 99.8 ev/s |
| **Typical** — observed span | 262.0 ms | 19.0 ms | 14.3 ms |
| — before handler entry | 262.0 ms | 19.0 ms | 14.3 ms |
| — inside the handler | 0.0 ms | 0.0 ms | 0.0 ms |
| **Tail band** — observed span | 1499.5 ms | 589.0 ms | **87.4 ms** |
| — before handler entry | 1499.5 ms | 589.0 ms | **87.4 ms** |
| — inside the handler | 0.0 ms | 0.0 ms | 0.0 ms |
| Write (insert, not commit — not established) | 8.2 ms | 3.5 ms | 5.3 ms |
| Clock offset | −0.45 ± 0.74 ms | +1.72 ± 0.66 ms | −1.71 ± 0.77 ms |
| Write-leg standing | Established | Established | Established |

### The figure the decision is about, divided

**Run 3's tail band is 87.4 ms.** The p99 recorded for this pipeline is 85 ms, and
the recorded range 85–236 ms. That is the figure, reproduced under a paced
sustained ~100 ev/s — and **87.4 ms of it precedes the audit handler.**

In-handler is 0.0 ms. The write is 12.4 ms in that band.

**Three levers have been applied or rejected against this budget — parallel
listeners (ADR-0124), settling at the broker (ADR-0126), batching audit writes
(ADR-0127). All three changed the consumer side. The consumer side is 0.0 ms of
handler work plus a single-digit-to-low-teens write.**

### That the shape is not an artefact of one environment

| Environment | Load | Observed span | Before handler | In handler |
|---|---|---|---|---|
| Fixture, 1 writer | 15.6 ev/s | 11.9 ms | 11.9 ms | 0.0 ms |
| Fixture, paced | 98.7–99.0 ev/s | 1376.8–7361.9 ms | same | 0.0 ms |
| Fixture, unpaced | 244.4 ev/s | 5521.7 ms | 5521.7 ms | 0.0 ms |
| **Run mode, paced** | **97.2–99.8 ev/s** | **14.3–262.0 ms** | **same** | **0.0 ms** |

The span moves across three orders of magnitude. **The division does not.**
Before-handler is the whole of it at every load, in both environments, idle and
saturated alike. A finding that survives that is a finding.

### Run mode is faster than the fixture, and both are unstable

At the same paced rate, run mode ran 14.3–262.0 ms where the fixture ran
1376.8–7361.9 ms. That gap is the reason ADR-0135's figures could not answer this
question, and it is now quantified rather than asserted.

**But neither environment is reproducible at this rate**, which is itself worth
recording:

- Fixture, seven runs: 267.4, 1376.8, 1414.9, 1634.9, 2642.5, 5516.0, 7361.9 ms.
- Run mode, three runs: 14.3, 19.0, 262.0 ms.

**ADR-0135 recorded "1376.8–2642.5 ms" from three runs as though that were the
spread. It was not** — those three happened to cluster, and the true fixture range
is 27×. The same caution applies to the three run-mode figures here: they are
three samples of a wide distribution, not a range.

The likely cause is coherent: **100 ev/s sits at the consumer's drain ceiling**, so
a run either keeps up or falls behind and accumulates backlog. Bistable at the
knee. That is a property of the pipeline, not noise to be averaged away.

## Consequences

**What this establishes.** At a sustained ~100 ev/s in run mode, the audit ingest
span is spent before the audit handler is entered. The handler's own work is
unmeasurable at millisecond resolution. Each row's parts cover that row's span
exactly, checked row by row rather than by comparing medians, which do not add.

**What it does not establish:**

- **The write leg**, and therefore the requirement span's floor. Run mode has the
  same host-process/container split as the fixture: `received_at` is a host stamp
  and `written_at` is `clock_timestamp()` inside the Postgres container. The
  clocks were established here (worst case 2.49 ms) — better than on the fixture —
  but the leg also ends at **insert, not commit**, where NFR-001's words are
  "audit row committed". It under-reports by the commit's cost.
- **Which of four things spends the time.** "Before handler" is one interval
  covering the publisher's transaction, the outbox hop, the broker, and
  Wolverine's dispatch. No publisher-side stamp was added. *It isn't the handler
  or the write* is established; *it's the broker hop* is not.
- **That this reproduces the original run.** The historic run-mode figures came
  from a driver that was never committed, so "the same conditions" is a claim
  about the **environment**, not about the load that produced them.
- **A range for either environment.** Three samples of a bistable distribution are
  three samples.
- **Anything about production.** There is none (ADR-0130).
- **That any lever would help**, including the obvious one this breakdown suggests.

**A measurement default sits on a production write path**, off unless configured,
asserted off on a row written through the ordinary path.

## Alternatives Considered

**Reusing the Aspire fixture.** Impossible by definition: the fixture boots its
own stack, which is exactly what makes it not run mode. The run-mode driver
carries no collection attribute — the mechanism that injects the fixture — and
that absence is asserted, because the failure would otherwise be silent and would
publish a fixture's figures labelled "run mode".

**Discovering endpoints automatically.** Nothing here has a stable address: every
service uses `WithHttpEndpoint()` with no port and the gateway runs ≥2 replicas.
The e2e script scrapes a TypeScript module off the Vite dev server; that is a fair
trick for a smoke check and a bad foundation for a measurement. The operator
supplies the addresses and the run reports the ones it reached.

**Going through the API gateway.** Rejected: a proxy hop plus load-balancing
across replicas would be a per-request difference between the two runs, and the
comparison requires differences to be nil or named. Both runs target
`system-variables` directly.

**Closing the write leg with a post-commit stamp.** Rejected in spec 053 as a
second round trip on a path this work only observes, and that rejection stands.
It remains the obvious way to establish the back of the span.

## Implementation Notes

The measurement stays **excluded from CI** (`Category!=Measurement`) — it needs a
stack CI does not run, and it refuses rather than starting one. NFR-001's budget
stays at 50 ms. Nothing here was tuned to make anything pass.
