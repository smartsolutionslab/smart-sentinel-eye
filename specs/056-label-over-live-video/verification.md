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
[decode] 23 → 49 frames in 1000ms (+26, threshold 10) across 1 element(s)
```

The clip runs at 25 fps, so ~25 frames per second is the healthy figure and the
threshold of 10 sits comfortably below it. Observed across runs at +20, +25,
+26, +30.

**This figure is from the code that merges.** An earlier draft quoted a run
taken against the *summed* reader, which review replaced with a per-element
check — so the quoted evidence had not exercised the code it was offered for.
Re-run after the change.

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
| The fixture cleans up **cameras, layouts and overlays** | three teardown sweeps, each reporting its totals | a green run alone |
| **That the fixture cleans up everything it creates (FR-006)** | **nothing — it does not** | the three sweeps that do run |
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

## 6a. FR-006 is not met, and saying so is the point

The seed creates four things. Three now have a sweep:

| Artefact | Swept | By |
|---|---|---|
| Camera | yes | existing `E2E ` prefix |
| Layout | yes | prefix added here |
| Overlay | yes | **new sweep** — 111 of 124 overlays were residue, ninety per cent |
| **Variable** | **no** | **the product has no way to remove one** |

**A system variable cannot be deleted** — no control, no endpoint. 1618 have
accumulated. So FR-006, *"clean up whatever it creates"*, is **unmeetable** for
one of the four, and raised rather than glossed.

**And "including on a partial run" is only partly true.** A Playwright teardown
project runs after the projects that name it, but **not** after an aborted run —
Ctrl-C or a worker crash leaves everything. The sweeps are prefix-matched, so a
later run clears the residue; nothing clears it at the moment of the abort.

An earlier draft of this note claimed the fixture cleans up, citing "3 cameras,
3 layouts" — evidence for two of four kinds, offered as proof of a requirement
about all of them.

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

## 7a. Phase 6 — fifteen findings, and the worst was a truncated search

All confirmed. The four that mattered:

**1. The AppHost gate was wrong, from an absence I never verified.** I gated the
video container on `isRunMode` alone, justified by "`E2ETests` is never set to
true anywhere". **It is set** — by `AspireFixture` and `AppHostE2ESwitchTests`.
My search printed twelve lines when there are twenty-one matches, and I read the
truncation as absence. The consequence was real: every integration-test run
booted a container, a 45 MB bind mount and a permanently looping FFmpeg that
nothing consumed. Now gated `isRunMode && !isE2ETests`, exactly as `camera-sim`
is — the end-to-end stack still gets it, because that boot does not set the flag.

**2. A bug was dressed up as the specification working.** The span "refused",
and the ADR called that FR-009's honesty policy being exercised. FR-009's
refusal is about **clocks**. The refusal actually reached was *the value never
arrives* — a defect. The outcome was also non-monotonic: nought figures passed,
one failed, two passed, so a total regression would have looked like today.
Now it fails, and the check is held back explicitly.

**3. The span was not idempotent.** It asserted the seeded initial value as a
precondition and left the variable on its last value. CI retries at *test*
granularity, so any first failure guaranteed two more blaming the precondition
instead of the cause.

**4. My stated fix for the wall collision did not cover the third test.** It
lived in a separate file that sorts *before* the merged one, drove the same
variable, and was inert only because it was held back. Folded in; that file is
gone.

Also fixed: the decode reader summed across elements, so one live tile could
carry a black one past the threshold — the very failure this feature exists to
catch, latent in my own reader. And two bugs in the overlay sweep I had just
written, one of which counted a successful archive as a skip.

**And one more claim of mine that did not survive checking.** I justified the
overlay sweep with "111 of 124 overlays were residue — ninety per cent". True as
a count, misleading as a picture: about a hundred were **already Archived**, so
they were never on the default list. What actually accumulates is a handful per
run. Corrected in the file and here.

---

## 8. Phases

- Phases 1–3: spec, plan, tasks.
- Phase 4: T001–T014. T008 held back with evidence.
- Phase 5: this note, ADR-0138, and §IV — which **changed no cell**.
