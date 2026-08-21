# Research: Every leg of the latency budget can be watched

**Feature**: `024-latency-budget-visible` · **Phase 0** · 2026-08-21

The spec said not to assume the five unexamined legs are instrumented, because
spec 023 assumed that of the most important one and was wrong. Looking turned up
something further: **three of the six legs have nothing to measure yet.** They
are not uninstrumented — the code paths do not exist.

---

## Leg by leg

| # | Leg | Budget | State |
|---|---|---|---|
| 1 | Camera → SFU | ≤ 80 ms | **Measurable, cheaply.** MediaMTX is already a container resource with its API on `:9997`. Its config does not enable `metrics`, which it supports. A config change, not custom code. |
| 2 | SFU → kiosk decode | ≤ 120 ms | **Nothing to measure.** `apps/kiosk-web` contains no `<video>`, no `MediaStream`, no `srcObject`, no `RTCPeerConnection`. The kiosk does not decode video. |
| 3 | Presentation buffer (PTP) | ≤ 200 ms | **Nothing to measure.** PTP appears in ADR-0014 and in spec 002's *out of scope* section — *"PTP frame-sync is NOT in scope for spec 002 … PTP-anchored sync is a future-add on top."* No implementation exists. |
| 4 | Event → overlay state | ≤ 200 ms | **Traced, not measured.** Spec 023 registered Wolverine's activity source, so spans exist and cross services. No aggregation, no percentile, no dashboard. |
| 5 | Overlay composite + render | ≤ 50 ms | **Half exists.** The kiosk renders overlays (`CellPage`), but composites them over nothing — there is no video underneath. |
| 6 | Headroom | ≤ 150 ms | **Not a leg.** The arithmetic remainder of the other five against 800 ms. |

### What that means

**Legs 2, 3 and 5 cannot be instrumented by this feature**, and the reason is not
a tooling gap that effort would close. Live video is rendered by
**management-web**, through `apps/shared/src/streaming/WhepClient.ts`; the kiosk —
the screen the 800 ms SLO is *about* — shows overlays and no video.

That is worth stating carefully, because it is a finding about the product and
not only about observability: **the end-to-end path the budget describes is not
yet assembled end to end.** Three of its six legs are future work. The budget was
written for the system as designed, and the system as built has not reached it.

FR-007 exists for exactly this and SC-003 counts a recorded reason as handling a
leg. This is the case the spec anticipated, arriving larger than expected.

---

## Q1 — Can the event→overlay leg be aggregated from what spec 023 added?

**Decision: no, and the distinction is the feature.**

Spans answer *where did this one event go*. A budget is a claim about a
distribution — the tail is the part that matters, and one span cannot describe a
tail. The OpenTelemetry pipeline already configured in `ServiceDefaults` carries
metrics as well as traces (`.WithMetrics(...)` with runtime, ASP.NET Core and
HttpClient instrumentation), so the transport exists; what is missing is an
instrument that records this leg's duration.

**Alternatives considered.** Deriving percentiles from stored spans — rejected:
it needs a trace backend with query capability, which is exactly the ADR-0026
decision that has not been made, and it makes every budget question a query
someone must write. Timing in tests only — rejected: that is spec 023's position,
which is how a two-order-of-magnitude breach stayed invisible.

---

## Q2 — Where does a dashboard come from? (FR-011)

**Decision: present the options, do not pick one in the plan.**

ADR-0026 (Locked) commits to a collector fanning OTLP to **both** the Aspire
dashboard and a Grafana stack during a comparison phase, with a single sink
chosen before v1 GA and an explicit sunset clause.

**None of it exists.** No collector, Prometheus, Grafana, Loki, Tempo or
Alertmanager anywhere in the AppHost. The comparison phase never started, so its
sunset clause has nothing to sunset.

Three coherent responses:

1. **Enact it.** Stand up the collector and the Grafana stack as Aspire
   resources. Largest, and delivers the dashboards §VII asks for in the place the
   ADR intends.
2. **Amend it.** Record that the Aspire dashboard is the sink, and that Grafana
   arrives with production deployment rather than now. Smallest, and honest about
   what is being built.
3. **Split it.** Instrument now against the existing Aspire pipeline; make the
   sink decision when there is something to compare.

This is a governance decision with an ADR marked **Locked**, so it belongs to the
reviewer rather than to the implementer. The plan proceeds on option 3's
technical path — instrument first — because that work is identical under all
three and blocks none of them.

---

## Q3 — Is the Aspire dashboard enough to satisfy "a dashboard"?

**Open, and deliberately.** The Aspire dashboard displays metrics from the OTLP
pipeline, so a histogram would be visible there without a Grafana stack. Whether
"visible in the dev dashboard" satisfies §VII's *"latency-budget dashboards are
mandatory"* is the same question as Q2 and gets the same answer: the reviewer's.

What the plan can do without prejudging it is make the measurement exist, since
every option needs that.

---

## Q4 — What does instrumentation cost? (FR-006, SC-004)

**Decision: measure it, do not argue it.**

Spec 023 recorded warm-path figures either side of adding a trace source and
found no difference at two samples — then said in its own note that the
comparison might be vacuous, because the OTLP exporter only attaches when
`OTEL_EXPORTER_OTLP_ENDPOINT` is set and whether the fixture sets it was never
confirmed.

That ambiguity should not be inherited. Before/after figures here need the
exporter's state established, or the measurement means nothing.

---

## Q5 — Which measurement is the event→overlay leg?

**Decision: publish-to-effect, and say what it excludes.**

Spec 022's `EventReachesItsEffectsTests` already measures arrival-to-effect and
logs it per case; spec 023 built two more harnesses on the same path. The leg as
ADR-0015 defines it is *"event → overlay state (RabbitMQ + projection)"*, which
ends at the state change rather than at the screen.

The honest instrument is therefore the span from the event being accepted to the
effect being applied — and the note must say it excludes delivery to a kiosk,
which is legs 2, 3 and 5 and does not exist.

---

## What this means for the plan

1. **Instrument leg 4** — the only leg that is both implemented and budgeted.
2. **Enable leg 1's metrics** — a MediaMTX config change, cheap, and it makes two
   legs visible rather than one.
3. **Record legs 2, 3, 5 and 6 as unmeasurable, with the reason** — three because
   they do not exist, one because it is arithmetic. This is FR-007's purpose and
   it is most of the six.
4. **Put the ADR-0026 decision in front of the reviewer** rather than resolving
   it by implementation.
5. **Raise the bigger finding separately.** That the SLO's path is not built end
   to end is a product fact this feature discovered and should not absorb — the
   #1655 precedent.
