# Tasks — 056 label over live video

**Feature**: [spec.md](./spec.md) · [plan.md](./plan.md) · [contracts/the-fixture.md](./contracts/the-fixture.md)
**Branch**: `056-label-over-live-video`

15 tasks in four phases. Phase 1 is a gate, and **its gate is a decoded
frame** — not a container that started.

---

## Do not

- **Do not un-gate `camera-sim` or extend the scenario simulator.** ADR-0111
  accepts its cost because prod and CI never see it.
- **Do not add a static path to `mediamtx.yml`.** It keeps `paths: {}` because
  static entries collide with the control API's.
- **Do not change the browser or set a `channel`.** That re-acquires the H.264
  risk this feature retired by execution.
- **Do not sum per-leg figures**, anywhere, for any reason.
- **Do not claim the 800 ms budget is met** from a span that omits three legs.
- **Do not change any budget**, and **do not make anything faster**. This
  feature observes and measures.
- **Do not touch the dashboard question** or representative-hardware
  re-measurement.
- **Do not add a kiosk latency measurement name.** The reported set stays five.
- **Do not write a second reader of the `inbound-rtp` stats.**
- **Do not hard-code the video source's host and port in a spec file.**
- **Do not write bare `#NNNN` issue numbers in committed docs.**

---

## Phase 1 — A frame that actually decodes *(the gate)*

**The gate is not "the container is up".** A container that starts, a path
that resolves, and a WHEP negotiation that completes are each compatible with
a black tile. Phase 1 is done when `framesDecoded` is observed **advancing in
a browser**, once, by hand if necessary.

Stopping at "the container is up" would be this spec's own defect — a check
that passes on half a system — committed inside the fix.

- [x] T001 Add an end-to-end-gated video-source container in `src/AppHost/AppHost.cs`, from `bluenviron/mediamtx:latest-ffmpeg`, bind-mounting `Resources/clips`, and a `src/AppHost/Resources/fixture-video.yml` holding one static path that loops one clip over RTSP
- [x] T002 Publish the source's RTSP address to the end-to-end run as configuration in `src/AppHost/AppHost.cs`, so no spec file composes a host and port
- [x] T003 **GATE — observe a decoded frame.** Bring the stack up, register a camera at the source, open the wall in a browser, and confirm `framesDecoded` **increases between two readings**. Record the two numbers in the verification note. Do not proceed on a resolving path or a completed negotiation.

---

## Phase 2 — US1: the fixture that sees both halves *(P1)*

**Goal**: a check that cannot pass on half a tile.

**Independently testable**: remove the video → it fails; remove the label → it
fails; and both failures are *observed*, not argued.

- [x] T004 [US1] Add `e2e/support/live-video-wall.ts` owning every name the fixture creates, each carrying the end-to-end prefix the teardown matches on
- [x] T005 [US1] Add `e2e/support/seed-live-video-wall.setup.ts` creating the camera (at the configured source address), variable, bound overlay and published one-tile wall
- [x] T006 [P] [US1] Extend the teardown in `e2e/support/` to remove this fixture's rows, matching on the prefix
- [x] T007 [US1] Add `e2e/kiosk-shows-a-label-over-video.spec.ts` asserting **both halves on the same tile**: ongoing decode (two samples 1000 ms apart, delta ≥ 10 frames, via `decodeSampleFrom`/`decodeElapsedBetween`) **and** the overlay's resolved text — each assertion naming which half it inspected and what it saw instead
- [~] T008 [US1] Add `e2e/kiosk-label-follows-its-variable.spec.ts` changing the variable and asserting the label follows **while video keeps decoding**, so a label correct by coincidence cannot pass
- [x] T009 [US1] **Demonstrate both refusals by running them** (contract C3): point the camera at an address nothing serves and observe the failure; unbind the label and observe the failure. Record both messages. "It would fail" is not evidence — a prior spec's guard passed with its mechanism entirely unwired
- [x] T010 [US1] Assert the decode threshold **rejects a stalled stream**, not merely that it accepts a healthy one

---

## Phase 3 — US2: the span, timed or refused *(P1)*

- [x] T011 [US2] Add `e2e/kiosk-shows-a-label-over-video.spec.ts (merged)` measuring submission → label-visible as **one subtraction on one clock**, both stamps taken by the test process; five iterations
- [x] T012 [US2] Report **every** figure plus median and range, with `legsCovered`, `legsNotCovered` and the conditions — structured so a figure **cannot** be printed without its scope
- [x] T013 [US2] **Exercise the refusal path**: a run that cannot establish one clock reports what it could not establish and **no figure**. An untested refusal is the branch that will be wrong when it matters
- [x] T014 [US2] Confirm **by search** that no field and no code path sums per-leg figures into an end-to-end number

---

## Phase 4 — US3: the record *(P2)*

- [x] T015 [US3] Write `docs/adr/0138-<slug>.md` (what was seen, what was timed, which legs the span covers, that the hold is inside it); update **§IV's leg table** in `.specify/memory/constitution.md` for the **two** legs whose state changes; measure and report the added CI time against the **3-minute ceiling** and the **10m35s** baseline; run the mutation table; write `specs/056-label-over-live-video/verification.md`

