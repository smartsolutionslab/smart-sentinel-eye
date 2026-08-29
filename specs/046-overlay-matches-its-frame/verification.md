# Verification — 046 the overlay and the picture it annotates

Phase 5. Two parts, verified separately because they ship separately: the record
correction (Part 1) is the certain benefit, and the mechanism (Part 2) is the one
whose value cannot be demonstrated.

---

## 0. The thing to say first

**Nobody can see this.** The mismatch this feature removes is tens of
milliseconds. That is below the threshold at which an eye distinguishes a label
from the frame under it, so there is no human confirmation step in what follows
and none was performed. A walk that reported "the wall looked right" would be
reporting that the observer could not tell, which is true whether or not the
mechanism works.

What can be shown is that the mechanism does what it says: the hold is applied,
it tracks the tile's own measured frame age, it is bounded, it fails open, and
the figure that reaches a dashboard is the one that was achieved. That is what is
verified below. Whether it is worth doing is ADR-0129's argument, not a
measurement's.

---

## 1. Automated checks — run the way CI runs them

Not a subset. Spec 045 shipped a green subset and CI caught an architecture test
that had never been run locally.

| Job | Command | Result |
|---|---|---|
| Frontend format | `pnpm format:check` | pass |
| Frontend lint | `pnpm -r --filter "./apps/**" lint` | pass (3 packages) |
| Frontend typecheck | `pnpm -r --filter "./apps/**" typecheck` | pass (3 packages) |
| Frontend test | `pnpm -r --filter "./apps/**" test` | **390 pass** (shared 130, kiosk 57, management 203) |
| Backend build | `dotnet build SmartSentinelEye.slnx -c Release` | **0 warnings, 0 errors** |
| Backend tests | every project under `tests/` except Integration, one at a time | **1,862 pass across 28 projects, 0 failures** |

**The format check failed first, on two files, and that is worth recording** —
both were mine, and CI would have caught them. They were fixed inside the commits
that introduced them rather than bolted on as a formatting commit at the end, so
each commit is clean on its own.

**Not run: the coverage gate itself** (`scripts/coverage-check.ps1`). It requires
PowerShell 7 and this machine has only Windows PowerShell 5.1. The 28 test
projects it runs were all run individually and all pass; what was not computed is
the ADR-0065 threshold arithmetic. The change adds no production code to a gated
assembly — `LabelDelay.cs` is in `ServiceDefaults` and the endpoint change is in
`StreamDistribution.Api`, and the gate covers Domain, Application and Shared — so
the thresholds are very unlikely to have moved. **Unlikely is not verified**, and
CI is where this is actually established.

---

## 2. Mutation testing — every guard, before trusting it

Spec 045 wrote down the lesson that a test can pass with the mechanism deleted,
and its review then found five vacuous tests anyway. So each guard here was
broken deliberately and checked to fail.

| # | Mutation | Killed by |
|---|---|---|
| M1 | Never hold (`holding := false`) | 2 tests |
| M2 | Drop the monotonic sequence guard | **nothing — see §3** |
| M3 | Hold even when the tile has no overlay | 2 tests |
| M4 | Make the jittering frame age an effect dependency | 1 test |
| M5 | Drop the effect cleanup | 1 test (ordering) |
| M6 | `frameAgeFor` returns the buffer instead of the whole age | 1 test |
| M7 | `frameAgeFor` falls back to `0` instead of `null` | 1 test |
| M8 | Report the intended hold instead of the achieved one | 1 test |
| M9 | Report a zero for a tile that was never held | 1 test |
| M10 | Client learns a name the server does not know | 2 tests |
| M11 | Server accepts a name no kiosk can send | 2 tests |
| M12 | `label_delay` filed as a `LatencySegment` | 2 tests |
| M13 | The refusal message forgets a value | 1 test |

---

## 3. What mutation testing found — a guard that guarded nothing

**M2 survived.** The hook carried a monotonic sequence counter on the timer
callback, with a comment claiming it was what kept two labels inside one window
in order (FR-012). Removing it broke no test, and the reason is that it never did
that job: React runs an effect's cleanup before re-running the effect, so
`clearTimeout` had already cancelled the superseded timer. The counter was dead
code taking credit for a guarantee something else provided.

It was removed, along with a `try`/`catch` around `window.setTimeout` guarding a
throw that cannot happen. The ordering test now dies when the **cleanup** is
dropped (M5), which is the mechanism that actually holds it up.

Worth noting because the sequence guard was not sloppy — it was defensible,
commented, and modelled on a real pattern elsewhere in the file. It was still
wrong, and only mutation testing said so.

**A second defect, found by a test written before the code was believed
finished:** a tile with **no overlay** scheduled a timer anyway, and worse — once
an overlay was *removed*, the hook kept returning the previously held label from
state. A label outliving the overlay that owns it is stale text an operator would
read as live. Both were fixed by making the no-label case derived rather than
held.

---

## 4. What the automated checks prove, and what they do not

