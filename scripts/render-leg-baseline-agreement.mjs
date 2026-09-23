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

// `figures.md` (plan.md §5, spec 144's format) carries three sections — the
// `develop` baseline table, a preliminary/pre-fix CI-runs table, and a
// one-tile/pre-widening comparison table. Only the first is ever meant to be
// compared against `baseline.json`; the other two are provenance for rows
// that never landed in `baseline.json` on purpose, not a disagreement.
// Scoped to headings starting "## `develop` baseline" (spec 144's own
// heading carries a trailing parenthetical, e.g. "(pre-fix, CI)" or
// "(four-tile fixture, CI)") and stopping at the next "## " heading.
const BASELINE_HEADING_PATTERN = /^##\s+`develop`\s+baseline\b/;
const HEADING_PATTERN = /^##\s+/;

function readFileOrFail(path, label) {
  try {
    return { ok: true, content: readFileSync(path, 'utf8') };
  } catch (error) {
    return { ok: false, reason: `could not read ${label} (${path}): ${error.message}` };
  }
}

// Returns a Map<runId, { runId, sha, p50 }> from figures.md's baseline
// table only — rows under any other "## " section are ignored (S2: a real
// figures.md has a preliminary-runs table and a one-tile comparison table
// that were never meant to agree with baseline.json).
function parseFigures(markdown) {
  const rows = new Map();
  let inBaselineSection = false;
  for (const line of markdown.split(/\r?\n/)) {
    const trimmed = line.trim();
    if (HEADING_PATTERN.test(trimmed)) {
      inBaselineSection = BASELINE_HEADING_PATTERN.test(trimmed);
      continue;
    }
    if (!inBaselineSection) continue;
    const match = TABLE_ROW_PATTERN.exec(trimmed);
    if (!match) continue;
    const [, runId, sha, p50] = match;
    rows.set(runId, { runId, sha: sha.toLowerCase(), p50: Number(p50) });
  }
  return rows;
}

// Returns { ok: true, rows: Map<runId, { runId, sha, p50 }> } from
// baseline.json's runs[], or { ok: false, reason } when the data is not
// something a comparison can be run against at all (S1): an empty/missing
// runs array is absence of data, not agreement, and a run with a
// non-finite p50Milliseconds is not comparable — `Math.abs(x - undefined)`
// is `NaN`, and `NaN > epsilon` is `false`, which would otherwise read as
// silent agreement.
function parseBaseline(baseline) {
  if (!Array.isArray(baseline.runs) || baseline.runs.length === 0) {
    return {
      ok: false,
      reason: "baseline.json has no 'runs' array, or it is empty — an empty comparison is not agreement",
    };
  }

  const rows = new Map();
  for (const run of baseline.runs) {
    if (run === null || typeof run !== 'object' || typeof run.p50Milliseconds !== 'number' || !Number.isFinite(run.p50Milliseconds)) {
      return {
        ok: false,
        reason: `baseline.json: run ${run?.runId ?? '?'} has no numeric p50Milliseconds — a malformed run record is not comparable`,
      };
    }
    rows.set(String(run.runId), {
      runId: String(run.runId),
      sha: typeof run.sha === 'string' ? run.sha.toLowerCase() : run.sha,
      p50: run.p50Milliseconds,
    });
  }
  return { ok: true, rows };
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
  const baselineResult = parseBaseline(baseline);
  if (!baselineResult.ok) {
    print(`render-leg-baseline-agreement: disagree: ${baselineResult.reason}`);
    process.exit(1);
  }
  const baselineRows = baselineResult.rows;

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
