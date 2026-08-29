# ADR-0128: A wall is aligned by receiver playout, not by PTP

**Status:** Accepted
**Date:** 2026-08-28
**Amends:** ADR-014 (video-wall sync), constitution §III's Stream Distribution
description, §IV's leg names, and §Frontend's browser requirements
**Relates to:** ADR-015, ADR-0021, ADR-0117, ADR-0122, ADR-0123, spec 045, issue 1714

## Context

ADR-014 is **Locked** and specifies the one leg of the 800 ms budget that has
never been built:

> Video-wall sync = frame-accurate via PTP (IEEE 1588) + coordinated playout.
> StreamKeeper emits presentation timestamps; kiosks buffer ~100–200 ms and
> present at T. Inter-display skew target < 5 ms. Deployment prerequisite:
> PTP-aware switches in the fab OT network.

Spec 045 set out to build it. **Three facts in the system as built contradict it,
and none of them is a detail.**

**1. Nothing we own is in the media path.** ADR-014 assigns the emitting of
presentation timestamps to "StreamKeeper". The SFU is **MediaMTX** — an
unmodified third-party container given a config file (`AppHost.cs`). Our
`StreamDistribution` context provisions paths and authorizes WHEP sessions; it
never touches a frame. There is no component of ours positioned to stamp one, and
putting one there means forking MediaMTX or writing an SFU.

**2. No browser exposes a PTP clock.** Constitution §Frontend requires target
browsers to have *"WebRTC and PTP-aware time APIs"*. **No shipping browser
exposes any such API to a web page.** That requirement has been unsatisfiable
since ratification, and no browser choice would satisfy it.

**3. The hardware ADR-014 requires is not present.** A grandmaster and PTP-aware
switches are ADR-014's own stated deployment prerequisite. Everything in this
repository is verified on a single developer workstation. Any claim about
**inter-display** skew is unverifiable here, and would stay unverifiable however
the software were written.

So the leg could not be built as specified. It could, however, be built —
**spec 045 measured the alternative before proposing it** (`specs/045-.../research.md`),
against the same MediaMTX image the AppHost runs and a real Chromium:

- `estimatedPlayoutTimestamp`, the statistic that would answer *"what instant is
  this tile showing"*, is **absent** from Chromium's `inbound-rtp` for video.
- `RTCRtpReceiver.jitterBufferTarget` **is** settable and tracks its setpoint:
  set to 300 ms, a tile's per-frame buffer delay moved ~10 ms → ~273 ms.
- Per-tile lag is computable from `jitterBufferDelay` and `totalProcessingDelay`
  as deltas between samples.
- **RTCP sender reports flow, and every tile on a wall is served by one MediaMTX
  process — so every tile already shares one sender clock.**
- Aligning two tiles to a common target closed their spread 9.410 ms → 4.683 ms.

The last point is the one that matters. **A shared reference already exists, and
it is not PTP.** It is the SFU's own clock, carried in RTCP, available to every
tile of a wall without a grandmaster and without a PTP-aware switch anywhere.

## Decision

**Intra-wall playout alignment is done receiver-side against the SFU's RTCP
sender clock. PTP is not required for it, and no component emits presentation
timestamps.**

Four things follow, and each amends something.

### 1. "StreamKeeper emits presentation timestamps" is withdrawn

Nothing emits presentation timestamps, and nothing will while the SFU is a
third-party process we do not modify.

**Replaced by:** every tile of a wall is served by one SFU and therefore already
shares that SFU's clock via RTCP sender reports. A kiosk measures each tile's
lag from its own receiver statistics and equalises it by setting each receiver's
playout target. The alignment reference is the SFU's send clock.

**This bounds what "one instant" can mean, and the bound is honest.** Cameras are
independent devices with unsynchronised capture clocks; nothing downstream can
recover a moment that was never recorded. A wall is made *self-consistent* — what
an operator comparing tiles actually needs — with no claim that two tiles
captured at the same absolute microsecond.

### 2. The `< 5 ms` inter-display target is retained, and is not the intra-wall bound

**It stays ADR-014's target for inter-display synchronisation, which remains
unbuilt and out of scope.** Achieving it needs a clock shared between machines —
which is PTP, with the grandmaster and PTP-aware switches ADR-014 already names.
That is a later feature, on a fab network, and this ADR does not weaken it.

**The intra-wall bound is 33 ms**, and it is a different number for a different
problem. Tiles on one wall are painted by one compositor in one frame, so two
tiles cannot visibly differ by less than a frame interval; 33 ms is one frame at
the 30 Hz cadence floor ADR-0123 already requires of a kiosk.

**These two numbers must not be confused.** Applying 5 ms intra-wall would set a
sub-frame target that is unobservable in a browser and would pass or fail on
measurement noise. Applying 33 ms inter-display would silently relax a target set
for hardware that genuinely can do better.

### 3. §Frontend's "PTP-aware time APIs" requirement is removed

It describes no browser that exists. It is replaced by what the kiosk actually
requires: **WebRTC with `RTCRtpReceiver.jitterBufferTarget`, and `getStats`
reporting `inbound-rtp` jitter-buffer and processing counters.**

Both were verified present in Chromium before this was written. This is a
requirement that can be checked against a browser, which the old one could not
be.

