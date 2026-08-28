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

---

## 3. `WallTarget` — the decision, and its two outcomes

What the controller computes for a wall, once per cycle.

| Field | Meaning |
|---|---|
| `targetMilliseconds` | The lag every held tile is pushed to |
| `heldTiles` | Tiles that can reach the target |
| `releasedTiles` | Tiles that cannot, without breaching the cap |

**Rule**: `target = min(max(lag over tiles), 200 ms)`.

Aligning means waiting for the slowest, so the target is the **worst** lag — and
the 200 ms cap is the leg's budget (FR-005). A tile whose lag exceeds the cap is
**released**, not held: holding it would drag every other tile past the budget,
which is FR-006's breach and the silent regression US3 exists to catch.

**A released tile keeps playing** (FR-012b). The wall gives up the *claim* about
that tile, never the picture.

**State transition, with hysteresis:**

```
held  ──lag > 200 ms for N consecutive cycles──▶  released
released ──lag ≤ 200 ms − margin for N cycles──▶  held
```

The margin and the consecutive-cycle requirement are not decoration. A tile
sitting exactly at the cap would otherwise flip every cycle, and the operator
would watch a badge blink — the boundary case the spec's edge cases name.

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
