# Tasks: A wall shows one instant

**Feature**: `045-wall-shows-one-instant` · **Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md)

**26 tasks across seven phases.** The code is a pure arithmetic module, one
control-loop hook, one receiver accessor and two contract names. The rest is an
ADR, two constitution rows, and a set of tests that are worthless unless they
induce the condition they assert on.

**The mechanism is already settled** — measured against a real MediaMTX and a
real Chromium in [research.md](./research.md). Nothing here needs to rediscover
that `estimatedPlayoutTimestamp` does not exist.

**The suite can be green with the controller deleted.** On an idle box with two
identical sources the spread was **9.410 ms before this feature existed**, well
inside the 33 ms bound. Every skew assertion below therefore induces skew first.
This is stated in the test tasks themselves, not only in quickstart, because a
test that forgets it looks exactly like a test that works.

---

## Do not

- **Do not start any mechanism task before T001 lands.** ADR-014 is Locked and
  cannot be built as written. Picking a mechanism first is silently redesigning
  a locked decision, which is the failure this spec was written to prevent.
- **Do not touch `WhepClient`'s session lifecycle.** Expose the video receiver
  and nothing else — mirror exactly how spec 040 exposed `stats()`: read-only
  access to an object the client already owns, with the transceivers,
  reconnection and teardown untouched.
- **Do not split the contract change across two commits.** The endpoint 400s an
  unknown `Measurement` and `reportKioskLatency` swallows failures, so a split
  ships a kiosk that looks healthy and reports nothing. See T014.
- **Do not record `wall_skew` as a `LatencySegment`.** It is the natural thing to
  do and it would pass every test. A spread is not a journey. See T015.
- **Do not report `jitterBufferTarget`.** It is write-only — absent from
  `getStats`. Reporting the setpoint yields a perfect number that measures
  nothing.
- **Do not add a telemetry SDK to the kiosk bundle**, and **do not add an
  observability sink**. ADR-0118 and ADR-0122 stand: the browser reports an
  elapsed figure to a service, and that is all.
- **Do not build a pluggable clock strategy or an alignment-source abstraction.**
  One controller, one actuator. There is no second implementation and no caller
  asking for one.
- **Do not mount the controller in `management-web`.** It has no wall. See T019.
- **Do not write `#1714`-style bare issue numbers** in committed docs — the
  automation closes a merely-mentioned issue on merge.

---

## Phase 1: The ADR — this gates everything

**Goal**: a Locked decision that can actually be implemented. No code in this
phase.

- [x] T001 Write `docs/adr/0128-playout-alignment-without-ptp.md` amending row 014 of `docs/adr/0000-initial-decisions.md`. Four things it must settle, each with its evidence in [research.md](./research.md): (a) what replaces *"StreamKeeper emits presentation timestamps"* — nothing we own is in the media path, MediaMTX is an unmodified third-party container, and the replacement is receiver-side alignment against the SFU's RTCP sender clock (R5); (b) the fate of the **`< 5 ms` inter-display** target — out of scope, because it needs the grandmaster and PTP-aware switches ADR-014 itself names as a deployment prerequisite and which do not exist here; (c) constitution §Frontend's *"PTP-aware time APIs required"* of target browsers — **no browser exposes any**, so the requirement is unsatisfiable as written and must be corrected or removed; (d) **the leg's name** — §IV calls it *"Presentation buffer (PTP)"* and the mechanism uses no PTP (R5, D3). Rename it or justify keeping it.
- [x] T002 Update the ADR index / `docs/adr/0000-initial-decisions.md` row 014 status to **Amended**, following how ADR-0118 amended row 026.

**Checkpoint**: the mechanism is legal to build. **Nothing below starts before this.**

---

## Phase 2: Measure a tile's lag

**Goal**: the observable before the actuator, so the controller is never flying
blind. Pure arithmetic — no browser, no React.