### 4. The leg is renamed: "presentation buffer (PTP)" → "presentation buffer (playout alignment)"

The mechanism uses no PTP. **A leg named after a technology it does not use is a
record that will mislead the next reader**, and this programme has already paid
for that once: spec 040 found two legs recorded as unbuilt while their code ran
on every kiosk, because four documents agreed with each other and none had been
checked against the code.

ADR-015's leg name — *presentation buffer* — and its ≤ 200 ms budget are
unchanged. Only the parenthetical naming the mechanism changes.

**PTP itself is not abandoned.** Constitution §Stack's *"PTP (IEEE 1588)
grandmaster per fab"* stands, for inter-display sync and for fab-wide time
correlation. What changes is that **this leg does not depend on it**.

## Consequences

**Positive — the leg becomes buildable.** A quarter of the 800 ms budget has been
unbuilt since ratification because its design required a component and a browser
API that do not exist. It can now be built and shipped.

**Positive — and verifiable on the hardware we have.** No grandmaster, no
PTP-aware switch, no fab network. The alignment can be demonstrated on a
workstation, which is where every other claim in this repository is demonstrated.

**Positive — it unblocks a second leg.** The decode leg stands at *"in part"* in
§IV because *"a browser cannot see the sending end without a clock shared with
the SFU"*. That clock is now identified. See the caveat below.

**Negative — alignment is bought with latency, out of the budget it belongs to.**
Measured: aligning two tiles roughly doubled absolute lag, ~30 ms → ~59 ms. The
≤ 200 ms leg budget is therefore a **hard cap in the controller**, not guidance,
and a tile that cannot be held inside it is released and marked rather than held.
Without that cap this leg fixes a wall and breaks the SLO.

**Negative — the reference is the SFU, not the world.** Alignment is against
send time, so it is only as good as the SFU's own consistency, and it says
nothing about when a camera captured a frame. Stated plainly here so no later
reader mistakes it for frame-accurate capture-time sync.

**Negative — a multi-SFU wall reopens this.** ADR-012's shard-by-camera scaling
could serve one wall's tiles from two SFUs, and two SFU clocks are not one
reference. Out of scope today; this ADR is the record that it is a question.

**Negative — one leg of the budget remains genuinely unbuilt.** Inter-display
sync. §IV's leg table must continue to say so.

**Neutral — ADR-0021 is left unsettled and is not settled here.** It says *"the
overlay engine renders at the frame whose presentation timestamp matches
`used_ts`"*, and there are no presentation timestamps. That claim is affected by
this decision and needs its own amendment when a feature attempts it. Recording
it rather than quietly leaving it contradicted is the point.

## Alternatives Considered

**Implement ADR-014 as written — REJECTED.** It requires a component in the media
path that does not exist and a browser API that no browser exposes. This is not a
cost judgement; it is not possible.

**Fork MediaMTX, or write an SFU, to emit presentation timestamps — REJECTED.**
Out of all proportion to one leg, and it takes on permanent ownership of media
plumbing that a maintained third-party project does better. ADR-011's passthrough
model exists precisely so we do not own the media path.

**Wait for PTP hardware and build the whole of ADR-014 — REJECTED.** The hardware
is a deployment prerequisite for a fab network, not for a wall. Intra-wall
alignment needs no PTP, so waiting would leave a quarter of the budget unbuilt in
order to gain nothing for the case that is actually in front of us.

**Keep the leg named "(PTP)" and note the discrepancy elsewhere — REJECTED.**
That is exactly the shape of the defect spec 040 found: a record that is wrong,
with the correction living somewhere the reader of the record will not look.

**Set one fixed buffer depth for every tile — REJECTED.** Simple, and it ignores
the per-tile lag it is supposed to equalise: it either over-delays healthy tiles
or fails to catch lagging ones. The measured spread is what has to be closed.

**Adopt `< 5 ms` as the intra-wall bound too, for consistency — REJECTED.** One
number is easier to remember and would be wrong. It is sub-frame, so it is
unobservable in a browser and unverifiable on any hardware we have.

## Implementation Notes

Constitution changes landing **with this ADR**:

- **§III** — Stream Distribution's *"PTP-synced presentation timestamps"* becomes
  the playout-alignment description.
- **§IV** — both tables: the leg's parenthetical is renamed.
- **§Frontend** — the *"PTP-aware time APIs"* requirement is replaced.
- Version bump and amendment-history entry, per governance.

Constitution changes that must **wait for the code** (spec 045 T020/T021):

- **§IV's leg-state table** — this leg's *Implemented / Measured / Dashboard*
  columns stay as they are until the mechanism actually ships. **Renaming a leg
  is not building it**, and flipping the column now would recreate, in the same
  paragraph that warns about it, the exact error §IV's warning sentence
  describes.
- **The decode leg's "in part"** must be revisited once this leg ships. Expect it
  to be **restated rather than raised**: RTCP gives a sender clock and RTT gives
  a round trip, but Chromium exposes no per-frame send-to-arrival mapping, so
  SFU-send → decoded remains an estimate (`RTT/2 + buffer + decode`). Rounding an
  estimate up to *measured whole* is what §IV's own wording forbids.
