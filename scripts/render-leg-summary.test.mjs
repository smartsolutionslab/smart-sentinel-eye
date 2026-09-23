// Spec 225 US1 — the composite-and-render leg's figure, escaping the run.
//
// `render-leg-summary.mjs` reads the per-attempt JSON files
// `e2e/support/render-leg.ts` writes (`test-results/render-leg-attempt-<n>.json`)
// and renders a `$GITHUB_STEP_SUMMARY` section naming the leg, its §IV budget,
// the observed frame interval and an explicit statement that no threshold is
// asserted (NFR-001) — sibling of `scripts/summarise-e2e-retries.mjs`, not an
// extension of it (plan.md §3): that script is contractually forbidden from
// rendering stdout, and this one renders no stdout either.
//
// Driven as a real child process, the same shape
// `scripts/summarise-e2e-retries.test.mjs` already uses for its own script.

import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import test from 'node:test';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const script = path.join(repositoryRoot, 'scripts', 'render-leg-summary.mjs');

function stubDirectory() {
  return mkdtempSync(path.join(tmpdir(), 'render-leg-summary-'));
}

function attemptRecord({
  attempt = 0,
  samples = [30, 40, 50],
  frameIntervalBeforeMilliseconds = 16.7,
  frameIntervalAfterMilliseconds = 16.7,
  runId = null,
  sha = null,
}) {
  const sorted = [...samples].sort((a, b) => a - b);
  const mid = Math.floor(sorted.length / 2);
  const p50 = samples.length === 0 ? null : sorted.length % 2 === 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
  const max = samples.length === 0 ? null : sorted[sorted.length - 1];
  return {
    measurement: 'overlay_draw',
    attempt,
    runId,
    sha,
    samples,
    count: samples.length,
    p50,
    max,
    p95: null,
    frameIntervalBeforeMilliseconds,
    frameIntervalAfterMilliseconds,
  };
}

function writeAttemptFile(directory, attempt, record) {
  mkdirSync(directory, { recursive: true });
  writeFileSync(path.join(directory, `render-leg-attempt-${attempt}.json`), JSON.stringify(record));
}

