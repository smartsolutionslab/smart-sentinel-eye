# ADR-0138: What a wall was seen to do, and what was not timed

**Status:** Accepted
**Date:** 2026-09-01
**Supersedes:** (none)
**Superseded by:** (none)

## Context

The system exists to draw a label about the world over a live picture of that
world, fast enough for the label still to be true. Every part of that was built.
**No part of it had ever been checked together.**

Two absences, different in kind:

1. **Nothing had seen it.** Every automated check involving an overlay ran
   against a wall whose camera pointed at `rtsp://10.0.5.71/stream` — an address
   nothing serves. Those tiles render `WHEP returned 404` and create no
   receiver. So a tile that drew its label **only when the video failed** passed
   the entire suite, in both directions, silently.
2. **Nothing had timed it.** The six legs of the budget are instrumented
   individually. The span the budget is actually about — an event arriving,
   an operator seeing it — had no instrument at all.

## Decision

### A wall is evidence only when both halves are seen together

An automated check now brings up a tile with a camera whose video actually
arrives and an overlay bound to a variable, and asserts **both**:

- video **decoding on an ongoing basis** — two samples 1000 ms apart, the second
  at least 10 frames ahead;
- the overlay's **resolved text**, on the same tile.

**The delta is the assertion; the count is not.** A source that emits one frame
and stops satisfies "frames have been decoded" while showing something an
operator cannot distinguish from a frozen wall — and neither can a screenshot,
which is why no screenshot-based check could have closed this.

Evidence from the first passing run: `22 → 42 frames in 1000 ms (+20,
threshold 10)`. The clip runs at 25 fps.

### Both failure directions were demonstrated, not argued

- Pointing the camera back at `rtsp://10.0.5.71/stream` — **the address every
  existing overlay fixture uses** — fails with *"no video frame ever decoded on
  this tile"*. That is the direct evidence that those fixtures run with no video.
- Removing the overlay's token fails the other way, **while video keeps
  decoding**, so the two halves are independently load-bearing.

### The video source is a separate, single-path server

A container serving one looping clip over RTSP, which the SFU pulls exactly as
it pulls a camera. Not the Scenario Simulator's `camera-sim`: **nothing waits
for that worker to seed**, so a fixture leaning on it would be racing, and a
fixture must own and remove its own data.

### The span is reported as unmeasured, and that is the outcome

The measurement ran and **refused**: the value never reached the tile within its
window, so it reports what it could not establish and **no figure**.

**This is the required outcome, not a failure** — the specification made an
honestly-reported gap a passing result precisely so that the alternative could
be ruled out. The alternative was available and cheap: six per-leg figures exist
and adding them yields an 800 ms-shaped number in a minute. ADR-0135 established
that medians do not add. Such a number would have closed the question while
leaving the risk exactly where it is, and would be indistinguishable from a
measurement to anyone reading it later.

There is no field, and no code path, that could produce one. Confirmed by search.

### What the span would have covered, had it been measured

| Leg | In the span |
|---|---|
| Camera → SFU | no — the picture's path |
| SFU → kiosk decode | no — the picture's path |
| Presentation buffer | no — the picture's path |
| **Event → overlay state** | **yes** |
| **Overlay composite + render** | **yes** |

**Three of six legs are absent.** A figure from this span, had one been
obtained, would not have established that the 800 ms budget holds.

## Consequences

**What this establishes.** A label over live video is now checked, on one tile,
by something that fails when either half is missing — and both failures have
been observed rather than asserted. The address that every prior overlay fixture
used is now known, by demonstration, to produce no video at all.

**No leg of §IV changes state, and the prediction that two would was wrong.**
The task list for this feature said decode would move from *in part* and that
event → overlay state would gain a figure. Neither happened:

- Decode is now **observed** — frames advancing in a browser — but observation
  is not a latency figure, and §IV's Measured column is about figures.
- Event → overlay state gained **no** figure, because the span was refused.

Recording that plainly matters more than the update would have. §IV recorded
three legs as unbuilt for months after they were built; a table that gains a
"measured" because a task list predicted one is the same defect, arriving by a
tidier route.

**Two findings were raised rather than absorbed:**

- **A variable change does not reach an online kiosk tile**, and no test has
  ever covered that path — the only existing check makes its change while the
  kiosk is *offline* and asserts after reconnect. Ruled out by running: the
  label hold (fails identically with no video), the locator, and propagation.
  Not filed as a proven defect, because the working path was not reproduced
  side-by-side.
- **The Scenario Simulator runs in CI**, contrary to ADR-0111's *"All dev-only,
  so prod/CI are untouched"*: `E2ETests` is never set to `true` anywhere, so the
  guard `isRunMode && !isE2ETests` is true in CI. Three committed comments
  reason from that guard to a conclusion it does not support.

**What this does not establish:**

- **That the 800 ms budget holds.** No end-to-end figure exists, and the span
  that was attempted omits three legs.
- **That anyone has watched a wall align.** Nothing automated can, and this
  feature does not close it.
- **That a fab kiosk behaves like this.** These runs are a developer machine and
  a CI runner.
- **That the label path works.** It demonstrably does not, for an online change,
  and that is the open finding above.

## Alternatives Considered

**Summing the per-leg figures.** Available, cheap, and forbidden. See above.

**Reusing the Scenario Simulator's video.** Rejected on the race, not on the
video: nothing waits for its worker, and the fixture would not own its data.

**Asserting `currentTime` advances.** Rejected: it can advance over a stalled
track, reporting a frozen picture as healthy — the exact failure being closed.

**A second reader of the WebRTC `inbound-rtp` statistics.** Rejected: the
application owns that reading. The check asks the `<video>` element what it drew,
via `getVideoPlaybackQuality()`, which needs no production change.

**Deleting the failing label check.** Rejected. It is marked `fixme` with its
evidence, not removed — a check quietly deleted is one nobody returns to. Not
marked `fail`, because that would assert the failure is understood.

## Implementation Notes

No production code changed. The only C# is AppHost composition: one container,
gated to run mode rather than to `E2ETests`, because that flag is never set and
a resource gated on it would be dead code.
