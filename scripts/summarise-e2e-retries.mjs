#!/usr/bin/env node
// #2077 / spec 145 — implements option 1 of the issue only: surface retried
// tests as a signal, rather than removing retries (option 2) or leaving the
// asymmetry and recording why (option 3). Both stay open on #2077.
//
// `playwright.config.ts` retries a failed test twice in CI, and a test that
// passes on its second or third attempt is reported exactly like a test that
// passed first time — the run summary, the job's exit code and the merge gate
// carry no trace of the retry. This script reads Playwright's own JSON report
// and renders a Markdown section naming every test that needed a retry, to
// `$GITHUB_STEP_SUMMARY` (falling back to stdout so it is runnable off a
// runner). It never changes the job's outcome: it always exits 0, and never
// throws — see specs/145-a-retried-pass-says-so/plan.md.
//
// Never rendered: `stdout`, `stderr`, `error` or `attachments` from a test
// result. The e2e stack mints real Keycloak tokens, and a job summary is
// readable by anyone who can read the run (spec, *Auth*).

import { appendFileSync, readFileSync } from 'node:fs';

const DEFAULT_REPORT_PATH = 'test-results/e2e-report.json';

// Walks `suites[].specs[].tests[]`, recursing through nested `suites`
// (JSONReportSuite.suites) — a non-recursive walk silently under-counts.
function collectTests(suites, out = []) {
  for (const suite of suites ?? []) {
    for (const spec of suite.specs ?? []) {
      for (const test of spec.tests ?? []) {
        out.push({ test, spec, suite });
      }
    }
    collectTests(suite.suites, out);
  }
  return out;
}

function renderAttempts(results) {
  return (results ?? []).map((result) => result.status ?? 'unknown').join(' → ');
}

function renderSection(report) {
  const entries = collectTests(report.suites);
  const flakyEntries = entries.filter(({ test }) => test.status === 'flaky');

  const walkedCount = flakyEntries.length;
  const statsCount = report.stats?.flaky;

  const lines = ['### Playwright e2e — retried outcomes', ''];

  const noun = walkedCount === 1 ? 'test' : 'tests';
  lines.push(`**${walkedCount} ${noun} passed only on retry.**`, '');

  if (typeof statsCount === 'number' && statsCount !== walkedCount) {
    lines.push(
      `⚠️ **The tree walk and \`stats.flaky\` disagree.** The suite tree contains ${walkedCount} flaky ` +
        `test(s); \`stats.flaky\` reports ${statsCount}. This usually means the report shape changed — ` +
        'treat both figures as suspect rather than trusting either.',
      '',
    );
  }

  const expected = report.stats?.expected ?? 0;
  const unexpected = report.stats?.unexpected ?? 0;
  const skipped = report.stats?.skipped ?? 0;

  lines.push(
    '| Outcome | Tests |',
    '| --- | --- |',
    `| Passed first attempt | ${expected} |`,
    `| Passed only on retry | ${walkedCount} |`,
    `| Failed | ${unexpected} |`,
    `| Skipped | ${skipped} |`,
    '',
  );

  if (walkedCount > 0) {
    lines.push('| Test | Project | Attempts | Statuses |', '| --- | --- | --- | --- |');
    for (const { test, spec } of flakyEntries) {
      const attempts = test.results?.length ?? 0;
      lines.push(`| \`${spec.file}\` › ${spec.title} | ${test.projectName} | ${attempts} | ${renderAttempts(test.results)} |`);
    }
    lines.push('');
  }

  return lines.join('\n');
}

function renderMissing(reportPath) {
  return [
    '### Playwright e2e — retried outcomes',
    '',
    `⚠️ **No Playwright JSON report was found** at \`${reportPath}\`. A retried-test count cannot be ` +
      'reported for this run — this is not the same as zero retries.',
    '',
  ].join('\n');
}

function renderParseFailure(reportPath, error) {
  return [
    '### Playwright e2e — retried outcomes',
    '',
    `⚠️ **The Playwright JSON report at \`${reportPath}\` could not be parsed** (${error.message}). A ` +
      'retried-test count cannot be reported for this run — this is not the same as zero retries.',
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

function main() {
  const reportPath = process.argv[2] ?? DEFAULT_REPORT_PATH;

  let raw;
  try {
    raw = readFileSync(reportPath, 'utf8');
  } catch {
    writeSummary(renderMissing(reportPath));
    return;
  }

  let report;
  try {
    report = JSON.parse(raw);
  } catch (error) {
    writeSummary(renderParseFailure(reportPath, error));
    return;
  }

  writeSummary(renderSection(report));
}

try {
  main();
} catch (error) {
  // Never throws, never changes the job's outcome (FR-007) — a bug in this
  // script must not turn a green e2e run red.
  process.stderr.write(`summarise-e2e-retries: unexpected error: ${error.stack ?? error}\n`);
}
