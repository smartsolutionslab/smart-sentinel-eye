// Spec 225 §9.4/§9.5, #2770 — `render-leg-gate-report.mjs`'s own tests.
//
// This job's entire purpose is visibility, not enforcement: FR-019 was found
// not met on the real FR-022 window (figures.md `## Verdict`), so no
// baseline.json is committed and `render-leg-check.mjs` (T020) must not be
// invoked from this job against a derived, uncommitted threshold (plan.md
// §9.4). Every test here proves the one invariant that matters: whatever the
// shard layout looks like, the script exits 0 and never throws.
//
// Driven as a real child process, the same shape
// `scripts/render-leg-check.test.mjs` and `scripts/render-leg-summary.test.mjs`
// already use for their own scripts.

import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import test, { after } from 'node:test';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const script = path.join(repositoryRoot, 'scripts', 'render-leg-gate-report.mjs');

function renderLegRecord({ attempt = 0, p50 = 20, complete = true } = {}) {
  const samples = [p50 - 10, p50, p50 + 10];
  return {
    measurement: 'overlay_draw',
    attempt,
    runId: '123',
    sha: 'a'.repeat(40),
    samples,
    count: samples.length,
    p50,
    max: samples[samples.length - 1],
    p95: null,
    frameIntervalBeforeMilliseconds: 16.67,
    frameIntervalAfterMilliseconds: 16.5,
    complete,
  };
}

function shardTestResultsDirectory(root, shard) {
  const directory = path.join(root, `playwright-report-${shard}-of-4`, 'test-results');
  mkdirSync(directory, { recursive: true });
  return directory;
}

function writeShardRecords(root, shard, records) {
  const directory = shardTestResultsDirectory(root, shard);
  for (const record of records) {
    writeFileSync(
      path.join(directory, `render-leg-attempt-${record.attempt}.json`),
      JSON.stringify(record, null, 2),
      'utf8',
    );
  }
}

const tempDirectories = [];

function tempDirectory() {
  const directory = mkdtempSync(path.join(tmpdir(), 'render-leg-gate-report-'));
  tempDirectories.push(directory);
  return directory;
}

after(() => {
  for (const directory of tempDirectories) {
    rmSync(directory, { recursive: true, force: true });
  }
});

// GITHUB_STEP_SUMMARY deleted from the child's env (mirrors
// render-leg-summary.test.mjs's own `runScript` helper, `withSummaryFile:
// false` mode): every assertion below reads `result.stdout`, but
// `writeSummary` only writes there when this var is unset. Not deleting it
// passed locally and failed in CI, where the OUTER job -- this very test
// process -- already has it set, and `spawnSync` inherits `process.env` by
// default.
function runReport(shardsDirectory, upstreamResult) {
  const environment = { ...process.env };
  delete environment.GITHUB_STEP_SUMMARY;

  const args = [script, shardsDirectory];
  if (upstreamResult !== undefined) {
    args.push(upstreamResult);
  }
  return spawnSync('node', args, { cwd: repositoryRoot, encoding: 'utf8', env: environment });
}

function describeFailure(result) {
  return `exit ${result.status}\nstdout:\n${result.stdout}\nstderr:\n${result.stderr}`;
}

function summary(result) {
  return result.stdout;
}

test('no shard directories exist at all — exits 0, says so, never a crash', () => {
  const root = tempDirectory();

  const result = runReport(path.join(root, 'never-created'), 'success');

  assert.equal(result.status, 0, describeFailure(result));
  assert.match(summary(result), /no render-leg record in any shard/i, describeFailure(result));
});

test('one shard has a complete record — exits 0, prints the figure and the report-only notice', () => {
  const root = tempDirectory();
  for (const shard of [1, 2, 3]) {
    shardTestResultsDirectory(root, shard);
  }
  writeShardRecords(root, 4, [renderLegRecord({ attempt: 0, p50: 42 })]);

  const result = runReport(root, 'success');

  assert.equal(result.status, 0, describeFailure(result));
  assert.match(summary(result), /report-only/i, describeFailure(result));
  assert.match(summary(result), /FR-019 not met/i, describeFailure(result));
  assert.match(summary(result), /Shard 4/, describeFailure(result));
  assert.match(summary(result), /42\.00 ms/, describeFailure(result));
  assert.match(summary(result), /\(complete\)/, describeFailure(result));
});

test('an incomplete attempt is rendered as incomplete, not silently dropped — exits 0', () => {
  const root = tempDirectory();
  writeShardRecords(root, 4, [renderLegRecord({ attempt: 0, p50: 10, complete: false })]);

  const result = runReport(root, 'success');

  assert.equal(result.status, 0, describeFailure(result));
  assert.match(summary(result), /\(incomplete\)/, describeFailure(result));
});

test('a record is not valid JSON — exits 0, names the file rather than crashing', () => {
  const root = tempDirectory();
  const directory = shardTestResultsDirectory(root, 4);
  writeFileSync(path.join(directory, 'render-leg-attempt-0.json'), '{ not json', 'utf8');

  const result = runReport(root, 'success');

  assert.equal(result.status, 0, describeFailure(result));
  assert.match(summary(result), /could not be parsed/i, describeFailure(result));
});

test('records exist in two different shards — exits 0 and still renders both, never a crash from the "one shard" assumption', () => {
  const root = tempDirectory();
  writeShardRecords(root, 1, [renderLegRecord({ attempt: 0, p50: 10 })]);
  writeShardRecords(root, 4, [renderLegRecord({ attempt: 0, p50: 20 })]);

  const result = runReport(root, 'success');

  assert.equal(result.status, 0, describeFailure(result));
  assert.match(summary(result), /Shard 1/, describeFailure(result));
  assert.match(summary(result), /Shard 4/, describeFailure(result));
});

test('e2e-shards result is "skipped" — exits 0, says unmeasured, never downloads or discovers shards', () => {
  const root = tempDirectory();
  // No shard directories at all — proves the skipped branch does not try to
  // read a layout that was never produced.

  const result = runReport(path.join(root, 'never-created'), 'skipped');

  assert.equal(result.status, 0, describeFailure(result));
  assert.match(summary(result), /unmeasured: e2e-shards skipped/i, describeFailure(result));
});

test('e2e-shards result is "cancelled" — exits 0, says unmeasured', () => {
  const root = tempDirectory();

  const result = runReport(path.join(root, 'never-created'), 'cancelled');

  assert.equal(result.status, 0, describeFailure(result));
  assert.match(summary(result), /unmeasured: e2e-shards cancelled/i, describeFailure(result));
});

test('no upstream-result argument at all — defaults to treating the run as measured, exits 0', () => {
  const root = tempDirectory();
  writeShardRecords(root, 4, [renderLegRecord({ attempt: 0, p50: 15 })]);

  const result = runReport(root, undefined);

  assert.equal(result.status, 0, describeFailure(result));
  assert.match(summary(result), /report-only/i, describeFailure(result));
});
