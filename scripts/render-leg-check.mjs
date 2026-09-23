#!/usr/bin/env node
// scripts/render-leg-check.mjs
//
// Spec 225 §9 / plan.md §8.4, T020 — the composite-and-render regression gate
// (US4, closes #2337). Reads the committed baseline
// (`specs/225-the-render-leg-ci-never-reads/baseline.json`) and the union of
// every shard's `render-leg-attempt-*.json` files, and decides pass /
// regressed / unmeasured / refused per plan.md §8.4's decision table. Runs as
// its own `render-leg-gate` CI job (plan.md §8.1), *outside* the Playwright
// test, so `retries: isCI ? 2 : 0` cannot re-roll the verdict (FR-011).
//
// Usage:
//   node scripts/render-leg-check.mjs <shards-directory> <baseline.json path>
//
// `<shards-directory>` holds one subdirectory per shard artifact
// (`playwright-report-<n>-of-4`), each with its own `test-results/` —
// `actions/download-artifact`'s own layout for a glob pattern, mirrored here
// rather than invented (plan.md §8.1).
//
// Unlike `render-leg-summary.mjs`, this script's whole job is to redden the
// build on a regression — it is the one script in this family that is
// SUPPOSED to exit non-zero. It never falls back to a permissive default: a
// missing or malformed baseline, or a shard record it cannot make sense of,
// fails the check rather than passing silently (FR-012, FR-013).

import { readFileSync, readdirSync } from 'node:fs';
import { join } from 'node:path';
import { readRenderLegRecords } from '../e2e/support/render-leg.ts';

const SHARD_DIRECTORY_PATTERN = /^playwright-report-(\d+)-of-4$/;
const MINIMUM_BASELINE_RUNS = 5; // FR-018
const SHA_PATTERN = /^[0-9a-f]{40}$/i;
const AGREEMENT_EPSILON = 0.01; // baseline.json's self-check precision (plan.md §8.3)

function twoDecimals(value) {
  return value.toFixed(2);
}

function print(message) {
  process.stdout.write(`${message}\n`);
}

// ---- baseline.json: read and self-verify (plan.md §8.3) -------------------

function readBaseline(baselinePath) {
  let raw;
  try {
    raw = readFileSync(baselinePath, 'utf8');
  } catch (error) {
    return { ok: false, reason: `could not read ${baselinePath}: ${error.message}` };
  }

  let baseline;
  try {
    baseline = JSON.parse(raw);
  } catch (error) {
    return { ok: false, reason: `${baselinePath} is not valid JSON: ${error.message}` };
  }

  if (baseline === null || typeof baseline !== 'object' || !Array.isArray(baseline.runs)) {
    return { ok: false, reason: `${baselinePath} has no 'runs' array` };
  }
  if (baseline.runs.length < MINIMUM_BASELINE_RUNS) {
    return {
      ok: false,
      reason: `${baselinePath} carries only ${baseline.runs.length} run(s); FR-018 needs at least ${MINIMUM_BASELINE_RUNS}`,
    };
  }
  for (const run of baseline.runs) {
    if (typeof run.sha !== 'string' || !SHA_PATTERN.test(run.sha)) {
      return {
        ok: false,
        reason: `${baselinePath}: run ${run.runId ?? '?'}'s sha ('${run.sha}') is not 40 hex characters`,
      };
    }
    if (typeof run.p50Milliseconds !== 'number' || !Number.isFinite(run.p50Milliseconds)) {
      return { ok: false, reason: `${baselinePath}: run ${run.runId ?? '?'} has no numeric p50Milliseconds` };
    }
  }
  if (typeof baseline.toleranceMilliseconds !== 'number' || !(baseline.toleranceMilliseconds > 0)) {
    return { ok: false, reason: `${baselinePath}: toleranceMilliseconds must be a positive number` };
  }
  if (typeof baseline.baselineP50Milliseconds !== 'number') {
    return { ok: false, reason: `${baselinePath}: baselineP50Milliseconds must be a number` };
  }

  // Self-verification: nobody can hand-edit the mean or the tolerance without
  // the derivation failing (plan.md §8.3's closing paragraph).
  const p50s = baseline.runs.map((run) => run.p50Milliseconds);
  const mean = p50s.reduce((sum, value) => sum + value, 0) / p50s.length;
  if (Math.abs(mean - baseline.baselineP50Milliseconds) > AGREEMENT_EPSILON) {
    return {
      ok: false,
      reason:
        `${baselinePath}: baselineP50Milliseconds (${twoDecimals(baseline.baselineP50Milliseconds)}) does not match ` +
        `the mean of runs[].p50Milliseconds (${twoDecimals(mean)}) — the file does not check out against itself`,
    };
  }

  // Sample standard deviation (n-1 denominator, FR-018), 3σ tolerance rule.
  const variance = p50s.reduce((sum, value) => sum + (value - mean) ** 2, 0) / (p50s.length - 1);
  const derivedTolerance = 3 * Math.sqrt(variance);
  if (Math.abs(derivedTolerance - baseline.toleranceMilliseconds) > AGREEMENT_EPSILON) {
    return {
      ok: false,
      reason:
        `${baselinePath}: toleranceMilliseconds (${twoDecimals(baseline.toleranceMilliseconds)}) does not match 3 * ` +
        `sample stddev of runs[].p50Milliseconds (${twoDecimals(derivedTolerance)}) — the file does not check out against itself`,
    };
  }

  return { ok: true, baseline };
}

// ---- shard discovery --------------------------------------------------------