| Claim | Proved by | Not proved by |
|---|---|---|
| The record no longer claims frame synchronisation | `OverlayFrameClaimTests` | — |
| The guard permits a legitimate rewording | its exclusion-list theory | — |
| A label is held for as long as its picture is old | `useLabelDelay` tests + M1 | that an operator benefits |
| The hold tracks a tile's **own** measured age | `frameAgeFor` tests + M6/M7 | that the age is itself correct — that is spec 045's |
| The hold is bounded at 200 ms | `labelDelay` tests | that 200 ms is the right bound |
| Every failure path shows the label at once | 4 tests + M1/M3 | a failure mode nobody thought of |
| Two labels in one window keep their order | ordering test + M5 | — |
| The reported figure is the achieved hold | M8/M9 | that the browser's clock is honest |
| Client and server accept the same names | `KioskMeasurementContractTests` + M10/M11 | that either name reaches a dashboard |
| A hold is not filed as a latency leg | M12 | — |
| **An operator reads a matched label** | **nothing** | **nothing — it is below what an eye resolves** |

---

## 5. Live walk — partial, and the missing half is named

Run mode without `E2ETests`, so `camera-sim` and the scenario simulator were
live and tiles carried real video. A temporary Playwright harness patched
`RTCPeerConnection` to collect receivers, read their statistics, induced buffer
by writing `jitterBufferTarget`, and captured every `kiosk-latency` POST. It was
deleted afterwards; it was an instrument, not a test.

### What was observed

| Reading | Figure |
|---|---|
| Frame age on a real tile **carrying a real bound label** | **23.6 ms** (9.5 buffer + 14.1 processing) |
| Frame age across four simulator tiles | **~31 ms** (12.9 buffer + 18.3 processing) |
| The same four after inducing `jitterBufferTarget = 140` | buffer **12.9 → 24.5 ms** per frame |
| Measurements reaching the endpoint from a live kiosk | `overlay_draw`, `presentation_buffer`, `wall_skew`, `receive_to_decoded` |
| `label_delay` reports | **none — see below** |

The first three rows are the inputs the hold consumes, and they are real: a tile
that carries a label knows how old its picture is, and writing
`jitterBufferTarget` genuinely makes that number move. **The measured 23.6 ms
sits inside the 200 ms cap**, so the hold that tile would apply is ~24 ms.

### What was not observed, and why

**A label changing while a readable frame age was in hand.** That is the last
link in the chain and it was not reached. The reason is a gap in the fixtures,
not a finding about the code:

- The **e2e seed wall** has an overlay bound to a variable, but registers its
  camera at a fixed RTSP address the SFU has no path for. Its tiles render
  `WHEP returned 404` and produce no receiver at all.
- The **simulator wall** has real video on four tiles and **no overlay on any of
  them** — the tile DOM is a bare `<video>`, and a 60 s watch saw no label text
  at any point. So `label_delay` reporting nothing there was correct behaviour:
  there was no label to hold.
- **Building a third wall with both failed.** The layout editor's camera picker
  caps at 50 options, and the accumulated e2e seed cameras (51 at the time of
  writing, two more every run) have pushed every simulator camera off it.

One run did assemble a wall with both halves, just before the picker overflowed
— video, a label reading `BEFORE`, and the 23.6 ms above. Changing the variable
then did not reach that kiosk within 20 s, so the label never changed and no
hold engaged.

**That stale label is not the hold.** The hold is capped at 200 ms and fails
open on every path; it cannot hold anything for 20 s. The control for this is
`kiosk-reconciliation.spec.ts`, which asserts the same variable-to-kiosk
propagation, predates this change, and **passes on this stack with this code in
place** — so label updates still arrive end-to-end in a real browser. What the
harness relied on was the live push while connected, on a wall whose tile had no
readable age anyway.

### Cost

Eight attempts. Several failures were the harness's own — a wrong button name, a
Keycloak form filled before the sign-in click, which hung outright because this
Playwright config sets no action timeout. Recorded because the next person to
attempt this will meet the same fixtures.

### Worth filing

Two fixture defects, neither in scope here, both raised rather than absorbed:

- **No wall in this stack has both video and a bound overlay** (issue 1978),
  which leaves the overlay-over-video path — the product — undemonstrable end to
  end.
- **The e2e seeds accumulate cameras without bound** and the layout editor's
  camera picker silently truncates at 50 (issue 1979). The second half matters
  beyond e2e: the constitution targets 250 cameras, so that picker is unusable
  at the scale the system is designed for.

---

## 6. What is not verified

- **The last link of the live walk** — a label changing while a readable frame
  age is in hand (§5). The inputs were observed; the hold itself was not.
- **The coverage gate arithmetic** (§1).
- **Any human confirmation** — impossible by construction, §0.
- **A wall larger than two tiles.** The same limit spec 045 recorded. The hold is
  per tile and shares no state between tiles, so tile count should not matter;
  "should" is doing work in that sentence.
