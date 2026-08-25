# Phase 0 — Research: measuring two legs that already run

**Feature**: `040-kiosk-latency-legs` · **Spec**: [spec.md](./spec.md)

Eight questions. The first two decided whether the feature is possible; the
fourth found that one of the two legs **is not directly observable** and needs
the same refusal spec 024 made rather than a plausible number.

---

## 1. There is a real SFU, and a real simulated camera — in dev only

**Finding**: the dev stack runs an actual WebRTC path end to end.

- `mediamtx` — a real SFU container (`bluenviron/mediamtx:latest-ffmpeg`) with
  `rtsp`, `whep`, `api` and `metrics` endpoints. The `metrics` endpoint was added
  by **spec 024** precisely so camera → SFU could be read, which is the working
  precedent for discharging a leg here.
- `camera-sim` — a second MediaMTX serving a looping `sim-loop.mp4` over RTSP,
  plus `scenario-simulator`, which seeds the catalog and provisions a loop path
  per registered camera.
- ICE is published straight to the host (`--publish 8189:8189/udp` + `tcp`,
  `MTX_WEBRTCADDITIONALHOSTS=127.0.0.1`) so a browser on the developer's machine
  can actually receive media.

**So both legs are exercisable by hand.** A kiosk in `dotnet run` shows real
decoded video with a real overlay on it.

---

## 2. Neither leg is exercisable in CI, **by design**

**Finding**: `camera-sim`, `scenario-simulator` and the ICE host-publishing are
all inside `if (isRunMode && !isE2ETests)`.

Under e2e there is no simulated camera, nothing provisioning loop paths, and no
route for ICE to reach the browser. **A Playwright kiosk gets no video**, and that
is a deliberate choice made before this feature, not an oversight to fix here.

**Decision**: verification is a **written manual procedure** against the run-mode
stack, not an automated check.

The spec anticipated this and permits it — *"a written procedure a person follows
is an acceptable answer; an automated check that passes without exercising the
measurement is not."* Automated tests still cover everything that does not need a
stream: the guards, the separation of the two figures, the transport, and the
corrected record. What they cannot cover is a real number from a real frame.