- [ ] T003 Create `apps/shared/src/observability/wallAlignment.ts` with a `LagSample` reader over an `RTCStatsReport` (`inbound-rtp`, video): `jitterBufferDelay`, `jitterBufferEmittedCount`, `totalProcessingDelay`, `framesDecoded`. Mirror `decodeSampleFrom`'s shape in `kioskLatency.ts`, including returning `null` on a malformed report.
- [ ] T004 In the same file, add the per-frame delta: `Δ jitterBufferDelay / Δ jitterBufferEmittedCount + Δ totalProcessingDelay / Δ framesDecoded`, in ms. **`null`, never `0`**, when no frames were emitted or a counter went backwards — `kioskLatency.ts` already carries the reasoning (*"a zero would read as a perfect score for a journey nobody timed"*) and it is unchanged here. **Divide by `jitterBufferEmittedCount`, not `framesDecoded`** — they are different counters and the wrong one skews the figure silently.
- [ ] T005 [P] `apps/shared/src/observability/wallAlignment.test.ts` — the delta against fabricated reports: a normal pair, no frames emitted (→ `null`), a counter that went backwards (→ `null`), and a missing-field report (→ `null`). No browser needed.
- [ ] T006 [P] Test that a **cumulative** ratio and a **delta** disagree on a session containing one excursion. This is the test that fails if someone "simplifies" T004 back to a lifetime average, which is the whole reason the delta exists.

**Checkpoint**: each tile's lag is computable and tested.

---

## Phase 3: Align the wall (US1) — the MVP

**Goal**: a wall coordinates. Shippable on its own.

