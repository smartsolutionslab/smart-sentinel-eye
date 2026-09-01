# Implementation Plan: An overlay label over live video, seen and timed

**Branch**: `056-label-over-live-video` | **Date**: 2026-09-01 | **Spec**: [spec.md](./spec.md)

## Summary

Give the end-to-end suite a wall whose video **actually arrives**, assert both
halves of a tile together, and time the label's journey as **one span on one
clock**. Then write down what that span covers — which is a proper subset of
the 800 ms budget — and what still requires a person in front of a wall.

Three deliverables, in dependency order:

1. **A video source CI can use.** A small container serving one looping clip
   over RTSP, which the main SFU pulls exactly as it pulls a real camera.
2. **A check that cannot pass on half a tile.** Ongoing decode and resolved
   label text, asserted together, with both failure directions demonstrated.
3. **A measured span, or an honest refusal.** Submission to label-visible,
   both ends stamped on one machine's clock, repeated, with the conditions
   and the covered legs stated beside the figure.

---

## Technical Context

**Language/Version**: TypeScript (Playwright end-to-end), C# / .NET 10 (AppHost
composition only)
**Primary Dependencies**: Playwright, MediaMTX (`bluenviron/mediamtx:latest-ffmpeg`),
.NET Aspire AppHost
**Storage**: none new
**Testing**: Playwright end-to-end against the Aspire stack
**Target Platform**: CI runner (Linux container host) and a developer machine
**Project Type**: Web application (existing) — this feature adds test
infrastructure and one AppHost resource
**Performance Goals**: added end-to-end run time **≤ 3 minutes** against a
10m35s baseline (FR-016); this is a ceiling to respect, not a target to
optimise
**Constraints**: no change to the 800 ms budget or any sub-budget; nothing is
made faster; the simulator is not extended; per-leg figures are not summed
**Scale/Scope**: one camera, one tile, one bound overlay, one clip

### Resolved by probe, not assumption

| Question | Answer | How it is known |
|---|---|---|
| Can CI's browser decode H.264? | **Yes** — `video/H264` in `RTCRtpReceiver.getCapabilities`, `canPlayType` → `"probably"` | executed against Playwright's bundled Chromium |
| How does a stream reach a tile? | main SFU **pulls** the camera's RTSP URL via `POST /v3/config/paths/add/{name}` | `MediaMtxRtspGateway.AddPathAsync` |
| Are the clips available in CI? | **Yes**, tracked in git (~46 MB) | `git ls-files src/AppHost/Resources/clips` |
| Can both ends of the span share a clock? | **Yes** — same machine, same OS clock | see Decision 4 |

**No NEEDS CLARIFICATION remain.** The one open question the specification
left to this phase — which container serves the clip — is decided below on an
argument from a locked decision rather than on preference.

---

## Constitution Check

**We checked, and there is no conflict.** Recording that plainly, because a
check that found nothing is worth the same as one that found something only if
it is written down.

| Decision | Bearing on this feature | Verdict |
|---|---|---|
| **011** (initial decisions) — passthrough when the camera profile is WebRTC-compatible | The clip is H.264 in MP4, which is WebRTC-compatible, so the SFU passes it through and no transcode path is exercised | **consistent** |
| **ADR-0111** — scenario simulator, dev-only | Explicitly accepts its cost on the grounds that *"All dev-only, so prod/CI are untouched"* | **decides Decision 1** — see below |
| **ADR-0117** — dashboards bind implemented legs | This feature adds no dashboard and claims no discharge of §VII | **untouched, out of scope** |
| **ADR-0118** — one telemetry sink per environment | The kiosk reports rather than exports; unchanged here | **untouched** |
| **ADR-0123** — the render leg is the operator's wait, instrument is correct as built | This feature reads that instrument and does not "fix" it | **consistent** |
| **ADR-0128** — playout alignment without PTP | Alignment engages once video arrives; this feature observes it, changes nothing | **consistent** |
| **ADR-0129** — labels are aged, not frame-matched | The hold is *inside* the measured span and is correct behaviour, not overhead | **consistent — and load-bearing**, see Decision 5 |
| **ADR-0135** — medians do not add | Forbids the shortcut this feature is most tempted by | **consistent — binding** |

