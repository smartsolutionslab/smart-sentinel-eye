# Research — 056 label over live video

Phase 0. Every entry below was settled by executing something or reading the
code, not by reasoning about what is probably true.

---

## 1. Can CI's browser decode H.264? — **Yes**, and this was the feature-killing risk

**Decision**: rely on Playwright's bundled Chromium. Do not set a `channel`.

**Why it was in doubt.** `playwright.config.ts` uses `devices['Desktop Chrome']`
with no `channel`, which resolves to Playwright's **bundled Chromium** rather
than Google Chrome. That build has historically shipped without proprietary
codecs, and H.264 is what the clips carry. **No end-to-end test has ever
decoded a frame** — the overlay seed points at an address the SFU has no path
for — so this capability had never been exercised, and nothing in the suite
would have revealed its absence.

**How it was settled.** Executed against the bundled browser:

```
RTCRtpReceiver.getCapabilities('video').codecs
  → video/VP8, video/rtx, video/VP9, video/H264, video/AV1, video/red, …
canPlayType('video/mp4; codecs="avc1.42E01E"')  → "probably"
```

**Consequence.** The plan may rely on it, and must not silently re-acquire the
risk: switching to `channel: 'chrome'`, or to Firefox/WebKit, changes the codec
set. Whoever changes the browser owns re-checking this.

**Alternatives considered.** Transcoding the clip to VP8 to sidestep the
question — rejected: it would make the fixture exercise a codec path no real
camera uses, and decision 011 makes H.264 passthrough the normal case.

---

## 2. How does a stream reach a tile? — the SFU **pulls** the camera's URL

**Decision**: register the fixture's camera at an RTSP address that a
container on the AppHost network serves.

**Mechanism**, from `MediaMtxRtspGateway.AddPathAsync`:

```
POST /v3/config/paths/add/{name}   body: { "source": "rtsp://..." }
```

The main SFU then pulls that source. So the fixture does not inject a stream —
it stands up something for the existing code path to pull, which is why the
fixture exercises the real path rather than a shortcut around it.

---

## 3. Which container serves the clip? — a new end-to-end-only one

**Decision**: a container from `bluenviron/mediamtx:latest-ffmpeg` with its own
config holding **one static looping path**, gated to end-to-end only.

**Alternative A — un-gate `camera-sim`.** Rejected on a locked decision rather
than on taste. ADR-0111 accepts the simulator's cost explicitly because *"All
dev-only, so prod/CI are untouched."* Un-gating spends a cost that ADR was told
it would not have to pay, and would need an amendment. It also drags in the
provisioning worker — Keycloak client credentials, RabbitMQ, a seeded catalog —
to serve one tile, and puts two components in the business of provisioning
paths for the same cameras.

**Alternative B — a static path on the main SFU.** Rejected on the config's own
recorded reason: `mediamtx.yml` keeps `paths: {}` because static entries
collide with the ones `StreamDistribution` adds through the control API. The
source must be a separate endpoint for the pull to be a real pull.

**Cost accepted**: roughly twenty lines of duplicated server configuration.
Cheaper than the machinery either alternative brings.

---

## 4. Are the media clips available in CI? — **Yes**, they are tracked

`git ls-files src/AppHost/Resources/clips` lists them; about 46 MB, in the
repository. CI has them on checkout with no download step, no cache, and no
credential. `sim-loop.mp4` is already the default for a camera with no scenario
asset.

---

## 5. Can both ends of the span share a clock? — **Yes**, and this is the enabling fact

**Decision**: the test process stamps both ends. The browser's clock is not
mixed in.

**Why this is settled rather than assumed.** Spec 053 examined two shapes and
reached different verdicts:

| Shape | Verdict there | This measurement |
|---|---|---|
| Two processes reading **one OS clock** | **safe** — how the front of its span was established | **this one** |
| A host stamp minus a container stamp | **not established**, still open | not this |

The test process and the browser it drives run on one machine. Only the test
process's clock is read, so nothing is subtracted across a boundary.

**The refusal (FR-009).** If a run cannot establish that shape — a remote
browser, a grid, a containerised runner — it reports the span **unmeasured**,
names what it could not establish, and reports no figure. A refusal is the
required outcome there, not a degraded one.

---

## 6. What does the span actually cover? — a **proper subset** of the budget

| Leg | In the measured span? |
|---|---|
| Camera → SFU | no — the picture's path |
| SFU → kiosk decode | no — the picture's path |
| Presentation buffer | no — the picture's path |
| **Event → overlay state** | **yes** |
| **Overlay composite + render** | **yes** |
| Headroom | n/a — arithmetic remainder |

**Consequence, and it is the one most likely to be lost.** A figure well under
800 ms does **not** establish that the budget is met, because three legs are
not in it. FR-012 requires every comparison to say which legs are covered.

**And the sum is still forbidden.** ADR-0135 established that medians do not
add; `IngestAttribution.PerRowResidualMs` exists because of it. Completing this
span by adding the three missing legs' medians would produce exactly the
fabrication that ADR rules out.

---

## 7. What is already exercised, and what is not

**`useLabelDelay` has never run its hold in CI.** It ages a label to match its
picture (ADR-0129) and **fails open** — a null frame age shows the label
immediately, no timer, no state write. Every end-to-end run to date has a null
frame age because no video arrives.

Two consequences:

1. This fixture runs a code path CI has never run. That is a benefit, and also
   a risk: a defect in the hold would surface here first.
2. The measured span **includes the hold**, correctly. A figure from the
   existing video-less wall would be smaller and would describe a different
   system.

---

## 8. How is "ongoing decode" evidenced? — a delta, with stated numbers

**Decision**: two samples of `framesDecoded`, **1000 ms apart**, requiring a
delta of **at least 10 frames**. Reuse `decodeSampleFrom` and
`decodeElapsedBetween` from `kioskLatency.ts`.

**Why a delta.** A source that emits one frame and stops satisfies "frames have
been decoded" while showing something an operator cannot tell from a frozen
wall. Only a delta shows the picture moving.

**Why these numbers.** Any sane frame rate yields far more than 10 frames in a
second, so the threshold rejects a stall without being sensitive to a slow
runner. Fixed duration, not a poll — the cost is known in advance.

**Alternative considered.** Reading `<video>.currentTime` advancing. Rejected:
it reports the element's playback position, which can advance over a stalled
track, and it says nothing about frames actually decoding.

---

## 9. What does this cost CI? — a stated ceiling, not an emergent property

**Baseline**: the end-to-end job runs about 10m35s and is the longest of the
four checks.

**Budget**: **≤ 3 minutes** added (FR-016), measured and reported rather than
assumed.

**Why a ceiling matters more than a target.** A fixture that doubles the job is
one that gets disabled, and a disabled check is indistinguishable from an
absent one — which is the state this feature exists to leave.

---

## 10. Locked-decision check — **no conflict**

Checked ADR-0111, 0117, 0118, 0123, 0128, 0129, 0135 and initial decision 011.
None contradicts this feature.

**ADR-0111 decides something rather than merely permitting it**: its recorded
cost/benefit rests on the simulator being absent from CI, which is what rules
out Alternative A in §3. Not extending it is the conservative reading, not just
the smaller one.

**ADR-0129 is load-bearing** rather than merely compatible: it is why the hold
belongs inside the span (§7).

**ADR-0135 is binding**: it forbids the shortcut this feature is most tempted
by (§6).

Recording "we checked and found nothing" because a check whose result is not
written down is one the next person has to repeat.