- [ ] T007 [US1] In `apps/shared/src/streaming/WhepClient.ts`, expose the video `RTCRtpReceiver`. **Read-only access to an object this client already owns** — copy the framing and the caution of the existing `stats()` accessor, including its "checked rather than assumed" guard. Session lifecycle untouched.
- [ ] T008 [US1] In `apps/shared/src/ui/composites/useWhepSession.ts`, pass a `setPlayoutTarget(ms)` through, stable across renders via `useCallback` with no deps. **A fresh identity each render tears down callers' effects** — that is exactly how the decode sampler was silently killed (issue 1889), and the existing comment on `stats` says so.
- [ ] T009 [US1] In `apps/shared/src/ui/composites/CameraViewer.tsx`, accept an optional playout target and apply it to the receiver. Optional so `management-web` passes nothing and behaves exactly as today.
- [ ] T010 [US1] In `wallAlignment.ts`, add the decision: `target = min(max(lag over tiles), 200 ms)`, returning held and released sets per [data-model.md](./data-model.md) §3. **Fewer than two tiles returns no target at all** — not a target of zero (FR-004).
- [ ] T011 [US1] Add **hysteresis** to T010: a tile crosses to released only after N consecutive cycles above the cap, and returns to held only below `cap − margin`. Without it a tile sitting at 200 ms flips every cycle and the operator watches a badge blink.
- [ ] T012 [US1] Create `apps/kiosk-web/src/features/cell/useWallAlignment.ts` — the control loop: sample every tile on an interval, compute the target, apply it. **Every failure path is a no-op**: unreadable stats, a receiver that rejects a target, an arithmetic `null`. Video continues (FR-013). Mount it in `CellPage.tsx`.
- [ ] T013 [US1] `apps/kiosk-web/src/features/cell/useWallAlignment.test.ts` — **induce the skew, then assert convergence**. Fabricate tiles with lags 20/30/**120** ms; assert every held tile is driven to 120 ms and the spread closes to ≤ 33 ms. **A test that asserts a small spread without first creating a large one passes with the controller deleted** (research R6) — that is the failure mode this whole feature is most likely to ship with.

**Checkpoint**: **US1 shippable.** An induced spread converges; a wall coordinates.

---

## Phase 4: The leg gets a number (US2)

**Goal**: §IV's row stops saying "no" and says something true.

- [ ] T014 [US2] **One task, one commit, both sides.** Add `presentation_buffer` and `wall_skew` to the closed set in `src/StreamDistribution/Api/StreamEndpoints.cs` **and** send them from `apps/shared/src/observability/kioskLatency.ts`. Update the validation message to name all four. **Split this and the kiosk posts every measurement into a 400 while looking perfectly healthy** — `reportKioskLatency` swallows failures deliberately, so nothing surfaces. See [contracts/kiosk-latency-report.md](./contracts/kiosk-latency-report.md).
- [ ] T015 [US2] Add `LatencySegment.PresentationBuffer` in `src/ServiceDefaults/LatencyBudget.cs` — 200 ms budget, **`isWholeLeg: true`** (unusual and earned: the kiosk both causes the delay and observes it, so nothing is missing). Then create `src/ServiceDefaults/WallSkew.cs` for skew as **its own instrument**. **Do not reuse `LatencySegment` for skew.** It is the obvious move, it compiles, it passes every test, and it files a spread under a name that means a journey — the precise mislabelling `isWholeLeg`, *"in part"* and *"recorded, not yet readable"* all exist to prevent.
- [ ] T016 [US2] In `useWallAlignment.ts`, report both figures — **the achieved value from T004's delta, never the setpoint**. `jitterBufferTarget` is absent from `getStats` (research R2), so reporting what was asked for yields a perfect number describing nothing (FR-007).
- [ ] T017 [P] [US2] `tests/ServiceDefaults.Tests/KioskLatencyTests.cs` — `presentation_buffer` records with `isWholeLeg: true` against its 200 ms budget; skew records to its own instrument and **not** to the segment histogram.
- [ ] T018 [P] [US2] `tests/Integration.Tests/StreamDistribution/` — the endpoint accepts both new names; an unknown name is still refused and the message names all four; a **non-kiosk** principal sending either is accepted and dropped (`IsBrowserKiosk()`, #1893).
- [ ] T019 [P] [US2] Test that `management-web` never sends these: it mounts `CameraViewer` but never `useWallAlignment`, so it has no wall and no controller. **The server's `IsBrowserKiosk()` gate is the backstop, not the design** — if this test needs the gate to pass, the client is wrong.
- [ ] T020 [US2] Update constitution §IV's leg table: this leg's Implemented / Measured / Dashboard columns become true (FR-010). Requires T001's ADR — §IV cannot change without one.
- [ ] T021 [US2] Revisit the **decode** leg's *"in part"* in the same change (FR-011). **Restated rather than raised is the expected and acceptable outcome**: RTCP gives a sender clock and RTT a round trip, but Chromium exposes no per-frame send-to-arrival mapping, so `RTT/2 + buffer + decode` is an **estimate**. Rounding an estimate up to *measured whole* is exactly what §IV's wording forbids — record the reason and leave it.

**Checkpoint**: **US2 shippable.** Both figures readable in the sink, per tile.

---

## Phase 5: Say when it cannot hold (US3)

- [ ] T022 [US3] Create `apps/kiosk-web/src/features/cell/TileAlignmentBadge.tsx` — a released tile is visibly marked, and the condition is logged for an engineer (FR-012). **A released tile keeps playing** (FR-012b): the wall gives up the claim, never the picture.
- [ ] T023 [P] [US3] Test the boundary: a tile oscillating around the 200 ms cap settles held or released and **stays** there. Drive it to 195 ms and 205 ms across several cycles and assert the state does not flip — this is T011's hysteresis, and nothing else catches its absence.
- [ ] T024 [P] [US3] Test FR-013 explicitly: make the stats read throw and the receiver reject a target, and assert **every tile still has a live video element**. An observer that can break what it observes is worse than no observer (spec 040's rule).

**Checkpoint**: **US3 shippable.**

---

## Phase 6: The single-tile guarantee

- [ ] T025 [P] Test that a **1×1 wall sets no target at all** — assert `jitterBufferTarget` was **never written**, not merely that latency is unchanged (FR-004). Asserting on latency is too weak: a controller that sets a target equal to the tile's own lag changes nothing measurable and is still wrong.

---

## Phase 7: The part no machine can do

- [ ] T026 Follow [quickstart.md](./quickstart.md) against `dotnet run --project src/AppHost`. **Induce skew at every step** — §1's 400 ms outlier must be released, §1's 120 ms outlier must be held, and both spreads recorded before and after. Walk §2's boundary, §3's reconnect, §4's single tile, §5's deliberate break, §6's dashboard read and §7's end-to-end (**run it twice** — the first measurement after machine churn looks exactly like a regression). Then record **what was not verifiable here** (FR-015): **inter-display skew above all**, since the grandmaster and PTP-aware switches do not exist; representative kiosk hardware (issue 1941's standing caveat); and any step not performed. **Name any step skipped** — a step skipped silently reads as a step that passed.

---

## Dependencies

```
T001 ─▶ T002 ─────────────────────────────────────────  the gate; nothing precedes it
          │
          ├─▶ T003 ─▶ T004 ─▶ T005, T006                (the observable, then its guards)
          │             │
          │             ├─▶ T007 ─▶ T008 ─▶ T009        (the seam, one file at a time)
          │             │              │
          │             └─▶ T010 ─▶ T011 ─▶ T012 ─▶ T013   (decide, damp, run, prove)
          │                                     │
          │                                     ├─▶ T014 ─▶ T015 ─▶ T016 ─▶ T017, T018, T019
          │                                     │                              │
          │                                     │                    T020 ─▶ T021
          │                                     │
          │                                     ├─▶ T022 ─▶ T023, T024
          │                                     └─▶ T025
          │
          └──────────────────────────────────────────────────────▶ T026
```

**T001 before everything.** It is a Locked-ADR amendment, not paperwork: the
mechanism is illegal to build until it lands.

**T004 before T010.** The controller cannot decide a target from a lag that
cannot be computed.

**T011 before T013.** Without hysteresis the convergence test is flaky for a
real reason, and it will be re-run rather than fixed.

**T014 and T015 together, before T016.** The kiosk cannot report a name the
server refuses.

**T020 needs T001.** §IV cannot change without an ADR.

## Parallel opportunities

- **T005 and T006** — two tests on one pure module.
- **T017, T018 and T019** — segment tests, endpoint tests and the
  management-web test touch three different trees.
- **T023, T024 and T025** — three independent behaviours, three files.
- **Phase 3 is NOT parallel**: T007→T008→T009 is one seam threaded through
  three files, and T010→T011→T012 is one control loop.

## Implementation strategy

**MVP is T001 through T013.** The ADR, the observable, the actuator and the loop.
At that point a wall coordinates and an induced spread converges — which is US1,
the whole complaint, before a single measurement is exported.

**Do Phase 2 as one commit.** The reader, the delta and their tests only make
sense together.

**T014 and T015 are one commit** for the reason stated in T014.

**Budget real time for T026.** It is the only step that can confirm a wall looks
like one instant, and inducing skew at each step is slower than it reads.

---

## Three things most likely to go wrong

1. **The suite is green because the box is quiet.** The spread was already
   9.4 ms before any of this existed. Every skew test passes with the controller
   deleted unless it induces skew first. Only T013, T023 and T026 prevent it, and
   all three depend on someone remembering *why* — which is why the reason is
   written into the tasks rather than left in quickstart.

2. **Alignment eats the budget.** R4 measured absolute lag roughly doubling,
   30 → 59 ms. Without T010's cap and T026 §7's end-to-end re-measurement, this
   leg fixes a wall and breaks the 800 ms SLO — trading one breach for another,
   invisibly, because nothing else re-measures the whole path.

3. **`wall_skew` becomes a `LatencySegment`.** T015 says not to, and it is
   still the path of least resistance: the type exists, the transport exists, it
   compiles, and every test passes. The result is a spread filed under a name
   meaning a journey — and this repo has been caught by exactly that kind of
   mislabelling three times already, which is why `isWholeLeg` exists at all.

---

## What the automated suite does and does not prove

| Claim | Proved by | Not proved by |
|---|---|---|
| A tile's lag is computed as a delta, not an average | T005, T006 | — |
| An induced spread converges to one target | T013 | any test that does not induce one |
| A tile at the cap does not oscillate | T023 | — |
| A single-tile wall sets no target | T025 | a latency-unchanged assertion |
| A controller failure does not stop video | T024 | — |
| The new names are accepted; unknown ones still refused | T018 | — |
| Skew is not filed as a latency segment | T017 | — |
| management-web never reports these | T019 | the server-side gate |
| The leg's figure is readable by a person | T026 §6 | any test |
| **The three tiles of a wall look like one instant** | **T026 — a person** | everything above |
| **Inter-display skew is within any bound** | **nothing — the hardware does not exist** | everything above |

The last two rows are the honest ones. Point every tile at the same target and
every row above them stays green.
