// Spec 225 §9 / plan.md §8.4, T020 — the composite-and-render regression gate
// (US4, closes #2337).
//
// `render-leg-check.mjs` reads the committed baseline
// (`specs/225-the-render-leg-ci-never-reads/baseline.json`) and the union of
// every shard's `render-leg-attempt-*.json` files, and decides pass /
// regressed / unmeasured / refused (plan.md §8.4's decision table). It runs
// as its own `render-leg-gate` CI job (plan.md §8.1), outside the Playwright
// test, so `retries: isCI ? 2 : 0` cannot re-roll the verdict (FR-011).
//
// Invocation (plan.md §8.1):
//   node scripts/render-leg-check.mjs <download dir> <baseline.json path>
// `<download dir>` holds one subdirectory per shard artifact
// (`playwright-report-<n>-of-4`), each with its own `test-results/` —
// `actions/download-artifact`'s own layout for a glob pattern, mirrored here
// rather than invented.
//
// Driven as a real child process, the same shape
// `scripts/render-leg-summary.test.mjs` already uses for its sibling script.
//
// Doubly red (tasks.md T020 / plan.md §8.6): run once before this script
// exists (module-not-found — every case fails for the same reason, which
// proves nothing on its own), and once more against a stub that always
// exits 0 (every case still fails, but each for ITS OWN reason — the
// wrong verdict, the wrong exit code, the missing word). Both runs are
// quoted verbatim in the PR; neither is faked by editing this file.

import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import test from 'node:test';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const script = path.join(repositoryRoot, 'scripts', 'render-leg-check.mjs');

// ---- baseline fixture -------------------------------------------------
//
// Five runs, p50 = [48, 48, 50, 52, 52]. Chosen so the arithmetic is exact
// in floating point: mean = 50, sample variance (n-1=4) = (4+4+0+4+4)/4 = 4,
// sample stddev = 2, tolerance (3*stddev, FR-018) = 6.
// baseline + tolerance = 56 exactly — the boundary tests below rely on this
// being an exact value, not an approximation.
const BASELINE_P50S = [48, 48, 50, 52, 52];
const BASELINE_MEAN = 50;
const BASELINE_TOLERANCE = 6; // 3 * stddev(2)
const BASELINE_PLUS_TOLERANCE = BASELINE_MEAN + BASELINE_TOLERANCE; // 56

function fortyHexSha(seed) {
  return seed.toString(16).padStart(40, '0');
}

function validBaselineRuns() {
  return BASELINE_P50S.map((p50, index) => ({
    runId: String(30000000000 + index),
    sha: fortyHexSha(index + 1),
    attempt: 0,
    p50Milliseconds: p50,
  }));
}

function validBaselineObject(overrides = {}) {
  return {
    measurement: 'overlay_draw',
    fixture: { tiles: 4, iterations: 10 },
    runner: 'ubuntu-latest, headless Chromium, software rasterisation',
    baselineP50Milliseconds: BASELINE_MEAN,
    toleranceMilliseconds: BASELINE_TOLERANCE,
    rule: 'tolerance = 3 * sample stddev of first-complete-attempt p50 across the runs below (spec FR-018)',
    runs: validBaselineRuns(),
    ...overrides,
  };
}

function writeBaseline(directory, content) {
  const filePath = path.join(directory, 'baseline.json');
  const body = typeof content === 'string' ? content : JSON.stringify(content, null, 2);
  writeFileSync(filePath, body, 'utf8');
  return filePath;
}

// ---- record fixtures ----------------------------------------------------

function uniformSamples(value, count = 40) {
  return Array.from({ length: count }, () => value);
}

function renderLegRecord({
  attempt = 0,
  p50 = 50,
  complete = true,
  runId = '40000000000',
  sha = fortyHexSha(99),
  frameIntervalBeforeMilliseconds = 16.67,
  frameIntervalAfterMilliseconds = 16.4,
} = {}) {
  const samples = uniformSamples(p50);
  return {
    measurement: 'overlay_draw',
    attempt,
    runId,
    sha,
    samples,
    count: samples.length,
    p50,
    max: p50,
    p95: p50,
    frameIntervalBeforeMilliseconds,
    frameIntervalAfterMilliseconds,
    complete,
  };
}

function shardTestResultsDirectory(root, shard) {
  const directory = path.join(root, `playwright-report-${shard}-of-4`, 'test-results');
  mkdirSync(directory, { recursive: true });
  return directory;
}

