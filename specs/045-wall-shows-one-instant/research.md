# Phase 0 Research: A wall shows one instant

**Feature**: `045-wall-shows-one-instant` · **Spec**: [spec.md](./spec.md)

**Everything below was measured, not read.** The spec's central risk is a
mechanism that sounds right and does not exist in the browser we ship to, so the
questions were settled against a real MediaMTX and a real Chromium before any of
them reached the plan.

## How it was measured

An **isolated** MediaMTX container — `bluenviron/mediamtx:latest-ffmpeg`, the
same image the AppHost runs — with two paths, `alpha` and `beta`, both looping
`mill-roughing.mp4`. **Identical sources deliberately**: with the same content on
both, any difference between the tiles is buffer skew and cannot be content.

Two WHEP sessions opened in one Playwright Chromium page
(`HeadlessChrome/151.0.7922.34`), same-origin against the container. That is the
shape of two tiles on one wall: one browser, one compositor, one SFU.

Isolated rather than against the running AppHost stack, so nothing here depended
on Keycloak, the gateway, or the auth callback — and so the probe could not
disturb a stack that had been up for two hours.

---

## R1. `estimatedPlayoutTimestamp` does not exist. The obvious design is dead.

**Finding**: `estimatedPlayoutTimestamp` is **absent** from Chromium's
`inbound-rtp` statistics for video — with an audio transceiver negotiated and
without one.

It is in the spec, it is exactly the primitive this feature wants — *"what
instant is this tile showing"* — and Chromium does not populate it for video.

**Consequence**: any design that reads each tile's playout position directly is
not implementable. This had to be found now. Discovering it in Phase 4 would have
meant a rewrite after the mechanism was already in the tasks.

**Decision**: build the observable out of statistics that *are* populated (R3),
and align on a computed lag rather than a reported playout position.

---

## R2. `jitterBufferTarget` is a real actuator

**Finding**: `RTCRtpReceiver.jitterBufferTarget` (milliseconds, standardised)
and `playoutDelayHint` (seconds, legacy) both exist on the prototype and assign
without throwing. Setting `jitterBufferTarget = 300` moved that tile's per-frame
buffer delay from **~10 ms to ~273 ms**. It tracks its setpoint.

**Decision**: `jitterBufferTarget`, not `playoutDelayHint` — standardised,
milliseconds (the unit the budget is written in), and the legacy one buys nothing
here.

**The setpoint is not evidence.** `jitterBufferTarget` is **not** reported back
in `getStats`. What was asked for and what happened are two different numbers,
and only the achieved value is a measurement. This is the same trap as spec 040's
"a configured value is an intention" — FR-007 already says measure, don't assume,
and R2 is why that requirement exists.

---

## R3. The observable is a delta, and the house already has the idiom

**Finding**: per-tile lag is computable from statistics that are populated:

```
lag = Δ jitterBufferDelay / Δ jitterBufferEmittedCount
    + Δ totalProcessingDelay / Δ framesDecoded
```

**Deltas between two samples, never cumulative ratios.** A cumulative ratio
reports the session average and flattens exactly the excursion a budget is about.

**Decision**: reuse the existing idiom rather than invent a second one.
`decodeElapsedBetween` in `apps/shared/src/observability/kioskLatency.ts` already
does precisely this, already documents why, and already returns `null` rather
than `0` when there is nothing to report — *"a zero would read as a perfect score
for a journey nobody timed"*. The new code follows it, including the null.

---

## R4. Alignment converges — and it is bought with latency

**Measured.** Both tiles sampled, then both set to `max(lag) + 20 ms = 55 ms`:

| | before | after |
|---|---|---|
| alpha | 35.053 ms | 61.334 ms |
| beta | 25.644 ms | 56.651 ms |
| **spread** | **9.410 ms** | **4.683 ms** |

Two conclusions, and the plan needs both.

**The control loop works.** Spread halved and both tiles converged on the
setpoint. The mechanism is real.

**Absolute lag roughly doubled.** ~30 ms became ~59 ms. This is FR-005 and FR-006
stated in numbers rather than in prose: **alignment is paid for in latency**, out
of the same 800 ms budget this leg belongs to. A controller with no ceiling would
buy perfect alignment at any price, silently.

**Decision**: the 200 ms leg budget is a hard cap in the controller, not a
guideline — and the cap is what FR-012a's release-and-mark behaviour hangs off.

---

## R5. The shared reference exists, and it is not PTP

**Finding**: `remote-outbound-rtp.remoteTimestamp` is populated and RTCP sender
reports flow (`reportsSent` climbing steadily). Round-trip time is available from
`candidate-pair.currentRoundTripTime` — **2 ms** on this box — and also on
`remote-outbound-rtp.roundTripTime`.

Every tile on a wall is served by **one MediaMTX process**, so every tile's
sender reports carry **one clock**. That is the common reference the spec's
Assumptions depend on, and it exists **without a grandmaster, without PTP-aware
switches, and without any PTP at all**.

**Decision**: align against the SFU's send clock. This is exactly the scope the
spec claimed — *"aligned against a reference the system actually has, not true
wall-clock capture time"* — and R5 is the evidence that the claim was safe.

**It also names the leg wrongly.** §IV calls this leg *"presentation buffer
(PTP)"*. What is being built uses no PTP. See D3.

---

