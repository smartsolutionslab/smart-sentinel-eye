# Spec 225 — The render leg no CI run ever reads

**Issue:** [#2337](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2337)
— "Nothing stops a redesign from spending the 50 ms render leg".
Labelled `agent:ready`, Project #13, status Todo.
**Branch:** `2337-render-leg-ci-never-reads`
**Created:** 2026-09-23
**Status:** US1 merged (PR #2549), US2 merged (PR #2550). **Resumed 2026-09-23 for
US3 + US4 on branch `ci/2337-composite-render-regression-gate`** — see §9, which
supersedes anything above it where the two disagree.
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

---

## 9. Resumption, 2026-09-23: US3 and US4, after US1 and US2 merged

This section is the Phase 1 artefact for the rest of #2337. §§0–8 above still
govern. Where this section tightens or corrects them, it wins, and it says so.
It follows spec 225 rather than opening a new spec number, because US3 and US4
are already specified here. A second directory would be a second source of
truth for one issue, and CLAUDE.md names the latest artefact as the resumption
point.

### 9.1 What has shipped

| Story | State | Where |
|---|---|---|
| US1: the figure leaves every run | **merged** | PR #2549. `e2e/support/render-leg.ts` (record, writer, reader), `scripts/render-leg-summary.mjs` + `.test.mjs`, `ci.yml` step "Summarise the render leg (composite + render, section IV)" in `e2e-shards`, `if: always()`. Commits on `develop`: `6fd3126a`, `69e4538c`, `9d8cecbc`. T001–T009 done. |
| US2: a four-tile fixture wall | **merged** | PR #2550, `b8c14eb9`. Seeds a 2×2 wall, asserts four distinct cameras and at least `ITERATIONS` samples per camera. T010–T014 done. |
| US3: a committed baseline with its variance | **not started** | There is no `figures.md` and no `baseline.json`. |
| US4: a regression fails the run | **not started** | There is no `render-leg-check.mjs`. The summary still prints "No threshold is asserted here". |

#2337 is therefore about two-thirds done. Steps 1 and 4 (measure, and report on
every run) are done. Step 2 (a committed baseline) and step 3 (fail on a
regression) remain.

### 9.2 Four findings from reading the CI record since US2

All four come from the `render-leg-attempt-*.json` files in the
`playwright-report-4-of-4` artifacts of every CI run between US1's throwaway PR
and `develop@859426c8`. They were downloaded and read on 2026-09-23. The
artifacts expire 14 days after each run, so the figures are copied into
`figures.md` (T016) rather than linked.

**F1: the span test has been unstable on the four-tile wall since US2 landed,
and it is failing `develop` now.** This blocks US3.

| Run | SHA | Attempt 0 | Attempt 1 | Attempt 2 |
|---|---|---|---|---|
| 35892337959 (PR #2550 itself) | `94efb231` | passed | — | — |
| 35894668870 (develop) | `b8c14eb9` | failed: one tile had only 7 of 10 samples | timed out at 300 s | passed |
| 35907278215 (PR) | `ec9b4c63` | failed: one tile had only 9 of 10 samples | failed: "Pick a layout" heading was not visible | passed |
| 35918329596 (develop) | `4599d26c` | failed: one tile had only 6 of 10 samples | passed | — |
| 35918472066 (PR) | `3d93a58e` | passed | — | — |
| 35920524319 (develop) | `859426c8` | failed: "iteration 8: the value never painted … (0 label mutation(s) were seen)" | failed: one tile had only 9 of 10 samples | failed: overlay label was not visible. **The shard is red.** |

The span test passed on its first attempt in only 2 of 6 runs. The three
single-tile runs before US2 all passed on attempt 0. A leading hypothesis fits
the code but has not been verified: `armOverlayPaint`/`awaitOverlayPaint` (spec
file `:616-687`) watch only the *first* label on the page
(`document.querySelector`). So each iteration waits for tile 1 alone, and a
tile that lags can have two value changes coalesced into one draw. The same
hypothesis does *not* explain "0 label mutations in 60 s" on tile 1, and that
failure may be product-shaped or caused by runner saturation. **A baseline
taken on a harness that re-rolls itself would record the retry that happened to
succeed.** T026 resolves this before T015 takes a single figure.

**F2: on this runner, render cost shows up as cadence. Normalising by `T`
would cancel the very regression the gate exists to catch.** Moving from one
tile to four is a natural counterfactual already on record. The observed frame
interval went from **16.39–16.94 ms** (runs 35873924190, 35878409081,
35883494479) to **29.87–32.27 ms** (all five complete four-tile attempts), and
p50 went from ~29 ms to ~51 ms. The extra render work did not appear as "work"
above a fixed floor. It *moved the floor*, because the page dropped from every
vsync to every other one. So a gate on `p50 − 1.5·T` would have read the
four-tile change as roughly zero. This is new, measured support for plan §2.5's
existing decision: **the gate asserts raw `p50(overlay_draw)` and reports `T`
beside it, never subtracts it.** It is recorded here so that nobody later
"improves" the gate into a cadence-normalised one.

**F3: preliminary variance. This is not the baseline.** These are the five
four-tile attempts that completed (the first complete attempt in each run):

| Run | SHA | Attempt | n | p50 | mean `T` |
|---|---|---|---|---|---|
| 35892337959 | `94efb23101ede2bde313d6c49c67de7da7411cae` | 0 | 48 | 50.80 ms | 32.03 ms |
| 35894668870 | `b8c14eb9a6f781f787fcf4a0f236d524cb43f951` | 2 | 48 | 50.15 ms | 32.26 ms |
| 35907278215 | `ec9b4c633811b07ff821232a98729be5086e74c0` | 2 | 48 | 56.45 ms | 32.27 ms |
| 35918329596 | `4599d26c0e271389cece8eb05acc4c147b2a359a` | 1 | 48 | 43.50 ms | 29.87 ms |
| 35918472066 | `3d93a58e5c84e64fb837e3ad9381b4244149b9ee` | 0 | 48 | 54.25 ms | 32.03 ms |

Across runs, p50 has a mean of **51.03 ms** and a sample standard deviation of
**4.93 ms** (range 43.50–56.45). These figures cannot be the committed
baseline, for three reasons. Three of the five runs are PR runs, and
`3d93a58e` touches the viewer. Every run was taken on the unstable harness from
F1. And T026 may change the harness. They are recorded here, and copied into
`figures.md` labelled *preliminary*, because they show a tolerance is likely to
exist (see §9.4). Deciding that is T018's job, on the committed data.

**F4: the 48 samples are about 12 independent readings, not 48.** Samples
arrive in groups of four, one per tile per iteration, and the four in a group
nearly coincide (for example `62.3, 63, 62.1, 61.8`). Frame phase is shared
across the wall. So the unit of sampling is the *iteration*. A bootstrap over
all 48 samples gives a standard error of the median of 3–6 ms, and even that
understates the noise. The run-to-run σ is about what twelve samples of this
spread would produce. If the gate ever needs to be tighter, **the lever is
`ITERATIONS`, not tiles**. That is recorded as an option and is out of scope
here (it costs shard time, and #2376 is open).

**Observed, out of scope, raise separately:** `measureFrameInterval` (spec file
`:750-775`) divides the elapsed time by `frames`, but the elapsed time spans
`frames − 1` intervals. `T` therefore reads about 3% low. The 60 Hz / 30 Hz
vsync values would read 16.67 / 33.33 ms, and they appear as 16.39 / 32.26 ms.
The only effect is on the summary's floor arithmetic. It does not affect the
gate, which does not use `T`. This is a defect in merged code, so it is its own
issue and is not folded into this one.

### 9.3 Requirements added or tightened

- **FR-016 (tightens FR-011): "complete attempt" is defined once, by the test,
  and carried in the record.** `RenderLegRecord` gains `complete: boolean`. It
  is computed from the **same predicate** as the test's four-camera /
  `≥ ITERATIONS`-per-camera assertion, written once in `e2e/support/render-leg.ts`
  and called by both, so the gate and the test cannot disagree about what
  complete means. A record without the field is malformed, so the check reads
  it as *unmeasured*, never as a pass.
- **FR-017 (tightens FR-011 and FR-012): the verdict is taken across all four
  shards, in its own job.** Three of four shards legitimately produce no
  record, so a per-shard check cannot tell "not this shard" from "the span test
  produced nothing". A new job, `render-leg-gate`, has
  `needs: e2e-shards` and `if: always()`. It downloads the four
  `playwright-report-*-of-4` artifacts and evaluates the union. Zero records
  across all shards is *unmeasured*. It gets its own check name so that a
  regression is never mistaken for an e2e failure, and vice versa.
- **FR-018 (makes FR-010 concrete): the tolerance rule.** The baseline is the
  mean of the first-complete-attempt p50s over **at least five** `develop` push
  runs on the four-tile fixture, after T026. The tolerance is **3σ** of those
  p50s (sample standard deviation). The gate fails when the p50 of the first
  complete attempt is **greater than** baseline + tolerance.
  - *Why 3σ.* At 2σ the one-sided false-positive rate is about 2.3% per run.
    At this repo's CI volume (about 20 `ci.yml` runs on 2026-09-23 alone) that
    is a false red every two or three days, which is the flaky gate #2337
    forbids. 3σ gives about 0.13% per run.
  - *Why five runs, not three.* A σ estimated from three points has a 95%
    interval of roughly ×0.5 to ×6.3. From five it is roughly ×0.6 to ×2.9.
    The repo's standing lesson (the first run after machine churn looks like a
    regression) also argues for more than three.
- **FR-019 (makes FR-014 mechanical): the feasibility test.** A tolerance may
  be committed only if **3σ < 1.5 × the vsync quantum**. On this runner the
  quantum is 16.67 ms (60 Hz, true value; see the F4 note), so the limit is
  25 ms. F2 shows why this is the right yardstick: a render regression that
  costs a frame moves `T` up by one quantum, and so moves the p50 by about
  1.5 quanta. If 3σ is not below that, the gate cannot tell a one-frame
  regression from noise. Then FR-014 applies: ship report-only, record the
  finding, and hand back for an ADR.
- **FR-020: the summary stops saying "no threshold is asserted" once one
  is.** Once the gate ships, US1's summary line would be false. It is replaced
  with the baseline, the tolerance, the threshold, and a pointer to the
  `render-leg-gate` check. This changes behaviour on purpose.
  `render-leg-summary.test.mjs:134`'s assertion changes because the required
  wording changes, and the old assertion is not weakened. The PR says which of
  the two it is.

### 9.4 What the preliminary figures predict, so T018 can be checked against it

On F3's numbers, 3σ = **14.8 ms**, which is under the 25 ms limit. So a gate
is feasible, and the threshold would be about **65.8 ms**. **No attempt on
record would have tripped it.** That includes the incomplete four-tile
attempts, whose p50s were 36.75, 62.70, 34.75, 51.00 and 55.00 ms. A shift of
about 3σ + 1.65σ ≈ **23 ms** in p50 would be caught about 95% of the time.
That is roughly one vsync step. A regression smaller than a frame on this
runner is **not** detectable, and the gate claims no more than that. Software
rasterisation works in the gate's favour here: a `backdrop-filter` or blur that
costs a few ms on a fab GPU costs far more under SwiftShader. The T022
counterfactual is the proof, not this paragraph.

This is a **prediction** and does not decide anything. T018 decides on the
committed baseline, and if it contradicts this section, T018 wins.

### 9.5 Acceptance scenarios added for US4 (Gherkin)

```gherkin
Feature: the composite-and-render regression gate

  Background:
    Given specs/225-the-render-leg-ci-never-reads/baseline.json carries a baseline p50, a tolerance, and the run ids and SHAs it came from

  Scenario: within tolerance (happy path)
    Given the first complete attempt's p50 is less than or equal to baseline + tolerance
    When render-leg-gate runs
    Then it exits 0 and prints the baseline, the observed p50, the margin, and both T readings

  Scenario: regressed (the conflict case)
    Given the first complete attempt's p50 is greater than baseline + tolerance
    When render-leg-gate runs
    Then it exits non-zero with the word "regressed", names the baseline, observed, tolerance and T, and states ADR-0123's triage order

  Scenario: a retry must not re-roll the verdict
    Given attempt 0 is incomplete and attempt 1 is complete and within tolerance
    When render-leg-gate runs
    Then it evaluates attempt 1, lists attempt 0 as skipped-incomplete, and says the verdict is not from attempt 0

  Scenario: unmeasured (bad input)
    Given no shard produced a record, or every record is incomplete, or a record lacks "complete"
    When render-leg-gate runs
    Then it exits non-zero with the word "unmeasured", never "regressed" and never a pass

  Scenario: a missing or malformed baseline (bad configuration)
    Given baseline.json is absent or fails its schema
    When render-leg-gate runs
    Then it exits non-zero, names the file, and does not fall back to any default threshold
```

There is no auth scenario. This is a CI job that reads artefacts. It holds no
secrets and needs no token beyond the workflow's default `GITHUB_TOKEN` for
`download-artifact`. The artifacts it reads have already been through the
fail-closed scrubber (#2287).

### 9.6 Latency-budget impact

Unchanged from §5. **Leg: composite + render (≤ 50 ms).** The gate adds no work
to the leg and changes no product code. T026 changes only the test harness. If
T026 finds a product defect, that is a STOP and becomes a separate issue. No
leg in §IV changes state.
