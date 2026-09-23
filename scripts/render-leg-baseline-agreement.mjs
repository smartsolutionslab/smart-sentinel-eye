#!/usr/bin/env node
// scripts/render-leg-baseline-agreement.mjs
//
// Spec 225 §9 / plan.md §8.3's closing paragraph, T019b — `figures.md`'s
// baseline table and `baseline.json` must carry the same run ids, SHAs and
// p50s. `render-leg-check.mjs`'s own self-verification checks `baseline.json`
// against *itself* (its stated mean/tolerance must equal what its own
// `runs[]` compute to); this script checks the *data* (`baseline.json`)
// against the *prose* (`figures.md`) — two places recording the same fact,
// read separately, is exactly how §II and §IV both drifted (tasks.md T019b).
//
// Usage:
//   node scripts/render-leg-baseline-agreement.mjs <figures.md path> <baseline.json path>
//
// Exits 0 when every run in `baseline.json` has a matching row in
// `figures.md`'s baseline table (same run id, same SHA, same p50, to two
// decimal places — `figures.md`'s own stated precision, plan.md §5) and vice
// versa; exits non-zero and names the disagreement otherwise. Never falls
// back to a permissive default — a file it cannot read or parse fails the
// check, naming that file, rather than passing silently.

import { readFileSync } from 'node:fs';

const AGREEMENT_EPSILON = 0.01;

function print(message) {
  process.stdout.write(`${message}\n`);
}

// `figures.md`'s baseline table (plan.md §5):
//   | [<id>](https://…/actions/runs/<id>) | `<sha>` | <n> | <p50> ms | … |
// Parsed loosely on purpose: only the run id, SHA and p50 columns matter
// here, and the exact column count/order of the rest is figures.md's own
// business, not this script's.
const TABLE_ROW_PATTERN = /^\|\s*\[(\d+)\]\([^)]*\)\s*\|\s*`([0-9a-fA-F]{40})`\s*\|\s*\d+\s*\|\s*([\d.]+)\s*ms\s*\|/;

function readFileOrFail(path, label) {
  try {
    return { ok: true, content: readFileSync(path, 'utf8') };
  } catch (error) {
    return { ok: false, reason: `could not read ${label} (${path}): ${error.message}` };
  }
}

// Returns a Map<runId, { runId, sha, p50 }> from figures.md's baseline table.
function parseFigures(markdown) {
  const rows = new Map();
  for (const line of markdown.split(/\r?\n/)) {
    const match = TABLE_ROW_PATTERN.exec(line.trim());
    if (!match) continue;
    const [, runId, sha, p50] = match;
    rows.set(runId, { runId, sha: sha.toLowerCase(), p50: Number(p50) });
  }
  return rows;
}

// Returns a Map<runId, { runId, sha, p50 }> from baseline.json's runs[].
function parseBaseline(baseline) {
  const rows = new Map();
  for (const run of baseline.runs ?? []) {
    rows.set(String(run.runId), {
      runId: String(run.runId),
      sha: typeof run.sha === 'string' ? run.sha.toLowerCase() : run.sha,
      p50: run.p50Milliseconds,
    });
  }
  return rows;
}

function describe(row) {
  return `runId ${row.runId}, sha ${row.sha}, p50 ${row.p50?.toFixed?.(2) ?? row.p50} ms`;
}

function compareRows(figuresRow, baselineRow) {
  const problems = [];
  if (figuresRow.sha !== baselineRow.sha) {
    problems.push(`sha: figures.md has '${figuresRow.sha}', baseline.json has '${baselineRow.sha}'`);
  }
  if (Math.abs(figuresRow.p50 - baselineRow.p50) > AGREEMENT_EPSILON) {
    problems.push(`p50: figures.md has ${figuresRow.p50.toFixed(2)}, baseline.json has ${baselineRow.p50.toFixed(2)}`);
  }
  return problems;
}

function main() {
  const [, , figuresPathArgument, baselinePathArgument] = process.argv;
  if (!figuresPathArgument || !baselinePathArgument) {
    print('render-leg-baseline-agreement: usage: node scripts/render-leg-baseline-agreement.mjs <figures.md path> <baseline.json path>');
    process.exit(1);
  }

  const figuresFile = readFileOrFail(figuresPathArgument, 'figures.md');
  if (!figuresFile.ok) {
    print(`render-leg-baseline-agreement: disagree: ${figuresFile.reason}`);
    process.exit(1);
  }

  const baselineFile = readFileOrFail(baselinePathArgument, 'baseline.json');
  if (!baselineFile.ok) {
    print(`render-leg-baseline-agreement: disagree: ${baselineFile.reason}`);
    process.exit(1);
  }

  let baseline;
  try {
    baseline = JSON.parse(baselineFile.content);
  } catch (error) {
    print(`render-leg-baseline-agreement: disagree: ${baselinePathArgument} is not valid JSON: ${error.message}`);
    process.exit(1);
  }

  const figuresRows = parseFigures(figuresFile.content);
  const baselineRows = parseBaseline(baseline);

  const mismatches = [];

  for (const [runId, baselineRow] of baselineRows) {
    const figuresRow = figuresRows.get(runId);
    if (!figuresRow) {
      mismatches.push(`run ${runId} is in baseline.json (${describe(baselineRow)}) but has no matching row in figures.md`);
      continue;
    }
    for (const problem of compareRows(figuresRow, baselineRow)) {
      mismatches.push(`run ${runId} mismatch — ${problem}`);
    }
  }

  for (const [runId, figuresRow] of figuresRows) {
    if (!baselineRows.has(runId)) {
      mismatches.push(`run ${runId} is in figures.md (${describe(figuresRow)}) but is missing from baseline.json`);
    }
  }

  if (mismatches.length > 0) {
    print('render-leg-baseline-agreement: disagree');
    for (const mismatch of mismatches) {
      print(`  ${mismatch}`);
    }
    process.exit(1);
  }

  print(
    `render-leg-baseline-agreement: agree — ${baselineRows.size} run(s) carry the same run id, sha and p50 in both files`,
  );
  process.exit(0);
}

main();
