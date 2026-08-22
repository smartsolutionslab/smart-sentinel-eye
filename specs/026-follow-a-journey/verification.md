# Verification: A cross-service journey can be followed end to end

**Feature**: `026-follow-a-journey` · **Issue**: #1750 · 2026-08-22

Observed against the real Aspire stack on the development machine, dashboard at
`https://localhost:17069`.

---

## Before (T100)

Every publish from `event-ingestion` was a **trace root** containing only
`event-ingestion` spans — a nested pair of `send` producers and nothing else. The
work it caused was a **separate root**.

Two ends of one journey, unconnected:

| | trace | service |
|---|---|---|
| published | `1d701bae9eff6e9dc2a53f0a7dbf2f3f` | event-ingestion |
| received | `b8e1234ca6fcac2f31f901721e204b17` | automation |

Same message, `08df004c-5fa4-6f2b-c85b-763a4fb00000`. Nothing relates them.

### What the baseline also found, and the spec had missed

Trace `195d91230e630d835afd39ffc1132890`, **before any change**:

```
automation          receive  FabEventIngestedV1            42 ms
  ├─ automation     send     OverlayHighlightRequestedV1    0 ms
  ├─ audit-obs      receive  OverlayHighlightRequestedV1   58 ms   (+0.7 s)
  └─ layout-comp    receive  OverlayHighlightRequestedV1    1 ms   (+4.3 s)
```

One trace, three services, **through RabbitMQ and through the outbox**, across a
4.3-second store-and-forward wait. This is what falsified the premise: nothing
was being lost in the outbox. See research.md, Findings 3 and 4.

---

## After (T108) — SC-001, SC-002, FR-008

Trace **`a44f7abc3e8af7ea3f6d1c89da91a930`**, now titled **"ingest plant-floor
event"**:

```
event-ingestion   ingest plant-floor event          root, Producer   0 ms
  ├─ event-ingestion  send     FabEventIngestedV1                    0 ms
  ├─ audit-obs        receive  FabEventIngestedV1                   10 ms
  └─ automation       receive  FabEventIngestedV1                    7 ms
       ├─ automation     send     OverlayHighlightRequestedV1        0 ms
       ├─ audit-obs      receive  OverlayHighlightRequestedV1       12 ms
       └─ layout-comp    receive  OverlayHighlightRequestedV1        0 ms
```

**One trace. Four services. Plant floor to overlay highlight.** 29 ms end to end.

- **SC-001** — from the layout-composition highlight, the originating event is
  two parents up, by relationship and not by timestamp.
- **SC-002** — from the ingest span, everything it caused hangs beneath it.
- **SC-004** — fan-out appears in both directions: `FabEventIngestedV1` reaches
  audit-observability *and* automation as siblings; the highlight reaches
  audit-observability *and* layout-composition. One event, several trails.

Nothing else changed to achieve this. The publish now happens inside a journey,
and the messaging layer did the rest — as it already did for every other hop.

---

## Per-event, not per-batch (FR-006, SC-005)

Four consecutive ingests, four distinct traces:

```
a44f7abc3e8af7ea3f6d1c89da91a930   13:44:27.925
13fd59514304cbd3a78180f910ff32a5   13:44:28.436
04f10d6c2c7df5a6a3152c5c9221c330   13:44:28.949
88912e616f132eec2383113cdcc47148   13:44:29.458
```

**Stated plainly, because it is the weaker half of this evidence**: these arrived
about 500 ms apart on the simulator's billet tick, so they were probably
*different* batches. The live observation shows per-event traces; it does not by
itself prove the same-batch case.

That case is covered by test, at both layers:

- `JourneyOriginTests.Each_event_begins_its_own_journey` — two origins, two trace
  ids.
- `EventIngestedJourneyOriginTests.Each_event_in_a_batch_begins_its_own_journey`
  — three domain events through one handler, three journeys.

And structurally: `DomainEventDispatcher` invokes handlers one domain event at a
time, so there is no code path on which a batch shares an origin.

---

## Durations did not grow (SC-003, FR-010)

Span durations in the joined trace: **0, 0, 10, 7, 0, 12, 0 ms**. Trace total
**29 ms**. Each span measures its own work.

