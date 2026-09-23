// Spec 225 §9 / plan.md §8.3's closing paragraph, T019b — `figures.md`'s
// baseline table and `baseline.json` must carry the same run ids, SHAs and
// p50s. `baseline.json`'s own self-verification (T020/render-leg-check.mjs:
// its stated mean/tolerance must equal what its own `runs[]` compute to)
// checks the *data* against itself; this checks the *data* against the
// *prose* — because "§II and §IV both drifted" (tasks.md T019b) by exactly
// this kind of gap: two places recording the same fact, read separately,
// never compared.
//
// Neither `figures.md` nor `baseline.json` is committed yet — that is US3's
// and US4's job (T016, T019), not this PR's. So this test does not read the
// real files; it validates the agreement-checking logic itself against
// synthetic fixture pairs written to a temp directory: a matching pair, and
// a pair with one deliberately mismatched p50 (tasks.md's own two cases).
//
// `render-leg-baseline-agreement.mjs` does not exist yet. Invocation
// (mirroring every other checker in this family — spawned as a real child
// process, the shape `render-leg-summary.test.mjs` and
// `render-leg-check.test.mjs` both use for their own scripts):
//
//   node scripts/render-leg-baseline-agreement.mjs <figures.md path> <baseline.json path>
//
// Exits 0 when every run in `baseline.json` has a matching row in
// `figures.md`'s baseline table (same run id, same SHA, same p50, to two
// decimal places — `figures.md`'s own stated precision, plan.md §5) and
// vice versa; exits non-zero and names the disagreement otherwise.

import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import test, { after } from 'node:test';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const script = path.join(repositoryRoot, 'scripts', 'render-leg-baseline-agreement.mjs');

function fortyHexSha(seed) {
  return seed.toString(16).padStart(40, '0');
}

// ---- fixture builders ----------------------------------------------------

// Mirrors plan.md §5's committed table shape exactly:
// | Run id | SHA | Samples | p50 | p95 | max | Observed T | Raw |
function figuresMarkdown(rows) {
  const header = [
    '# Spec 225 baseline (synthetic fixture — not the real file)',
    '',
    '## `develop` baseline (four-tile fixture, CI)',
    '',
    '| Run id | SHA | Samples | p50 | p95 | max | Observed T | Raw |',
    '| --- | --- | --- | --- | --- | --- | --- | --- |',
  ];
  const body = rows.map(
    (row) =>
      `| [${row.runId}](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/${row.runId}) ` +
      `| \`${row.sha}\` | 48 | ${row.p50.toFixed(2)} ms | 55.00 ms | 60.00 ms | 32.03 ms | \`[...]\` ms |`,
  );
  return [...header, ...body, ''].join('\n');
}

function baselineJson(rows) {
  const p50s = rows.map((row) => row.p50);
  const mean = p50s.reduce((sum, value) => sum + value, 0) / p50s.length;
  return {
    measurement: 'overlay_draw',
    fixture: { tiles: 4, iterations: 10 },
    runner: 'ubuntu-latest, headless Chromium, software rasterisation',
    baselineP50Milliseconds: mean,
    toleranceMilliseconds: 6,
    rule: 'tolerance = 3 * sample stddev of first-complete-attempt p50 across the runs below (spec FR-018)',
    runs: rows.map((row) => ({ runId: row.runId, sha: row.sha, attempt: 0, p50Milliseconds: row.p50 })),
  };
}

function fiveMatchingRows() {
  return [48, 48, 50, 52, 52].map((p50, index) => ({
    runId: String(35900000000 + index),
    sha: fortyHexSha(index + 1),
    p50,
  }));
}

// Phase-6 review (spec 225): none of these temp directories were ever
// cleaned up. Every `tempDirectory()` call is tracked here and swept once
// after the whole file's tests finish, rather than adding a try/finally to
// each test — keeps the existing test bodies untouched.
const tempDirectories = [];

function tempDirectory() {
  const directory = mkdtempSync(path.join(tmpdir(), 'render-leg-agreement-'));
  tempDirectories.push(directory);
  return directory;
}

after(() => {
  for (const directory of tempDirectories) {
    rmSync(directory, { recursive: true, force: true });
  }
});

function writeFixturePair(directory, rows, { figuresRows = rows } = {}) {
  const figuresPath = path.join(directory, 'figures.md');
  const baselinePath = path.join(directory, 'baseline.json');
  writeFileSync(figuresPath, figuresMarkdown(figuresRows), 'utf8');
  writeFileSync(baselinePath, JSON.stringify(baselineJson(rows), null, 2), 'utf8');
  return { figuresPath, baselinePath };
}