**Alternatives considered**: turning the simulator on under e2e (rejected —
changes CI's shape and runtime for one feature's convenience, and the exclusion
looks deliberate enough to be someone's considered decision); a fake
`MediaStream` via `--use-fake-device-for-media-stream` (rejected — it produces a
locally-generated stream with no SFU in the path, so the number would describe
Chromium's test pattern rather than the leg).

---

## 3. The browser cannot reach a sink, so it reports to a service instead

**Decision**: the kiosk **posts its measurement to a service endpoint**, which
records it through the existing meter. The browser does not emit telemetry
directly.

**Rationale**:

- **Nothing gives the browser an OTLP endpoint.** The apps receive exactly three
  environment values — `VITE_API_GATEWAY_URL`, `VITE_KEYCLOAK_URL`,
  `VITE_LAYOUT_HUB_ORIGIN`. No `OTEL_*`, no OTLP anything. Aspire injects its
  exporter into **services**; a browser is not one.
- **It keeps ADR-0118 intact.** One sink per environment, and the browser does
  not become a second emitter into it. The measurement enters observability the
  same way every other number does — through a service's meter.
- **It reuses `ILatencyBudget`'s shape and its two guards**, which is the whole
  reason those guards live in the implementation rather than at call sites: a
  second caller cannot forget them.
- **The network hop does not corrupt the figure.** The elapsed time is computed in
  the browser *before* it is sent; the post carries a number, not a start.

**Alternatives considered**: browser → the Aspire dashboard's OTLP/HTTP endpoint
directly (rejected — needs the endpoint exposed cross-origin with CORS and
whatever auth it carries, makes the browser an emitter ADR-0118 never
contemplated, and would work only in dev where the dashboard exists); a
`console.info` line in the existing `resilienceLog` idiom (rejected as
*sufficient* — see §7 — though it is worth having alongside).

---

## 4. **The decode leg is not directly observable, and this plan will not invent a number for it**

**Finding**: no single WebRTC statistic is *"SFU → kiosk decode ≤ 120 ms"* as
ADR-0015 defines it. What `getStats()` offers on an `inbound-rtp` report:

| Statistic | What it actually measures | Is it the leg? |
|---|---|---|
| `totalDecodeTime / framesDecoded` | Codec work per frame, typically single-digit ms | **No** — a fraction of it |
| `jitterBufferDelay / jitterBufferEmittedCount` | How long frames wait to be played out | **No** — that is the *presentation buffer*, the unbuilt leg |
| `totalProcessingDelay / framesAssembled` | First packet received → frame handed to the decoder | Closest, but excludes network transit from the SFU |
| `estimatedPlayoutTimestamp` | Sender-clock estimate of playout | Needs a synchronised clock, which is the unbuilt PTP leg |

The honest position: the budget's 120 ms spans *SFU sends → kiosk has decoded*,
and the browser cannot see the sending end without a shared clock. **The one
statistic that would close the gap depends on the leg that does not exist.**

**Decision**: record **`totalProcessingDelay + totalDecodeTime`, per frame**, and
**name it for what it is** — receive-to-decoded, not SFU-to-decoded. Its name must
not claim the leg.

**This is spec 024's refusal, applied a second time.** That spec declined to
record an available fragment as the event → overlay leg, on the grounds that *"it
is not the leg"* and a plausible number reported against a budget looks like the
budget passing. The same reasoning applies, and the same shape of answer: record
the honest fragment under an honest name, and say in §IV that the leg is measured
**in part**.

**Consequence for SC-007**: this leg lands **partly discharged**, and the record
must say so in the vocabulary it already uses for #1707 rather than rounding up.
The plan does not pretend otherwise.

**Alternatives considered**: recording `jitterBufferDelay` as the leg (rejected —
it is a different leg, and the one that is unbuilt); waiting for PTP (rejected —
it would leave a built leg with no number at all, which is the state this feature
exists to end).

---

## 5. Composite + render is directly observable, and cheaply

**Decision**: timestamp when the overlay's state changes, measure after the
browser has painted, using two chained animation frames.

The first frame callback runs after React has committed and before paint; the
second runs after that paint has happened. The difference from the state change
is *overlay changed → overlay on screen*, which is the leg as ADR-0015 defines it.

**Cost**: two callbacks and one subtraction, on a path that already re-renders.
Against a 50 ms budget (**FR-012**) that is noise.

**`performance.now()`, never `Date.now()`** — following `CellPage`'s existing
comment, which records the reason: fab clocks are PTP-stepped and an epoch
comparison can pin a highlight on forever or clear it early. That precedent
decides the clock question; this plan applies it rather than re-litigating it.

**Alternatives considered**: Element Timing (rejected — needs an `elementtiming`
attribute and reports first paint of an element, not each subsequent change);
Event Timing (rejected — the overlay changes from a hub push, not a user
interaction, so there is no event to attribute it to).

---

## 6. Per tile, not per wall

**Decision**: both figures carry the tile's camera as a dimension.

A wall shows up to four tiles, each with its own peer connection and its own
overlay. A per-wall figure hides one bad tile behind three good ones — and a
kiosk showing one frozen camera among four is exactly the failure an operator
would report and a per-wall average would not show.

Cardinality is bounded and small: at most four tiles per wall, and the grid
invariants cap it (≤ 4 tiles, ADR-0112).

---

## 7. `resilienceLog` is the right idiom to follow, and not sufficient on its own

`apps/shared/src/observability/resilienceLog.ts` is a structured `console.info`
with a stable `[resilience]` prefix, documented as **an observable contract**:
Playwright asserts on it and kiosk remote-debug sessions grep for it.

**Decision**: emit a matching structured line **as well as** posting the
measurement, and keep the same folder and shape.

Not instead: a console line is exactly the *recorded, not readable* state the
constitution calls **half** discharged for #1707 — reading it needs devtools
attached to a kiosk, which is the "attach a debugger" FR-010 rules out. But it is
free, it matches the one idiom the codebase already has, and it is what makes the
measurement visible during the manual procedure §2 forces this feature to rely on.

---

## 8. An ADR is warranted, and it is 0122

**Decision**: write `docs/adr/0122-browser-measurements-enter-through-a-service.md`.
`docs/adr/` runs to 0121; 0122 is free.

ADR-0118 decided **one sink per environment**. It did not contemplate an emitter
that is not a service, because until now there wasn't one. This decides that a
browser measurement reaches observability by being **reported to a service that
records it**, rather than by the browser emitting to a sink — which preserves
ADR-0118's single sink rather than working around it, and gives the next
browser-side measurement a rule to follow instead of a precedent to guess at.

It should also record §4's refusal: that a leg may be recorded **in part** under a
name that says so, rather than approximated under a name that claims the whole
budget.
