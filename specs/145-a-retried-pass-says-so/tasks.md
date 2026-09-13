# Spec 145 — Tasks

**Phase:** 3 (Tasks) · **Date:** 2026-09-13 · **Issue:** #2077
**Board:** #2077 is already an item on Project #13 (`Smart Sentinel Eye`),
labelled `tech-debt`, `ci`, `agent:ready`. **The phase-3 gate is satisfied
without adding anything** — verified by
`gh issue view 2077 --json projectItems`. No per-task issues (CLAUDE.md: the
repo stopped creating those after spec 028).

**Implementer:** `infra-engineer` throughout.
**Phase 4a colour: RED.** This is new behaviour — a script that does not exist,
producing output nothing produces today. Not a refactor, so the
characterisation path does not apply. Ambiguity resolves to red and there is no
ambiguity here.

---

## Phase 4a — the reds, observed failing before any implementation

All in one new file, `scripts/summarise-e2e-retries.test.mjs`, run with
`node --test "scripts/**/*.test.mjs"` (i.e. `pnpm test:guards`). **Write the
fixtures inline in the test file** — the three existing guards build their
inputs in-process rather than carrying fixture directories, and six small JSON
objects do not earn a directory.

The first run must fail because `scripts/summarise-e2e-retries.mjs` does not
exist. **That is a legitimate red but a weak one**, so the tests must be
written to fail *specifically* afterwards too: implement the script as a stub
that writes nothing, re-run, and quote the assertion failures — those are the
failures that belong in the PR body, not `ERR_MODULE_NOT_FOUND`.

| ID | [P] | Story | Task |
|---|---|---|---|
| **T001** | | US1 | Test: **two flaky tests are counted and named.** Fixture with two `status: 'flaky'` tests — `[timedOut, passed]` in `chromium`, `[failed, failed, passed]` in `kiosk`. Assert the summary contains `2 tests passed only on retry`, both spec files, both titles, `timedOut → passed` and `failed → failed → passed`, and that the process exits `0`. Run the **real script** as a child process with `GITHUB_STEP_SUMMARY` pointed at a temp file, the way `wait-for-e2e-stack.test.mjs` runs the real shell script — not by importing a function. |
| **T002** | | US1 | Test: **a clean report still renders the section.** All tests `expected`. Assert `0 tests passed only on retry` is present, the totals table is present, exit `0`. |
| **T003** | | US1 | Test: **nested suites are walked.** A flaky test inside `suites[].suites[].specs[]`. Assert it is counted and named. |
| **T004** | | US1 | Test: **the walk and `stats.flaky` disagreeing is reported, not resolved.** `stats.flaky: 3`, one flaky test in the tree. Assert both numbers appear and the text says they disagree. Exit `0`. |
| **T005** | | US1 | Test: **a missing report says so.** Point the script at a path that does not exist. Assert the summary names the path, does **not** contain `0 tests passed only on retry`, exit `0`. |
| **T006** | | US1 | Test: **unparseable JSON says so.** Truncated file. Assert the summary names the path and reports a parse failure, exit `0`. |
| **T007** | | US1 | Test: **no test output is rendered.** Fixture whose flaky result carries `stdout`, `stderr`, `error.message` and an `attachments` entry containing a distinctive sentinel string. Assert the sentinel appears **nowhere** in the summary. This is the *Auth* constraint and the 1 MiB constraint in one assertion. |
| **T008** | | US1 | Test: **no `GITHUB_STEP_SUMMARY` means stdout.** Same fixture as T001, variable unset. Assert the Markdown lands on stdout and exit `0`. |
| **T009** | | US1 | Guard: **the wiring exists and the gate values did not move.** Read `playwright.config.ts` and assert (a) the CI reporter list includes a `json` entry naming `test-results/e2e-report.json`, (b) `retries: isCI ? 2 : 0` is unchanged, (c) `expect: { timeout: isCI ? 30_000 : 15_000 }` is unchanged. Read `.github/workflows/ci.yml` and assert the `e2e` job has a step invoking `scripts/summarise-e2e-retries.mjs` with `if: always()`. **This guard reads an artefact and proves only that the design was written down** — say so in its header comment, as `lint-scope.test.mjs` does; what proves it *holds* is phase 5. |