// Writes one shard's attempt files (the span test runs in exactly one of
// four shards in real CI — plan.md §2.2 — so most fixtures below write to
// shard 4 alone and leave 1-3 empty, mirroring the real layout).
function writeShardRecords(root, shard, records) {
  const directory = shardTestResultsDirectory(root, shard);
  for (const record of records) {
    writeFileSync(path.join(directory, `render-leg-attempt-${record.attempt}.json`), JSON.stringify(record, null, 2), 'utf8');
  }
}

function emptyFourShardLayout(root) {
  for (const shard of [1, 2, 3, 4]) {
    shardTestResultsDirectory(root, shard);
  }
}

function tempDirectory() {
  return mkdtempSync(path.join(tmpdir(), 'render-leg-check-'));
}

function runChecker(shardsDirectory, baselinePath) {
  const result = spawnSync('node', [script, shardsDirectory, baselinePath], {
    cwd: repositoryRoot,
    encoding: 'utf8',
  });
  return result;
}

function describeFailure(result) {
  return `exit ${result.status}\nstdout:\n${result.stdout}\nstderr:\n${result.stderr}`;
}

function output(result) {
  return `${result.stdout ?? ''}\n${result.stderr ?? ''}`;
}

// ==== baseline absent or malformed → refused, never a permissive default ===

test('baseline.json absent — refused, names the file, exits non-zero', () => {
  const root = tempDirectory();
  emptyFourShardLayout(root);
  const missingBaselinePath = path.join(root, 'baseline.json');

  const result = runChecker(root, missingBaselinePath);

  assert.notEqual(result.status, 0, describeFailure(result));
  assert.match(output(result), /baseline refused/i, describeFailure(result));
  assert.match(output(result), /baseline\.json/, describeFailure(result));
});

test('baseline.json is not valid JSON — refused, not a crash with no message', () => {
  const root = tempDirectory();
  emptyFourShardLayout(root);
  const baselinePath = writeBaseline(root, '{ not valid json');

  const result = runChecker(root, baselinePath);

  assert.notEqual(result.status, 0, describeFailure(result));
  assert.match(output(result), /baseline refused/i, describeFailure(result));
});

test('baseline.json has fewer than five runs — refused (FR-018 needs ≥5)', () => {
  const root = tempDirectory();
  emptyFourShardLayout(root);
  const baselinePath = writeBaseline(
    root,
    validBaselineObject({ runs: validBaselineRuns().slice(0, 3) }),
  );

  const result = runChecker(root, baselinePath);

  assert.notEqual(result.status, 0, describeFailure(result));
  assert.match(output(result), /baseline refused/i, describeFailure(result));
});

test('baseline.json carries a SHA that is not 40 hex characters — refused', () => {
  const root = tempDirectory();
  emptyFourShardLayout(root);
  const runs = validBaselineRuns();
  runs[0] = { ...runs[0], sha: 'not-a-sha' };
  const baselinePath = writeBaseline(root, validBaselineObject({ runs }));

  const result = runChecker(root, baselinePath);

  assert.notEqual(result.status, 0, describeFailure(result));
  assert.match(output(result), /baseline refused/i, describeFailure(result));
});

test('baseline.json has a non-positive tolerance — refused', () => {
  const root = tempDirectory();
  emptyFourShardLayout(root);
  const baselinePath = writeBaseline(root, validBaselineObject({ toleranceMilliseconds: 0 }));

  const result = runChecker(root, baselinePath);

  assert.notEqual(result.status, 0, describeFailure(result));
  assert.match(output(result), /baseline refused/i, describeFailure(result));
});

test("baseline.json's stated mean does not match the mean of its own runs — refused, self-verification catches a hand edit", () => {
  const root = tempDirectory();
  emptyFourShardLayout(root);
  const baselinePath = writeBaseline(root, validBaselineObject({ baselineP50Milliseconds: 999 }));

  const result = runChecker(root, baselinePath);

  assert.notEqual(result.status, 0, describeFailure(result));
  assert.match(output(result), /baseline refused/i, describeFailure(result));
});

test("baseline.json's stated tolerance does not match 3σ of its own runs — refused", () => {
  const root = tempDirectory();
  emptyFourShardLayout(root);
  const baselinePath = writeBaseline(root, validBaselineObject({ toleranceMilliseconds: 999 }));

  const result = runChecker(root, baselinePath);

  assert.notEqual(result.status, 0, describeFailure(result));
  assert.match(output(result), /baseline refused/i, describeFailure(result));
});

