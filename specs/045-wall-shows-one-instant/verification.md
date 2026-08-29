# Verification: A wall shows one instant

**Feature**: `045-wall-shows-one-instant` · **Date**: 2026-08-29 · **Issue**: 1714

**Status: T026 is partly done. The measurement half was walked; the part that
needs a person's eyes was not.** What follows separates the two, because FR-015
requires it and because a verification note that blurs them is worse than none.

---

## 1. How it was walked

`dotnet run --project src/AppHost` in **run mode without `E2ETests`**, so
`camera-sim`, the scenario simulator and ICE host-publishing were all live and
the tiles carried **real video** — the condition CI never has.

A temporary Playwright harness drove a real kiosk wall: it patched
`RTCPeerConnection` to collect the sessions the app opened, read each tile's
statistics, induced skew by writing `jitterBufferTarget` directly on one tile's
receiver, and captured every `kiosk-latency` POST the app made. The harness was
deleted afterwards; it was an instrument, not a test.

**The wall had two tiles.** That is the smallest wall this feature applies to and
therefore the weakest version of the demonstration. See §5.

**Every run induced skew before asserting anything closed** — research R6, and
the reason is in §4.

---

## 2. What was found — two defects, both real

### 2a. The cap was applied to the wrong quantity

**The first run against a real wall aligned nothing at all.**

| | reading |
|---|---|
| per-tile lag | **256.9 / 257.5 ms** |
| per-tile buffer | ~131 ms |
| tiles badged out of alignment | **2 of 2**, before any skew was induced |

`wallTargetFrom` compared **buffer + processing** against the **200 ms
presentation-buffer budget**. Processing delay belongs to the *decode* leg. So
every tile was judged over budget while each was comfortably inside the budget it
was actually being judged against, and the controller released the entire wall.

This is the same leg-conflation the code takes pains to avoid when *reporting* —
`bufferDelayBetween` exists precisely to keep the two apart — reproduced in the
control law, where nothing was checking.

**Fixed**: the target equalises lag; the budget bounds buffer. A tile is held
when `target − its own processing ≤ 200 ms`.

### 2b. The setpoint was a total lag, and the loop ran away

With 2a fixed the wall aligned — and then climbed.

| | reading |
|---|---|
| induced at 120 ms, ~40 s later | **653.6 / 655.4 ms**, spread 1.8 ms |
| reported presentation buffer | **322–476 ms** |

Beautifully aligned with each other and half a second behind the world.
`targetFor` handed each tile the *target* as its `jitterBufferTarget`. But that
setpoint is a **buffer depth**: setting buffer to `T` makes lag `T + processing`,
so the next cycle's target is `T + processing`, and the wall climbs by one
processing time every two seconds for as long as it runs.

**Fixed**: a tile is asked for `target − its own processing`, which makes the
target a fixed point rather than a ramp.

**Neither defect is visible without live video.** Both unit suites were green
throughout, and stayed green — they used fixtures where buffer and processing
were not distinguished, because the code that produced them did not distinguish
either.

---

## 3. What the walk demonstrated after the fixes

Two runs, because the first measurement after machine churn looks exactly like a
regression.

| Step | run 1 | run 2 |
|---|---|---|
| Baseline spread | 7.1 ms | 11.2 ms |
| **Induced 120 ms → spread** | **1.1 ms** | **1.9 ms** |
| Tile lags after the 120 ms induction | 31.5 / 30.4 | 27.7 / 25.8 |
| Induced 500 ms | pulled back; 0 badges | **released, 1 badge**, neighbour held at 25.4 ms |
| Presentation buffer reported | 10–26 ms | 10–13 ms (held tile) |
| `wall_skew` reports | 39 | 25 |

**Claims this supports**

- **FR-001/FR-003** — an induced 120 ms outlier is absorbed and the spread closes
  to ~1–2 ms, well inside the 33 ms bound, with tile lags staying ~25–30 ms
  rather than climbing.
- **FR-012a/FR-012b** — run 2 shows the intended refusal: a 500 ms tile was
  **released and badged** while its neighbour stayed at 25.4 ms. The wall
  declined to follow it past the budget and said so, and the released tile kept
  playing.
- **FR-005** — the presentation buffer settled at **10–26 ms** against its
  200 ms budget.
- **US2 transport** — `presentation_buffer`, `wall_skew` and `receive_to_decoded`
  were all accepted by the endpoint (202) during the walk.

**Run 1 and run 2 disagree on the 500 ms case** — run 1 pulled the tile back
rather than releasing it, run 2 released it. Both are defensible outcomes of the
same rule depending on where the tile sat when the cycle landed, and the
disagreement is recorded rather than smoothed over.

---

## 4. Why "the spread was small" proves nothing on its own

The baseline spread was **7–11 ms** before any coordination was applied — inside
the 33 ms bound the feature commits to. A check that merely asserted a small
spread would pass with the controller deleted.

Every figure in §3 is therefore a **convergence from an induced spread**, and the
unit tests that assert on skew induce it first for the same reason.

---

## 5. What was NOT verified — FR-015

**The part that needs a person, and it is the central claim.**

- **Nobody has looked at the wall.** Quickstart asks whether the tiles *look*
  like one instant, and whether a tile can be named without the layout. No
  measurement answers that, and none of the above does either. This is the same
  row spec 044 ended on, for the same reason.
- **Nobody has read the figures in the sink.** The measurements were observed
  leaving the browser and being accepted (202). They were **not** read off the
  Aspire dashboard, which is what §VII's obligation is about. §IV therefore still
  reads **"recorded, not yet observed"**, and that is accurate rather than
  cautious.
- **Inter-display skew: nothing at all.** Out of scope (ADR-0128), and the
  grandmaster and PTP-aware switches it needs do not exist here. No statement in
  this note bears on it.
- **A wall larger than two tiles.** Only a 2-tile wall was driven. Release
  behaviour on a wall where *several* tiles are infeasible is untested against
  real video.
- **Representative kiosk hardware.** Everything ran on one workstation hosting
  the whole stack plus Playwright. Issue 1941 is the standing form of this
  caveat for the render leg; it applies to every number here.
- **The 800 ms path end to end** (FR-006, SC-003) was **not** re-measured with
  alignment active. Quickstart §7 asks for it and it was not done. Alignment is
  bought with latency out of that same budget, so this is the most load-bearing
  gap in this note.
- **Quickstart §2 (the hysteresis boundary), §3 (reconnect) and §5 (breaking the
  controller on purpose) were not walked** against live video. They are covered
  by unit tests only.

---

## 6. Consequences

- **#1714 stays open.** Every leg is built, which is not the same as the path
  holding end to end — and §5 lists what is still unmeasured.
- **§IV keeps "recorded, not yet observed"** until someone reads the figure in
  the sink.
- **T026 stays unticked.** The measurement half is done and recorded here; the
  person's half is not.
