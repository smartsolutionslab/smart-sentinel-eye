# Tasks — Spec 225, the render leg no CI run ever reads

**Spec:** `specs/225-the-render-leg-ci-never-reads/spec.md`
**Plan:** `specs/225-the-render-leg-ci-never-reads/plan.md`
**Issue:** [#2337](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2337)
(on Project #13, status Todo — the phase-3 gate is already satisfied)
**Lane:** autonomous (ADR-0144)

Format: `[TNNN] [P?] [Story] description`. `[P]` marks tasks owning disjoint
files that may run concurrently (ADR-0109).

---

## The two colours (constitution §Testing, ADR-0144 phase 4a)

| Task group | Colour | Why |
|---|---|---|
| US1, US3, US4 | **RED** | New behaviour — a figure that did not exist, a summary line that did not exist, a check that did not exist. Every test must be observed failing first and its output quoted in the PR. |
| US2's fixture widening | **RED** for the new four-tile assertions | A four-tile wall is new behaviour of the fixture. |
| US2's `:148` tile-count edit | **neither** | An expected value changes because the fixture changed. The decode assertions around it must pass **unmodified**; if one has to be edited, that is a finding and a STOP (spec US2 scenario 4), not an adjustment. |

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

## US3 — A baseline in the tree, with its provenance and its variance (P3)

*Depends on US1 (the figure must be machine-readable) and on US2 (the fixture
must be final).*

- **[T015] [US3] Take at least three `develop` runs.** Not one. This repo's
  standing lesson is that the first run after machine churn looks exactly like a
  regression. Capture run id, full 40-character SHA, and the emitted file from
  each.
- **[T016] [US3] Write `specs/225-*/figures.md`** in spec 144's format: run id
  linked, full SHA, sample count, p50, p95-or-null, max, observed `T`, **raw
  samples**. A local figure is labelled `local` — unlabelled, it is not evidence.
  Margins truncated, never rounded up. Any run that produced no samples is
  recorded as a refusal with its reason, never dropped.
- **[T017] [US3] Compute the variance and write the `## Verdict`.** Shown, not
  characterised — a reader must be able to re-derive it from the raw samples in
  the same file. State the **smallest regression detectable above this
  variance**, and separate cadence variation from render-cost variation using
  the recorded `T` rather than reporting one blended number (ADR-0123
  consequence 3).

---

## US4 — A regression fails the run (P4)

*Closes #2337. The only story that can make CI worse if it is wrong.*

- **[T018] [US4] ⟨GATE⟩ The FR-014 decision, taken against T017's number, before
  any threshold is written.** If the measured variance exceeds the smallest
  regression this gate could plausibly need to catch, **do not ship a
  threshold**: record the finding in `figures.md`, ship T020-T022 report-only,
  and hand back with the note that an ADR is needed on what CI may enforce on
  this leg. **The lane may not write that ADR** (ADR-0144). A number picked to
  look like a gate is worse than no gate.
- **[T019] [US4] `specs/225-*/baseline.json`.** Baseline p50, tolerance, runner
  class, and the run ids and SHAs it derives from. The tolerance's derivation
  from T017's variance is shown in `figures.md`; a task asserts the two files
  agree, because prose and data drifting apart is how §II and §IV both drifted.
- **[T020] [US4] `scripts/render-leg-check.mjs` + `.test.mjs`.** Reads
  `baseline.json` and the attempt files. Evaluates the **first attempt that
  produced a complete measurement**; reports every attempt (FR-011). Exits
  non-zero on: over tolerance (worded **regressed**), no figure (worded
  **unmeasured** — a distinct word, FR-012), baseline missing or malformed
  (naming the file, never a permissive default, FR-013). The red message names
  baseline, observed, tolerance, both `T`s, and ADR-0123's triage order.
  Node test covers all six cases.
- **[T021] [US4] One further `ci.yml` step**, same constraints as T008.
- **[T022] [US4] ⟨GATE⟩ Prove it by counterfactual — the load-bearing task.**
  Add a deliberate render cost (`filter: blur(4px)` on the tile), run, **observe
  the gate red**, capture the message verbatim. Revert; confirm `git diff apps/`
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
