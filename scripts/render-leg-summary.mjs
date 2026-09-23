#!/usr/bin/env node
// scripts/render-leg-summary.mjs
//
// Spec 225 US1 / #2337 — "the render leg no CI run ever reads". The
// composite-and-render leg's figure already exists (`overlay_draw`, measured
// by `apps/shared/src/observability/kioskLatency.ts:135-142` and harvested by
// `e2e/kiosk-shows-a-label-over-video.spec.ts`), and `e2e/support/render-leg.ts`
// now writes it to `test-results/render-leg-attempt-<n>.json` per attempt. This
// script is the last step: reading those files back and rendering them to
// `$GITHUB_STEP_SUMMARY` (falling back to stdout so it is runnable off a
// runner) so a reviewer can read the figure without downloading a 14-day
// artifact.
//
// Sibling of `scripts/summarise-e2e-retries.mjs`, not an extension of it
// (plan.md §3): that script is contractually forbidden from rendering stdout
// (the e2e stack mints real Keycloak tokens), and this one renders no test
// output at all — only numbers this script itself computed or read from a
// record this repository's own instrument produced.
//
// NFR-001: this step adds no assertion. It never exits non-zero and never
// throws past its own top-level catch, mirroring summarise-e2e-retries.mjs's
// contract — a bug here must never redden an otherwise-green e2e run.

import { appendFileSync } from 'node:fs';
import { readRenderLegRecords } from '../e2e/support/render-leg.ts';

const DEFAULT_DIRECTORY = 'test-results';
const LEG_BUDGET_MILLISECONDS = 50;

function oneDecimal(value) {
  return Number.isInteger(value) ? value.toFixed(0) : value.toFixed(1);
}

function twoDecimals(value) {
  return value.toFixed(2);
}

function renderFrameInterval(record) {
  return `${twoDecimals(record.frameIntervalBeforeMilliseconds)} → ${twoDecimals(record.frameIntervalAfterMilliseconds)} ms/frame`;
}

function renderAttemptRow(attempt) {
  if (!attempt.ok) {
    return `| ${attempt.file} | ⚠️ could not be parsed (${attempt.error}) — this attempt's figure is unavailable, not zero | | | |`;
  }

  const { record } = attempt;
  if (record.count === 0) {
    return `| attempt ${record.attempt} | no samples | — | — | ${renderFrameInterval(record)} |`;
  }

  return (
    `| attempt ${record.attempt} | ${record.count} | ${oneDecimal(record.p50)} ms | ` +
    `${oneDecimal(record.max)} ms | ${renderFrameInterval(record)} |`
  );
}

function renderSection(attempts) {
  const lines = [
    '### Composite + render leg (`overlay_draw`) — spec 225 US1 / #2337',
    '',
    `Constitution §IV budget: ≤ ${LEG_BUDGET_MILLISECONDS} ms (overlay composite + render). ` +
      '**No threshold is asserted here** — this step only makes the figure readable; a regression gate is ' +
      'a separate story (US4).',
    '',
  ];

  if (attempts.length === 0) {
    lines.push(
      "no figure in this shard — the `kiosk` project's span test " +
        "(`e2e/kiosk-shows-a-label-over-video.spec.ts`) did not run in this shard, or produced no attempt file.",
      '',
    );
    return lines.join('\n');
  }

  lines.push(
    '| Attempt | Samples | p50 | max | Frame interval (before → after) |',
    '| --- | --- | --- | --- | --- |',
  );
  for (const attempt of attempts) {
    lines.push(renderAttemptRow(attempt));
  }
  lines.push('');

  // ADR-0123 consequence 3: a breach is read as cadence first, compositing
  // second, and this is the mechanical form of that rule — the reading is
  // beside the figure on every run, not only when someone remembers to ask.
  lines.push(
    'The frame interval is the floor `overlay_draw` cannot beat on this runner (ADR-0123): a shared ' +
      '`ubuntu-latest` box, headless Chromium, software rasterisation. Read a high figure as cadence first, ' +
      'compositing second.',
    '',
  );

  return lines.join('\n');
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
    '### Composite + render leg (`overlay_draw`) — spec 225 US1 / #2337',
    '',
    '⚠️ **The render-leg summariser hit an internal error and could not report a figure for this run** — ' +
      "this is not the same as no samples. See the job's logs for details.",
    '',
  ].join('\n');
}

function main() {
  const directory = process.argv[2] ?? DEFAULT_DIRECTORY;
  const attempts = readRenderLegRecords(directory);
  writeSummary(renderSection(attempts));
}

try {
  main();
} catch (error) {
  // Never throws, never changes the job's outcome (NFR-001) — a bug in this
  // script must not turn a green e2e run red. Still try to leave the section
  // in the job summary — its own try/catch, because writeSummary (e.g. an
  // unwritable GITHUB_STEP_SUMMARY path) can throw too.
  try {
    writeSummary(renderInternalFailure());
  } catch (writeError) {
    process.stderr.write(`render-leg-summary: failed to write internal-failure summary: ${writeError.stack ?? writeError}\n`);
  }
  process.stderr.write(`render-leg-summary: unexpected error: ${error.stack ?? error}\n`);
}
