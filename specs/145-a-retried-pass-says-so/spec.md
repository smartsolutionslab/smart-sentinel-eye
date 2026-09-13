# Spec 145 — A retried pass says so

**Issue:** #2077 (option 1 only) · **Branch:** `chore/2077-surface-retried-test-signal`
**Phase:** 1 (Specify) · **Date:** 2026-09-13 · **Tree read at:** `3fe61674`
**ADRs:** ADR-0037 (the phased workflow, and the skip rule this spec declined),
ADR-0108 (Playwright against a live `aspire run` stack — the suite whose report
this reads), ADR-0109 (parallel work owns disjoint files), ADR-0117 (an
implemented leg is subject to §VII), ADR-0139 + constitution §Testing (new
behaviour starts red), ADR-0144 (the lane may not weaken a gate and may not
write an ADR).
**Constitution:** §IV (the latency budget — indirectly; see *Latency-budget
impact*), §VII (observability), §Testing.

---

## What this is, and what it is not

**This is option 1 of #2077 and nothing else.** The issue names three possible
decisions and asks for one of them:

1. **Surface retried tests as a signal** rather than removing retries — "the
   option that costs nothing in stability". **In scope.**
2. Uniform `expect.timeout` across local and CI. **Out of scope, and left
   open.** It is a policy trade-off about what "too slow" means, not an
   observability gap.
3. Leave it and record why. **Out of scope, and left open.** It is a decision
   not to act, and taking it would foreclose 1 and 2 together.

Options 2 and 3 remain a human's call. Nothing in this spec, plan or task list
changes `retries`, changes `expect.timeout`, or expresses a preference between
2 and 3. **No value in `playwright.config.ts` that decides whether a test
passes is touched.** ADR-0144 forbids the lane from weakening a gate to reach
green; it equally forbids it from *tightening* one on its own judgement, and
both `retries` and `expect.timeout` are gate values.

What lands is **a report**, not a rule. The suite's pass/fail outcome, the
job's exit code, and the merge gate are byte-for-byte what they are today.

## The asymmetry, re-verified against `3fe61674`

`playwright.config.ts` — the three lines the issue describes, confirmed
present and unmodified:

- `:13` — `expect: { timeout: isCI ? 30_000 : 15_000 }`
- `:15` — `retries: isCI ? 2 : 0`
- `:17` — `reporter: isCI ? [['list'], ['html', { open: 'never' }]] : [['list']]`

So in CI a test may fail twice and pass on the third attempt, and **the run
summary says exactly what it says for a test that passed first time**. The
`list` reporter does print a `flaky` line into the log, and the `html` report
records it — but the log is thousands of lines and the HTML report is a
download. Nothing puts a number where a person reading the run sees it.

`.github/workflows/ci.yml` — the `e2e` job (`:190`) runs `pnpm test:e2e`
(`:258`) and uploads `playwright-report` + `test-results` as an artifact
(`:278`). **There is no `$GITHUB_STEP_SUMMARY` write anywhere in the
repository** — `grep -rn GITHUB_STEP_SUMMARY .github scripts` returns nothing.
This spec's mechanism is the first.

## Why this is worth doing, in one already-documented case

`e2e/click-to-first-frame.spec.ts:65-76` has written the consequence down
already, about the click-to-first-frame budget:

> retries fire only on failure, so the *pass* criterion is
> `min(p95 over up to 3 runs) < 3000`: a p95 that breaches on two runs out of
> three is reported flaky and the job exits 0. The retry count hardens the red
> and biases the green, which is the opposite of the rule.

That is not a hypothetical class of defect. It is a **latency-budget
measurement whose breach CI currently reports as green**, in a suite whose
whole point is §IV. A flaky `click-to-first-frame` means two of three samples
breached 3000 ms. Today that fact exists only in a log line nobody reads.
After this spec it is a number on the job summary page.