The before-trace `195d9123…` is the known-good reading for the delayed case:
**4305 ms overall, spans of 42 / 0 / 58 / 1**. The queue wait lands in the
trace's elapsed time, which is the honest place for it, and in no span.

This is research.md Finding 2 confirmed by measurement, and the exact opposite of
what the spec's first version claimed would happen.

`EventToOverlayLatency` is untouched — it computes from `RootIngestedAt`
in-process and takes no input from telemetry.

---

## Tests

| Suite | Result |
|---|---|
| `EventIngestion.Application.Tests` | 49 passed |
| `ServiceDefaults.Tests` | 98 passed |
| `Architecture.Tests` | 32 passed |
| Release build (analyzers, SonarAnalyzer, collection expressions) | clean |

---

## Not done, and why

**A cross-service integration test asserting the relationship.** Aspire runs each
service as its own process, so an integration test cannot observe another
service's `Activity.Current`, and the Aspire dashboard has no supported query API
to assert against. The automated coverage is therefore at the unit level, and the
cross-service proof is the dashboard walk above — which is what FR-008 and SC-007
ask for in the first place ("followable by someone reading it, rather than by a
test asserting it exists").

Said out loud rather than papered over: **if this regresses, no test in CI will
catch it.** A future spec that gives the project a queryable telemetry sink
(ADR-0118 defers that to the production deployment) would close the gap.

---

## Notes from the run

**The scenario simulator died on first boot**, unrelated to this change:
`ScenarioSeeder` timed out after 30 s calling overlay-designer during a cold
start, and `BackgroundServiceExceptionBehavior=StopHost` took the host down —
so nothing published and the stack looked idle rather than broken. Restarting
the resource once the stack was warm fixed it. Worth knowing, because an empty
trace list reads exactly like a feature that does not work.

**A shell `&&` reported a publish that never happened.** `docker exec … | head
&& echo PUBLISHED` printed PUBLISHED on `head` succeeding while the exec had
failed with "executable file not found". Caught immediately, and recorded
because it is the same shape as everything else this spec has been caught by.

---

## Measurement (T101, T111) — SC-006, FR-009

`dotnet test tests/Integration.Tests --filter "Category=Measurement"`, three runs,
same machine, same session. **The middle run has the change reverted in place**
(`git revert -n 724af4c`), which is how the baseline T101 failed to take was
recovered.

| | run A — change in | run B — **reverted** | run C — change in |
|---|---|---|---|
| offered | 365/s | **408/s** | 405/s |
| sustained end to end | 355/s | **398/s** | 396/s |
| arrival→visible **p50** | 169 ms | **138 ms** | **137 ms** |
| p95 | 1 090 ms | 920 ms | 1 339 ms |
| p99 | 1 471 ms | 1 259 ms | 2 839 ms |
| out of order | 0 | 0 | 0 |
| first-event split, round 2 | 893 ms | 478 ms | 427 ms |
| first-event split, round 3 | 931 ms | 520 ms | 414 ms |

**No regression.** Run C carries the same code as run A and lands on the
baseline: 405 vs 408/s offered, 396 vs 398/s sustained, p50 **137 vs 138 ms**,
split rounds 427/414 against 478/520 — slightly *better* on the split.

**Run A was contaminated**, and I would have reported it as a regression. It
followed two killed fixture boots and a Release build on the same machine; every
figure in it is worse, consistently, across two independent tests, which is
exactly what a real regression looks like. What separated them was running the
experiment a third time instead of reasoning about the first two.

p95/p99 move around between runs (920 → 1 090 → 1 339) and the tail is not a
stable measure here. **p50 and sustained throughput are**, and both are flat.

Against the historical record: spec 020 recorded 398/s sustained and p50 164 ms
on this same test. Both hold.

### Why the cost is near zero

One `Activity` per ingested event, started and stopped around a publish that
already does a database write and a broker send. At the 400/s this harness can
offer it is unmeasurable; at the 5 000/s spec 006 sizes the path for it remains
one allocation per event against an outbox insert.

**Not argued — that reasoning is why run A nearly got reported.** The number is
137 ms against a 138 ms baseline.