**ADR-0111 is the one that decides something.** Its recorded cost/benefit rests
on the simulator being absent from CI. Un-gating `camera-sim` for end-to-end
runs would spend a cost that ADR was told it would not have to pay, and would
need an amendment. Not doing that is therefore the *conservative* option, not
merely the smaller one.

**Latency budget (§IV).** This feature does not touch the event-to-overlay
path; it observes it. No leg's budget changes. §IV's leg table **is** updated
(FR-013) because this feature changes what is known about two legs, and §IV is
where that claim lives.

---

## Decisions

### Decision 1 — the video source is an end-to-end-only container with one static path

**Decided**: a container from the same `bluenviron/mediamtx:latest-ffmpeg`
image, carrying its own small config with **one static path** that loops one
clip via FFmpeg. It runs **in end-to-end mode only**, never in run mode and
never in production.

**Why not depend on `camera-sim`. The original reason was wrong; the
replacement is stronger.** There is nothing to un-gate — **`camera-sim` already
runs in CI**, because `E2ETests` is never set anywhere and CI boots in run
mode, so `isRunMode && !isE2ETests` is true (research §3). Depending on it is
still rejected, because **nothing waits for the simulator to seed** — that is a
race, and a fixture that races is the implicit coupling
`seed-published-layout.setup.ts` argues against by name — and because a fixture
must own and remove its own data.

This also means **ADR-0111's "All dev-only, so prod/CI are untouched" does not
describe what happens.** Raised, not absorbed.

**Why not a static path on the main SFU.** `mediamtx.yml` keeps `paths: {}`
deliberately: static entries collide with the ones `StreamDistribution` adds
through the control API. A source must be a *different* endpoint so the main
SFU can pull it the way it pulls anything else — which is also what makes this
fixture exercise the real path rather than a shortcut around it.

**Run mode is untouched.** The simulator keeps run-mode video. The two never
run together: this container is gated to end-to-end, that one to run mode.

**How the seed learns the address.** From configuration supplied by the
AppHost, not by hard-coding a host and port — the address is a container-network
name and must not become a second thing to keep true.

### Decision 2 — ongoing decode is two samples, and the numbers are stated here

**Decided**: sample `framesDecoded` **twice, 1000 ms apart**, and require the
second to exceed the first by **at least 10 frames**. Reuse `decodeSampleFrom`
and `decodeElapsedBetween`; write no second reader of the same statistics.

**Why a delta rather than a count.** A source that emits one frame and stops
satisfies "frames have been decoded" while showing something an operator cannot
distinguish from a frozen wall. Only a delta shows the picture is *moving*.

**Why those numbers.** At any sane frame rate a second yields far more than 10
frames, so the threshold rejects a stalled stream without being sensitive to a
slow runner. The cost is one second per assertion, which is inside the budget.

**Bounded.** Waiting for the *first* frame has a stated timeout (Decision 6);
the sampling itself is a fixed 1000 ms, not a poll-until-it-looks-right.

### Decision 3 — three tests, and each failure names its own half

**Decided**: three end-to-end tests, not one.

| Test | Establishes | Fails when |
|---|---|---|
| **Both halves together** | the product's central behaviour | either half is absent |
| **The label follows its variable** | the binding is live, not coincidence | the value changes and the label does not, or video stops while it does |
| **The refusals** | the check is capable of failing | a half is removed and the check still passes |

**Why not one test.** A single test that asserts everything reports one failure
for four different causes. The failure message is the deliverable when a check
fails at 3 a.m., and *"the wall was not ready"* is one nobody can act on. Each
assertion states which half it was looking at and what it saw instead — frames
decoded, or the text found.

**The third row is the one that is usually skipped.** FR-004 requires both
failure directions be *demonstrated*, which means removing a half and observing
the failure, not asserting that it would fail.

### Decision 4 — the start is stamped by the test process, and the browser's clock is not used

**Decided**: the **test process** stamps the submission, and the **test
process** stamps the observation of the changed label. The browser's own clock
is never mixed with the test's.

**Why this is safe, and which shape it is.** Spec 053 examined exactly two
shapes: two processes reading **one OS clock** (safe — this is how the front of
its span was established), and a stamp taken in a host process subtracted from
one taken in a container (**not established**, and still open). This
measurement is the **first** shape: the test process and the browser it drives
run on one machine, and only the test process's clock is read. Nothing is
subtracted across a boundary.

