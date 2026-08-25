# Verification: Every leg of the latency budget can be watched

**Feature**: `024-latency-budget-visible` · #1681 · observed 2026-08-21

**Status: one leg readable, one instrument built and unused, four legs
explained — and §VII remains unmet.** That is the honest outcome and it is
stated first rather than at the end, because a feature that closes an issue by
explaining why it cannot be closed should not spring that on a reviewer.

---

## 1. The premise held (T001)

The question the feature exists to make answerable — *what is the p99 of the
event-to-overlay leg?* — could not be answered. No `Meter`, histogram or counter
existed anywhere in `src/`; no collector, Prometheus or Grafana in the AppHost.
Nothing measured how long any leg took.

## 2. The exporter is attached, and spec 023's caveat can be retired (T002)

Spec 023 measured instrumentation cost before and after, then recorded that the
comparison **might be vacuous**: the OTLP exporter only attaches when
`OTEL_EXPORTER_OTLP_ENDPOINT` is set, and nobody had checked whether the fixture
sets it. T002 checked.

```
migrations, camera-catalog, stream-distribution, layout-composition, …
    OTEL_EXPORTER_OTLP_ENDPOINT = http://localhost:4317
postgres, rabbitmq, keycloak, mediamtx, mosquitto, minio
    (none — container resources, expected)
```

Every .NET project resource exports. **Spec 023's no-regression figure stands**,
and this feature inherits a resolved question instead of an open one.

## 3. The instrument (T003, T004)

`LatencyBudget` in `ServiceDefaults`, recording a **distribution**. Registered
with the meter provider beside spec 023's trace source — an unregistered meter
records into nothing and reports nothing about it, the same silence an
unregistered trace source produces.

Every measurement carries `leg`, `leg.budget_ms` and **`segment.is_whole_leg`**.
The last one matters more than it looks: it is usually false, and putting it on
the metric rather than in a document means a dashboard can refuse to compare a
fragment against a whole leg's budget.

## 4. The SFU leg is readable, for one config line (T009–T011)

`camera → SFU`, budget 80 ms, previously nothing.

MediaMTX already measures its own RTP ingest and can publish it; the AppHost had
never enabled it. Turning it on exposed **154 lines** of metrics.

T011 exists because this edits the configuration of a running media server, and
"should be harmless" is not evidence on the streaming path: the API still lists
paths afterwards, so the change opened a listener and touched no media path.

**The cheapest thing in the feature, and the only leg the product does not have
to instrument itself.**

## 5. Why the event-to-overlay leg is still not measured — the finding (T005, T006)

**The leg cannot be measured inside the product without a contract change.**

ADR-0015 defines it as *RabbitMQ + projection*: an event accepted through to its
effect applied. No service sees both ends.

- `FabEventIngestedV1` carries `IngestedAt` — the moment of acceptance.
- Automation consumes it and publishes `SystemVariableValueRequestedV1` with
  **fresh metadata**: `new EventMetadata(Guid.CreateVersion7(), requestedAt, fab, null)`.
- The service that applies the effect therefore knows when *Automation asked*,
  and has no way to know when the event *arrived*.

Measuring the whole leg needs the ingestion timestamp propagated through the
chain. That is a change to `Shared.Contracts`, and plan.md said an interface
change would be a finding to raise rather than a thing to quietly make.

**What was deliberately not done.** The available fragment —
Automation-asked to variable-applied — could have been recorded and would have
produced a plausible number. Two reasons it was not:

1. **It is not the leg.** It starts after ingestion and rule evaluation and ends
   before anything reaches a screen. Reported against a 200 ms budget it would
   look like the leg passing. T006 exists to catch exactly this, and it caught it
   before the code was written rather than after.
2. **It would need a layering violation to obtain.** The applying handler is in
   `SystemVariables.Application`, which does not reference `ServiceDefaults` —
   deliberately: BoundaryTests describes ServiceDefaults as existing "so every
   context's **API layer** can take" its shared pieces. Adding an infrastructure
   reference to an Application layer, in order to publish a misleading number, is
   two wrongs.

So `LatencySegment.AutomationToVariableApplied` is defined, documented as a
fragment, and **not recorded anywhere**. The instrument exists; this leg does not
yet feed it.

## 6. The four legs that get an explanation (T012–T015, T018)