This is the same defect shape the repository has had to correct repeatedly:
a record that nobody checked against what was actually happening. §IV said a
leg was unbuilt after it was built; CLAUDE.md described a Phase 3 gate sixteen
specs ignored. Here, a green says "the suite passed" when what happened was
"the suite passed on the third try".

## User stories

### US1 (P1) — a person reading a CI run sees how many tests needed a retry

**As** someone reading the `e2e (Playwright, full stack)` job on a CI run,
**I want** the job summary to state how many tests passed only on retry, and
which ones, **so that** an absorption that today looks identical to a clean
green becomes a number I can watch across runs.

**Independently shippable.** One job, one summary section, no dependency on any
other work. The suite, its gate and its exit code are unchanged, so shipping it
cannot break a build in a way the build would not already have broken.

This is the only story. There is no P2.

## Functional requirements

- **FR-001 — a machine-readable report exists.** The CI reporter list gains a
  `json` entry writing to a fixed path. The `list` and `html` reporters stay
  exactly as they are. **Local runs are unchanged** — the non-CI branch of
  `reporter` is not touched, and with `retries: 0` locally there is nothing for
  it to report.
- **FR-002 — a summariser turns that report into Markdown.** A Node script
  under `scripts/` reads the JSON report and emits a Markdown section. It
  writes to `$GITHUB_STEP_SUMMARY` when that variable is set and to stdout
  otherwise, so it is runnable and testable off a runner.
- **FR-003 — the section is always rendered, including when the count is
  zero.** A summary that appears only when something is wrong cannot be told
  apart from a summary mechanism that has quietly broken. `0 tests passed only
  on retry` is the load-bearing case: it is the evidence the mechanism ran.
- **FR-004 — the detail names the tests.** For each test that passed only on
  retry: the spec file and title, the Playwright project, the number of
  attempts, and the status of each attempt in order (e.g. `timedOut → passed`).
  An attempt whose status is absent renders as `unknown` rather than being
  dropped — `JSONReportTestResult.status` is typed `TestStatus | undefined`.
- **FR-005 — the totals are derived from the report's own tree, and
  cross-checked.** Counts come from walking `suites[].specs[].tests[]`
  (recursing through nested `suites`). `stats.flaky` is compared against the
  walk; **if they disagree, both figures are printed and the section says they
  disagree** rather than silently preferring one. A Playwright upgrade that
  changes the shape then announces itself instead of reporting zero.
- **FR-006 — a missing or unreadable report is stated, not swallowed.** If the
  JSON file is absent (a cancelled or timed-out job never finishes writing one
  — run 33623647778 is the precedent recorded at `ci.yml:261`) or does not
  parse, the section says so, naming the path. It does not print a zero.
- **FR-007 — the summariser never changes the job's outcome.** It exits `0` in
  every case above, including a missing report, and its CI step runs with
  `if: always()` so a red e2e run still gets its retry section. **A non-zero
  flaky count does not fail the job** — that would be deciding option 2, which
  this spec does not do.
- **FR-008 — the mechanism is visible in the repository, not only on a
  runner.** The reporter entry and the CI step are asserted by a guard test, in
  the manner `scripts/lint-scope.test.mjs` already establishes: "a config that
  nothing invokes is the same gap wearing a different hat."

## Acceptance scenarios

Scenarios below are over the summariser, which is a pure function of a JSON
report file plus an environment variable. `SUMMARY` denotes the file named by
`$GITHUB_STEP_SUMMARY`.

### Happy — tests that passed only on retry are counted and named

```gherkin
Given a Playwright JSON report in which two tests have status "flaky"
  And the first has attempts [timedOut, passed] in project "chromium"
  And the second has attempts [failed, failed, passed] in project "kiosk"
 When the summariser runs with GITHUB_STEP_SUMMARY set
 Then SUMMARY contains "2 tests passed only on retry"
  And SUMMARY names both spec files and both test titles
  And SUMMARY renders "timedOut → passed" for the first
  And SUMMARY renders "failed → failed → passed" for the second
  And the process exits 0
```

