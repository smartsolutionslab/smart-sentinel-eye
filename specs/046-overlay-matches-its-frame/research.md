# Phase 0 Research: The overlay and the picture it annotates

**Feature**: `046-overlay-matches-its-frame` · **Spec**: [spec.md](./spec.md)

## Outcome: the chosen scope is not achievable, and FR-017 says bring it back

The spec scoped this feature to **full frame accuracy** and named one candidate
route that would avoid ADR-0128's rejection of owning the media path. **That
route is closed.** Two further blockers sit behind it, and the third is
structural rather than an API gap.

Per FR-017 this is **returned for a decision rather than downgraded quietly**.
No plan follows this document.

---

## How it was probed

An isolated MediaMTX — `bluenviron/mediamtx:latest-ffmpeg`, the image the AppHost
runs — serving one looping H.264 path, with a Chromium page opening a real WHEP
session against it. Same method as spec 045's research, and for the same reason:
the question is what actually arrives in a browser, not what a specification
permits.

---

## R1. The capture-time extension is absent, and cannot be forced

**Neither side offers it.** Chromium's generated offer does not advertise
`abs-capture-time`, and MediaMTX's answer does not either. The negotiated
extensions were:

```
a=extmap:10 urn:ietf:params:rtp-hdrext:sdes:rtp-stream-id
a=extmap:4  http://www.ietf.org/id/draft-holmer-rmcat-transport-wide-cc-extensions-01
a=extmap:11 urn:ietf:params:rtp-hdrext:sdes:repaired-rtp-stream-id
a=extmap:9  urn:ietf:params:rtp-hdrext:sdes:mid
```

**Forcing it does not help.** The offer SDP was rewritten to advertise
`http://www.webrtc.org/experiments/rtp-hdrext/abs-capture-time` explicitly.
MediaMTX **did not echo it in the answer**, and per-frame metadata still carried
no `captureTime`.

**Consequence**: a frame's capture instant does not reach the browser, and no
change on our side of the wire makes it. Obtaining it means an SFU that supports
the extension — patching MediaMTX, or replacing it — which is **exactly the
media-path ownership ADR-0128 rejected and FR-017 forbids**.

## R2. Per-frame metadata exists, and identifies a frame — but not an instant

`requestVideoFrameCallback` is supported and populates:

```
expectedDisplayTime, height, mediaTime, presentationTime,
presentedFrames, processingDuration, receiveTime, rtpTimestamp, width
```

**`rtpTimestamp` is present**, which is genuinely useful: it names *which* frame
this is on the sender's media timeline. What is absent is any wall-clock instant
for it. `presentationTime`, `receiveTime` and `expectedDisplayTime` are all
**local browser timestamps** — when this machine received or will display the
frame — not when the scene happened.

So the metadata answers "which frame" and not "of when".

## R3. The sender-clock bridge is not reconstructible in the browser

The obvious repair for R2 is RTCP: a sender report carries an (NTP, RTP)
correspondence, which would convert `rtpTimestamp` into sender wall-clock time.
Spec 045 established that sender reports do flow here.

**But `getStats` exposes only half the pair.** `remote-outbound-rtp` carries
`remoteTimestamp` — the NTP instant of the last report — with **no accompanying
RTP timestamp**. The full field set observed:

```
id, timestamp, type, codecId, kind, mediaType, ssrc, transportId,
bytesSent, packetsSent, localId, remoteTimestamp, reportsSent,
roundTripTimeMeasurements, totalRoundTripTime
```

`inbound-rtp` offers only `timestamp` and `lastPacketReceivedTimestamp`, both
local, and **`estimatedPlayoutTimestamp` is absent** — confirming spec 045 R1
against a second, independent setup.

**Consequence**: the browser holds RTP timestamps and NTP timestamps that it
cannot relate to each other.

## R4. The structural blocker, which survives fixing R1–R3

**Suppose R1–R3 were all solved.** The result would be the instant at which the
*camera* captured a frame, on the *camera's* clock.

An overlay value's instant (`used_ts`) comes from event ingestion, on a
*server's* clock. Matching one to the other requires those two clocks to be
related — and they are not. ADR-0128 already stated this while scoping spec 045:

> Cameras are independent devices with unsynchronised capture clocks; nothing
> downstream can recover a moment that was never recorded.

Relating them is precisely PTP across the fab OT network — ADR-014's stated
deployment prerequisite, the grandmaster and PTP-aware switches that **do not
exist here** and whose absence is why spec 045 scoped inter-display sync out.

**So frame accuracy is not blocked by a missing browser API. It is blocked by
the absence of a shared clock between the thing that saw the event and the thing
that filmed it.** No amount of browser work reaches it.

---

## What this means for the three options

| Option | Status after the probe |
|---|---|
| **(c) full frame accuracy** — chosen | **Not achievable.** R1 closes the route that avoided ADR-0128; R4 blocks it even with unlimited work inside the browser. Needs PTP hardware *and* an SFU we own. |
| **(b) age-match** | Still achievable. R2's `rtpTimestamp` is not needed — spec 045 already measures each tile's frame age directly. Buys consistency, not accuracy, and the spec forbids calling it accuracy. |
| **(a) amend the record** | Still achievable, and unchanged by any of this. |

**A fourth possibility the probe surfaces**: option (b) plus an honest name.
Age-matching would make the label as old as the picture *without* claiming to
match an event to a frame, and §IV's wording would say so. That is (b) with
FR-002 read as it was originally written, before the scope changed it.

---

## Decision required

FR-017 is triggered. The spec's instruction is explicit, and it is the reason
this document stops here rather than continuing into a plan:

> If frame instants cannot be obtained without that, the scope is not achievable
> as specified and MUST be brought back for a decision rather than quietly
> downgraded to age-matching. **Age-matching wearing the name "frame accuracy"
> is the outcome to prevent.**

**No plan has been written.** Writing one would mean choosing a fallback on the
user's behalf, which is the specific thing FR-017 exists to stop.

---

## What the probe cost, and what it saved

Two container runs and two browser sessions, against a real SFU. It established
in minutes that a feature scoped to frame accuracy cannot be built here, before
any task list existed to build it from.

Spec 045 paid for this lesson: the statistic its design depended on
(`estimatedPlayoutTimestamp`) did not exist, and only a probe found out. **The
same statistic is confirmed absent here**, independently — which is a small piece
of evidence that the discipline generalises rather than having been luck.