// ==== zero records across all shards → unmeasured, never a pass ===========

test('no shard produced any record — unmeasured, not a pass, never says regressed', () => {
  const root = tempDirectory();
  emptyFourShardLayout(root);
  const baselinePath = writeBaseline(root, validBaselineObject());

  const result = runChecker(root, baselinePath);

  assert.notEqual(result.status, 0, describeFailure(result));
  assert.match(output(result), /unmeasured/i, describeFailure(result));
  assert.match(output(result), /no render-leg record in any shard/i, describeFailure(result));
  assert.doesNotMatch(output(result), /regressed/i, describeFailure(result));
});

test('shard directories do not even exist yet — unmeasured, not a crash', () => {
  const root = tempDirectory(); // no shard subdirectories at all
  const baselinePath = writeBaseline(root, validBaselineObject());

  const result = runChecker(root, baselinePath);

  assert.notEqual(result.status, 0, describeFailure(result));
  assert.match(output(result), /unmeasured/i, describeFailure(result));
});

// ==== a malformed record → unmeasured, not a crash, not a pass ============

test('a record file is not valid JSON — unmeasured, names the file, never a pass', () => {
  const root = tempDirectory();
  emptyFourShardLayout(root);
  const directory = shardTestResultsDirectory(root, 4);
  writeFileSync(path.join(directory, 'render-leg-attempt-0.json'), '{ not valid json', 'utf8');
  const baselinePath = writeBaseline(root, validBaselineObject());

  const result = runChecker(root, baselinePath);

  assert.notEqual(result.status, 0, describeFailure(result));
  assert.match(output(result), /unmeasured/i, describeFailure(result));
  assert.match(output(result), /render-leg-attempt-0\.json/, describeFailure(result));
});

test("a record has no 'complete' field — unmeasured, a malformed record is never read as a pass", () => {
  const root = tempDirectory();
  emptyFourShardLayout(root);
  const directory = shardTestResultsDirectory(root, 4);
  const record = renderLegRecord({ attempt: 0, p50: 50 });
  delete record.complete;
  writeFileSync(path.join(directory, 'render-leg-attempt-0.json'), JSON.stringify(record), 'utf8');
  const baselinePath = writeBaseline(root, validBaselineObject());

  const result = runChecker(root, baselinePath);

  assert.notEqual(result.status, 0, describeFailure(result));
  assert.match(output(result), /unmeasured/i, describeFailure(result));
});

// ==== N1 — malformed records fail cleanly (a decision-table verdict), never
//      with an unhandled crash / stack trace ================================

test('a complete record is missing frameIntervalBeforeMilliseconds — unmeasured, malformed, not a TypeError stack trace', () => {
  const root = tempDirectory();
  emptyFourShardLayout(root);
  const directory = shardTestResultsDirectory(root, 4);
  const record = renderLegRecord({ attempt: 0, p50: 50, complete: true });
  delete record.frameIntervalBeforeMilliseconds;
  writeFileSync(path.join(directory, 'render-leg-attempt-0.json'), JSON.stringify(record), 'utf8');
  const baselinePath = writeBaseline(root, validBaselineObject());

  const result = runChecker(root, baselinePath);

  assert.notEqual(result.status, 0, describeFailure(result));
  assert.match(output(result), /unmeasured/i, describeFailure(result));
  assert.match(output(result), /malformed/i, describeFailure(result));
  assert.doesNotMatch(output(result), /TypeError/i, describeFailure(result));
});

test("a record's 'attempt' field is non-numeric — unmeasured, malformed, not a crash", () => {
  const root = tempDirectory();
  emptyFourShardLayout(root);
  const directory = shardTestResultsDirectory(root, 4);
  const record = renderLegRecord({ attempt: 0, p50: 50, complete: true });
  record.attempt = 'zero';
  writeFileSync(path.join(directory, 'render-leg-attempt-0.json'), JSON.stringify(record), 'utf8');
  const baselinePath = writeBaseline(root, validBaselineObject());

  const result = runChecker(root, baselinePath);

  assert.notEqual(result.status, 0, describeFailure(result));
  assert.match(output(result), /unmeasured/i, describeFailure(result));
  assert.match(output(result), /malformed/i, describeFailure(result));
  assert.doesNotMatch(output(result), /TypeError/i, describeFailure(result));
});

