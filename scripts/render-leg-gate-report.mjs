#!/usr/bin/env node
// scripts/render-leg-gate-report.mjs
//
// Spec 225 §9.4/§9.5, #2770 — the `render-leg-gate` CI job's own script.
//
// T018 (plan.md §9.4, `figures.md` `## Verdict`) found FR-019 is not met on
// the real FR-022 window: 3σ = 25.65 ms (21-run set) / 26.11 ms (20-run set)
// against a 25 ms limit. Per FR-014/plan.md §9.4's report-only branch, no
// `baseline.json` is committed — a baseline that exists is a threshold that
// exists, and this data does not support one honestly. `render-leg-check.mjs`
// (T020) is therefore NOT invoked from this job: running it against a
// derived, uncommitted threshold would be a threshold by the back door.
//
// This script's entire purpose is visibility, not enforcement (#2770): it
// reads every shard's `render-leg-attempt-*.json` files (the union
// `render-leg-check.mjs`'s `discoverShards` already knows how to find, in the
// `actions/download-artifact` layout `ci.yml`'s `render-leg-gate` job
// produces) and renders one readable report, in one place, instead of a
// reviewer hunting through four shards' 14-day artifacts. It NEVER exits
// non-zero and NEVER throws past its own top-level catch — mirroring
// `render-leg-summary.mjs`'s NFR-001 contract exactly, for the same reason: a
// bug here must never redden an otherwise-green run, and on this branch
// nothing here is even supposed to redden one on purpose.
//
// Usage:
//   node scripts/render-leg-gate-report.mjs <shards-directory> [e2e-shards result]
//
// `[e2e-shards result]` is `${{ needs.e2e-shards.result }}` (FR-023's
// skipped/cancelled case) — read as a CLI argument, not re-derived from the
// filesystem, because "no shard directories" and "e2e-shards never ran" are
// different facts this job must not conflate.

import { appendFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';
import { readRenderLegRecords } from '../e2e/support/render-leg.ts';
import { discoverShards } from './render-leg-check.mjs';
import { twoDecimals } from './render-leg-summary.mjs';

const DEFAULT_SHARDS_DIRECTORY = 'shards';
const FR019_LIMIT_MILLISECONDS = 25;
// From specs/225-the-render-leg-ci-never-reads/figures.md's `## Verdict`
// (T018): the primary (21-run, FR-022-complete) dataset's 3σ. Re-derive from
// that file's raw samples before changing this constant — it is not this
// script's own measurement, and the ADR #2337 is waiting on may replace it.
const MEASURED_THREE_SIGMA_MILLISECONDS = 25.65;
const FIGURES_PATH = 'specs/225-the-render-leg-ci-never-reads/figures.md';

const HEADING = '### render leg gate (composite + render, section IV) — report-only, #2337';

function reportOnlyNotice() {
  return (
    `**report-only: FR-019 not met** (3σ = ${twoDecimals(MEASURED_THREE_SIGMA_MILLISECONDS)} ms ≥ ` +
    `${FR019_LIMIT_MILLISECONDS} ms); an ADR is needed on what CI may enforce on this leg before a ` +
    `threshold ships. See \`${FIGURES_PATH}\`. This job enforces nothing and cannot fail the build ` +
    '(plan.md §9.4) — it only collects every shard\'s figure into one place.'
  );
}

function renderAttemptLine(attempt) {
  if (!attempt.ok) {
    return `  - ${attempt.file}: could not be parsed (${attempt.error})`;
  }

  const { record } = attempt;
  const completeness = record.complete === true ? 'complete' : 'incomplete';
  if (record.count === 0) {
    return `  - attempt ${record.attempt}: no samples (${completeness})`;
  }
  return (
    `  - attempt ${record.attempt}: ${record.count} samples, p50 ${twoDecimals(record.p50)} ms, ` +
    `max ${twoDecimals(record.max)} ms (${completeness})`
  );
}

// Reads every discovered shard's attempt files (plan.md §8.1's
// `actions/download-artifact` layout, via `render-leg-check.mjs`'s own
// `discoverShards` — one definition of that layout, not two that could
// drift). Unlike `render-leg-check.mjs`, this never treats "records from more
// than one shard" as a failure: the span test running twice is a harness
// fault worth reporting, not one this report-only job is entitled to redden
// the build over.
function renderBody(shardsDirectory) {
  const shards = discoverShards(shardsDirectory);
  const activeShards = shards
    .map((shard) => ({ ...shard, attempts: readRenderLegRecords(shard.testResultsDirectory) }))
    .filter((shard) => shard.attempts.length > 0);

  if (activeShards.length === 0) {
    return [
      HEADING,
      '',
      reportOnlyNotice(),
      '',
      "no render-leg record in any shard — the `kiosk` project's span test " +
        '(`e2e/kiosk-shows-a-label-over-video.spec.ts`) did not run, or produced no attempt file.',
      '',
    ].join('\n');
  }

  const lines = [HEADING, '', reportOnlyNotice(), ''];
  for (const shard of activeShards) {
    lines.push(`**Shard ${shard.shard}:**`, '');
    for (const attempt of shard.attempts) {
      lines.push(renderAttemptLine(attempt));
    }
    lines.push('');
  }
  return lines.join('\n');
}

function renderSkippedUpstream(result) {
  return [
    HEADING,
    '',
    `unmeasured: e2e-shards ${result}, no figure was taken`,
    '',
    'This job still exits 0 — report-only, never enforcing (#2770).',
    '',
  ].join('\n');
}

function writeSummary(markdown) {
  const summaryPath = process.env.GITHUB_STEP_SUMMARY;
  if (summaryPath) {
    appendFileSync(summaryPath, `${markdown}\n`);
  } else {
    process.stdout.write(`${markdown}\n`);
  }
}

function renderInternalFailure() {
  return [
    HEADING,
    '',
    '⚠️ **This report-only job hit an internal error and could not render a figure for this run** — ' +
      "see the job's logs. It still exits 0: a bug here must never redden the build.",
    '',
  ].join('\n');
}

function main() {
  const shardsDirectory = process.argv[2] ?? DEFAULT_SHARDS_DIRECTORY;
  const upstreamResult = process.argv[3] ?? 'success';

  if (upstreamResult === 'skipped' || upstreamResult === 'cancelled') {
    writeSummary(renderSkippedUpstream(upstreamResult));
    return;
  }

  writeSummary(renderBody(shardsDirectory));
}

// Same CLI-entry-point guard as render-leg-summary.mjs, for the same reason:
// this file imports `discoverShards`/`twoDecimals` from sibling scripts, and
// those scripts must be able to import this one's exports in turn without
// running `main()` as a side effect.
const isMainModule = process.argv[1] !== undefined && import.meta.url === pathToFileURL(process.argv[1]).href;

if (isMainModule) {
  try {
    main();
  } catch (error) {
    // Never throws, never changes the job's outcome — this job's whole
    // purpose is visibility, not enforcement (#2770). Still try to leave a
    // section in the job summary — its own try/catch, because writeSummary
    // (e.g. an unwritable GITHUB_STEP_SUMMARY path) can throw too.
    try {
      writeSummary(renderInternalFailure());
    } catch (writeError) {
      process.stderr.write(
        `render-leg-gate-report: failed to write internal-failure summary: ${writeError.stack ?? writeError}\n`,
      );
    }
    process.stderr.write(`render-leg-gate-report: unexpected error: ${error.stack ?? error}\n`);
  }
}
