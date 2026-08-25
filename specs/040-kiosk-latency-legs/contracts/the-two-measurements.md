# Contract: the two measurements, and what each is allowed to claim

**Feature**: `040-kiosk-latency-legs` · **Plan**: [plan.md](../plan.md)

Two figures, two budgets, two names. **One of them does not name its leg**, and
that is the most important line in this document.

---

## Measurement 1 — overlay draw (composite + render, ≤ 50 ms)

**This one is the leg.** ADR-0015 defines it as the overlay being composited and
rendered; the browser can observe exactly that.

| | |
|---|---|
| **Starts** | The overlay's state changes — resolved text arrives, or a highlight is set |
| **Ends** | The browser has painted that change |
| **How** | `performance.now()` at the state change; two chained animation frames to reach after-paint |
| **Name** | `kiosk.overlay_draw` — it may claim the leg, because it is the leg |
| **Budget** | 50 ms |
| **Dimension** | The tile's camera |

`performance.now()`, never `Date.now()` — `CellPage` already carries the reason:
fab clocks are PTP-stepped and an epoch comparison can pin a highlight on forever
or clear it early.

The first animation-frame callback runs after React commits and before paint; the
second runs after that paint. Two callbacks and a subtraction, on a path that
already re-renders — which is what keeps the observer clear of the 50 ms it
observes (**FR-012**).

---

## Measurement 2 — receive-to-decoded (**a fragment of** SFU → kiosk decode, ≤ 120 ms)

**This one is not the leg, and its name must not say it is.**

| | |
|---|---|
| **Covers** | First packet of a frame received → frame decoded |
| **Excludes** | Transit from the SFU to the kiosk — the front half of the budget |
| **How** | `RTCPeerConnection.getStats()`, `inbound-rtp`: `(totalProcessingDelay + totalDecodeTime) / framesDecoded`, sampled as a delta between reads |
| **Name** | `kiosk.receive_to_decoded` — **not** `kiosk.decode_leg`, **not** anything containing the budget |
| **Budget** | **None recorded against it.** It is a fragment; comparing it to 120 ms would report the budget passing on the strength of its cheaper half |
| **Dimension** | The tile's camera |

### Why not the whole leg

The budget spans *SFU sends → kiosk has decoded*. The browser cannot see the
sending end without a clock shared with the SFU, and establishing one is the
**PTP presentation-buffer leg — the leg that is not built**. The statistic that
would close the gap depends on the thing whose absence created this issue.

The other candidates are worse:

- `jitterBufferDelay / jitterBufferEmittedCount` measures how long frames wait to
  be played out. That is the *presentation buffer* — a different leg, and the
  unbuilt one. Recording it here would attribute one leg's time to another.
- `totalDecodeTime` alone is codec work, typically single-digit milliseconds. It
  would report ~3 ms against a 120 ms budget and look magnificent.

### The precedent this follows

Spec 024 declined to record an available fragment as the event → overlay leg,
because *"it is not the leg"* and a plausible number reported against a budget
looks like the budget passing. It defined the fragment, documented it as one, and
recorded it nowhere.

This goes one step further — the fragment **is** recorded, because a built leg
with no number at all is the state this feature exists to end — but under a name
that describes the fragment and with no budget attached. §IV records the leg as
measured **in part**.

---

## What must be true of both

| Requirement | Assert |
|---|---|
| **FR-007** | The two figures are separable. One combined "kiosk latency" satisfies any assertion that a number exists while measuring neither budget |
| **FR-008** | A leg whose start is unknown records **nothing** — asserted as an absence, never as a zero. A zero reads as a perfect score for a journey nobody timed |
| **FR-009** | Negative, or large enough to describe a suspended page rather than a journey, records nothing |
| **FR-011** | The kiosk's picture, overlay, connection and reconnection behave exactly as before |
| **FR-012** | The observer is not a meaningful share of 50 ms |

---

## The transport

The kiosk **posts the elapsed number**, not the start. A slow post makes the
report late; it can never make the measurement large.

```
POST <gateway>/stream-distribution/kiosk-latency
{ "measurement": "overlay_draw" | "receive_to_decoded",
  "camera": "<guid>",
  "elapsedMilliseconds": <number> }
```

Hosted by **StreamDistribution's API** — the context the kiosk already calls
about what it is displaying (`/authorize` for WHEP). The recorder itself lives in
**ServiceDefaults**, which owns the meter and exists so every context's API layer
can take its shared pieces. Neither figure enters a domain model: a latency
number is telemetry, not domain state.

**Both guards are enforced server-side**, in `ILatencyBudget`'s implementation
alongside `RecordEventToOverlayState`. The browser applies them too — a figure
that fails should not be sent — but the browser is untrusted input (§VIII) and
the enforcement point is the service.

The kiosk **also** emits a structured `console.info` line in the existing
`[resilience]` idiom. Not instead: a console line is the *recorded, not readable*
state the constitution calls half discharged. Alongside, because it costs nothing
and it is what makes the measurement visible during the manual procedure that CI
cannot replace.
