# Spec 225 — The render leg no CI run ever reads

**Issue:** [#2337](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2337)
— "Nothing stops a redesign from spending the 50 ms render leg".
Labelled `agent:ready`, Project #13, status Todo.
**Branch:** `2337-render-leg-ci-never-reads`
**Created:** 2026-09-23
**Status:** Phase 1 complete — awaiting review before Phase 2 sign-off
**Lane:** autonomous (ADR-0144)

**ADRs this spec is bound by:**

- **ADR-0123** — *A render leg is the operator's wait, and a wait has a floor*.
  The governing ADR. It fixes what this leg means, forbids "fixing" the
  instrument, and supplies the arithmetic that makes a naive CI gate a cadence
  detector. Everything below is downstream of it.
- **ADR-0122** — a browser measurement reaches observability by being reported
  to a service; a leg may be recorded *in part* under a name that says so, never
  approximated under a name claiming the whole budget.
- **ADR-0015** / constitution **§IV** — the 50 ms budget and the leg-state table.
- **ADR-0138** — the honesty rules: no figure assembled from other figures, no
  leg-state change a person did not earn, a defect recorded as a defect.
- **ADR-0128** Implementation Notes — the constitution's Implemented/Measured/
  Dashboard columns do not move until the code ships and a person reads a figure.
- **ADR-0146** — the redesign discipline this gate exists to police. Note its
  250-tile arithmetic is wrong (#2363); see §0.
- **ADR-0144** — the autonomous lane, including the three things it may not do.
- **ADR-0065** — the coverage gate, whose shape (a committed threshold + a script
  CI invokes) this spec reuses rather than inventing a new one.

**No ADR is amended and no constitution section is amended.** §IV's leg-state
table is untouched — see §5.

---

## 0. The corrected premise

Issue #2337 as filed argues from **a wall of 250 tiles**. Its author corrected
that on 2026-09-14 and this spec is built on the correction, not the original.

`GridDimensions.MaxTiles = 4` and `MaxCells = 4` — a 2×2 grid — are **domain
invariants**, declared at
`src/LayoutComposition/Domain/Layout/GridDimensions.cs:18,21` and enforced at
`src/LayoutComposition/Domain/Layout/Layout.cs:79`. The doc comment there names
the reason outright: the cap exists "so the kiosk never decodes more than
`MaxTiles` simultaneous WHEP peers — the §IV latency mitigation". 250 is a *fab*
figure (constitution §Scale, 250 concurrent cameras per fab), not a *wall*
figure. ADR-0112:139 says the same. ADR-0146 carries the identical error in four
places and is filed for correction as **#2363**; this spec must not inherit it,
including in any comment or commit message it produces.

**The corrected case is the stronger one.** ADR-0123 records this leg as
**already over budget**: p50 **54.2 ms** against §IV's 50 ms, p95 79.2 ms, max
164.6 ms (#1891). The leg has *no headroom at all*, today, measured. The
argument for guarding it does not need an amplification factor that does not
exist.

**One correction of our own, made before it can mislead anyone downstream.**
ADR-0123's three figures are a **citation to #1891, not a record**. ADR-0123
performs no measurement and states no sample count, window, or run id. The only
first-hand reading of this leg in the tree is
`specs/040-kiosk-latency-legs/verification.md` §4, and it is **one sample per
tile** — 139.4 ms and 100.5 ms, on a machine whose cadence was ~15–20 Hz — with
its own warning that the dashboard's numbers there were bucket boundaries rather
than measurements. So the honest statement of the starting position is: *this
leg has never had a distribution recorded in this repository.* That is precisely
the gap #2337 files, and it is a larger gap than #2337 realised.

---

## 1. The gap, precisely

Constitution §IV gives **Overlay composite + render ≤ 50 ms** and records it
`Implemented: yes | Measured: yes | Dashboard: no`. §IV line 140 requires every
PR touching the event-to-overlay path to "cite which leg it affects and
demonstrate the budget still holds" — **by argument**. Nothing automates it.

Three of the four pieces already exist, which is why this is a smaller change
than it looks:

| Piece | State today | Where |
|---|---|---|
| The instrument | **exists** | `measureOverlayDraw`, `apps/shared/src/observability/kioskLatency.ts:135-142` — two chained `requestAnimationFrame` around the overlay's state change. Called per tile at `apps/kiosk-web/src/features/cell/CellPage.tsx:542`. |
| The e2e harvest | **exists** | `e2e/kiosk-shows-a-label-over-video.spec.ts:1137-1181` reads the product's `[latency]` console object (spec 108 US2) and `reportLegs` prints `overlay_draw: N sample(s), p50 X ms, max Y ms — budget 50 ms (section IV composite + render)`. |
| A machine-readable figure | **missing** | Every number is stdout only. It survives solely inside `test-results/e2e-report.json` and the HTML report, uploaded per shard with `retention-days: 14`, gated on the scrubber succeeding. `scripts/summarise-e2e-retries.mjs` renders retried outcomes to the job summary and is contractually forbidden from rendering stdout. **No human ever sees the number.** |
| A committed baseline | **missing** | `.github/workflows/ci.yml` has zero matches for `baseline`, `p50`, `p99`, `percentile`. Nothing in CI reads a committed figure back. |
| A gate | **missing** | No step asserts a millisecond threshold on any §IV leg. The only latency assertion in CI is `e2e/click-to-first-frame.spec.ts`'s `P95_BUDGET_MS = 3000`, which is WHEP negotiation and explicitly **not** one of §IV's six legs. |

So a PR adding a `backdrop-filter`, an animated shadow, or a compositing layer
per tile merges green today, and the redesign programme #2329–#2336 is about to
change how every surface paints. **#2353 is already blocked on this** — a
`container-type: inline-size` per tile, which is a containment context added
directly to this leg's paint path.

---

## 2. The finding that reshapes the work

**A gate on the raw `overlay_draw` figure in CI is a frame-cadence detector, not
a render-cost detector.** This follows from ADR-0123's own arithmetic and is the
single most important constraint on this spec.

ADR-0123 decomposes the instrument's elapsed time into three terms:

> the **wait until the next frame boundary** (0 to one frame interval), then
> **one whole frame interval** while the first frame paints, then the **drawing
> work**. Only the third is compositing.

So `elapsed ≈ (0…T) + T + work`, giving `p50 ≈ 1.5·T + work` and a tail
`≈ 2·T + work`. ADR-0123's cadence table follows: at 27 Hz the floor alone is
37.0–74.1 ms against a 50 ms budget. Its third decision is explicit —

> **A breach of this leg is read as cadence first, compositing second.**

The environment makes this decisive rather than academic. The e2e job
(`ci.yml:583-741`, `e2e-shards`) runs on **`ubuntu-latest`** — a shared
GitHub-hosted runner — with Playwright's bundled Chromium, **headless, no GPU
flags, no display server, software rasterisation (SwiftShader)**. `T` on that
runner is unknown, unmeasured, and the least stable quantity in the arithmetic.
A threshold on `p50(overlay_draw)` would fire on a noisy neighbour and stay
silent on a 5 ms-per-tile blur.

**Three consequences, and they are the shape of the delivery:**

1. **The frame interval must be reported beside the figure or the figure is
   uninterpretable.** ADR-0123 consequence 3 currently binds a human reading a
   breach; this spec makes it mechanical. This is new capability — nothing in
   the repo measures cadence today.
2. **The tolerance cannot be chosen before the variance is measured**, and the
   variance cannot be measured before the figure is machine-readable. That
   orders the stories.
3. **Whether a defensible tolerance exists at all is an empirical question, not
   a design choice.** US4 carries an explicit refusal path: if the measured
   run-to-run variance swamps any regression this gate could plausibly catch,
   the deliverable is a recorded finding and a report-only check — not a
   threshold picked to look like a gate. See FR-014.

**Two further environment facts, both load-bearing, both to be confirmed before
anything is built (T001):**

- **CI does get video on this wall.** `fixture-video` is added at
  `src/AppHost/AppHost.cs:228-236` gated on `isRunMode` **only** — deliberately
  not on the scenario-simulator switch — and `ci.yml` boots the stack with a
  plain `dotnet run`, which does not set `E2ETests`. ADR-0122:147-149 says
  "neither measurement can be verified in CI"; ADR-0138:121-132 found the
  narrower truth that supersedes it for this path. **This discrepancy is itself
  a corrected claim, so it is verified empirically before it is relied on.**
- **`retries: isCI ? 2 : 0`** (`playwright.config.ts:15`). Retries fire only on
  failure, so as `e2e/click-to-first-frame.spec.ts:61-76` already records, "the
  retry count hardens the red and biases the green". A threshold evaluated
  inside the test would be re-rolled up to three times. The gate therefore does
  **not** live inside a Playwright assertion — see FR-011.

---

## 3. User stories

Four slices, P1 first, each independently shippable and each leaving the repo
better than it found it. The chain is largely sequential and §Parallelism says
why.

### User Story 1 — The number, and the cadence that explains it, leave every run (Priority: P1)

A reviewer, or anyone watching the redesign land, can open a CI run and read what
the composite-and-render leg measured on that commit, together with the frame
interval that sets its floor — without downloading a 14-day artifact, unzipping
an HTML report, or knowing that the figure is buried in one shard's stdout.

**Why P1:** it closes half of #2337 ("report the number on every run so a slow
drift is visible before it breaches") on its own, it introduces **no gate and
therefore no flake risk**, and it is the only way to obtain the run-to-run
variance that US3 and US4 need. Shipping it alone is a real improvement: today
the number exists and nobody can see it.

**Independent Test:** open a CI run on this branch, read the job summary, and
find the leg's sample count, p50, max, and the observed frame interval. Confirm
the same figures appear in a committed-format machine-readable artifact. No
threshold is asserted anywhere.

**Acceptance Scenarios:**

1. **Given** a green e2e shard that exercised the live wall,
   **When** the run finishes,
   **Then** the job summary carries one line naming the leg, its sample count,
   p50, max, the observed frame interval `T`, and the §IV budget — and states
   plainly that no threshold is asserted.
2. **Given** the same run,
   **When** the machine-readable figure is read back,
   **Then** it carries the raw samples, not only the percentiles, and the
   run id and commit SHA it was produced from.
3. **Given** a run in which the wall produced **no** `overlay_draw` samples at
   all (a dead fixture, a tile that never painted),
   **When** the summary is written,
   **Then** it says `no samples` — never `0 ms`, which would read as a perfect
   score for a journey nobody timed (the rule `reportLegs` already follows, and
   `kioskLatency.ts`'s own comment states).
4. **Given** the test is retried,
   **When** the figures are emitted,
   **Then** every attempt's figure is recorded and distinguishable by attempt
   number — a retry must not overwrite the attempt before it.
5. **Given** the scrubber (`scripts/scrub-playwright-artifacts.mjs`, which fails
   closed and deletes artifacts it cannot scrub, #2287),
   **When** the new figure file passes through it,
   **Then** the artifact upload still happens and the figure is intact.

---

### User Story 2 — The wall under measurement is the domain's real ceiling (Priority: P2)

The fixture wall that produces this figure carries **four** tiles — the 2×2 the
domain permits — so a per-tile cost is distinguishable from fixed overhead, and
a per-tile regression is measured at the amplitude a real wall would suffer it.

**Why P2:** today's `seed-live-video-wall.setup.ts` creates a **1×1 grid, one
tile, one camera** (its own comment: "One tile is enough"). #2353's queued fix
adds a containment context *per tile*; measured on one tile it is under-read
four-fold. Severable — US1's reporting and US3's baseline are meaningful on a
one-tile wall, just less sensitive — but it must land **before** US3 or the
baseline is taken against a fixture that then changes and has to be thrown away.

**Independent Test:** run the kiosk wall e2e locally against the four-tile
fixture; assert four `<video>` elements decode and that `overlay_draw` yields
samples from four distinct cameras. Compare the run's p50 against the one-tile
figure and record both.

**Acceptance Scenarios:**

1. **Given** the seeded live-video wall,
   **When** the kiosk opens it,
   **Then** the grid is 2×2 and exactly four tiles decode ongoing video from
   `fixture-video` (the existing `isDecodeOngoing` rule, per element).
2. **Given** an overlay value change on that wall,
   **When** the leg's samples are harvested,
   **Then** samples arrive tagged with four distinct camera identifiers.
3. **Given** a request to seed a wall larger than 2×2,
   **When** the layout is created,
   **Then** the domain refuses it (`GridViolation.TooLarge`) — the fixture is
   built at the ceiling, never past it, and the test that asserts "exactly one
   tile" (`kiosk-shows-a-label-over-video.spec.ts:148`) is updated rather than
   left asserting a stale shape.
4. **Given** four decodes on one shared CI runner rather than one,
   **When** the e2e shard runs,
   **Then** it completes inside `timeout-minutes: 45`, and the cost in wall
   clock is measured and recorded rather than assumed.

---

### User Story 3 — A baseline in the tree, with its provenance and its variance (Priority: P3)

`specs/225-the-render-leg-ci-never-reads/figures.md` carries the leg's baseline
measured on `develop`: **at least three separate CI runs**, each with its run id
and full SHA, each with sample count, p50, p95 where n permits, max, raw
samples, and the observed frame interval — plus the run-to-run variance computed
from them and a plain verdict on whether a tolerance can be set.

**Why P3:** this is the artefact that makes any later figure comparable, and the
one #2337 asks for by name. Spec 136 established the rule it obeys: artifact
retention is 14 days, so the run id **and** the figure go in the tree, because a
figure whose provenance expired is not evidence. Three runs, not one, because
this repo's standing lesson is that the first run after machine churn looks
exactly like a regression.

**Independent Test:** read `figures.md` and, for any row, follow its run id to
the Actions run and its SHA to a commit — then re-derive the stated variance
from the raw samples printed in the same file.

**Acceptance Scenarios:**

1. **Given** three or more `develop` runs of the instrumented suite,
   **When** `figures.md` is written,
   **Then** each row carries run id (linked), full 40-character SHA, sample
   count, p50, max, raw samples, and observed `T` — in the format
   `specs/144-a-wait-that-actually-waits/figures.md` established.
2. **Given** a figure taken on a developer machine rather than in CI,
   **When** it is recorded,
   **Then** it is explicitly labelled `local` — spec 144's rule: a labelled
   local figure is evidence, an unlabelled one is not.
3. **Given** the three runs,
   **When** the variance is stated,
   **Then** it is computed and shown, not characterised — and the file states
   what the smallest regression detectable above that variance would be.
4. **Given** runs whose observed `T` differs materially between them,
   **When** the verdict is written,
   **Then** it separates cadence variation from render-cost variation rather
   than reporting one blended number (ADR-0123 consequence 3).
5. **Given** a run that produced no samples,
   **When** the baseline is assembled,
   **Then** that run is recorded as a refusal with its reason, never dropped —
   a harness whose error path discards samples reports the distribution of the
   samples that happened to be fast enough to be observed.

---

### User Story 4 — A regression fails the run (Priority: P4)

A CI step, outside any Playwright assertion, reads the committed baseline and
the run's own figure and **fails** when the leg has regressed beyond the stated
tolerance — with the tolerance derived from US3's measured variance.

**Why P4:** it is the half of #2337 that cannot be built honestly until the
three above have landed, and the only one that can make CI worse if it is wrong.
It closes the issue.

**Independent Test:** prove it by counterfactual — introduce a deliberate render
cost (a `filter: blur()` on the tile, reverted afterwards), observe the gate go
red and name the figure, then restore and observe green. A gate never seen
failing has not been shown to gate anything.

**Acceptance Scenarios:**

1. **Given** a commit whose figure sits within tolerance of the baseline,
   **When** the check runs,
   **Then** it passes and prints both figures and the margin.
2. **Given** a commit whose figure exceeds baseline + tolerance,
   **When** the check runs,
   **Then** it fails, naming the baseline, the observed figure, the tolerance,
   the observed `T` for both, and stating ADR-0123's triage order — cadence
   first, compositing second.
3. **Given** a run in which the e2e test was retried,
   **When** the check runs,
   **Then** it evaluates the **first attempt that produced a complete
   measurement** and reports every attempt — a retry may not re-roll the
   measurement (FR-011).
4. **Given** a run that produced no figure at all,
   **When** the check runs,
   **Then** it fails as *unmeasured* with that word, distinct from *regressed* —
   a missing measurement must never read as a pass.
5. **Given** the baseline file is absent or malformed,
   **When** the check runs,
   **Then** it fails naming the file, rather than defaulting to a permissive
   threshold.
6. **Given** US3's measured variance is wider than any regression this gate
   could plausibly catch,
   **When** US4 is planned,
   **Then** the gate is **not** shipped as a threshold; the finding is recorded
   in `figures.md`, the check ships report-only, and the need for an ADR on what
   CI may enforce on this leg is raised (FR-014). A number picked to look like a
   gate is worse than no gate.

---

## 4. Requirements

### Functional

- **FR-001** The composite-and-render leg's samples, harvested from the
  product's own `overlay_draw` instrument, are emitted from the e2e run in a
  machine-readable form carrying: sample count, raw samples, p50, max, p95 where
  the sample count supports one, the observed frame interval, the attempt
  number, the run id and the commit SHA.
- **FR-002** The run's **observed frame interval `T`** is measured on the same
  page, in the same run, and reported beside the leg's figure. Nothing else in
  this repository measures cadence; this is new.
- **FR-003** A run that produced no samples reports `no samples`, never `0`.
- **FR-004** Every retry attempt's figure is recorded and attributable to its
  attempt; a later attempt never overwrites an earlier one.
- **FR-005** The figure reaches `$GITHUB_STEP_SUMMARY` on every e2e run,
  including red ones, alongside the §IV budget and an explicit statement of
  whether a threshold is being asserted.
- **FR-006** The new artefact survives `scripts/scrub-playwright-artifacts.mjs`
  intact and does not cause it to fail closed.
- **FR-007** The instrument itself is **not modified**. ADR-0123 decision 1:
  "the instrument is correct as built and is not to be 'fixed'." This spec adds
  observation around it and changes nothing about what it measures.
- **FR-008** The seeded live-video wall is a 2×2 grid of four tiles, all backed
  by `fixture-video`, at the domain ceiling and never past it (US2).
- **FR-009** The baseline lives at `specs/225-the-render-leg-ci-never-reads/figures.md`
  in the format spec 144 established: run id linked, full SHA, figures, raw
  samples, and a plain verdict. At least three CI runs.
- **FR-010** The tolerance is derived from the variance recorded in FR-009 and
  the derivation is shown. It is not a round number chosen for looking like one.
- **FR-011** The gate is evaluated **outside** the Playwright test, in a CI step
  or script that reads the emitted figures, so `retries: 2` cannot re-roll it.
  It evaluates the first attempt that produced a complete measurement and
  reports all attempts.
- **FR-012** A run with no figure fails as **unmeasured**, worded distinctly
  from **regressed**.
- **FR-013** A missing or malformed baseline fails the check naming the file; it
  never falls back to a permissive default.
- **FR-014** If FR-009's measured variance exceeds the smallest regression the
  gate could plausibly need to catch, US4 ships **report-only**, the finding is
  recorded, and the spec raises that an ADR is required on what CI may enforce
  on this leg. The lane may not write that ADR (ADR-0144).
- **FR-015** No document produced by this spec repeats the 250-tile premise
  (#2363). Where a per-tile argument is made, the ceiling is four.

### Non-functional

- **NFR-001** The reporting path (US1) adds **no assertion** and therefore
  cannot redden a run. Its failure mode is a missing line in a summary.
- **NFR-002** The observation must not perturb what it observes. Cadence
  sampling runs outside the timed window, and nothing is added to the tile's
  render path.
- **NFR-003** The e2e shard carrying this work stays inside
  `timeout-minutes: 45` (`ci.yml:604`). The four-tile wall's cost in wall clock
  is measured and recorded (US2 scenario 4), because #2376 is open on that
  timeout already.
- **NFR-004** The gate, once shipped, must have been observed **red** by
  counterfactual before it is trusted (US4 Independent Test). This repo's
  standing lesson: a guard that has never failed has not been shown to catch
  anything.

### Out of scope, stated so it is a decision rather than an omission

- **Changing `measureOverlayDraw`, or the leg's definition.** ADR-0123 settles
  both. A higher-resolution instrument that excluded the frame wait would
  measure a different quantity than the one §IV budgets.
- **Discharging §VII's dashboard obligation for this leg.** §IV records
  `Dashboard: no` for all five legs and §IV:188-195 notes every leg is now
  subject. A CI check is not a dashboard. That is **#1940** and stays open.
- **Changing any leg's state in constitution §IV.** See §5.
- **A fab-representative absolute figure.** ADR-0123 already records that
  re-reading cadence on representative hardware is open work; a shared
  `ubuntu-latest` runner with SwiftShader is not that hardware, and this spec
  gates a *relative* quantity for exactly that reason.
- **The 250-camera synthetic load test** named in constitution §Testing:599.
  Unrelated and aspirational.
- **Making this a required status check.** #2288 records that `develop` has no
  required status checks, so the gate's force is bounded by a repository setting
  this spec does not change. Stated, not fixed.
- **#2353's containment fix.** Blocked on this spec; it is not this spec.
- **Correcting ADR-0146's 250-tile arithmetic.** That is #2363, and it is an ADR
  edit the lane may not make.

---

## 5. Latency-budget impact

**Leg affected: Overlay composite + render (≤ 50 ms), constitution §IV.**

This spec adds **no work to that leg**. It adds observation around an instrument
it does not touch (FR-007), on a fixture wall, in tests. NFR-002 keeps the
cadence probe outside the timed window.

**US2 raises the fixture from one tile to four**, which will raise the *measured*
figure on the e2e runner. That is not a regression in the product; it is the
measurement moving to the wall the domain actually permits. US3's baseline is
taken on the four-tile fixture for exactly that reason, and any one-tile figure
recorded along the way is labelled as such.

**No leg changes state.** §IV already records this leg `Measured: yes`. A CI
figure does not make it `Dashboard: yes` — ADR-0128's Implementation Notes rule:
the columns do not move until the code ships and a person reads a figure, and
"renaming a leg is not building it". §IV:197-200 warns in both directions and
this spec moves nothing.

---

## 6. Independent end-to-end test procedure

Performed by a human, or by the lane at phase 5, against a real stack —
`verify.md`'s rules apply, and the note quotes output rather than characterising
it.

1. Boot the stack the way CI does, so the fixture wall carries real video:
   `dotnet run --project src/AppHost/SmartSentinelEye.AppHost.csproj -- ScenarioSimulator=false`
   (run mode, `E2ETests` unset — the ADR-0138:121-132 path).
2. Run the instrumented kiosk wall suite:
   `pnpm test:e2e --project=kiosk -g "span from a value being submitted"`.
3. Read the emitted figure file. Confirm it carries raw samples, the observed
   frame interval, and the attempt number — and that its p50 agrees with the
   `[legs] overlay_draw:` line the same run printed to stdout. **Corrected at
   phase-6 review**: this is not two independent readings — `reportLegs`
   (stdout) and the record writer derive p50 from the identical
   `latencyLines` filter and the identical `percentiles(...)` call, in the
   same process. Agreement here checks serialisation, not derivation; it
   cannot itself surface a computation error in `percentiles`.
4. Open the kiosk wall in a browser against the same stack and confirm four
   tiles are showing moving pictures. A figure from a wall nobody looked at is
   the failure mode `specs/045-.../verification.md` §5 names.
5. **Counterfactual (US4, load-bearing).** Add `filter: blur(4px)` to the tile,
   re-run, observe the gate red and read its message. Revert; confirm
   `git diff apps/` is empty; re-run; observe green. Quote both outputs.
6. Record in `verification.md`: what was observed, the exact commands, the
   output quoted, the leg and its figure, and an explicit *what this does not
   verify* section.

---

## 7. Success criteria

- **SC-001** A reviewer can read this leg's figure, and the cadence that
  explains it, from a CI run's summary without downloading anything. (US1)
- **SC-002** The figure is produced against a four-tile wall — the domain's
  ceiling — with all four decoding real video. (US2)
- **SC-003** `figures.md` carries ≥3 CI runs with run id, full SHA, raw samples
  and a computed variance, and every figure in it can be traced to its run. (US3)
- **SC-004** A deliberate render regression has been **observed** turning the
  gate red, and the output is quoted in the PR. (US4, NFR-004)
- **SC-005** No artefact of this spec repeats the 250-tile premise. (FR-015)
- **SC-006** §IV's leg-state table is byte-identical before and after.

## 8. Assumptions, marked because they are guesses until T001 confirms them

- **A1** `fixture-video` really does serve video in the `e2e-shards` job, making
  `overlay_draw` samples obtainable in CI. ADR-0122 says the opposite;
  ADR-0138:121-132 corrects it. **Confirmed empirically at T001 before anything
  is built.** If A1 is false, US1 has nothing to report and the whole spec
  becomes a different, larger piece of work — that is a STOP, not a workaround.
- **A2** The existing span test (`kiosk-shows-a-label-over-video.spec.ts`)
  yields enough `overlay_draw` samples per run for a p50 to mean something. It
  drives `ITERATIONS = 10` overlay changes; on a four-tile wall that should be
  ~40. Measured at T001, not assumed — spec 040 got **one sample per tile**
  because static labels never redraw.
- **A3** The job summary is the right surface. `scripts/summarise-e2e-retries.mjs`
  already writes there (#2077) and is deliberately forbidden from rendering
  stdout, so a sibling writer is the pattern rather than an extension of it.
- **A4** The frame interval can be measured in-page with a short `rAF` counting
  loop. No new dependency; consistent with every other in-page instrument in
  `e2e/` (none of which uses `PerformanceObserver` or CDP — neither appears
  anywhere in this repository).