test("baseline.json's runs array contains a null entry — refused, not a crash reading a field off null", () => {
  const root = tempDirectory();
  emptyFourShardLayout(root);
  const runs = validBaselineRuns();
  runs[1] = null;
  const baselinePath = writeBaseline(root, validBaselineObject({ runs }));

  const result = runChecker(root, baselinePath);

  assert.notEqual(result.status, 0, describeFailure(result));
  assert.match(output(result), /baseline refused/i, describeFailure(result));
  assert.doesNotMatch(output(result), /TypeError/i, describeFailure(result));
});

// ==== records from more than one shard → unmeasured, the harness ran twice =

test('records exist in two different shards — unmeasured, names the shard count, refuses to guess which is right', () => {
  const root = tempDirectory();
  emptyFourShardLayout(root);
  writeShardRecords(root, 3, [renderLegRecord({ attempt: 0, p50: 51, complete: true })]);
  writeShardRecords(root, 4, [renderLegRecord({ attempt: 0, p50: 51, complete: true })]);
  const baselinePath = writeBaseline(root, validBaselineObject());

  const result = runChecker(root, baselinePath);

  assert.notEqual(result.status, 0, describeFailure(result));
  assert.match(output(result), /unmeasured/i, describeFailure(result));
  assert.match(output(result), /2 shards/i, describeFailure(result));
  assert.match(output(result), /expected 1/i, describeFailure(result));
});

// ==== every attempt incomplete → unmeasured, distinct from regressed ======

test('every attempt in the shard is incomplete — unmeasured, never regressed, even if the p50 would have breached', () => {
  const root = tempDirectory();
  emptyFourShardLayout(root);
  // p50 = 999 would clearly breach if it were evaluated — proving the
  // checker refuses to read an incomplete attempt as a figure at all,
  // rather than silently evaluating it anyway.
  writeShardRecords(root, 4, [
    renderLegRecord({ attempt: 0, p50: 999, complete: false }),
    renderLegRecord({ attempt: 1, p50: 999, complete: false }),
  ]);
  const baselinePath = writeBaseline(root, validBaselineObject());

  const result = runChecker(root, baselinePath);

  assert.notEqual(result.status, 0, describeFailure(result));
  assert.match(output(result), /unmeasured/i, describeFailure(result));
  assert.match(output(result), /every attempt incomplete/i, describeFailure(result));
  assert.doesNotMatch(output(result), /regressed/i, describeFailure(result));
});

// ==== within tolerance → pass, prints baseline, observed, margin, both T ===

test('the first complete attempt is comfortably within tolerance — passes, prints baseline, observed, margin and both T readings', () => {
  const root = tempDirectory();
  emptyFourShardLayout(root);
  writeShardRecords(root, 4, [
    renderLegRecord({
      attempt: 0,
      p50: 53, // baseline 50 + tolerance 6 = 56; 53 is well inside
      complete: true,
      frameIntervalBeforeMilliseconds: 16.67,
      frameIntervalAfterMilliseconds: 16.4,
    }),
  ]);
  const baselinePath = writeBaseline(root, validBaselineObject());

  const result = runChecker(root, baselinePath);

  assert.equal(result.status, 0, describeFailure(result));
  assert.match(output(result), /within tolerance/i, describeFailure(result));
  assert.match(output(result), /50/, describeFailure(result)); // baseline
  assert.match(output(result), /53/, describeFailure(result)); // observed
  assert.match(output(result), /16\.67/, describeFailure(result)); // frame interval, before
  assert.match(output(result), /16\.40|16\.4\b/, describeFailure(result)); // frame interval, after
  assert.doesNotMatch(output(result), /regressed/i, describeFailure(result));
});

// ==== the "≤" / ">" boundary — the correct side wins =======================

test('exactly at baseline + tolerance (56) — the boundary passes, comparison is ">" not "≥"', () => {
  const root = tempDirectory();
  emptyFourShardLayout(root);
  writeShardRecords(root, 4, [renderLegRecord({ attempt: 0, p50: BASELINE_PLUS_TOLERANCE, complete: true })]);
  const baselinePath = writeBaseline(root, validBaselineObject());

  const result = runChecker(root, baselinePath);

  assert.equal(result.status, 0, describeFailure(result));
  assert.match(output(result), /within tolerance/i, describeFailure(result));
  assert.doesNotMatch(output(result), /regressed/i, describeFailure(result));
});

