# Verification — 056 label over live video

Phase 5.

---

## 0. What shipped, and what did not

**Shipped:** an automated check that a tile carries an overlay label over
*decoding* video, with both failure directions demonstrated by running them. A
container serving one looping clip, which the SFU pulls exactly as it pulls a
camera.

**Not shipped:** a measured span. The measurement ran and **refused**, which is
the outcome the specification made a passing one.

---

## 1. The gate

Phase 1's gate was **two numbers, the second larger** — not a container that
started, not a path that resolved, not a WHEP negotiation that completed. Every
one of those is compatible with a black tile.

```
[decode] 22 → 42 frames in 1000ms (+20, threshold 10) across 1 element(s)
```

The clip runs at 25 fps, so ~25 frames per second is the healthy figure and the
threshold of 10 sits comfortably below it. Observed across runs at +20, +25,
+26, +30.

---

## 2. Both failure directions, run rather than argued

| Mutation | Verified applied | Result |
|---|---|---|
| Camera points at `rtsp://10.0.5.71/stream` | yes | **killed** — *"no video frame ever decoded on this tile"* |
| Overlay carries no token | yes | **killed** — *"the label is present but does not carry the variable's resolved value"* |

**The first mutation is the evidence for this whole feature.** That address is
the one **every existing overlay fixture uses**. Pointing at it fails the new
check, which is the direct demonstration that those fixtures run with no video —
previously an inference from reading the AppHost.

The second failed *while video kept decoding*, so the halves are independently
load-bearing rather than one assertion wearing two hats.

**The rule also rejects a stall**, checked in both directions inside the test: a
delta of +1 and a delta of 0 are both false against the threshold. Arithmetic
deliberately — it costs no stack time, and a real stalled source would need a
second wall.

---

## 3. The span: refused, and why that is the result

```
[span] UNMEASURED — iteration 0: the value never reached the tile within 60000ms
[span] no figure is reported, and none is derived from per-leg figures
```

FR-009 required exactly this. The alternative was cheap and available — six
per-leg figures exist, and adding them produces an 800 ms-shaped number in a
minute. ADR-0135 established medians do not add. **There is no field and no code
path that could produce one**, confirmed by search (T014).

Had a figure been obtained, it would have covered *event → overlay state* and
*composite + render* only. **Three of six legs are absent**, so it would not have
established that the budget holds.

---

## 4. Cost (FR-016)

| Run | Seconds |
|---|---|
| Full suite, with spec 056 | 321 |
| Full suite, without it | 307 |
| Earlier full run (before the isolation fix) | 363 |

**The added cost is smaller than the machine's run-to-run variance**, so "+14 s"
is not a figure worth quoting — it is a single pair, and this repository's own
rule is that a single pair yields no effect size. The honest statement: **within
noise, and far below the 180 s ceiling.** The CI figure against the recorded
10m35s baseline comes from this branch's own run.

---

## 5. What the checks do and do not prove

| Claim | Proved by | **Not** proved by |
|---|---|---|
| A tile shows a label over live video | both halves on one tile | a screenshot, which cannot show motion |
| The picture is moving | the delta | `framesDecoded > 0`, true of one frame |
| The check can fail | both mutations, run | asserting that it would |
| The old fixtures had no video | mutation 1 | reading the AppHost |
| Nothing sums the legs | the search | nobody having meant to |
| The fixture cleans up | teardown output: 3 cameras, 3 layouts | a green run alone |
| **That the 800 ms budget holds** | **nothing** | a span that omits three legs |
| **That the label path works** | **nothing — it demonstrably does not** | the suite being green |
| **That anyone has watched a wall align** | **nothing** | every check above passing |

---

## 6. Five things that were wrong, and what caught each

1. **The plan's premise.** `camera-sim` is *not* gated off in CI — `E2ETests` is
   never set to `true` anywhere, so the guard is true there. Three committed
   comments and ADR-0111 say otherwise. Caught by reading `ci.yml` before
   writing code against the assumption. Raised, not absorbed.

2. **The specs were named `wall-*`,** which routed them to the `:5175`
   `kiosk-wall` instance — a different client asking for a grant that outlives
   the session ceiling — instead of the ordinary kiosk. Caught by a control run:
   an *existing* kiosk test passed while mine failed on sign-in.

3. **An invented API.** The first decode reader used a `window` hook that does
   not exist. Caught by grepping for it instead of assuming. Replaced with
   `getVideoPlaybackQuality()`, which needs no production change.

4. **A page opened from the kiosk context** inherits `:5174`, so the operator
   sign-in landed on the layout picker.

5. **Two of my tests shared a wall and collided**, so the label read `SPAN0`
   where `BEFORE` was expected. In CI — one worker, alphabetical order — the
   span file runs *first*, so this would have failed there every time. Caught by
   running the full suite rather than the subset. Fixed by putting both in one
   file, making the order explicit rather than resting on filenames sorting the
   way someone hoped.

**And one thing that was nearly wrong.** A tidy hypothesis said the label hold
(ADR-0129) broke updates once video engaged — it explained everything and was
testable. The control disproved it: the failure is identical on a wall with no
video, where the hold cannot engage. Filing it would have been the second issue
raised against working code this session.

---

## 7. Raised rather than absorbed

- **A variable change does not reach an already-open tile**, and no test has
  ever covered that path. Evidence sharpened by the collision in §6.5: a tile
  opened *afterwards* shows the new value immediately, so the server has it.
  Marked `test.fixme` with its evidence — not `fail`, which would assert the
  cause is understood.
- **The simulator runs in CI**, contrary to ADR-0111's recorded scope.
- **`system-variables.spec.ts` fails locally on a cold stack**, the same cause
  as the two seeds fixed here. Those two blocked this feature; that one did not,
  so it is raised rather than swept into this diff.

---

## 8. Phases

- Phases 1–3: spec, plan, tasks.
- Phase 4: T001–T014. T008 held back with evidence.
- Phase 5: this note, ADR-0138, and §IV — which **changed no cell**.