// Runs `node scripts/render-leg-summary.mjs <directory>`, pointing
// GITHUB_STEP_SUMMARY at a fresh temp file (mirrors summarise-e2e-retries's
// own `runScript` helper).
function runScript(directory, { withSummaryFile = true } = {}) {
  const environment = { ...process.env };
  let summaryPath;

  if (withSummaryFile) {
    summaryPath = path.join(stubDirectory(), 'step-summary.md');
    environment.GITHUB_STEP_SUMMARY = summaryPath;
  } else {
    delete environment.GITHUB_STEP_SUMMARY;
  }

  const args = directory === undefined ? [] : [directory];
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

// ---- no files at all — three of four shards, every run ---------------------

test('a directory with no attempt files says so, not a fabricated zero', () => {
  const directory = stubDirectory();

  const outcome = runScript(directory);

  assert.equal(outcome.result.status, 0, describeFailure(outcome));
  assert.match(outcome.summary, /no figure in this shard/i, describeFailure(outcome));
  assert.doesNotMatch(outcome.summary, /\b0 ms\b/, describeFailure(outcome));
});

test('a directory that does not exist at all is treated the same as an empty one', () => {
  const missing = path.join(stubDirectory(), 'never-created');

  const outcome = runScript(missing);

  assert.equal(outcome.result.status, 0, describeFailure(outcome));
  assert.match(outcome.summary, /no figure in this shard/i, describeFailure(outcome));
});

// ---- one attempt, real figures ---------------------------------------------

test('a single attempt with samples prints the leg, its budget, and the frame interval', () => {
  const directory = stubDirectory();
  writeAttemptFile(
    directory,
    0,
    attemptRecord({ samples: [30, 40, 50], frameIntervalBeforeMilliseconds: 16.67, frameIntervalAfterMilliseconds: 16.4 }),
  );

  const outcome = runScript(directory);

  assert.equal(outcome.result.status, 0, describeFailure(outcome));
  assert.match(outcome.summary, /overlay_draw/, describeFailure(outcome));
  assert.match(outcome.summary, /50 ms/, describeFailure(outcome)); // the §IV budget
  assert.match(outcome.summary, /\b3\b/, describeFailure(outcome)); // sample count
  assert.match(outcome.summary, /40/, describeFailure(outcome)); // p50
  assert.match(outcome.summary, /16\.67/, describeFailure(outcome));
  assert.match(outcome.summary, /16\.40\b/, describeFailure(outcome));
  assert.match(outcome.summary, /no threshold is asserted/i, describeFailure(outcome));
});

// ---- three attempts — a retried test, none overwriting another ------------

test('three attempts (a full retry chain) are each rendered, none overwriting another', () => {
  const directory = stubDirectory();
  writeAttemptFile(directory, 0, attemptRecord({ attempt: 0, samples: [10, 20] }));
  writeAttemptFile(directory, 1, attemptRecord({ attempt: 1, samples: [30, 40] }));
  writeAttemptFile(directory, 2, attemptRecord({ attempt: 2, samples: [50, 60] }));

  const outcome = runScript(directory);

  assert.equal(outcome.result.status, 0, describeFailure(outcome));
  for (const attempt of [0, 1, 2]) {
    assert.match(outcome.summary, new RegExp(`attempt ${attempt}\\b`, 'i'), describeFailure(outcome));
  }
  assert.match(outcome.summary, /15\b/, describeFailure(outcome)); // p50 of [10,20]
  assert.match(outcome.summary, /35\b/, describeFailure(outcome)); // p50 of [30,40]
  assert.match(outcome.summary, /55\b/, describeFailure(outcome)); // p50 of [50,60]
});

// ---- zero samples — never rendered as 0 ms ---------------------------------

test('an attempt with zero samples says "no samples", never "0 ms" (FR-003)', () => {
  const directory = stubDirectory();
  writeAttemptFile(directory, 0, attemptRecord({ samples: [] }));

  const outcome = runScript(directory);

  assert.equal(outcome.result.status, 0, describeFailure(outcome));
  assert.match(outcome.summary, /no samples/i, describeFailure(outcome));
  assert.doesNotMatch(outcome.summary, /\b0 ms\b/, describeFailure(outcome));
});

// ---- a malformed file — named, not crashed over ----------------------------

test('a malformed attempt file is named as unreadable rather than crashing the script', () => {
  const directory = stubDirectory();
  mkdirSync(directory, { recursive: true });
  writeFileSync(path.join(directory, 'render-leg-attempt-0.json'), '{ not valid json');

  const outcome = runScript(directory);

  assert.equal(outcome.result.status, 0, describeFailure(outcome));
  assert.match(outcome.summary, /render-leg-attempt-0\.json/, describeFailure(outcome));
  assert.match(outcome.summary, /could not be (read|parsed)/i, describeFailure(outcome));
});

test('a malformed attempt does not stop a sibling attempt from rendering', () => {
  const directory = stubDirectory();
  mkdirSync(directory, { recursive: true });
  writeFileSync(path.join(directory, 'render-leg-attempt-0.json'), '{ not valid json');
  writeAttemptFile(directory, 1, attemptRecord({ attempt: 1, samples: [30, 40, 50] }));

  const outcome = runScript(directory);

  assert.equal(outcome.result.status, 0, describeFailure(outcome));
  assert.match(outcome.summary, /render-leg-attempt-0\.json/, describeFailure(outcome));
  assert.match(outcome.summary, /attempt 1\b/i, describeFailure(outcome));
  assert.match(outcome.summary, /40/, describeFailure(outcome));
});

// ---- no GITHUB_STEP_SUMMARY means stdout -----------------------------------

test('with GITHUB_STEP_SUMMARY unset, the Markdown lands on stdout', () => {
  const directory = stubDirectory();
  writeAttemptFile(directory, 0, attemptRecord({ samples: [30, 40, 50] }));

  const outcome = runScript(directory, { withSummaryFile: false });

  assert.equal(outcome.result.status, 0, describeFailure(outcome));
  assert.match(outcome.summary, /overlay_draw/, describeFailure(outcome));
});

// ---- never throws — an internal error still leaves the section ------------

test('an unreadable directory (not merely absent) does not crash the script', () => {
  // A file where a directory is expected: readdirSync throws ENOTDIR, not
  // ENOENT — a different failure mode than the "no files" case above, and
  // the script must not distinguish the visitor's fortune from the absence
  // case in a way that silently swallows a real misconfiguration as "no
  // figure" — both are reported the same user-facing way (FR-005's promise
  // is that SOME line always appears), so this only proves the script does
  // not crash over it.
  const directory = stubDirectory();
  const notADirectory = path.join(directory, 'not-a-directory');
  writeFileSync(notADirectory, 'x');

  const outcome = runScript(notADirectory);

  assert.equal(outcome.result.status, 0, describeFailure(outcome));
  assert.ok(outcome.summary.length > 0, describeFailure(outcome));
});