T001–T009 are nine assertions in one file, so they are **not** `[P]` against
each other — one file, one author. They are collectively `[P]` against
everything else in the repository.

**Red evidence to quote in the PR:** the output of `pnpm test:guards` after the
stub exists, showing T001–T009 failing on assertions. Per ADR-0139 the failure
text goes in the PR body verbatim.

## Phase 4b — the implementation

| ID | [P] | Story | Task | Depends on |
|---|---|---|---|---|
| **T010** | | US1 | Write `scripts/summarise-e2e-retries.mjs`. Node 22 ESM, no dependencies. Reads `process.argv[2] ?? 'test-results/e2e-report.json'`; walks `suites` recursively; counts by `JSONReportTest.status`; renders the section of `plan.md`; appends to `$GITHUB_STEP_SUMMARY` when set, else writes stdout. **Never throws, always exits 0.** Header comment names #2077 and says which option of the three it implements. | T001–T008 |
| **T011** | | US1 | `playwright.config.ts:17` — append `['json', { outputFile: 'test-results/e2e-report.json' }]` to the **CI** reporter array only. Touch nothing else in the file. One-line diff plus a short comment pointing at #2077. | T009 |
| **T012** | | US1 | `.github/workflows/ci.yml` — insert a step after `Run the Playwright e2e suite` (`:258`), named e.g. `Summarise retried e2e outcomes`, `if: always()`, running `node scripts/summarise-e2e-retries.mjs`. Place it **before** the `AppHost log` step so the summary is written even when the job later fails. Do not change the upload step: `test-results` is already in its path. | T009, T010 |

T011 and T012 touch different files and could be `[P]`, but they are two lines
of work gated behind the same guard and there is nothing to gain by splitting
them across agents. **The genuine parallelism in this spec is nil** — one
implementer, four files, half a day. Recording that explicitly so the
orchestrator does not fan out a job that would spend more on coordination than
on the work.

## Phase 4b — nothing is behaviour-preserving here

There is no existing behaviour to characterise: the summariser is new, the
reporter entry is additive, the CI step is new. No characterisation tests are
required and none should be invented.

## Dependency graph

```
T001..T008 ─┐
            ├─→ T010 ─┐
T009 ───────┴─→ T011  ├─→ T012 ─→ phase 5
                      ┘
```

Nothing here is foundational to other specs: no `Shared.Kernel`, no
`Shared.Contracts`, no `AppHost`, no Aspire resource. Nothing else is blocked
by this branch.

## Verification (phase 5)

Per the spec's *Independent end-to-end test procedure*. Two readings, and
**both are required** — one alone is insufficient:

1. **The real run.** The PR's own `e2e` job summary page shows the section.
   Expect `0 tests passed only on retry` on a healthy run; quote it. This
   proves the wiring — the reporter wrote a file, the step found it, GitHub
   rendered it.
2. **The counterfactual.** Locally, run the script against the T001 fixture
   with `GITHUB_STEP_SUMMARY` set, and quote the rendered Markdown showing two
   named tests and their attempt chains. This proves the counting, which a
   zero cannot.
3. Confirm `test-results/e2e-report.json` is present in the downloaded
   `playwright-report` artifact (risk A1). If it is not, apply the pre-decided
   fallback and re-run.

**Latency:** no leg is on this path; cite the spec's *Latency-budget impact*
section, which explains why this is an observability improvement for §IV's
measurements rather than a change to §IV.

## PR notes (phase 7)

- The PR must **not** carry a closing keyword for #2077. Options 2 and 3 stay
  open on that issue; leave a comment saying option 1 landed and which two
  remain, and let a human close it. (MEMORY: a PR mention rarely auto-closes an
  issue — here that default is the desired one, but state it explicitly so a
  later reader does not read the open issue as unfinished work.)
- State in the PR body, in one line, that `retries` and `expect.timeout` are
  unchanged and that T009 asserts it.

## Gate — phase 3

Tasks are atomic and each names its file. The feature's issue (#2077) is on
Project #13 and was already there. Phase 4a is declared **red**, with the weak
form of the red (module-not-found) explicitly rejected in favour of assertion
failures against a stub.
