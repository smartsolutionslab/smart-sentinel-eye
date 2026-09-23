# Tasks — Spec 225, the render leg no CI run ever reads

**Spec:** `specs/225-the-render-leg-ci-never-reads/spec.md`
**Plan:** `specs/225-the-render-leg-ci-never-reads/plan.md`
**Issue:** [#2337](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2337)
(on Project #13, status Todo — the phase-3 gate is already satisfied)
**Lane:** autonomous (ADR-0144)

**Progress (2026-09-23):** T001–T009 **done** (US1, PR #2549). T010–T014 **done**
(US2, PR #2550). **Open: T026 → T027 → T015–T022 → T028 → T023–T025**, on
branch `ci/2337-composite-render-regression-gate`. Spec §9 and plan §8 govern
them. The engineer is **infra-engineer**: the work is CI workflow and
measurement harness, with nothing under `src/` or `apps/`.

Format: `[TNNN] [P?] [Story] description`. `[P]` marks tasks owning disjoint
files that may run concurrently (ADR-0109).

---

## The two colours (constitution §Testing, ADR-0144 phase 4a)

| Task group | Colour | Why |
|---|---|---|
| US1, US3, US4 | **RED** | New behaviour — a figure that did not exist, a summary line that did not exist, a check that did not exist. Every test must be observed failing first and its output quoted in the PR. |
| US2's fixture widening | **RED** for the new four-tile assertions | A four-tile wall is new behaviour of the fixture. |
| US2's `:148` tile-count edit | **neither** | An expected value changes because the fixture changed. The decode assertions around it must pass **unmodified**; if one has to be edited, that is a finding and a STOP (spec US2 scenario 4), not an adjustment. |

| T026 (harness fix), T027, T019b, T020, T021, T028 | **RED** | Resumption tasks. Plan §8.6 says what each red looks like. T026's red is already on record in four CI runs, and is reproduced locally once more. |

Ambiguity resolves to red. Nothing here is behaviour-preserving, so no task in
this spec takes the characterisation path.

---

## US1 — The number, and the cadence that explains it, leave every run (P1)

*The whole shippable slice for "report the number on every run". Delivers value
alone, adds no assertion, and cannot redden a build.*

- **[T001] [US1] ⟨GATE⟩ Confirm the two premises before writing anything.**
  Boot the stack the way `ci.yml` does (`dotnet run --project src/AppHost/... -- ScenarioSimulator=false`,
  `E2ETests` unset) and run the kiosk wall suite. Record, as raw output:
  (a) whether `fixture-video` serves video and tiles decode — **A1**, contradicted
  by ADR-0122:147-149 and corrected by ADR-0138:121-132, so it is verified, not
  cited; (b) **how many `overlay_draw` samples one run produces** — **A2**, where
  spec 040 got one sample per tile. **STOP and hand back if (a) is false**: the
  spec becomes different, larger work and that is not a thing to route around.
  If (b) is under ~10, raise it before proceeding — a p50 over three samples is
  not a p50.
- **[T002] [US1] Read the three precedents rather than reinventing them.**
  `scripts/summarise-e2e-retries.mjs` (+ its `.test.mjs`) for the summary-writing
  idiom and its stdout prohibition; `scripts/scrub-playwright-artifacts.mjs` for
  what fails closed; `e2e/support/live-video-wall.ts` for the cross-process
  handoff shape. Note in the PR which idiom each new file mirrors.
- **[T003] [US1] `e2e/support/render-leg.ts` — the record's shape, written once.**
  The TypeScript type, the writer (keyed on `test.info().retry`) and the reader,
  in one file so the test and the two scripts cannot drift. Carries: samples
  (raw), count, p50, max, p95-or-null, `T` before and after, attempt, run id,
  SHA. `p95` is **null** below the sample count that supports one — the rule
  `percentiles()` already follows at `kiosk-shows-a-label-over-video.spec.ts:756-766`
  rather than printing the max twice.
- **[T004] [US1] The cadence probe.** A short in-page `rAF` counting loop via
  `page.evaluate`, run **before** the timed loop and **again after** it, never
  during (NFR-002). Two readings, both recorded. Not derived from
  `getVideoPlaybackQuality()` — that is the fixture's 25 fps, a different number
  (plan §2.3).
- **[T005] [US1] Emit the file from the span test.** In
  `e2e/kiosk-shows-a-label-over-video.spec.ts`, write the record from the
  `overlay_draw` samples already collected at `:1329`. **No assertion added**
  (NFR-001). The `[legs]` stdout lines stay — two independent readings of one
  quantity, and a disagreement between them is a finding.
- **[T006] [US1] Prove the scrubber passes it.** Run
  `node scripts/scrub-playwright-artifacts.mjs` over a `test-results/` containing
  a real figure file; read its exit code and confirm the file survives intact
  (FR-006). **Before** the ci.yml step exists, because the upload is gated on
  `steps.scrub.outcome == 'success'` and a failure here silently costs every
  Playwright artifact in the shard.
- **[T007] [P] [US1] `scripts/render-leg-summary.mjs` + `.test.mjs`.** Reads the
  attempt files, writes the `$GITHUB_STEP_SUMMARY` block: leg name, §IV budget,
  per-attempt sample count / p50 / max / `T`, and an explicit line saying **no
  threshold is asserted**. `no samples` never renders as `0 ms` (FR-003).
  Node test covers: no files, one file, three attempts, zero samples, malformed
  file. Owns `scripts/` only — disjoint from T003-T005's `e2e/`, so it may run
  concurrently with them once T003 has fixed the shape.
- **[T008] [US1] One `ci.yml` step in `e2e-shards`**, `if: always()`, after the
  Playwright step and **before** the scrub step. Must not touch the shard matrix
  or the `pnpm test:e2e --shard=` invocation — `e2e-shard-coverage` text-parses
  both and will fail the build over a reworded line.
- **[T009] [US1] ⟨GATE⟩ Observe it in a real CI run.** Open the run, read the
  summary, quote it. Three of four shards must print `no figure in this shard`
  and pass; one must print the figure. A summary nobody has read is not a
  delivered summary.

**US1 ships here. #2337's "report the number" is closed.**

---

## US2 — The wall under measurement is the domain's real ceiling (P2)

*Severable. If dropped, US1 still ships and US3's baseline is simply taken on a
one-tile wall — less sensitive, not wrong. Must land **before** US3 or the
baseline is taken against a fixture that then changes.*

- **[T010] [US2] Seed a 2×2 wall.** In
  `e2e/support/seed-live-video-wall.setup.ts`: four cameras on
  `FIXTURE_VIDEO_RTSP_URL`, a 2×2 grid, four tiles, overlays bound to all four.
  The editor dialog defaults to 1×1 (`LayoutEditorDialog.tsx:51`), so the grid
  controls are actually driven rather than left at their default. Four is the
  ceiling (`GridDimensions.MaxTiles`), never past it.
- **[T011] [US2] Widen the handoff.** `e2e/support/live-video-wall.ts` carries
  four cameras/overlays instead of one. Same file-based handoff — the seed
  project and the spec projects are different worker processes.
- **[T012] [US2] Update the assertions the fixture change invalidates, and audit
  for others.** `kiosk-shows-a-label-over-video.spec.ts:148` (`toBe(1)` → four).
  Then **grep every `e2e/` spec that reads the handoff** and check each — the
  wall and kiosk specs are the candidates. An assertion that must be *weakened*
  rather than *renumbered* is a finding, not an edit.
- **[T013] [US2] Measure what four decodes cost the shard.** Wall clock before
  and after, recorded (US2 scenario 4). `timeout-minutes: 45` with #2376 already
  open on it (NFR-003). If the cost materially threatens the timeout, raise it
  **before** US3 takes a baseline against this fixture.
- **[T014] [US2] Confirm four distinct cameras appear in the samples.** Not four
  tiles rendered — four camera identifiers in the harvested `overlay_draw`
  lines. A wall where three tiles never redraw would pass a tile count and
  produce a one-tile figure.

---

## US2.1: Prerequisite to US3, a span test that does not re-roll itself

*Added at the 2026-09-23 resumption (spec §9.2 F1). `develop@859426c8` is red on
this test now. It has to be stable before any figure is taken.*

- **[T026] [US2] ⟨GATE⟩ Diagnose, then fix, the four-tile span-test
  instability.** **RED is already on record:** CI runs 35894668870,
  35907278215, 35918329596 and 35920524319, with their messages in spec §9.2
  F1. Reproduce the failure once locally, on unchanged code, and quote it.
  Then establish the cause **with evidence**. The leading hypothesis is that
  `armOverlayPaint` and `awaitOverlayPaint` (spec file `:616-687`) watch only
  `document.querySelector`'s first label, so an iteration moves on before
  tiles 2–4 have drawn.
  - **Harness cause:** make every iteration wait for the value on **every**
    tile's label before the next submit. Leave the per-camera `≥ ITERATIONS`
    assertion exactly as it is. The fix is to the waiting, never to the
    assertion. After the fix, the span test passes on attempt 0 in **at least
    3 consecutive local runs**, and those runs are quoted.
  - **Product-shaped or runner-saturation cause** (for example tile 1 seeing
    zero label mutations in 60 s): **STOP.** File the defect as its own issue
    and hand back. Do not weaken or re-tune the assertion (ADR-0144).
  - Owns `e2e/kiosk-shows-a-label-over-video.spec.ts` only.
- **[T027] [US4] The `complete` field (spec FR-016, plan §8.2).** Add
  `isCompleteRenderLegMeasurement(...)` and `RenderLegRecord.complete` in
  `e2e/support/render-leg.ts`. In the spec file, build the per-camera map
  before the record is written and set `complete` from the predicate. The
  existing `expect`s stay. **RED first:** the predicate's unit test, plus a
  check that the written record carries the field. Both are quoted failing.
  Update `render-leg-summary.mjs` to render incomplete attempts as
  `incomplete`, with its own test. After T026, because both touch the same
  spec file.

---

## US3 — A baseline in the tree, with its provenance and its variance (P3)

*Depends on US1 (the figure must be machine-readable), on US2 (the fixture must
be final), and on **T026 + T027** (the harness must be stable, and records must
carry `complete`).*

- **[T015] [US3] Collect at least five post-T027 `develop` push runs**
  (spec FR-018, plan §8.5). Three is not enough: a σ from three points is too
  loose to set 3σ, and this repo's standing lesson is that the first run after
  machine churn looks exactly like a regression. Download each run's
  `playwright-report-4-of-4` artifact **as the run lands**, because retention
  is 14 days. Record every run, including runs whose attempts were all
  incomplete. Those are refusals and are not dropped. If more than one of the
  five needed a retry to reach a complete attempt, STOP: T026 has not held.
- **[T016] [US3] Write `specs/225-*/figures.md`** in spec 144's format: run id
  linked, full SHA, sample count, p50, p95-or-null, max, observed `T`, **raw
  samples**. A local figure is labelled `local` — unlabelled, it is not evidence.
  Margins truncated, never rounded up. Any run that produced no samples is
  recorded as a refusal with its reason, never dropped. Also include spec §9.2
  F3's five rows, labelled **preliminary, pre-T026**, and the three one-tile
  rows (runs 35873924190, 35878409081, 35883494479), labelled **one-tile,
  pre-US2**.
- **[T017] [US3] Compute the variance and write the `## Verdict`.** Show it,
  don't describe it: a reader must be able to re-derive the mean, σ and 3σ from
  the rows in the same file. State the **smallest regression detectable above
  this variance**. Report `T` per run beside the figure. **Do not** normalise the
  figure by `T`: spec §9.2 F2 shows that on this runner render cost *is*
  cadence. Compare against spec §9.4's prediction (3σ ≈ 14.8 ms) and state any
  difference.

---

## US4 — A regression fails the run (P4)

*Closes #2337. The only story that can make CI worse if it is wrong.*

- **[T018] [US4] ⟨GATE⟩ The FR-014 decision, taken against T017's number, before
  any threshold is written.** It is now mechanical (spec FR-019): the gate
  ships only if **3σ < 25 ms**, which is 1.5 × the 16.67 ms vsync quantum. If
  it fails that test, **do not ship a threshold**: record the finding in `figures.md`, ship T020-T022 report-only,
  and hand back with the note that an ADR is needed on what CI may enforce on
  this leg. **The lane may not write that ADR** (ADR-0144). A number picked to
  look like a gate is worse than no gate.
- **[T019] [US4] `specs/225-*/baseline.json`** in plan §8.3's schema. The
  checker re-derives the mean and 3σ from `runs[]` and refuses the file if they
  disagree, so the file checks itself.
- **[T019b] [P] [US4] `scripts/render-leg-baseline-agreement.test.mjs`.** It
  asserts that `figures.md`'s baseline table and `baseline.json` carry the same
  run ids, SHAs and p50s, because prose and data drifting apart is how §II and
  §IV both drifted. **RED:** it fails before the files exist, and again against
  one deliberately mismatched p50. Owns only this file.
- **[T020] [P] [US4] `scripts/render-leg-check.mjs` + `.test.mjs`**, implementing
  plan §8.4's decision table over a *directory of shard directories*. One test
  case per row of the table, plus the `≤` / `>` boundary, all against
  synthetic records and baselines in a temp dir. **RED, twice** (plan §8.6):
  first before the script exists, then against a stub that always exits 0.
  The stub run proves the *regressed*, *unmeasured* and *refused* cases each
  fail for their own reason. The script **never** falls back to a default
  threshold. Owns `scripts/render-leg-check*` only, so it can run in parallel
  with T019b once T027 has fixed the record's shape. It does not depend on
  T015–T019: the tests use synthetic baselines.
- **[T021] [US4] The `render-leg-gate` job in `ci.yml`** (plan §8.1):
  `needs: [e2e-shards]`, `if: always()`, `actions/download-artifact` pinned by
  full SHA (this is its first use in the repo), and no change to the
  `e2e-shards` matrix or its `--shard=` line. Put the commit **after** T019 and
  T020, so the step never names a script or a baseline an earlier commit lacks
  (ADR-0087).
- **[T028] [US4] The summary stops claiming "no threshold is asserted"**
  (spec FR-020). `render-leg-summary.mjs` prints the baseline, the tolerance,
  the threshold, and the `render-leg-gate` check name. **RED:** the new test
  fails while the old wording stands. The assertion at
  `render-leg-summary.test.mjs:134` is *replaced*, because the required
  behaviour changed on purpose. It is not weakened, and the PR body says so.
- **[T022] [US4] ⟨GATE⟩ Prove it by counterfactual — the load-bearing task.**
  On a throwaway PR (closed unmerged, like #2548), add a deliberate render cost
  (`filter: blur(4px)` on the tile), run CI, **observe the `render-leg-gate`
  job red with `regressed`**, capture the message verbatim. If blur(4px) does
  not trip it, escalate the cost and record every step tried: the gate's
  detectable size is a finding, not a failure. Revert; confirm `git diff apps/`
  is empty; re-run; observe green. Quote both outputs in the PR. A guard that has
  never been seen failing has not been shown to catch anything, and this repo has
  disproved three guards' own claims this way.

---

## Wrap-up

- **[T023] Self-review against the FR/NFR table.** Every FR-001…FR-015 and
  NFR-001…NFR-004 named with where it is satisfied, or explicitly deferred.
  Particular attention to **FR-015 / SC-005**: no artefact of this spec — commit
  messages included — repeats the 250-tile premise (#2363). The nearest prose to
  this work, ADR-0146, is wrong about it.
- **[T024] Confirm §IV is byte-identical.** `git diff .specify/memory/constitution.md`
  must be empty (SC-006). `LatencyLegRecordTests` guards the table; this spec
  moves nothing in it, and §VII's dashboard obligation stays undischarged (#1940).
- **[T025] Housekeeping.** Commits per ADR-0030 (Conventional Commits, **no
  `Co-Authored-By` footer** — ADR-0086). PR to `develop` with `--base develop`
  (ADR-0028), the phase-4a red output quoted (ADR-0139), and the #2288 caveat
  stated: `develop` has no required status checks, so this gate's force is
  bounded by a repository setting this spec does not change. Closing keyword for
  #2337, and verify the issue state after the merge.

---

## Parallelism, stated once

**This spec is mostly a chain, and pretending otherwise would cost more than it
saves.** The tolerance cannot be chosen before the variance is measured; the
variance cannot be measured before the figure is machine-readable; the figure is
not worth baselining until the fixture is final. US1 → US2 → US3 → US4 is a real
dependency, not a preference.

The one genuine `[P]`: **T007** owns `scripts/` while **T004-T005** own `e2e/`.
Once T003 has fixed the record's shape, those are disjoint files and may run
concurrently.

Everything else should be dispatched as one sequential slice to one agent. Four
stories means four or more commits, each of which must build on its own
(ADR-0087) — in particular T008's `ci.yml` step must not reference a script a
later commit introduces.

---

## Dependency graph

```
T001 ⟨GATE: A1 false → STOP⟩
  └─ T002
       └─ T003 ────┬──────────────► T007 [P]  (scripts/)
                   └─ T004 ─ T005 ─ T006      (e2e/)
                                      └─ T008 ─ T009 ⟨GATE⟩
                                           ══ US1 shippable ══>
                                                └─ T010 ─ T011 ─ T012 ─ T013 ─ T014
                                                     ══ US2 shippable ══>
                                                          └─ T015 ─ T016 ─ T017
                                                               ══ US3 shippable ══>
                                                                    └─ T018 ⟨GATE: FR-014⟩
                                                                         └─ T019 ─ T020 ─ T021 ─ T022 ⟨GATE⟩
                                                                              ══ US4 / #2337 closed ══>
                                                                                   └─ T023 ─ T024 ─ T025
```

### Revised graph for the resumption (2026-09-23), which supersedes the tail above

```
T026 ⟨GATE: harness vs product; product → STOP, separate issue⟩     e2e/kiosk-shows-…spec.ts
  └─ T027  (complete field + summary renders "incomplete")          e2e/support/render-leg.ts, spec file, render-leg-summary*
       ├─ T020 [P]  render-leg-check.mjs + test (synthetic inputs)  scripts/render-leg-check*
       ├─ T019b [P] agreement test                                  scripts/render-leg-baseline-agreement.test.mjs
       └─ merge T026+T027 to develop  ══ PR A shippable (develop green again) ══>
            └─ T015 (≥5 develop runs, wall-clock bound) ─ T016 ─ T017
                 └─ T018 ⟨GATE: 3σ < 25 ms?  no → report-only + ADR hand-back⟩
                      └─ T019 ─ (T020, T019b done) ─ T021 ─ T028
                           └─ T022 ⟨GATE: blur counterfactual observed red⟩
                                └─ T023 ─ T024 ─ T025   ══ PR B, closes #2337 ══>
```

**Two PRs, not one.** PR A (T026 and T027, plus T020 and T019b if they are
ready) fixes a red `develop`. It must not wait for a baseline that needs five
more `develop` runs to exist. PR B (T015–T019, T021, T022, T028) closes #2337.
The one genuine wait in the chain is T015, which needs real CI runs to
accumulate on `develop`. T020 and T019b are built against synthetic inputs, so
they do not wait on it.

---

## Phase-3 gate checklist

- [x] Tasks atomic, each naming the file it owns.
- [x] Grouped by user story, P1 first, each story independently shippable.
- [x] `[P]` markers on disjoint-file tasks only; the sequential majority stated
      rather than disguised.
- [x] Phase-4a colour declared per group; ambiguity resolved to red.
- [x] The feature's issue (#2337) is on Project #13 — **verified with
      `--limit 2000`**, status Todo. `/speckit-tasks` adds nothing to the board;
      no per-task issues are created (the repo stopped after spec 028).
- [x] Three explicit ⟨GATE⟩ stops: T001 (premise), T018 (FR-014, may need an ADR
      the lane cannot write), T022 (counterfactual).