> ### ⚠ Correction, 2026-08-25 (spec 040)
>
> **Two rows of the table below are wrong, and the error propagated.** The
> kiosk **does** decode video and **does** composite overlays onto it:
> `CellPage` renders `<CameraViewer …>`, a **shared** composite in
> `apps/shared` that owns the `<video>` element, drives an
> `RTCPeerConnection` with `addTransceiver('video', {direction:'recvonly'})`,
> and draws the overlay on the live frame.
>
> **What the search missed.** It looked in `apps/kiosk-web` and found no
> `<video>`, no `MediaStream`, no `RTCPeerConnection` — all true of that
> directory, and none of it true of what the kiosk *renders*. The capability
> is one directory over, in the composite the kiosk imports.
>
> **Where it went.** From here into constitution §IV's leg table,
> `CLAUDE.md`'s latency section, and issue 1714 itself. Four documents
> agreed with each other; none had been checked against the code. Because
> §VII's obligation is conditional on §IV's table, both legs carried no
> observability obligation for as long as the record was wrong — the exact
> clerical error §IV warns about.
>
> **Left in place deliberately.** This note records what was found at the
> time and how a wrong finding travels; deleting it would remove the only
> trace of the second thing. The corrected state lives in constitution §IV.
>
> Only the **presentation buffer** row below is still accurate as written.


**"Not built" is a different problem from "built but unmeasured"**, and a reader
who cannot tell them apart will file the wrong follow-up.

| Leg | Budget | Why not measured | Which problem |
|---|---|---|---|
| SFU → kiosk decode | 120 ms | `apps/kiosk-web` has no `<video>`, no `MediaStream`, no `RTCPeerConnection`. The kiosk decodes nothing. | **Not built** |
| Presentation buffer | 200 ms | PTP appears in ADR-0014 and in spec 002's *out of scope* section as a "future-add". Nothing implements it. | **Not built** |
| Composite + render | 50 ms | Overlays render (`CellPage`), over nothing — there is no video underneath to composite onto. | **Half built** |
| Headroom | 150 ms | The arithmetic remainder of the other five against 800 ms. Not a segment that can be timed. | **Not a leg** |

### What would unblock each

- **Decode** and **composite + render**: the kiosk would need to display video.
  The capability exists — `apps/shared/src/streaming/WhepClient.ts`, used by
  **management-web** — so this is a matter of the kiosk using it, not of building
  a client.
- **Presentation buffer**: PTP-anchored playout has to exist first. It is a
  design decision with an ADR behind it, not an omission.
- **Headroom**: nothing. It is a subtraction, and treating it as measurable would
  be a category error.

### The finding underneath all of it

**The 800 ms path is not assembled end to end.** Three of its six legs are
future work. The budget was written for the system as designed; the system as
built has not reached it.

That is a fact about the product, discovered by an observability feature, and it
is filed separately rather than absorbed here — the precedent being #1655, where
a measurement feature filed what it found.

## 7. Where this leaves §VII (T019)

The constitution says:

> Latency-budget dashboards (per ADR-015) are mandatory. **A leg without a
> dashboard cannot ship.**

After this feature: **one leg readable, five not, no dashboards.** §VII is not
met and this feature does not meet it.

Three coherent responses, and the choice is a constitutional reading rather than
an implementation decision:

1. **Amend §VII** to bind only legs that are implemented, which is what the rule
   can actually mean for a system whose path is half-built.
2. **Accept the gap** with the reason recorded — §6 is that reason — and revisit
   when the kiosk displays video.
3. **Treat the unbuilt legs as blocking**, which would mean the streaming path
   cannot progress until it can be watched.

**No exception is requested.** The situation is reported for judgement.

## 8. The ADR-0026 decision (T024)

ADR-0026 is **Locked** and commits to a collector fanning OTLP to both the Aspire
dashboard and a Grafana stack during a comparison phase, with a single sink
chosen before v1 GA and an explicit sunset clause.

**None of it exists.** No collector, Prometheus, Grafana, Loki, Tempo or
Alertmanager anywhere in the AppHost. The comparison phase never started, so its
sunset clause has nothing to sunset.

- **Enact it** — stand the stack up as Aspire resources; largest, and delivers
  the dashboards §VII names.
- **Amend it** — record that the Aspire dashboard is the sink and Grafana arrives
  with production; smallest, and honest about what is being built.
- **Split it** — instrument now, decide the sink when there is something to
  compare.

This feature took the technical path common to all three and stopped there.

## What this does not establish

**No dashboard exists** (T020, T021 not done). The SFU publishes metrics and the
instrument records them; nothing displays either against a budget. SC-002 is not
met.

**Nothing about a fab.** The usual caveat from specs 020, 022 and 023 applies
unchanged.

**The instrumentation's cost is unmeasured** (T017). The instrument records
nothing yet, so there is nothing to measure the cost of. When the leg feeds it,
SC-004's 5% bound still applies.