**How a figure is refused (FR-009).** If the run cannot show both ends were
stamped by the same process on one machine — a remote browser, a container
runner, a distributed grid — it reports the span as **unmeasured**, names what
it could not establish, and reports no number. The refusal is the required
outcome, not a fallback.

### Decision 5 — the hold is inside the span, and that is the point

`useLabelDelay` ages a label to match its picture, and **fails open**: a null
frame age shows the label immediately. Every end-to-end run to date has a null
frame age, because no video arrives.

So this fixture exercises a path CI has never run, and the measured span
**legitimately includes the hold**. A figure taken against the existing
video-less wall would be smaller and would describe a different system. The
record must say which of the two it is.

### Decision 6 — repetition, spread, and what is reported

**Decided**: **five** timed iterations within one run. Report **every**
figure, plus the median and the range. Never a single number alone.

**Why five and why the raw figures.** The recorded trap is that the two sides
of a comparison are not equally noisy — where the machine is the bottleneck,
figures scatter. Reporting the spread is what lets a reader see which regime
they are in, and a median without its range hides exactly that.

**Timeouts, stated rather than discovered**: first frame **30 s**; label
change observed **5 s** per iteration. A run that exceeds either reports what
it was waiting for.

---

## Definition of done, per story, before any code

| Story | Done when | What this does **not** prove |
|---|---|---|
| **US1** | A test fails when video is removed, and a different test fails when the label is removed — both observed by running them, not argued | That a person has looked at a wall. Nothing automated can. |
| **US2** | Five figures, their median and range, the conditions, and the legs the span covers — or a refusal naming what could not be established | That the **800 ms budget** is met. The span is a proper subset (see below). |
| **US3** | §IV's leg table matches what is now true, and a reader can tell observed from measured without reading this plan | That inter-display sync, representative hardware, or dashboards are discharged. |

### What the span covers, and what it does not

The measured span runs from a variable's value being submitted to the resulting
text being visible. That covers **event → overlay state** and **overlay
composite + render**, plus the deliberate hold.

It does **not** cover **camera → SFU**, **SFU → kiosk decode**, or the
**presentation buffer** — those are legs of the *picture's* path, not the
label's. **Any comparison to 800 ms must say so.** A figure comfortably under
800 ms would not establish that the budget is met, because three legs are not
in it.

### What no automated check here can establish

- **That anyone has watched a wall align.** US3 records this as outstanding
  rather than closing it.
- **That a CI runner's figure describes a fab kiosk.** Different hardware,
  different load, different display. The conditions are reported for exactly
  this reason.
- **That the whole 800 ms budget holds.** See above.

---

## Project Structure

### Documentation (this feature)

```
specs/056-label-over-live-video/
├── spec.md
├── plan.md              # this file
├── research.md
├── data-model.md
├── contracts/
│   └── the-fixture.md
├── quickstart.md
└── checklists/requirements.md
```

### Source Code (repository root)

```
src/AppHost/
  AppHost.cs                     # one end-to-end-gated container
  Resources/<new>.yml            # one static looping path

e2e/
  support/
    seed-live-video-wall.setup.ts    # camera at the source, overlay bound
    live-video-wall.ts               # shared names, as bound-overlay-wall.ts is
    retire-*.teardown.ts             # extended; must survive a partial run
  wall-shows-a-label-over-video.spec.ts
  wall-label-follows-its-variable.spec.ts
  wall-label-over-video-refusals.spec.ts
  wall-label-span.spec.ts            # the measurement

apps/shared/src/observability/
  kioskLatency.ts                # read only — decodeSampleFrom, decodeElapsedBetween

docs/adr/
  0138-<slug>.md                 # what was seen, what was timed, what it covers

.specify/memory/constitution.md  # §IV leg table (FR-013)
```

---

## Complexity Tracking

| Addition | Why it is not avoidable | Cheaper option rejected because |
|---|---|---|
| One more container in the end-to-end stack | The SFU pulls a source; without one, no video exists to decode | Reusing `camera-sim` spends a cost ADR-0111 was told it would not pay |
| ~20 lines of duplicated server config | The main SFU's `paths` block must stay empty for the control API | A static path there collides with the API-managed entries |
| Three tests where one would compile | One test reports one failure for four causes | A single test's message cannot say which half broke |
| Five timed iterations | A single figure is not a measurement | One run hides the regime the machine is in |

**Not added**: no new package, no new instrument, no second stats reader, no
change to any budget, no dashboard.
