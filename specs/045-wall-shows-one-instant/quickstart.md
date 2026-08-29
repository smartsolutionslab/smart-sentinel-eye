# Quickstart: verifying that a wall shows one instant

**Feature**: `045-wall-shows-one-instant` · **Plan**: [plan.md](./plan.md)

---

## Read this first: the run that passes and proves nothing

On an idle developer box, with two tiles playing identical clips, the measured
spread was **9.410 ms before any of this feature existed** — comfortably inside
the 33 ms bound the feature commits to (research R6).

**So "the spread was under 33 ms" is not evidence.** Delete the controller and
that check still passes. Every step below therefore **induces skew first** and
asserts on *convergence from it*. A step that does not start by creating a
spread is not testing anything.

This is the single most likely way this feature ships broken.

---

## 0. Boot

```sh
dotnet run --project src/AppHost
```

Containers from a previous run-mode session survive and are reused — a stack
that is already up is fine. If you are rebuilding, **stop the AppHost first**:
a running one holds the service binaries and the failure looks like a broken
build, not a locked file.

Open the kiosk, bind it, and open a **multi-tile** wall — `rolling-mill-wall`
has four. Spec 044 gave each tile a visibly different clip, which is what makes
cross-tile comparison possible by eye at all.

---

## 1. Induce a spread, and watch it close (US1 — the core claim)

**This is the step that decides whether the feature works.**

In devtools, push one tile far out of alignment:

```js
// Pick one tile's peer connection and delay it hard.
receiver.jitterBufferTarget = 400;
```

Then watch, over the next few control cycles:

Then watch over the next few control cycles.

**Two outcomes are both correct, and which one you get depends on the tile's
own decode processing** — the cap bounds the *buffer* a tile is asked to hold
(`target − processing`), not its total lag. So:

- **The wall pulls the outlier back**, because a 400 ms setpoint is simply
  overwritten by the wall's own next setpoint. Observed on one run.
- **Or the wall releases and marks it**, because holding it would push another
  tile's buffer past 200 ms. Observed on the other run, with the neighbour
  holding steady at 25.4 ms.
- **Failure to look for**: the wall follows the outlier and *every* tile becomes
  ~400 ms late. That is FR-006's breach — alignment bought past the budget — and
  it is invisible without measuring.
- **The other failure, which is subtler**: every tile stays aligned with the
  others but the whole wall **climbs** run after run. That is the runaway T026
  found — two tiles induced at 120 ms reached ~654 ms. If the numbers rise every
  time you re-measure, the setpoint is being confused with the target.

Repeat with a value **inside** the cap (say 120 ms). The wall should absorb it:
spread closes to a couple of milliseconds and **absolute lag stays put**
(~25–35 ms observed), rather than settling at 120.

**Record the numbers, not the impression.** Spread before, spread after, and the
absolute lag of every tile in both cases. R4's isolated probe suggested aligning
roughly doubles absolute lag; on a real wall it did **not** — the worst tile came
out slightly faster. Confirm which happens here.

---

## 2. The boundary, where it will oscillate if it is going to

Drive a tile to alternate either side of **feasibility** — not of a fixed
200 ms, since the cap applies to `target − processing` rather than to the lag.
In practice: alternate a large and a modest `jitterBufferTarget` on one tile,
a few seconds apart, and watch its badge.

- **Expected**: it settles as held or released and **stays** there. Hysteresis
  is keyed on the feasibility decision and needs several consecutive cycles in
  either direction, so a tile straddling the line does not flip.
- **Failure to look for**: the badge blinks. A tile flipping between held and
  released every cycle is the edge case the spec names, and it is only visible
  by watching for several seconds.

---

## 3. A tile leaves and comes back (FR-003)

Kill one tile's stream and let it reconnect — patch the MediaMTX path rather
than using `setOffline`, which leaves WebRTC flowing and does not actually
produce an outage. Keep one tile untouched as a control.

- **Expected**: the returning tile rejoins the wall's instant rather than
  settling wherever its fresh buffer lands.
- Note that its statistics counters reset on reconnect. A controller that does
  not notice will compute a nonsense lag from a backwards delta — expect `null`,
  not a wild number.

---

## 4. A single-tile wall pays nothing (FR-004)

Open a 1×1 wall.

- **Expected**: no controller runs, `jitterBufferTarget` is never set, and
  time-to-first-frame and lag match a pre-feature run within noise (SC-006).
- **Failure to look for**: a target applied to a wall of one. There is nothing
  to align with, and any delay added is pure loss.

---

## 5. Break it on purpose (FR-013, SC-007)

With a wall running and aligned, make the controller fail — force the stats read
to throw, or the receiver to reject a target.

- **Expected**: **every tile keeps showing live video.** The wall loses its
  alignment claim, not its picture.
- This is the rule spec 040 set for the decode instrument and it holds here: an
  observer that can break the thing it observes is worse than no observer.

---

## 6. Read the numbers where a person can see them (US2, §VII)

In the Aspire dashboard → Metrics → resource `stream-distribution`, meter
`SmartSentinelEye.Latency`:

- `presentation_buffer` — per tile, against its 200 ms budget, `isWholeLeg: true`.
- **Wall skew is on its own instrument, not this meter's segment histogram**
  (data-model §4). If you find skew filed as a latency segment, that is the
  defect, not the display.

Two things about this dashboard, learned the hard way:

- **Pause is server-side and filters reset under live data.** Set filters, then
  read quickly.
- **The structured-log search reports zero hits for lines that are demonstrably
  being written.** Absence there means nothing. If a figure seems missing,
  confirm against the metric, not the log search.

---

## 7. End-to-end still fits (FR-006, SC-003)

Re-run the end-to-end latency check with alignment active and compare against a
run with it disabled. This leg spends from the 800 ms budget it belongs to, and
**a wall that is aligned but late has traded one breach for another.**

Run it **twice**. The first measurement after machine churn looks exactly like a
regression.

---

## 8. What could not be verified here (FR-015)

State this explicitly in the verification note. At minimum, expect:

- **Inter-display skew** — out of scope, and the grandmaster and PTP-aware
  switches it needs do not exist here. Nothing in this walk speaks to it.
- **Representative kiosk hardware** — everything above runs on a workstation
  hosting the whole stack. Issue 1941 is the standing version of this problem
  for the render leg; the same caveat applies to every figure here.
- **A wall larger than four tiles**, unless one was actually opened.
- **Any step not performed** — name it. A step skipped silently reads as a step
  that passed.
