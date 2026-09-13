// Guard for #2077 / spec 145.
//
// `playwright.config.ts` retries a failed test twice in CI, and a test that
// passes on its second or third attempt is reported exactly like a test that
// passed first time — the run summary, the job's exit code and the merge gate
// carry no trace of the retry. `e2e/click-to-first-frame.spec.ts:65-76`
// already wrote down the consequence for a latency assertion: a p95 that
// breaches on two runs out of three is reported flaky and the job exits 0.
//
// This asserts the summariser that turns Playwright's own JSON report into a
// Markdown section naming every test that needed a retry — a pure function of
// a report file plus an environment variable, run for real as a child
// process, the way `wait-for-e2e-stack.test.mjs` runs the real shell script.
//
// FR-004 / the spec's *Auth* note: the summariser must never render `stdout`,
// `stderr`, an error message or an attachment body into the summary, because
// the e2e stack mints real Keycloak tokens and any of those fields can carry
// one. T007 asserts that with a sentinel string planted in all four.

import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { existsSync, mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import test from 'node:test';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const script = path.join(repositoryRoot, 'scripts', 'summarise-e2e-retries.mjs');
const playwrightConfigPath = path.join(repositoryRoot, 'playwright.config.ts');
const workflowPath = path.join(repositoryRoot, '.github', 'workflows', 'ci.yml');

const SECRET_SENTINEL = 'ss3y-keycloak-token-should-never-render-in-a-job-summary';

// ---- fixture builders -------------------------------------------------
//
// Small, literal objects rather than a fixture directory (tasks.md: "six
// small JSON objects do not earn a directory"), shaped after the installed
// `@playwright/test@1.62.1` JSONReport* types, verified by reading
// `testReporter.d.ts` directly rather than remembered.

function makeReport({ suites, flaky = 0, expected = 0, unexpected = 0, skipped = 0 }) {
  return {
    config: {},
    suites,
    errors: [],
    stats: {
      startTime: '2026-09-13T00:00:00.000Z',
      duration: 1000,
      expected,
      unexpected,
      flaky,
      skipped,
    },
  };
}

function makeSuite({ title, file, specs, suites = [] }) {
  return { title, file, column: 0, line: 0, specs, suites };
}

function makeSpec({ title, file, line = 1, tests }) {
  return { tags: [], title, ok: true, tests, id: `${file}:${line}`, file, line, column: 0 };
}

function makeTest({ projectName, status, results }) {
  return {
    timeout: 60_000,
    annotations: [],
    expectedStatus: 'passed',
    projectName,
    projectId: projectName,
    results,
    status,
  };
}

function makeResult({ status, retry, extra = {} }) {
  return {
    workerIndex: 0,
    parallelIndex: 0,
    status,
    duration: 100,
    error: undefined,
    errors: [],
    stdout: [],
    stderr: [],
    retry,
    startTime: '2026-09-13T00:00:00.000Z',
    attachments: [],
    annotations: [],
    ...extra,
  };
}

// A single flaky test with the given attempt statuses, wrapped in its own
// spec/suite so callers only choose title, file and project.
function flakySuite({ title, file, project, statuses }) {
  const results = statuses.map((status, retry) => makeResult({ status, retry }));
  return makeSuite({
    title: file,
    file,
    specs: [makeSpec({ title, file, tests: [makeTest({ projectName: project, status: 'flaky', results })] })],
  });
}

// ---- running the real script ------------------------------------------

function stubDirectory() {
  return mkdtempSync(path.join(tmpdir(), 'summarise-e2e-retries-'));
}

function writeReport(report) {
  const directory = stubDirectory();
  const reportPath = path.join(directory, 'report.json');
  writeFileSync(reportPath, JSON.stringify(report));
  return reportPath;
}

// Runs `node scripts/summarise-e2e-retries.mjs <reportPath>`.
// `withSummaryFile: true` (the default) points GITHUB_STEP_SUMMARY at a fresh
// temp file and returns its eventual contents (or '' if the script never
// created it) as `summary`; `withSummaryFile: false` leaves the variable
// unset and reports stdout as `summary` instead, for T008.
function runScript(reportPath, { withSummaryFile = true } = {}) {
  const environment = { ...process.env };
  let summaryPath;

  if (withSummaryFile) {
    summaryPath = path.join(stubDirectory(), 'step-summary.md');
    environment.GITHUB_STEP_SUMMARY = summaryPath;
  } else {
    delete environment.GITHUB_STEP_SUMMARY;
  }

  const args = reportPath === undefined ? [] : [reportPath];
  const result = spawnSync('node', [script, ...args], {
    cwd: repositoryRoot,
    encoding: 'utf8',
    env: environment,
  });

  const summary = withSummaryFile ? (existsSync(summaryPath) ? readFileSync(summaryPath, 'utf8') : '') : result.stdout;

  return { result, summary };
}

function describeFailure({ result, summary }) {
  return `exit ${result.status}\nstdout:\n${result.stdout}\nstderr:\n${result.stderr}\nsummary file:\n${summary}`;
}

// ---- T001 — two flaky tests are counted and named ----------------------

test('two tests that passed only on retry are counted and named, with their attempt chains', () => {
  const report = makeReport({
    flaky: 2,
    expected: 41,
    suites: [
      flakySuite({
        title: 'registers a camera',
        file: 'e2e/cameras.spec.ts',
        project: 'chromium',
        statuses: ['timedOut', 'passed'],
      }),
      flakySuite({
        title: 'shows the published layout',
        file: 'e2e/kiosk-wall.spec.ts',
        project: 'kiosk',
        statuses: ['failed', 'failed', 'passed'],
      }),
    ],
  });

  const outcome = runScript(writeReport(report));

  assert.equal(outcome.result.status, 0, describeFailure(outcome));
  assert.match(outcome.summary, /2 tests passed only on retry/, describeFailure(outcome));
  assert.match(outcome.summary, /e2e\/cameras\.spec\.ts/, describeFailure(outcome));
  assert.match(outcome.summary, /registers a camera/, describeFailure(outcome));
  assert.match(outcome.summary, /e2e\/kiosk-wall\.spec\.ts/, describeFailure(outcome));
  assert.match(outcome.summary, /shows the published layout/, describeFailure(outcome));
  assert.match(outcome.summary, /timedOut → passed/, describeFailure(outcome));
  assert.match(outcome.summary, /failed → failed → passed/, describeFailure(outcome));
});

// ---- T002 — a clean run still renders the section -----------------------

test('a report with no retried tests still renders the retried-outcomes section', () => {
  const report = makeReport({
    flaky: 0,
    expected: 44,
    suites: [
      makeSuite({
        title: 'cameras.spec.ts',
        file: 'e2e/cameras.spec.ts',
        specs: [
          makeSpec({
            title: 'registers a camera',
            file: 'e2e/cameras.spec.ts',
            tests: [
              makeTest({
                projectName: 'chromium',
                status: 'expected',
                results: [makeResult({ status: 'passed', retry: 0 })],
              }),
            ],
          }),
        ],
      }),
    ],
  });

  const outcome = runScript(writeReport(report));

  assert.equal(outcome.result.status, 0, describeFailure(outcome));
  assert.match(outcome.summary, /0 tests passed only on retry/, describeFailure(outcome));
  assert.match(outcome.summary, /\|\s*Outcome\s*\|\s*Tests\s*\|/, describeFailure(outcome));
  assert.match(outcome.summary, /Passed first attempt/, describeFailure(outcome));
});

// ---- T003 — nested suites are walked -------------------------------------

test('a flaky test nested two suites deep is still counted and named', () => {
  const nestedSpec = makeSpec({
    title: 'aligns playout across displays',
    file: 'e2e/wall-alignment.spec.ts',
    tests: [
      makeTest({
        projectName: 'wall',
        status: 'flaky',
        results: [makeResult({ status: 'timedOut', retry: 0 }), makeResult({ status: 'passed', retry: 1 })],
      }),
    ],
  });

  const report = makeReport({
    flaky: 1,
    expected: 10,
    suites: [
      makeSuite({
        title: 'wall',
        file: 'e2e/wall-alignment.spec.ts',
        specs: [],
        suites: [makeSuite({ title: 'nested group', file: 'e2e/wall-alignment.spec.ts', specs: [nestedSpec] })],
      }),
    ],
  });

  const outcome = runScript(writeReport(report));

  assert.equal(outcome.result.status, 0, describeFailure(outcome));
  assert.match(outcome.summary, /1 tests? passed only on retry/, describeFailure(outcome));
  assert.match(outcome.summary, /aligns playout across displays/, describeFailure(outcome));
  assert.match(outcome.summary, /e2e\/wall-alignment\.spec\.ts/, describeFailure(outcome));
});

// ---- T004 — the walk and stats.flaky disagreeing is reported ------------

test('a mismatch between the tree walk and stats.flaky is printed, not silently resolved', () => {
  const singleFlakySpec = makeSpec({
    title: 'reconnects after a stream outage',
    file: 'e2e/reconnect.spec.ts',
    tests: [
      makeTest({
        projectName: 'chromium',
        status: 'flaky',
        results: [makeResult({ status: 'failed', retry: 0 }), makeResult({ status: 'passed', retry: 1 })],
      }),
    ],
  });

  // stats.flaky says 3; the tree contains exactly one flaky test (A2 in the
  // spec: the two numbers are assumed to agree, and this fixture is the
  // counter-example that proves the assumption is checked, not trusted).
  const report = makeReport({
    flaky: 3,
    expected: 20,
    suites: [makeSuite({ title: 'reconnect.spec.ts', file: 'e2e/reconnect.spec.ts', specs: [singleFlakySpec] })],
  });

  const outcome = runScript(writeReport(report));

  assert.equal(outcome.result.status, 0, describeFailure(outcome));
  assert.match(outcome.summary, /\b1\b/, describeFailure(outcome));
  assert.match(outcome.summary, /\b3\b/, describeFailure(outcome));
  assert.match(outcome.summary, /disagree/i, describeFailure(outcome));
});

// ---- T005 — a missing report says so -------------------------------------

test('a missing report file is named, not reported as a zero retry count', () => {
  const missingPath = path.join(stubDirectory(), 'no-report-here.json');

  const outcome = runScript(missingPath);

  assert.equal(outcome.result.status, 0, describeFailure(outcome));
  assert.match(outcome.summary, /no Playwright JSON report was found/i, describeFailure(outcome));
  assert.ok(outcome.summary.includes(missingPath), describeFailure(outcome));
  assert.doesNotMatch(outcome.summary, /0 tests passed only on retry/, describeFailure(outcome));
});

// ---- T006 — unparseable JSON says so -------------------------------------

test('a report file that is not valid JSON is named, and the parse failure is stated', () => {
  const directory = stubDirectory();
  const reportPath = path.join(directory, 'truncated-report.json');
  writeFileSync(reportPath, '{ "suites": [ { "title": "cut off mid-object"');

  const outcome = runScript(reportPath);

  assert.equal(outcome.result.status, 0, describeFailure(outcome));
  assert.match(outcome.summary, /could not (be )?pars/i, describeFailure(outcome));
  assert.ok(outcome.summary.includes(reportPath), describeFailure(outcome));
});

// ---- T007 — no test output is ever rendered ------------------------------

test('stdout, stderr, error messages and attachment bodies never reach the summary', () => {
  const leakyResult = makeResult({
    status: 'timedOut',
    retry: 0,
    extra: {
      stdout: [{ text: `note: ${SECRET_SENTINEL}` }],
      stderr: [{ text: `warn: ${SECRET_SENTINEL}` }],
      error: { message: `Timed out waiting for token ${SECRET_SENTINEL}` },
      attachments: [{ name: 'trace', contentType: 'application/octet-stream', body: SECRET_SENTINEL }],
    },
  });
  const passedResult = makeResult({ status: 'passed', retry: 1 });

  const report = makeReport({
    flaky: 1,
    expected: 5,
    suites: [
      makeSuite({
        title: 'signs in as an operator',
        file: 'e2e/sign-in.spec.ts',
        specs: [
          makeSpec({
            title: 'signs in as an operator',
            file: 'e2e/sign-in.spec.ts',
            tests: [makeTest({ projectName: 'chromium', status: 'flaky', results: [leakyResult, passedResult] })],
          }),
        ],
      }),
    ],
  });

  const outcome = runScript(writeReport(report));

  assert.equal(outcome.result.status, 0, describeFailure(outcome));
  assert.match(outcome.summary, /1 tests? passed only on retry/, describeFailure(outcome));
  assert.ok(!outcome.summary.includes(SECRET_SENTINEL), `sentinel leaked into the summary:\n${describeFailure(outcome)}`);
});

// ---- T008 — no GITHUB_STEP_SUMMARY means stdout --------------------------

test('with GITHUB_STEP_SUMMARY unset, the Markdown lands on stdout', () => {
  const report = makeReport({
    flaky: 1,
    expected: 5,
    suites: [
      flakySuite({
        title: 'registers a camera',
        file: 'e2e/cameras.spec.ts',
        project: 'chromium',
        statuses: ['timedOut', 'passed'],
      }),
    ],
  });

  const outcome = runScript(writeReport(report), { withSummaryFile: false });

  assert.equal(outcome.result.status, 0, describeFailure(outcome));
  assert.match(outcome.summary, /1 tests? passed only on retry/, describeFailure(outcome));
  assert.match(outcome.summary, /registers a camera/, describeFailure(outcome));
});

// ---- T009 — the wiring exists and the gate values did not move -----------
//
// This guard reads an artefact and proves only that the design was written
// down, not that it holds — phase 5 (the real CI run) is what proves the
// summary actually renders. See `lint-scope.test.mjs` for the same caveat
// about the same class of assertion.

test('the CI reporter list gains a json entry, and retries/expect.timeout are untouched', () => {
  const config = readFileSync(playwrightConfigPath, 'utf8');

  assert.match(
    config,
    /reporter:\s*isCI\s*\?\s*\[\s*\['list'\]\s*,\s*\['html',\s*\{\s*open:\s*'never'\s*\}\s*\]\s*,\s*\['json',\s*\{\s*outputFile:\s*'test-results\/e2e-report\.json'\s*\}\s*\]\s*\]/,
    'expected the CI reporter array to append a json entry writing to test-results/e2e-report.json',
  );
  assert.match(
    config,
    /retries:\s*isCI\s*\?\s*2\s*:\s*0/,
    'retries must remain isCI ? 2 : 0 — this issue does not change it (spec 145, out of scope)',
  );
  assert.match(
    config,
    /expect:\s*\{\s*timeout:\s*isCI\s*\?\s*30_000\s*:\s*15_000\s*\}/,
    'expect.timeout must remain isCI ? 30_000 : 15_000 — this issue does not change it (spec 145, out of scope)',
  );
});

test('the e2e job runs the summariser after the suite, unconditionally', () => {
  const workflow = readFileSync(workflowPath, 'utf8');

  const summariserStepIndex = workflow.indexOf('scripts/summarise-e2e-retries.mjs');
  assert.notEqual(summariserStepIndex, -1, 'expected a CI step invoking scripts/summarise-e2e-retries.mjs');

  // The nearest preceding "if:" line for that step must be `if: always()`, so
  // the section is written even when the suite itself failed (FR-007).
  const beforeStep = workflow.slice(0, summariserStepIndex);
  const stepStart = beforeStep.lastIndexOf('\n      - name:');
  const stepBlock = workflow.slice(stepStart, workflow.indexOf('\n      - name:', summariserStepIndex));

  assert.match(stepBlock, /if:\s*always\(\)/, `expected the summariser step to run with if: always():\n${stepBlock}`);
});