test('one hundredth of a millisecond past baseline + tolerance — regressed, the boundary does not round in its favour', () => {
  const root = tempDirectory();
  emptyFourShardLayout(root);
  writeShardRecords(root, 4, [
    renderLegRecord({ attempt: 0, p50: BASELINE_PLUS_TOLERANCE + 0.01, complete: true }),
  ]);
  const baselinePath = writeBaseline(root, validBaselineObject());

  const result = runChecker(root, baselinePath);

  assert.notEqual(result.status, 0, describeFailure(result));
  assert.match(output(result), /regressed/i, describeFailure(result));
});

// ==== regressed → fails, names baseline, observed, tolerance, excess, T,
//      and states ADR-0123's triage order =================================

test('a clear regression — fails, names baseline, observed, tolerance, excess, both T readings and the cadence-first triage order', () => {
  const root = tempDirectory();
  emptyFourShardLayout(root);
  writeShardRecords(root, 4, [
    renderLegRecord({
      attempt: 0,
      p50: 70, // baseline 50 + tolerance 6 = 56; 70 breaches by 14
      complete: true,
      frameIntervalBeforeMilliseconds: 32.03,
      frameIntervalAfterMilliseconds: 32.26,
    }),
  ]);
  const baselinePath = writeBaseline(root, validBaselineObject());

  const result = runChecker(root, baselinePath);

  assert.notEqual(result.status, 0, describeFailure(result));
  assert.match(output(result), /regressed/i, describeFailure(result));
  assert.match(output(result), /50/, describeFailure(result)); // baseline
  assert.match(output(result), /70/, describeFailure(result)); // observed
  assert.match(output(result), /\b6\b/, describeFailure(result)); // tolerance
  assert.match(output(result), /32\.03/, describeFailure(result));
  assert.match(output(result), /32\.26|32\.3\b/, describeFailure(result));
  assert.match(output(result), /cadence first,? compositing second/i, describeFailure(result)); // ADR-0123
});

// ==== a retry must not re-roll the verdict (FR-011, spec §9.5 scenario 3) ==

test('attempt 0 is incomplete and attempt 1 is complete and within tolerance — evaluates attempt 1, lists attempt 0 as skipped-incomplete, the verdict is not from attempt 0', () => {
  const root = tempDirectory();
  emptyFourShardLayout(root);
  writeShardRecords(root, 4, [
    renderLegRecord({ attempt: 0, p50: 999, complete: false }), // would regress if wrongly evaluated
    renderLegRecord({ attempt: 1, p50: 53, complete: true }), // within tolerance
  ]);
  const baselinePath = writeBaseline(root, validBaselineObject());

  const result = runChecker(root, baselinePath);

  assert.equal(result.status, 0, describeFailure(result));
  assert.match(output(result), /within tolerance/i, describeFailure(result));
  assert.doesNotMatch(output(result), /regressed/i, describeFailure(result));
  assert.match(output(result), /attempt 0/i, describeFailure(result));
  assert.match(output(result), /skip/i, describeFailure(result));
  assert.match(output(result), /incomplete/i, describeFailure(result));
  assert.match(output(result), /attempt 1/i, describeFailure(result));
  // The 999 ms figure from the skipped attempt 0 must never appear as the
  // evaluated observation — that would be exactly the re-roll FR-011 forbids.
  assert.doesNotMatch(output(result), /999/, describeFailure(result));
});

test('attempt 0 is incomplete and attempt 1 regresses — the verdict is regressed, taken from attempt 1, not silently passed because attempt 0 was skipped', () => {
  const root = tempDirectory();
  emptyFourShardLayout(root);
  writeShardRecords(root, 4, [
    renderLegRecord({ attempt: 0, p50: 40, complete: false }), // would pass if wrongly evaluated
    renderLegRecord({ attempt: 1, p50: 70, complete: true }), // regresses
  ]);
  const baselinePath = writeBaseline(root, validBaselineObject());

  const result = runChecker(root, baselinePath);

  assert.notEqual(result.status, 0, describeFailure(result));
  assert.match(output(result), /regressed/i, describeFailure(result));
  assert.match(output(result), /attempt 1/i, describeFailure(result));
});