**This predicted two legs would change state. Neither did, and the table
records what happened rather than what was expected.**

The prediction was: *SFU → kiosk decode* moves from **in part**, and *event →
overlay state* gains a figure. What the run established:

- Decode is now **observed** — frames advancing in a browser — but observation
  is not a latency figure, and the Measured column is about figures.
- Event → overlay state gained **no** figure: the span was **refused**, because
  the value never reached the already-open tile.

So §IV changed **no cell**, and says so explicitly. Leaving the prediction here
uncorrected would be the defect this feature exists to close, in the document
that ordered the work: the table once recorded three legs as unbuilt for months
after they were built, and a cell that gains a *measured* because a task list
expected one is the same error arriving by a tidier route.

---

## Mutations that must each kill a test

**Every mutation must be confirmed to have applied before its result is
read.** Twice in spec 055 a mutation reported a pass — once because the edit
never landed, once because the assertion ran before the state it tested
existed. Check the file, then run.

| # | Mutation | Must kill |
|---|---|---|
| 1 | Point the camera back at an address nothing serves | T007 |
| 2 | Read a single non-zero `framesDecoded` instead of a delta | T010 |
| 3 | Drop the 10-frame threshold to 1 | T010 |
| 4 | Assert the label without asserting decode | T007 |
| 5 | Stamp the span's start in the browser instead of the test process | T011 |
| 6 | Let a refused span report a figure anyway | T013 |
| 7 | Drop `legsNotCovered` from the report | T012 |
| 8 | Make the teardown match on something other than the prefix | T006 |

**Mutation 2 is the one to get right.** It is the difference between a frozen
wall and a working one, and a screenshot cannot tell them apart.

---

## Dependencies

```
T001 ─→ T002 ─→ T003 (GATE)
                  │
                  ├─→ T004 ─→ T005 ─→ T007 ─→ T008
                  │            │        └────→ T009
                  │            └─→ T006        └→ T010
                  │
                  └─────────────────→ T011 ─→ T012 ─→ T013
                                        └────────────→ T014
                                                        │
  T007..T014 ───────────────────────────────────────→ T015
```

**Nothing crosses T003.** Every task in phases 2–4 asserts against decoding
video; written before the gate holds, each would be written against a guess.

---

## Parallel opportunities

- **T006** runs alongside T007–T008 — different file, and the teardown does
  not depend on what the specs assert.
- **T009 and T010** are independent of each other once T007 exists.
- **T014** (the search) needs no running stack and can be done any time after
  T012.
- **Phase 3 does not wait for phase 2 to finish.** The span needs the seeded
  wall (T005), not the assertions about it.

---

## Implementation strategy

**MVP is T001–T010** — US1 alone closes the gap this feature was filed for. A
fixture that sees both halves is shippable without any measurement; the
measurement without the fixture is not, because there would be no wall with
video to measure.

**No coverage gate is live.** No Domain or Application code is touched; the
only C# is AppHost composition, which no coverage threshold covers. Do not
manufacture unit tests to satisfy a gate that is not running.

**The suite runs on the `wall` project** (`npx playwright test --project=wall`),
which already has `seed` as a dependency and `cleanup` as a teardown.

**Project #13**: `/speckit-tasks` adds nothing to the board. Add the feature's
issues by hand:

```sh
gh project item-add 13 --owner smartsolutionslab --url <issue-url>
```

---

## Three things most likely to go wrong

1. **The gate gets passed on a resolving path.** T003's evidence is two
   numbers, and the second must be larger. A path that appears in the SFU's
   config, a WHEP negotiation that completes, a `<video>` element that exists
   — all are compatible with a black tile, and all feel like progress.

2. **A mutation reports a pass because it never applied.** This has happened
   twice, recently, in this repository. The `perl`/`sed` edit silently matches
   nothing, the file is unchanged, the suite passes, and the row gets ticked.
   Confirm the file changed, then run.

3. **The span gets compared to 800 ms.** It will be tempting, the number will
   look good, and it will be wrong — three legs are not in it. `legsNotCovered`
   is structural for exactly this reason, and the ADR must say it in prose too.

---

## What the automated checks do and do not prove

| Claim | Proved by | **Not** proved by |
|---|---|---|
| A tile shows a label over live video | T007, both halves on one tile | a screenshot, which cannot show motion |
| The picture is moving | T010's delta | `framesDecoded > 0`, true of one frame |
| The binding is live | T008 | the text being right on first paint |
| The check can fail | T009, both directions run | asserting that it would |
| The span is one clock | T011 | the number looking plausible |
| A refusal works | T013 | the branch existing |
| Nothing sums the legs | T014's search | nobody having meant to |
| The fixture cleans up | T006 + a deliberate partial run | a full green run |
| **That the 800 ms budget holds** | **nothing** | a span omitting three legs |
| **That anyone has watched a wall align** | **nothing** | every check above passing |
| **That a fab kiosk behaves like this** | **nothing** | a CI runner's figure |

The last three are the honest ones. The first of them is why `legsNotCovered`
exists; the second is what stays open after this feature ships.