// Mirrors `actions/download-artifact`'s own layout for a glob pattern
// (plan.md §8.1) — one `playwright-report-<n>-of-4` directory per shard,
// each carrying its own `test-results/`.
function discoverShards(shardsDirectory) {
  let entries;
  try {
    entries = readdirSync(shardsDirectory, { withFileTypes: true });
  } catch {
    return [];
  }

  const shards = [];
  for (const entry of entries) {
    if (!entry.isDirectory()) continue;
    const match = SHARD_DIRECTORY_PATTERN.exec(entry.name);
    if (!match) continue;
    shards.push({
      shard: match[1],
      testResultsDirectory: join(shardsDirectory, entry.name, 'test-results'),
    });
  }
  return shards;
}

// ---- the decision (plan.md §8.4) --------------------------------------------

function main() {
  const [, , shardsDirectoryArgument, baselinePathArgument] = process.argv;
  if (!shardsDirectoryArgument || !baselinePathArgument) {
    print('render-leg-check: usage: node scripts/render-leg-check.mjs <shards-directory> <baseline.json path>');
    process.exit(1);
  }

  const baselineResult = readBaseline(baselinePathArgument);
  if (!baselineResult.ok) {
    print(`render-leg-check: baseline refused: ${baselineResult.reason}`);
    process.exit(1);
  }
  const { baseline } = baselineResult;
  const threshold = baseline.baselineP50Milliseconds + baseline.toleranceMilliseconds;

  const shards = discoverShards(shardsDirectoryArgument);
  const activeShards = [];
  for (const shard of shards) {
    const attempts = readRenderLegRecords(shard.testResultsDirectory);
    if (attempts.length > 0) {
      activeShards.push({ ...shard, attempts });
    }
  }

  if (activeShards.length === 0) {
    print('render-leg-check: unmeasured: no render-leg record in any shard');
    process.exit(1);
  }

  if (activeShards.length > 1) {
    print(
      `render-leg-check: unmeasured: records from ${activeShards.length} shards, expected 1 ` +
        `(the span test ran more than once — a harness fault, not a figure) ` +
        `(shards: ${activeShards.map((active) => active.shard).join(', ')})`,
    );
    process.exit(1);
  }

  const attempts = activeShards[0].attempts;

  // Any unreadable file, or any record missing 'complete', fails the whole
  // check as unmeasured — a malformed record is never quietly skipped in
  // favour of a sibling that happens to parse (plan.md §8.4).
  for (const attempt of attempts) {
    if (!attempt.ok) {
      print(`render-leg-check: unmeasured: ${attempt.file} malformed: ${attempt.error}`);
      process.exit(1);
    }
    if (typeof attempt.record.complete !== 'boolean') {
      print(`render-leg-check: unmeasured: ${attempt.file} malformed: missing 'complete' field`);
      process.exit(1);
    }
  }

  const sorted = [...attempts].sort((a, b) => a.record.attempt - b.record.attempt);
  const completeAttempts = sorted.filter((attempt) => attempt.record.complete === true);

  // The checker always prints every attempt's completeness, so a verdict is
  // never taken silently — but never the p50 of a SKIPPED attempt: that
  // figure was never evaluated, and printing it beside the verdict would read
  // as though it had been (plan.md §8.4 / #2077).
  if (completeAttempts.length === 0) {
    const lines = ['render-leg-check: unmeasured: every attempt incomplete', ''];
    for (const attempt of sorted) {
      lines.push(`  attempt ${attempt.record.attempt}: incomplete`);
    }
    print(lines.join('\n'));
    process.exit(1);
  }

  // "First" = lowest attempt number — a retry must not re-roll the verdict
  // (FR-011). Everything before it is necessarily incomplete, by construction.
  const chosen = completeAttempts[0];
  const skipped = sorted.filter((attempt) => attempt.record.attempt < chosen.record.attempt);

  const observedP50 = chosen.record.p50;
  if (typeof observedP50 !== 'number') {
    print(
      `render-leg-check: unmeasured: attempt ${chosen.record.attempt} is marked complete but carries no p50 ` +
        '(zero samples) — this is a harness contradiction, not a figure',
    );
    process.exit(1);
  }

  const lines = [];
  for (const attempt of skipped) {
    lines.push(
      `  attempt ${attempt.record.attempt}: incomplete — skipped (not evaluated; a retry must not re-roll the verdict)`,
    );
  }
  lines.push(
    `  attempt ${chosen.record.attempt}: p50 ${twoDecimals(observedP50)} ms (complete) — evaluated (first complete attempt)`,
    '',
    `  baseline p50: ${twoDecimals(baseline.baselineP50Milliseconds)} ms`,
    `  tolerance: ${twoDecimals(baseline.toleranceMilliseconds)} ms`,
    `  baseline + tolerance: ${twoDecimals(threshold)} ms`,
    `  frame interval (before → after): ${twoDecimals(chosen.record.frameIntervalBeforeMilliseconds)} → ` +
      `${twoDecimals(chosen.record.frameIntervalAfterMilliseconds)} ms/frame`,
  );

  // ">", not "≥" (plan.md §8.4) — a figure exactly at the threshold passes.
  if (observedP50 > threshold) {
    const excess = observedP50 - threshold;
    lines.unshift('render-leg-check: regressed');
    lines.push(
      `  excess: ${twoDecimals(excess)} ms over baseline + tolerance`,
      '',
      "  ADR-0123: read a high figure as cadence first, compositing second — check the frame interval " +
        "above before this tile's own render cost.",
    );
    print(lines.join('\n'));
    process.exit(1);
  }

  const margin = threshold - observedP50;
  lines.unshift('render-leg-check: within tolerance');
  lines.push(`  margin: ${twoDecimals(margin)} ms under baseline + tolerance`);
  print(lines.join('\n'));
  process.exit(0);
}

main();
