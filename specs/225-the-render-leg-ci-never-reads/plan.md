# Plan — Spec 225, the render leg no CI run ever reads

**Spec:** `specs/225-the-render-leg-ci-never-reads/spec.md`
**Issue:** [#2337](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2337)
**Branch:** `2337-render-leg-ci-never-reads`
**Status:** US1/US2 merged (#2549, #2550). **§8 is the Phase 2 addendum for the
US3 + US4 resumption (2026-09-23)**; it refines §§2.4, 3 and 5 where they differ.

---

## 1. Bounded context and layers

**None.** This spec touches no bounded context, no Domain, no Application, no
Infrastructure, no Api. It adds nothing to `src/` at all.

That statement is the design, not an omission. The quantity being guarded is
produced by a browser and consumed by CI; it crosses no context boundary and
defines no aggregate. NetArchTest has nothing to say about it, and neither §II
(value objects) nor §III (context isolation) is engaged. The one piece of
product code involved — `apps/shared/src/observability/kioskLatency.ts` — is
**read and not modified** (spec FR-007, ADR-0123 decision 1).

Surfaces touched:

| Surface | Change | Story |
|---|---|---|
| `e2e/` (Playwright specs + support) | harvest, cadence probe, emit | US1, US2 |
| `scripts/` (Node) | summariser, checker | US1, US4 |
| `.github/workflows/ci.yml` | two steps in `e2e-shards` | US1, US4 |
| `specs/225-*/figures.md` | the committed baseline | US3 |
| `src/` | **nothing** | — |
| `apps/` | **nothing** | — |

**No new runtime resource, so `AppHost` is untouched** (constitution §VI is not
engaged: this adds no Aspire resource, only a differently-seeded fixture through
the existing management API).

---

## 2. The mechanism decisions, with the alternatives rejected

### 2.1 Where the figure comes from — reuse, do not re-instrument

`e2e/kiosk-shows-a-label-over-video.spec.ts:1137-1181` already listens on the
kiosk page's console for the product's `[latency]` object and accumulates
`latencyLines`, filtering `overlay_draw` at `:1329` to compute a p50 it uses for
its own error term. **`reportLegs` already prints the figure.** The work is to
stop throwing that away.

*Rejected:* a new `PerformanceObserver` or a CDP `Tracing`/`Performance.getMetrics`
instrument. Neither appears anywhere in this repository, both would measure a
different quantity than the one §IV budgets, and ADR-0123 decision 1 forbids
re-instrumenting this leg. A `long-animation-frame` observer in particular
reports main-thread render work *excluding* the frame wait — precisely the
reading ADR-0123 considered and rejected.

*Rejected:* parsing stdout out of `test-results/e2e-report.json`. It works today
and would break the moment a log line is reworded, and the scrubber rewrites that
file (#2287). A structured emission is the contract.

### 2.2 How the figure escapes the run

**A per-attempt JSON file written by the test into `test-results/`**, named with
the attempt index from `test.info().retry`, e.g.
`test-results/render-leg-attempt-0.json`.

Why a plain file rather than a Playwright attachment: attachments of any size
are materialised as files under `test-results/` anyway and referenced by path in
the JSON report, so the attachment route buys indirection and costs a dependency
on report-shape. A named file is read by the summariser and the checker with no
report parsing at all, and it rides the existing
`playwright-report-${{ matrix.shard }}-of-4` artifact upload (`ci.yml:733-741`)
without a new upload step.

Constraints this must satisfy, each a task:

- **The scrubber must pass it.** `scripts/scrub-playwright-artifacts.mjs` fails
  closed — it deletes what it cannot scrub, and the artifact upload is gated on
  `steps.scrub.outcome == 'success'`. The file carries only numbers, camera
  identifiers (GUIDs already present throughout these artifacts) and a SHA, so
  it should pass; **verified empirically, not assumed** (spec FR-006).
- **Retries must not overwrite.** `test.info().retry` is 0, 1, 2 — one file each
  (spec FR-004). This is the direct answer to #2077's "a retried e2e pass is
  absorbed": here every attempt is on the record.
- **Sharding.** The wall specs live in the `kiosk` project and land in exactly
  one of the four `--shard=N/4` slices. The summariser and checker therefore run
  over *whatever files exist*, and say `no figure in this shard` rather than
  failing, in three of four shards. The synthesis job (`e2e`, `if: always()`) is
  where a cross-shard verdict belongs if one is ever needed.

### 2.3 How the frame interval is measured

A short in-page `requestAnimationFrame` counting loop (≈1 s, frames counted,
`T = window / frames`), executed via `page.evaluate` on the kiosk page **before
the timed loop begins and again after it ends**, never during.

Two readings rather than one, because a single pre-run reading would not notice
the runner losing cadence mid-test — which is exactly the failure that would
make a figure unreadable. Both are recorded; the checker uses the worse.

*Rejected:* deriving `T` from `getVideoPlaybackQuality()` frame counts. That
measures the *video's* 25 fps cadence (`sim-loop.mp4` is 25 fps, GOP 25), not the
compositor's frame interval. They are different numbers and conflating them would
put the fixture's encode rate into a display-cadence term.

*Rejected:* assuming 60 Hz. ADR-0123's whole point is that this term is not
assumable — the figures in it came from a 27 Hz wall.

### 2.4 Where the gate lives — and why not in an `expect`

**A Node script (`scripts/render-leg-check.mjs`) invoked as its own `ci.yml`
step after the Playwright step, `if: always()`.**

This is deliberately the shape of `scripts/coverage-check.ps1` + `ci.yml:104`
(ADR-0065): a committed threshold, a script that reads it, a CI step that fails
the job. Reusing an established pattern rather than inventing a second kind of
gate.

Why not an `expect` inside the test, which would be the obvious place:
`playwright.config.ts:15` sets `retries: isCI ? 2 : 0`. A threshold inside the
test gets up to three rolls of the dice and passes if any one of them is under.
`e2e/click-to-first-frame.spec.ts:61-76` already writes this down — *"the retry
count hardens the red and biases the green"* — and discharges it by manual
repetition. A separate step reads all three attempts' files and is not re-run by
Playwright at all.

**The baseline is committed as data the checker reads**, not hardcoded in the
script, so US3's file is the single source of truth and a re-baseline is a
one-file change with a visible diff. Format and location: §5.

### 2.5 What the gate asserts

**`p50(overlay_draw)` of the first complete attempt, against the committed
baseline p50 + tolerance.** Not against §IV's 50 ms absolute — the spec's §2
explains why: on a shared SwiftShader runner the absolute figure is dominated by
a cadence term the code does not control, and #2072 has already measured
555/758 ms on an enclosed 200 ms leg on this runner class.

**This asserts no new definition of the leg**, which is what keeps US4 inside the
lane's authority. It is a regression check on the repository's own figure, on
this runner class — exactly what #2337 asks for and nothing more. The observed
`T` is **reported alongside for triage** (ADR-0123 consequence 3: cadence first,
compositing second) and is **not** part of the assertion. Subtracting `1.5·T`
to gate on a derived "work" term would be a new statement about what CI enforces
on a §IV leg, and no ADR covers that — see §7 R3.

---

## 3. What changes, file by file

### US1 — the number leaves the run

| File | Change |
|---|---|
| `e2e/kiosk-shows-a-label-over-video.spec.ts` | Add the cadence probe (two readings). Write `test-results/render-leg-attempt-<retry>.json` from the `overlay_draw` samples already collected at `:1329`. No assertion added. The `[legs]` stdout lines stay — two independent readings of one quantity is a feature (spec §6 step 3). |
| `e2e/support/render-leg.ts` *(new)* | The file's shape, the writer, and the reader, in one place so the test and the two scripts cannot drift. Mirrors `e2e/support/live-video-wall.ts`, which exists for the same reason (a handoff between processes). |
| `scripts/render-leg-summary.mjs` *(new)* | Reads the attempt files, writes the `$GITHUB_STEP_SUMMARY` block. Sibling of `scripts/summarise-e2e-retries.mjs`, not an extension of it — that script is contractually forbidden from rendering stdout and this one renders no stdout either. |
| `scripts/render-leg-summary.test.mjs` *(new)* | Node test, mirroring `scripts/summarise-e2e-retries.test.mjs`. Covers: no files, one file, three attempts, zero samples, malformed file. |
| `.github/workflows/ci.yml` | One step in `e2e-shards`, `if: always()`, after the Playwright step and **before** the scrub step. |

### US2 — a four-tile wall

| File | Change |
|---|---|
| `e2e/support/seed-live-video-wall.setup.ts` | Seed four cameras on `FIXTURE_VIDEO_RTSP_URL`, four overlays (or one overlay bound four times, whichever the API permits without new backend work), a 2×2 grid, four tiles. The dialog defaults to 1×1 (`LayoutEditorDialog.tsx:51`), so the grid controls must actually be driven. |
| `e2e/support/live-video-wall.ts` | The handoff record grows from one camera/overlay to four. |
| `e2e/kiosk-shows-a-label-over-video.spec.ts` | `expect(second.elements, 'the wall should carry exactly one tile').toBe(1)` at `:148` becomes four. The per-element decode assertions already iterate (`settled.perElement.forEach`), so they need no change. |
| Any other spec reading that handoff | Audited, not assumed. `wall-*.spec.ts` and `kiosk-*.spec.ts` are the candidates. |

**Not changed:** `seed-published-layout.setup.ts` and `seed-bound-overlay-wall.setup.ts`,
whose cameras (`rtsp://10.0.5.70`, `.71`) are dead by design — those walls exist
so a kiosk has *a* published layout, not to carry a picture. Widening them would
cost CI time for nothing.

### US3 — the baseline

| File | Change |
|---|---|
| `specs/225-the-render-leg-ci-never-reads/figures.md` *(new)* | See §5. |

### US4 — the gate

| File | Change |
|---|---|
| `specs/225-the-render-leg-ci-never-reads/baseline.json` *(new)* | The machine-readable half of `figures.md`: baseline p50, tolerance, the runner class it was taken on, and the SHA/run ids it derives from. Prose and data beside each other, and a task asserts they agree. |
| `scripts/render-leg-check.mjs` *(new)* | Reads `baseline.json` + the attempt files; exits non-zero on regression, on unmeasured, and on a missing/malformed baseline. |
| `scripts/render-leg-check.test.mjs` *(new)* | Node test: within tolerance, over tolerance, no attempts, attempt 0 incomplete and attempt 1 complete, baseline absent, baseline malformed. |
| `.github/workflows/ci.yml` | One further step in `e2e-shards`. |

---

## 4. Boundary rules and constitution alignment

- **§II (value objects)** — not engaged; no domain model is touched.
- **§III / no cross-context references** — not engaged; nothing under `src/`.
- **§IV** — the affected leg is named in the spec and in every PR body. **No leg
  changes state** (ADR-0128 Implementation Notes; §IV:197-200 warns in both
  directions).
- **§VI (Aspire is the composition root)** — no new runtime resource.
- **§VII** — **not discharged.** A CI check is not a dashboard. `Dashboard: no`
  stands for this leg and #1940 stays open. Stated explicitly because §IV:188-195
  records that every leg is now subject, and a spec that quietly implied
  otherwise would be the clerical-error failure §IV itself names.
- **§Testing** — this is **behaviour-changing** work (new reporting, new gate),
  so phase 4a is **RED**, not characterisation. Exception: US2's edit to
  `kiosk-shows-a-label-over-video.spec.ts:148` changes an assertion's expected
  value because the *fixture* changed. That is a fixture change, not a moved
  product behaviour; the decode assertions around it must pass unmodified, and
  if they do not, that is a finding (spec US2 scenario 4).
- **ADR-0103** — integration via the real Aspire stack, no Testcontainers. This
  reuses the existing `ci.yml` boot; it adds nothing.
- **ADR-0087** — every commit builds on its own. Four stories, four or more
  commits, each independently sound; US1's ci.yml step must not reference a
  script a later commit introduces.

---

## 5. Evidence artefacts this spec commits to producing

Following spec 144's `plan.md:218-234` heading, and spec 136's rule: a CI figure
goes **into the tree** — run id *and* figure — because artifact retention is 14
days (`ci.yml`) and a link is not evidence.

**`specs/225-the-render-leg-ci-never-reads/figures.md`**, in spec 144's format:

```markdown
## `develop` baseline (four-tile fixture, CI)

| Run id | SHA | Samples | p50 | p95 | max | Observed T | Raw |
|---|---|---|---|---|---|---|---|
| [<id>](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/<id>) | `<40-char sha>` | 40 | .. ms | .. ms | .. ms | .. ms | `[..]` ms |
```

Non-negotiable conventions inherited from 136/144, each a review point:

- **Three or more runs.** One is not a variance.
- **A local figure is labelled `local`.** A labelled local figure is evidence; an
  unlabelled one is not (spec 144 tasks.md:30-32).
- **Raw samples printed**, not only percentiles — so the variance can be
  re-derived by the reader rather than trusted.
- **Margins truncated, never rounded up**, so no figure in this tree claims more
  headroom than was measured (spec 136 verification.md:94-95).
- **A `## Verdict`** stating plainly whether a tolerance can be set, and what the
  smallest detectable regression is.
- **Refusals recorded**, never dropped (spec US3 scenario 5).
- **A one-tile figure recorded too**, if one is taken before US2 lands, labelled
  as such — it is the comparison that shows what a tile costs.

**`specs/225-the-render-leg-ci-never-reads/verification.md`** at phase 5, per
`.claude/commands/verify.md`: what was observed, the exact command, output
**quoted not characterised**, the leg and its figure, and an explicit *what this
does not verify* section.

---

## 6. Contention

Checked against the frontend redesign programme (#2329–#2336) and in-flight work.

| File | Risk | Verdict |
|---|---|---|
| `.github/workflows/ci.yml` | **Real.** Shared, frequently touched, and spec 218's 4-way sharding landed in it on 2026-09-22. #2376 is open on the e2e timeout. | Two steps added inside `e2e-shards`, no restructuring, no change to the shard matrix or to `pnpm test:e2e --shard=` (which `e2e-shard-coverage` text-parses and will fail the build over). Rebase before every push. |
| `apps/kiosk-web/src/styles/index.css`, token files | #2334's territory (motion tokens) | **No overlap.** This spec touches nothing under `apps/`. #2334 is not `agent:ready` (gated on #2329), so nothing is in flight there today. |
| `apps/shared/src/observability/kioskLatency.ts` | The instrument | **Read only.** FR-007. |
| `e2e/kiosk-shows-a-label-over-video.spec.ts` | Large, recently reworked (spec 108, spec 204) | Sole owner during this spec. Changes are additive except the tile-count assertion. |
| `scripts/summarise-e2e-retries.mjs`, `scripts/scrub-playwright-artifacts.mjs` | #2077, #2287 | **Not modified.** A sibling script is added. The scrubber's fail-closed behaviour is a constraint to satisfy, not to relax — relaxing it would be the lane's forbidden "weaken a gate". |
| `specs/225-*` | New directory | 224 is the highest landed on `origin/develop` and no remote or local branch carries a 225. Re-check after any parked PR merges — two unmerged branches can both claim a number. |

---

## 7. Risks

- **R1 — A1 is false and CI has no video.** ADR-0122:147-149 says the browser
  gets none; ADR-0138:121-132 corrects it for this path. If the correction does
  not hold, US1 has nothing to report. **Mitigation:** T001 confirms it
  empirically against a real run before any code is written, with a STOP branch.
  This is the single most load-bearing fact in the spec and it is itself a
  previously-corrected claim.
- **R2 — Too few samples for a p50 to mean anything.** Spec 040 got **one sample
  per tile** because `overlay_draw` fires on overlay *change* and static labels
  never redraw. The span test drives 10 changes, so a four-tile wall should give
  ~40 — but "should" is the word that produced spec 040's surprise.
  **Mitigation:** T001 counts them. If the count is too low, the fix is more
  iterations in the harness, never a p50 over three samples.
- **R3 — The variance swamps the signal and no honest tolerance exists.** The
  likeliest bad outcome, and the one §2 predicts: `1.5·T` dominates and `T` is a
  shared runner's. **Mitigation:** FR-014 makes this a designed outcome, not a
  failure — report-only, finding recorded, ADR flagged. **The lane may not write
  that ADR** (ADR-0144), so this outcome ends with a hand-back, not a merge of
  something invented to fill the gap.
- **R4 — The four-tile wall costs e2e time.** Four decodes instead of one on a
  shared runner, in a job whose timeout was raised to 45 min under #2376 and
  which currently takes 360-545s per shard. **Mitigation:** measured and
  recorded (US2 scenario 4). If it materially threatens the timeout, US2 is
  reconsidered before US3 takes a baseline against it — not after.
- **R5 — The scrubber deletes the artifact.** It fails closed and the upload is
  gated on it. **Mitigation:** an explicit task runs the scrubber over a real
  figure file and reads its exit code, before the ci.yml step is added.
- **R6 — The gate is only as strong as #2288.** `develop` has no required status
  checks, so a red check can be merged past. Out of scope and stated in the spec
  rather than silently assumed away; the PR body says so.
- **R7 — Inheriting the 250-tile error.** ADR-0146 cites #2337 *with* the wrong
  arithmetic, so the nearest prose to this work is wrong. **Mitigation:** FR-015
  and SC-005; a review point on every artefact, including commit messages.

---

## 8. Addendum, 2026-09-23: plan for US3 and US4 after US1 and US2 merged

This section reads alongside spec §9. It keeps every decision in §§1–7 unless
it names one and changes it.

### 8.1 Changed decision: the gate is a job, not a step in the shard (supersedes the gate half of §2.4 and the US4 row of §3)

§2.4 put `render-leg-check.mjs` in a step inside `e2e-shards`. It cannot go
there. The span test runs in exactly one shard, so the other three shards would
each have to guess whether "no record" means *not my shard* or *unmeasured*.
FR-012 forbids guessing. §2.2 already said a cross-shard verdict belongs to a
job that sees every shard.

The job looks like this:

```yaml
render-leg-gate:
  name: render leg gate (composite + render, section IV)
  runs-on: ubuntu-latest
  needs: [e2e-shards]
  if: always()
  timeout-minutes: 5
  steps:
    - checkout                     # baseline.json, the script, e2e/support/render-leg.ts
    - setup-node 22                # same pin and version as e2e-shard-coverage; no pnpm install
                                   # (render-leg-summary.mjs already imports the .ts reader with plain node)
    - actions/download-artifact    # pattern: playwright-report-*-of-4, one directory per shard.
                                   # Pin it by full SHA, as upload-artifact is pinned. It is new to this repo.
    - node scripts/render-leg-check.mjs <download dir> specs/225-the-render-leg-ci-never-reads/baseline.json
```

- **The job does not touch `e2e-shards`' matrix or its `pnpm test:e2e --shard=`
  line**, so `e2e-shard-coverage`'s text parse is unaffected.
- **The job is not added to the `e2e` synthesis job's `needs`.** Keeping the
  check separate is deliberate (spec FR-017): a regression and a broken suite
  have to read differently. #2288 (no required status checks) limits this job
  exactly as it limits every other check. The PR states that.
- **An artifact that is missing because the scrubber refused it is read as
  *unmeasured*.** The upload is gated on `steps.scrub.outcome == 'success'`. The
  gate never gets a way round the scrubber, because that would weaken it.

### 8.2 The record gains one field (US1 files, additive)

In `e2e/support/render-leg.ts`:

- `RenderLegRecord.complete: boolean`.
- One exported predicate:
  `isCompleteRenderLegMeasurement(samplesPerCamera: ReadonlyMap<string, number>, expectedCameras: number, iterations: number): boolean`.
  It returns true when there are exactly `expectedCameras` distinct cameras and
  each has at least `iterations` samples.

`kiosk-shows-a-label-over-video.spec.ts` builds `overlayDrawSamplesPerCamera`
**before** it writes the record. Today the map is built after the write, at
`:1469`, so the build moves up. The spec then calls the predicate to set
`complete`. The existing `expect`s stay: they give a per-camera message the
boolean cannot. Now the gate and the test share one definition of complete
(spec FR-016).

The reader, `readRenderLegRecords`, stays as it is. Deciding that a record is
malformed, including when it has no `complete` field, is the checker's job, as
§2.2's reader contract already says.

### 8.3 `baseline.json`: the schema the checker enforces

```json
{
  "measurement": "overlay_draw",
  "fixture": { "tiles": 4, "iterations": 10 },
  "runner": "ubuntu-latest, headless Chromium, software rasterisation",
  "baselineP50Milliseconds": 0,
  "toleranceMilliseconds": 0,
  "rule": "tolerance = 3 * sample stddev of first-complete-attempt p50 across the runs below (spec FR-018)",
  "runs": [ { "runId": "…", "sha": "<40 hex>", "attempt": 0, "p50Milliseconds": 0 } ]
}
```

The checker **refuses** the file, naming it (FR-013), in any of these cases:

- `runs.length < 5`.
- A `sha` is not 40 hex characters.
- The tolerance is not positive.
- `baselineP50Milliseconds` does not equal the mean of `runs[].p50Milliseconds`,
  to 0.01 ms.
- `toleranceMilliseconds` does not equal 3 × the sample standard deviation, to
  0.01 ms.

The last two checks make the file **self-verifying**. Nobody can hand-edit the
tolerance without the derivation failing. That covers T019's "prose and data
agree" at the data level. A separate test (T019b) checks that `figures.md`'s
table carries the same run ids and p50s.

### 8.4 The checker's decision table

The checker works on the union of the downloaded records.

| Records | Verdict | Exit |
|---|---|---|
| Baseline absent or malformed | `baseline refused: <path>: <reason>` | 1 |
| Zero records across all shards | `unmeasured: no render-leg record in any shard` | 1 |
| Any record unparseable, or with no `complete` field | `unmeasured: <file> malformed` | 1 |
| Records from more than one shard | `unmeasured: records from N shards, expected 1` (the span test ran twice, which is a harness fault) | 1 |
| No record with `complete: true` | `unmeasured: every attempt incomplete` (each one listed) | 1 |
| First complete attempt's p50 ≤ baseline + tolerance | `within tolerance`: baseline, observed, margin, both `T` readings, plus the skipped attempts | 0 |
| First complete attempt's p50 > baseline + tolerance | `regressed`: baseline, observed, tolerance, excess, both `T` readings, and ADR-0123's "cadence first, compositing second" | 1 |

"First" means the lowest `attempt` number. The checker prints every attempt's
completeness, and the p50 of the one attempt it evaluates — never a skipped
attempt's p50, which was never evaluated and would read as though it had been
(#2077) — so a verdict taken from attempt 2 says so on its face.

The comparison is `>`, not `≥`: a figure exactly at the threshold passes. The
tests pin that boundary.

### 8.5 The US3 procedure

1. **Wait for T026.** The harness has to be stable first (spec §9.2 F1).
2. Collect the `develop` **push** runs made after T026 merged, until five of
   them have a complete attempt. Download each run's `playwright-report-4-of-4`
   artifact **within 14 days**. Record every run, including runs whose attempts
   were all incomplete. Those are refusals (US3 scenario 5), and they count
   against stability. They are not quietly skipped.
3. Write `figures.md` in spec 144's layout. It has three parts:
   - the five-or-more baseline rows, with run id links, full SHAs, n, p50, p95,
     max, both `T` readings, and the raw samples;
   - spec §9.2 F3's preliminary rows, labelled **preliminary, pre-T026**;
   - the three one-tile rows, labelled **one-tile, pre-US2**, as the comparison
     that shows what three extra tiles cost.
4. Compute the mean and σ, and write out the arithmetic. Apply FR-019's
   feasibility test and write `## Verdict`.

If T026's fix works, most of these runs complete on attempt 0 anyway. If more
than one of the five needed a retry, stop and say so in the verdict. It means
the harness is still re-rolling itself.

### 8.6 Red for this remainder: what the engineer must observe failing

Constitution §Testing and ADR-0139 apply. Every item is new behaviour, so every
item starts red. There is no characterisation path.

| Task | The red, concretely |
|---|---|
| T026 (harness) | **Already observed in CI.** Quote runs 35894668870, 35907278215, 35918329596 and 35920524319 (spec §9.2 F1). Also reproduce locally once on unchanged code and quote that, so the fix is not judged only against CI history. |
| T027 (`complete` field) | A test on the predicate, plus a test that the written record carries `complete`. Both fail before the field exists. |
| T020 (checker) | `node --test scripts/render-leg-check.test.mjs` against synthetic record directories and baseline files in a temp dir, one case per row of §8.4 plus the `≤`/`>` boundary. Run before the script exists, then run against a stub that always exits 0. The stub run matters: it proves that the *regressed*, *unmeasured* and *refused* cases fail for the right reason. A missing module fails every case at once, and on its own that shows nothing. |
| T019b (agreement) | The agreement test fails before `baseline.json`/`figures.md` exist, and fails again against a deliberately mismatched p50. |
| T028 (summary wording) | The new summary test fails while the summary still says "no threshold is asserted". Line `:134`'s assertion is *replaced*, and the PR says so (spec FR-020). |
| T022 (end to end) | A throwaway PR adds `filter: blur(4px)` to the tile and **`render-leg-gate` goes red with `regressed`**. The output is quoted verbatim. After the revert, the check is green again. A gate nobody has seen fail has not been shown to gate anything. |

### 8.7 New risks

- **R8 — T026 finds a product defect, not a harness race.** Tile 1 saw zero
  label mutations in 60 s on `859426c8`, and the one-tile hypothesis does not
  explain that. **Mitigation:** T026 is a ⟨GATE⟩. If the defect is
  product-shaped, STOP: file it as its own issue and hand back. Do not harden
  the harness around it. That would be the lane's forbidden weakening of a gate,
  here the per-camera assertion.
- **R9 — The baseline collection window.** It needs five post-T026 `develop`
  pushes inside the 14-day artifact retention. At today's rate that is one day
  of merges. **Mitigation:** download each run's artifact as it lands, not at
  the end.
- **R10 — Selection bias from "first complete attempt".** A slow attempt that
  also failed completeness is skipped, and the checker reads a luckier retry.
  **Mitigation:** the checker's own output prints only the evaluated attempt's
  p50 (a skipped attempt's p50 was never evaluated, and printing it would
  misrepresent the verdict — plan §8.4/#2077); it is **the job summary, not the
  checker**, that shows every attempt's p50 side by side for triage (T028's
  job). T026 is meant to make incomplete attempts rare. A later issue can
  tighten the rule to "attempt 0 or unmeasured" once the record shows that
  attempt 0 is reliably complete. That is not done now, because it would
  redden `develop` on a known harness fault.