### Happy — a clean run still renders the section

```gherkin
Given a Playwright JSON report in which every test has status "expected"
 When the summariser runs
 Then SUMMARY contains "0 tests passed only on retry"
  And SUMMARY still renders the totals table
  And the process exits 0
```

### Happy — nested suites are walked

```gherkin
Given a report whose flaky test sits in a suite nested inside another suite
 When the summariser runs
 Then that test is counted and named
```

### Conflict — the walk and stats.flaky disagree

```gherkin
Given a report whose stats.flaky is 3 but whose tree contains 1 flaky test
 When the summariser runs
 Then SUMMARY reports 1 from the tree and 3 from stats
  And SUMMARY says the two figures disagree
  And the process exits 0
```

### Bad request — the report is missing

```gherkin
Given no file at the configured report path
 When the summariser runs
 Then SUMMARY says no Playwright JSON report was found, naming the path
  And SUMMARY does not claim a count of zero retried tests
  And the process exits 0
```

### Bad request — the report is not valid JSON

```gherkin
Given a report file containing truncated JSON
 When the summariser runs
 Then SUMMARY says the report could not be parsed, naming the path
  And the process exits 0
```

### Auth — not applicable, and why

This mechanism reads a file produced in the same job and writes a file the
runner owns. It crosses no trust boundary, reads no user input, holds no
credential and reaches no network. There is no authenticated path to test.
**The one thing that could leak is test output**: the section renders test
titles and attempt statuses only — never `stdout`, `stderr`, error messages or
attachment bodies from the report, any of which can carry a token minted by the
e2e stack. FR-004 is exhaustive by design.

### The gate is untouched, asserted

```gherkin
Given the repository at this branch's tip
 When playwright.config.ts is read
 Then retries is still `isCI ? 2 : 0`
  And expect.timeout is still `isCI ? 30_000 : 15_000`
```

This scenario is an assertion in the guard test, not prose. It is how a later
reader confirms the spec did what it said.

## Independent end-to-end test procedure

Phases 4a/4b are exercised by `pnpm test:guards`. **The end-to-end observation
is a real CI run**, because the thing being built is a CI report and no local
run produces a GitHub job summary.

