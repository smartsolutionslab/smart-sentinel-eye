# Data Model: A wall shows one instant

**Feature**: `045-wall-shows-one-instant` · **Plan**: [plan.md](./plan.md)

**No persistence changes. No database, no migration, no aggregate.** Everything
here lives for the lifetime of a mounted wall or of one HTTP request. It is
written down because the *arithmetic* is where this feature is right or wrong,
and because two of these types exist to stop a number being filed under a name
that means something else.

---

## 1. `LagSample` — one reading from one tile

A raw snapshot of a tile's receiver statistics. Meaningless alone: every field is
a **monotonic counter over the session's life**, so a single sample can only ever
yield a session average.

| Field | Source (`inbound-rtp`, video) | Why |
|---|---|---|
| `jitterBufferDelaySeconds` | `jitterBufferDelay` | Cumulative time frames waited in the buffer — the term the controller moves |
| `jitterBufferEmittedCount` | `jitterBufferEmittedCount` | Denominator for the above. **Not `framesDecoded`** — different counters, and dividing by the wrong one silently skews the figure |
| `processingDelaySeconds` | `totalProcessingDelay` | Arrival → decoded, the part the controller cannot move but must account for |
| `framesDecoded` | `framesDecoded` | Denominator for processing delay |

**Invariant**: a sample is only comparable with another from the **same
session**. A reconnect resets the counters, and a counter that went backwards
means exactly that.

---

## 2. `TileLag` — the per-frame figure between two samples

The delta, and the only form the controller is allowed to read:

```
lag = Δ jitterBufferDelay / Δ jitterBufferEmittedCount
    + Δ totalProcessingDelay / Δ framesDecoded
```

**`null`, never `0`, when there is nothing to report** — no frames since the last
sample, or a counter that went backwards because the session restarted. This is
`kioskLatency.ts`'s existing rule and its reasoning holds unchanged: *a zero
would read as a perfect score for a journey nobody timed.*

**Why a delta.** A cumulative ratio reports the session average and flattens the
excursion a budget is about (research R3). A wall that fell out of alignment for
ten seconds an hour ago must not look aligned now.

**Two figures come out of one pair of samples, and they are not the same
number.** `lagBetween` adds processing because the controller must equalise the
whole of what makes a tile late; `bufferDelayBetween` returns the buffer wait
alone, because that is what the 200 ms budget bounds and what gets reported as
the leg. `TileLag` therefore carries **both** — an earlier version carried only
the lag, and that is how the cap came to be applied to the wrong quantity (§3).

---

## 3. `WallTarget` — the decision, and its two outcomes

What the controller computes for a wall, once per cycle.

| Field | Meaning |
|---|---|
| `targetMilliseconds` | The **total lag** every held tile is brought to |
| `held` | Tiles that can reach the target inside the budget |
| `released` | Tiles that cannot, and are marked instead |

> **This section was rewritten after T026.** The rule below is not the one
> planned; the planned one was wrong in two ways that only a real wall exposed,
> and both corrections are recorded here rather than in the code alone. See
> [verification.md](./verification.md) §2.

**Rule**: `target = max(lag)` over the largest feasible subset, where feasible
means **every held tile's buffer stays inside 200 ms**:

```
buffer_i = target − processing_i        must be ≤ 200 ms for every held tile
processing_i = lag_i − buffer_i         the decode leg's share, which we cannot move
```

**The cap bounds the buffer, not the lag** — and the planned rule
(`min(max(lag), 200 ms)`) tested the lag. Processing delay belongs to the decode
leg, so testing the combined figure charges this leg for another's time. On a
real wall both tiles measured ~257 ms of lag while buffering only ~131 ms, so
that rule released the entire wall and aligned nothing.

Tiles are dropped **laggiest first**, because the laggiest is the tile setting a
target the others cannot reach. Dropping to a single tile leaves nothing to
align, so the wall makes no claim — **but still names the tiles it dropped**, so
a two-tile wall with one bad tile does not badge the healthy one.

**A released tile keeps playing** (FR-012b). The wall gives up the *claim* about
that tile, never the picture.

**The setpoint is a buffer depth, not the target.** Each held tile is asked for
`target − processing_i`. Handing it the target directly is a runaway: setting
buffer to `T` makes lag `T + processing`, so the next cycle's target is
`T + processing`, and the wall climbs every cycle. T026 watched two tiles
induced at 120 ms reach **~654 ms** — perfectly aligned with each other and half
a second behind the world.

**State transition, with hysteresis on the feasibility decision:**

```
held     ──infeasible for N consecutive cycles──▶  released (badged)
released ──holdable again for N consecutive cycles──▶  held
```

Hysteresis governs **what is marked**; the cap governs **what is actuated**,
every cycle, with no hysteresis at all. Conflating them is what makes a badge
blink: an infeasible tile takes no part in the target immediately, but is only
badged once it has stayed infeasible.

**Fewer than two tiles → no target at all.** Not a target of zero: the
controller does not run, sets nothing, and the tile keeps whatever the browser
chose (FR-004).

---

## 4. `AlignmentReport` — what leaves the browser

Two measurements, sent through the existing service path
(ADR-0122, [contract](./contracts/kiosk-latency-report.md)):

| Measurement | Quantity | Recorded as |
|---|---|---|
| `presentation_buffer` | The delay this leg added to a tile — **achieved, not the setpoint** | `LatencySegment`, 200 ms budget, `isWholeLeg: true` |
| `wall_skew` | Spread between the most- and least-lagged held tile | **Its own instrument — not a latency segment** |

**Why the split** (research D2). `LatencyBudget.Record` answers *how long did
this segment take*. The added delay is a duration and fits. **Skew is a spread
between two tiles, not a journey any frame took**, and filing it as a latency
segment would put a number under a name that means something else — the precise
failure `isWholeLeg`, *"in part"* and *"recorded, not yet readable"* all exist in
this codebase to prevent.

**`isWholeLeg: true`** is unusual here and earned: the kiosk both *causes* the
delay and *observes* it, so nothing is missing from the figure.

**The achieved value, never the setpoint.** `jitterBufferTarget` is not reported
back in `getStats` (research R2), so what was asked for and what happened are
different numbers. Reporting the setpoint would yield a perfect score every time
and measure nothing (FR-007).

---

## What is deliberately not modelled

- **A presentation timestamp per frame.** ADR-014's design. Chromium exposes no
  such thing (research R1), and nothing we own is in the media path to add one.
- **A clock-offset or PTP model.** No PTP is involved. All tiles share one SFU
  clock already (research R5).
- **Cross-wall or cross-kiosk state.** Inter-display sync is out of scope; a
  wall is coordinated within one browser and nothing is shared between kiosks.
- **Persisted alignment history.** The measurements go to the meter. Nothing
  here is stored, and no query reads it back.