function runAgreementCheck(figuresPath, baselinePath) {
  const result = spawnSync('node', [script, figuresPath, baselinePath], {
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

// ==== a matching pair — the two files agree =================================

test('figures.md and baseline.json carry the same run ids, SHAs and p50s — agree, exits 0', () => {
  const directory = tempDirectory();
  const rows = fiveMatchingRows();
  const { figuresPath, baselinePath } = writeFixturePair(directory, rows);

  const result = runAgreementCheck(figuresPath, baselinePath);

  assert.equal(result.status, 0, describeFailure(result));
  assert.match(output(result), /agree/i, describeFailure(result));
  assert.doesNotMatch(output(result), /mismatch|disagree/i, describeFailure(result));
});

// ==== a deliberately mismatched p50 — named, not silently accepted =========

test('one row’s p50 differs between figures.md and baseline.json for the same run — disagreement, names the run id and both values, exits non-zero', () => {
  const directory = tempDirectory();
  const rows = fiveMatchingRows();
  // figures.md keeps the true value; baseline.json is hand-edited to a
  // different one for the same run — exactly the drift a reader comparing
  // the two files by eye is the least likely to catch.
  const mismatchedRunId = rows[2].runId;
  const mismatchedBaselineRows = rows.map((row) => (row.runId === mismatchedRunId ? { ...row, p50: row.p50 + 3.5 } : row));
  const figuresPath = path.join(directory, 'figures.md');
  const baselinePath = path.join(directory, 'baseline.json');
  writeFileSync(figuresPath, figuresMarkdown(rows), 'utf8');
  writeFileSync(baselinePath, JSON.stringify(baselineJson(mismatchedBaselineRows), null, 2), 'utf8');

  const result = runAgreementCheck(figuresPath, baselinePath);

  assert.notEqual(result.status, 0, describeFailure(result));
  assert.match(output(result), /mismatch|disagree/i, describeFailure(result));
  assert.match(output(result), new RegExp(mismatchedRunId), describeFailure(result));
  assert.match(output(result), /50\.00/, describeFailure(result)); // figures.md's value
  assert.match(output(result), /53\.50/, describeFailure(result)); // baseline.json's value
});

// ==== a run present in one file but absent from the other ==================

test('a run in baseline.json has no matching row in figures.md — disagreement, named, never silently ignored', () => {
  const directory = tempDirectory();
  const rows = fiveMatchingRows();
  const figuresPath = path.join(directory, 'figures.md');
  const baselinePath = path.join(directory, 'baseline.json');
  // figures.md is missing the last row entirely.
  writeFileSync(figuresPath, figuresMarkdown(rows.slice(0, 4)), 'utf8');
  writeFileSync(baselinePath, JSON.stringify(baselineJson(rows), null, 2), 'utf8');

  const result = runAgreementCheck(figuresPath, baselinePath);

  assert.notEqual(result.status, 0, describeFailure(result));
  assert.match(output(result), /mismatch|disagree|missing/i, describeFailure(result));
  assert.match(output(result), new RegExp(rows[4].runId), describeFailure(result));
});

// ==== before the files exist at all — named, not a crash ===================

test('figures.md does not exist yet — fails naming that file, not a stack trace', () => {
  const directory = tempDirectory();
  const rows = fiveMatchingRows();
  const missingFiguresPath = path.join(directory, 'figures.md');
  const baselinePath = path.join(directory, 'baseline.json');
  writeFileSync(baselinePath, JSON.stringify(baselineJson(rows), null, 2), 'utf8');

  const result = runAgreementCheck(missingFiguresPath, baselinePath);

  assert.notEqual(result.status, 0, describeFailure(result));
  assert.match(output(result), /figures\.md/, describeFailure(result));
});

test('baseline.json does not exist yet — fails naming that file, not a stack trace', () => {
  const directory = tempDirectory();
  const rows = fiveMatchingRows();
  const figuresPath = path.join(directory, 'figures.md');
  const missingBaselinePath = path.join(directory, 'baseline.json');
  writeFileSync(figuresPath, figuresMarkdown(rows), 'utf8');

  const result = runAgreementCheck(figuresPath, missingBaselinePath);

  assert.notEqual(result.status, 0, describeFailure(result));
  assert.match(output(result), /baseline\.json/, describeFailure(result));
});

// ==== S1 — an empty comparison is not agreement =============================

test("baseline.json's runs array is empty — refused, not a silent 0-run agreement", () => {
  const directory = tempDirectory();
  const figuresPath = path.join(directory, 'figures.md');
  const baselinePath = path.join(directory, 'baseline.json');
  writeFileSync(figuresPath, figuresMarkdown([]), 'utf8');
  writeFileSync(
    baselinePath,
    JSON.stringify(
      {
        measurement: 'overlay_draw',
        fixture: { tiles: 4, iterations: 10 },
        runner: 'ubuntu-latest, headless Chromium, software rasterisation',
        baselineP50Milliseconds: 0,
        toleranceMilliseconds: 6,
        rule: 'tolerance = 3 * sample stddev of first-complete-attempt p50 across the runs below (spec FR-018)',
        runs: [],
      },
      null,
      2,
    ),
    'utf8',
  );

  const result = runAgreementCheck(figuresPath, baselinePath);

  assert.notEqual(result.status, 0, describeFailure(result));
  assert.doesNotMatch(output(result), /agree — 0 run/i, describeFailure(result));
  assert.match(output(result), /disagree/i, describeFailure(result));
});

// ==== S1 — a malformed run record (missing p50) is never NaN-agreed =========

test("a baseline.json run has no numeric p50Milliseconds — refused, never silently 'agrees' via NaN comparison", () => {
  const directory = tempDirectory();
  const rows = fiveMatchingRows();
  const figuresPath = path.join(directory, 'figures.md');
  const baselinePath = path.join(directory, 'baseline.json');
  writeFileSync(figuresPath, figuresMarkdown(rows), 'utf8');
  const baseline = baselineJson(rows);
  // Corrupt one run's p50Milliseconds — Math.abs(x - undefined) is NaN, and
  // NaN > epsilon is false, which is exactly the silent-pass bug this guards.
  delete baseline.runs[2].p50Milliseconds;
  writeFileSync(baselinePath, JSON.stringify(baseline, null, 2), 'utf8');

  const result = runAgreementCheck(figuresPath, baselinePath);

  assert.notEqual(result.status, 0, describeFailure(result));
  assert.doesNotMatch(output(result), /^render-leg-baseline-agreement: agree/m, describeFailure(result));
});

// ==== S2 — only the `develop` baseline section is compared ==================

test('figures.md carries extra sections (preliminary runs, one-tile comparison) beyond the develop baseline table — their rows are ignored, not flagged as missing from baseline.json', () => {
  const directory = tempDirectory();
  const rows = fiveMatchingRows();
  const figuresPath = path.join(directory, 'figures.md');
  const baselinePath = path.join(directory, 'baseline.json');

  // Mirrors spec 144's real shape: a `develop` baseline table matching
  // baseline.json, followed by two more sections whose rows must NOT be
  // compared against baseline.json at all.
  const extraSections = [
    '',
    '## Preliminary, pre-fix (CI)',
    '',
    '| Run id | SHA | Samples | p50 | p95 | max | Observed T | Raw |',
    '| --- | --- | --- | --- | --- | --- | --- | --- |',
    `| [99999999999](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/99999999999) | \`${fortyHexSha(999)}\` | 40 | 999.00 ms | 999.00 ms | 999.00 ms | 32.00 ms | \`[...]\` ms |`,
    '',
    '## One-tile, pre-widening comparison',
    '',
    '| Run id | SHA | Samples | p50 | p95 | max | Observed T | Raw |',
    '| --- | --- | --- | --- | --- | --- | --- | --- |',
    `| [88888888888](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/88888888888) | \`${fortyHexSha(888)}\` | 40 | 888.00 ms | 888.00 ms | 888.00 ms | 32.00 ms | \`[...]\` ms |`,
    '',
  ];
  const markdown = figuresMarkdown(rows) + extraSections.join('\n');
  writeFileSync(figuresPath, markdown, 'utf8');
  writeFileSync(baselinePath, JSON.stringify(baselineJson(rows), null, 2), 'utf8');

  const result = runAgreementCheck(figuresPath, baselinePath);

  assert.equal(result.status, 0, describeFailure(result));
  assert.match(output(result), /agree/i, describeFailure(result));
  assert.doesNotMatch(output(result), /99999999999/, describeFailure(result));
  assert.doesNotMatch(output(result), /88888888888/, describeFailure(result));
});