1. Push the branch and open the PR. The `e2e` job runs on every pull request
   (`ci.yml`'s `pull_request` trigger is deliberately unfiltered).
2. Open the run's summary page and read the `e2e (Playwright, full stack)`
   job. **The retry section must be present.** A green suite will most likely
   show `0 tests passed only on retry` — that is the expected reading and is
   itself the proof the step ran.
3. Download the `playwright-report` artifact and confirm the JSON report is in
   it, at the path the config names.
4. **Counterfactual, because a zero proves the mechanism ran but not that it
   counts.** Locally: `node scripts/<summariser>.mjs <fixture>` against the
   two-flaky fixture from the first acceptance scenario, with
   `GITHUB_STEP_SUMMARY` pointed at a temp file, and read the rendered
   Markdown. This is the discharge of "prove a guard by counterfactual" — the
   fixture constructs exactly what CI is claimed to catch.
5. Quote both readings — the real job summary and the counterfactual render —
   in the verification note.

## Locked tech choices (nothing new is introduced)

| Concern | Choice | Why it is not a new decision |
|---|---|---|
| e2e runner | Playwright against a live `aspire run` stack | ADR-0108, unchanged |
| Report format | Playwright's built-in `json` reporter | Ships with `@playwright/test@1.62.1`, already a dependency; no new package |
| Summariser runtime | Node 22 ESM script under `scripts/` | `scripts/` already holds `wait-for-e2e-stack.sh` and three `*.test.mjs` guards |
| Its tests | `node --test` via `pnpm test:guards` | Already wired into `pnpm test`, which CI's `frontend` job runs at `ci.yml:126` |
| Surface | `$GITHUB_STEP_SUMMARY` | A GitHub Actions primitive; no external system, no new service, no new secret |

**No new dependency is added to `package.json`.** If the implementation finds
itself wanting one, that is a signal the design went wrong, not a licence.

## Latency-budget impact (constitution §IV)

**No leg is affected. Nothing on the event-to-overlay path changes** — this is
CI reporting, and no bytes of application or frontend code are touched.

It is not `N/A` in the usual sense, though, and the distinction is worth
recording. `e2e/click-to-first-frame.spec.ts` and
`e2e/kiosk-latency-contract.spec.ts` are §IV *measurements*, and §VII binds
implemented legs (ADR-0117) — every leg is now implemented, so every leg is now
subject. This spec does not change what those tests measure or the budgets they
assert. It changes whether a run can tell you that one of them **needed three
attempts to agree with its budget**. That is an improvement in the
observability of §IV's discharge, not a change to §IV.

## The ADR question — answered: no ADR needed

Every spec must cite at least one ADR (this one cites six) and must flag a
decision no ADR covers. This change introduces no architecture:

- ADR-0108 already establishes Playwright as the e2e runner and describes its
  CI job. Adding a reporter to an existing reporter list and a summary step to
  an existing job is that ADR being operated, not amended.
- The `scripts/*.test.mjs` guard pattern already exists three times over.
- `$GITHUB_STEP_SUMMARY` is a feature of the CI provider already in use.

**One decision is flagged as stopped rather than taken.** Options 2 and 3 of
#2077 — a uniform `expect.timeout`, or a recorded decision to leave the
asymmetry — *are* the kind of thing that would want an ADR or at least a human
judgement, and ADR-0144 forbids this lane from making either. They stay open on
#2077, which therefore must **not** be auto-closed by this PR.

## Out of scope

- Changing `retries` or `expect.timeout` in any environment.
- Failing the job, or any gate, on a non-zero retry count.
- A cross-run trend, a stored history, a dashboard, or a flaky-test quarantine.
  The issue asks for "a number someone can watch"; watching across runs is a
  person reading successive job summaries. No storage is built for a need that
  does not yet exist.
- The `integration` and `backend` jobs. xUnit has no retry mechanism here, so
  there is no equivalent absorption to surface.
- Uploading the JSON report anywhere beyond the artifact the job already
  uploads.
- Closing #2077. It survives this PR carrying options 2 and 3.

## Assumptions, marked

- **A1 — the `json` reporter's `outputFile` written under `test-results/`
  survives the run.** Playwright cleans `outputDir` when a run *starts*; the
  reporter writes in `onEnd`. Believed safe and chosen because `test-results/`
  is already gitignored (`.gitignore:70`) and already inside the artifact
  upload path (`ci.yml:283`). **If phase 5 finds the file absent from the
  artifact, the fallback is a repository-root path plus one `.gitignore` line
  and one upload path entry** — recorded here so the fallback is a decision
  already made rather than an improvisation.
- **A2 — `stats.flaky` and a tree walk agree today.** FR-005 exists precisely
  because this is an assumption; it is asserted rather than trusted.
- **A3 — files under `scripts/` are outside the ESLint and Prettier scopes.**
  `lint:e2e` covers `e2e` and `playwright.config.ts`; `format:check` covers
  `{apps,e2e}/**` plus `playwright.config.ts`. The three existing
  `scripts/*.test.mjs` files live outside both. The new files follow that
  precedent; **widening the lint or format scope is not part of this spec** and
  would be a separate change with its own blast radius.

## Gate — phase 1

Spec reviewed; no `[NEEDS CLARIFICATION]` remains. Two questions were resolved
inside the spec rather than left open, and both are recorded above as decisions
with reasons: *what threshold is worth surfacing* (FR-003 — none; always
render, zero included) and *whether a retried pass should fail the job*
(FR-007 — no; that is option 2, which stays open).
