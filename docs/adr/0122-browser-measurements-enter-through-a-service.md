# ADR-0122: A browser measurement enters observability through a service

**Status:** **Accepted**
**Date:** 2026-08-25
**Supersedes:** —
**Superseded by:** —

## Context

ADR-0118 settled where telemetry goes: **one sink per environment**. Development
and CI feed the Aspire dashboard through the OTLP exporter Aspire injects;
production is deferred until there is a production deployment to attach a sink
to.

It answered that question for **services**, because until now every emitter was
one. Aspire injects its exporter into projects it composes. A browser is not a
project it composes.

Spec 040 produced the first emitter that is not a service. Two legs of the latency
budget — the kiosk decoding a frame, and the overlay being drawn onto it — happen
in the browser and nowhere else. §VII makes a dashboard mandatory for every
implemented leg, so those numbers have to reach the sink somehow, and nothing in
the existing arrangement says how.

Two constraints bound the answer:

1. **The browser is given no OTLP endpoint.** `AppHost` passes the frontend apps
   exactly three values — the API gateway origin, the Keycloak origin, and the
   layout hub origin. No `OTEL_*` anything. Exporting directly would mean exposing
   the dashboard's OTLP endpoint cross-origin, with CORS and whatever auth it
   carries, in an environment where the dashboard exists at all.
2. **A browser-reported number is untrusted input.** It arrives from a client the
   server does not control, and constitution §VIII puts validation at trust
   boundaries.

## Decision

**A measurement taken in a browser reaches observability by being reported to a
service, which records it.** The browser does not export telemetry and does not
speak to a sink.

Three things follow, and each is part of the decision rather than a detail of it.

**1. The browser posts the number, not the start.** It computes the elapsed time
locally and sends the result. A slow or retried post makes the report *late*; it
can never make the measurement *large*. Sending a start timestamp and subtracting
server-side would put the network inside the figure and would additionally need a
clock shared between browser and server, which does not exist — that is the PTP
leg, and it is unbuilt.

**2. The receiving service records it through the same interface as its own
legs.** `ILatencyBudget` already carries the two guards that make a latency
number trustworthy, with their reasons written down: a leg with no recorded start
records **nothing, never a zero**, because a zero reads as a perfect score for a
journey nobody timed; and a negative elapsed time is a stepped clock rather than a
fast journey. Both apply unchanged to a browser's figure, and a third joins them —
an elapsed time long enough to describe a **suspended page** rather than a
journey, since browsers throttle backgrounded tabs.

The guards live in the implementation rather than at call sites precisely so a
second caller cannot forget them. A browser is a second caller.

**3. The browser applies the guards too, and the service enforces them.** A figure
that fails one should not be sent. But the browser is untrusted, so the service is
where refusal happens.

### The related refusal this records

Spec 040 also established a rule about **naming**, and it belongs here because it
will bind the next measurement as much as this one:

> A leg may be recorded **in part**, under a name that says so — but never
> approximated under a name that claims the whole budget.

The decode leg is the case. Its budget spans *SFU sends → kiosk has decoded*, and
a browser cannot observe the sending end without a clock shared with the SFU. So
what is recorded is `receive_to_decoded`, with **no budget attached**, and
constitution §IV records the leg as measured *in part*.

The temptation was a plausible alternative: `jitterBufferDelay` is available and
would produce a number. It measures how long frames wait to be played out — the
**presentation buffer**, a different leg, and the unbuilt one. Recording it as
decode would attribute one leg's time to another and report a budget as met on
the strength of a measurement of something else.

Spec 024 made the same refusal first, declining to record an available fragment as
the event → overlay leg because *"it is not the leg"*. This ADR generalises it.

## Consequences

**Positive:**

- ADR-0118's single sink is preserved rather than worked around. The browser's
  number arrives through a service's meter like every other number.
- The guards apply to browser figures for free, and cannot be forgotten by a new
  caller.
- No OpenTelemetry JS SDK, no bundle cost, no second exporter to configure per
  environment.
- The trust boundary is where §VIII says it should be.
- The next browser-side measurement has a rule to follow rather than a precedent
  to guess at.

**Negative:**

- **A report can be lost** in a way an in-process metric cannot: a failed post is
  a missing sample. Acceptable — these are distributions over many frames, not
  events where one matters.
- **An endpoint exists that accepts numbers from clients.** It is validated and
  guarded, but it is surface that would not exist if the browser exported
  directly.
- **The service that hosts the endpoint gains a concern that is not its domain.**
  Mitigated by the recorder living in `ServiceDefaults`, which exists so every
  context's API layer can take its shared pieces, and by nothing entering a
  domain model — a latency figure is telemetry, not domain state.
- **Two places apply the guards.** Deliberate, and the ADR says which one
  enforces.

## Alternatives Considered

**Option B — the browser exports OTLP directly to the sink — REJECTED.** The
shortest path conceptually, and it makes the browser a first-class emitter.
Rejected because nothing gives the browser the endpoint; exposing the dashboard's
OTLP endpoint cross-origin adds CORS and auth surface for one feature; it works
only where a dashboard exists, so production would need a second answer anyway;
and it makes ADR-0118's "one sink" quietly mean "one sink and also this".

**Option C — a structured console line only — REJECTED as sufficient.** It is the
existing idiom (`resilienceLog`) and costs nothing. Rejected as the *whole*
answer because reading it needs devtools attached to a kiosk, which is the
"recorded, not readable" state constitution §IV already calls **half** discharged
for another leg. Kept **alongside** the report, because it is free and it is what
makes manual verification practical — CI cannot produce video, so a person reading
a running kiosk is the only way either number is ever seen.

**Option D — wait for PTP — REJECTED.** A shared clock would make the decode leg
fully measurable and remove the need for the naming refusal above. Rejected
because it would leave two *built* legs with no number at all, which is the state
spec 040 exists to end, and PTP is the largest unbuilt piece of the budget with no
scheduled work behind it.

## Implementation Notes

Spec `040-kiosk-latency-legs` implements this. The endpoint lives in
StreamDistribution's API — the context the kiosk already calls about what it is
displaying — and the recorder in `ServiceDefaults`, beside the meter it owns.

**Neither measurement can be verified in CI.** `camera-sim`,
`scenario-simulator` and the ICE host-publishing all sit inside
`if (isRunMode && !isE2ETests)`, so a headless browser gets no video. The
automated tests cover the guards and the transport; the numbers themselves are
read by a person against the run-mode stack. That is a property of the
environment, not of this decision, but anyone adding the next browser measurement
will meet it too.