## R6. The verification will pass while proving nothing

**Finding**: on an idle local box with two identical sources, the spread was
**already 9.4 ms before any coordination existed** — comfortably inside the 33 ms
bound this feature commits to.

**So a green measurement here is nearly worthless on its own.** Delete the
controller and the test still passes. This is the single most likely way for this
feature to ship broken: a suite that is green because the problem does not occur
on the machine running it.

**Decision**: skew must be **deliberately induced** for any run to be evidence —
unequal `jitterBufferTarget` values, or a throttled source. Both the automated
tests and the manual walk assert on *convergence from an induced spread*, never
on a spread that happened to be small. Recorded in [quickstart.md](./quickstart.md).

This is FR-015's *"not verifiable here"* in its most concrete form.

---

## Decisions the plan takes from this

### D1. The contract is a closed set, so new names change both sides

`RecordKioskLatency` validates `Measurement` against exactly
`"overlay_draw"` and `"receive_to_decoded"` and returns a 400 for anything
else. A new measurement name is **not** additive on the client — the server
refuses it until the switch and its `LatencySegment` exist. Client and server
change together, in one commit, or the kiosk posts figures into a validation
error.

### D2. Skew is not a duration, and must not become a `LatencySegment`

`LatencyBudget.Record` records *how long a named segment took*. The delay this
leg adds is a duration and fits. **Inter-tile skew is a spread, not a journey** —
recording it as a latency segment would put a number under a name that means
something else, which is the exact failure this repo keeps catching (`in part`,
`recorded not readable`, the `isWholeLeg` flag all exist because of it).

**Decision**: `presentation_buffer` is a `LatencySegment` with the 200 ms budget
and `isWholeLeg: true` — the kiosk controls both ends of the delay it adds, so
nothing is missing. **Wall skew gets its own instrument**, not a latency segment.

### D3. The leg's name is wrong and the ADR has to settle it

§IV calls it *"Presentation buffer (PTP)"*. R5 shows the mechanism uses no PTP.
Leaving a leg named after a technology it does not use re-creates precisely the
condition spec 040 found — a record that four documents agreed on and nobody had
checked against the code. The ADR amending ADR-014 renames it or justifies
keeping the name.

### D4. Expect the decode leg to stay "in part"

FR-011 requires the decode leg's `in part` to be revisited. The honest expected
answer is **it stays, restated** — RTCP gives a sender clock and RTT gives a
round trip, but Chromium exposes no per-frame send-to-arrival mapping, so
SFU-send → decoded remains an *estimate* (`RTT/2 + buffer + decode`) rather than
a measurement. FR-011 permits either outcome and asks for the reason; the reason
is this. Raising it to *measured whole* on the strength of an estimate would be
rounding up, which §IV's own wording forbids.

### D5. Where the controller lives

Only the wall sees every tile. `CellPage` owns the wall; `CameraViewer` and
`useWhepSession` own one tile each and are **shared with management-web, which
has no wall**. So:

- the **decision** (what target every tile should hold) belongs to the wall;
- the **actuation** (set this receiver's target) belongs to the tile;
- the **arithmetic** is pure and belongs in a module with no React in it, so it
  is testable without a browser — mirroring `kioskLatency.ts`.

A single-tile wall runs no controller at all (FR-004): with one tile there is
nothing to align, and it must not pay a millisecond for the feature.

**management-web is already insulated on the server side**: `RecordKioskLatency`
drops reports from non-kiosk principals via `IsBrowserKiosk()` (#1893). It must
stay insulated on the client side too — by not mounting the controller, not by a
flag inside the shared composite.

### D6. One correction to the brief

**ADR-0084's 300 LOC/file limit is a C# rule** — SonarAnalyzer **S104**,
configured in `Directory.Build.props`. The frontend inherits only
*max-lines-per-function* (50). `CellPage.tsx` is **380 lines** and violates
nothing.

That is not a licence to grow it. The controller goes in its own hook and its
own pure module for the reasons in D5 — testability and management-web
isolation — and not because a limit forced it.

---

## Alternatives considered

| Option | Why not |
|---|---|
| Read `estimatedPlayoutTimestamp` per tile and align directly | **Does not exist in Chromium for video** (R1). This was the intended design. |
| `playoutDelayHint` as the actuator | Legacy, non-standard, and in seconds. `jitterBufferTarget` is standardised and already in the budget's unit (R2). |
| Stamp frames in the SFU, per ADR-014's "StreamKeeper emits presentation timestamps" | Nothing we own is in the media path. MediaMTX is an unmodified third-party container; doing this means forking it or writing a real SFU. Out of all proportion to the leg. |
| Align to a fixed buffer depth for every tile | Simple and wrong: it ignores the per-tile lag it is supposed to equalise, and either over-delays good tiles or fails to catch bad ones. R4 shows the spread is what has to be closed. |
| Trust the configured `jitterBufferTarget` as the reported figure | The setpoint is not the achieved value and is not in `getStats` (R2). FR-007 forbids it. |
| Cumulative `jitterBufferDelay / jitterBufferEmittedCount` | Reports the session average and hides excursions (R3). |
| Wait for PTP hardware and build the whole of ADR-014 | The hardware is a deployment prerequisite that does not exist here, and inter-display sync is explicitly out of scope. Intra-wall needs no PTP (R5). |
