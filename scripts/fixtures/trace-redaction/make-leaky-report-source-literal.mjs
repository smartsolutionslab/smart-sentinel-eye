#!/usr/bin/env node
// scripts/fixtures/trace-redaction/make-leaky-report-source-literal.mjs
//
// Spec 186 / #2287, phase-6 reopen round 3 (2026-09-20). Produces the four
// REAL artifacts a third security review found the scrubber still misses:
//
//   - `leaky-report-source-literal.json` — the real Playwright JSON reporter
//     output for all four tests in `fixture-source-literal.spec.ts` (B1's
//     `error.snippet` / `errors[].message` fields; S3's `Expected:`-side
//     credential).
//   - `leaky-report-source-literal-index.html` — the real self-contained HTML
//     report (B1's per-test `errors[].codeframe`, for the test whose own
//     failure has nothing to do with the leaked constant).
//   - `leaky-error-context-source-literal-far.md` — the real `error-context.md`
//     for the "far" test (B1's `# Test source` section).
//   - `leaky-error-context-source-literal-unlabelled.md` — the real
//     `error-context.md` for the unlabelled-password test (S2's `# Page
//     snapshot` section).
//
// Mirrors `make-leaky-report.mjs`'s approach exactly (a real
// `playwright test -c <throwaway config>` pass against a throwaway spec that
// is never part of `e2e/` and never run by `pnpm test:e2e` or CI), against
// its own throwaway config (`fixture-source-literal.playwright.config.ts`)
// and its own `generated-source-literal/` working directory, so regenerating
// this round's fixtures can never perturb the already-committed round-2 ones.
//
// **Requires no browser installation beyond what `pnpm test:e2e:install`
// already provides.** **Run by hand, not in CI** — regenerate only when
// `@playwright/test` is version-bumped, then re-run
// `scripts/scrub-playwright-artifacts.test.mjs`'s presence assertions to
// confirm the regenerated files still carry the sentinels in the places this
// file's header comment (and `spec.md`'s phase-6 reopen round 3) names. This
// script is not part of `pnpm test:guards` and is never invoked by CI.
//
//   node scripts/fixtures/trace-redaction/make-leaky-report-source-literal.mjs

import { spawnSync } from 'node:child_process';
import { copyFileSync, readdirSync, rmSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(here, '..', '..', '..');
const configPath = path.join(here, 'fixture-source-literal.playwright.config.ts');

const generatedDir = path.join(here, 'generated-source-literal');
const htmlReportDir = path.join(generatedDir, 'html-report');
const testResultsDir = path.join(generatedDir, 'test-results');

function findErrorContextFile(rootDir, needleInTitle) {
  for (const entry of readdirSync(rootDir, { withFileTypes: true })) {
    if (entry.isDirectory() && entry.name.includes(needleInTitle)) {
      return path.join(rootDir, entry.name, 'error-context.md');
    }
  }
  throw new Error(`could not find a test-results directory matching "${needleInTitle}" under ${rootDir}`);
}

function main() {
  rmSync(generatedDir, { recursive: true, force: true });

  // `pnpm` resolves to a `.cmd` shim on Windows, which Node's spawnSync
  // cannot invoke directly without a shell — a single pre-joined command
  // string avoids the args-array-plus-shell footgun `spawnSync` warns about.
  const result = spawnSync(`pnpm exec playwright test -c "${configPath}"`, {
    cwd: repositoryRoot,
    encoding: 'utf8',
    shell: true,
    env: {
      ...process.env,
      // Keeps the committed fixture small: the appended base64 report-data
      // zip is what this fixture needs, not a copy of the report.js /
      // report.css UI bundle.
      PLAYWRIGHT_HTML_DO_NOT_INLINE_ASSETS: '1',
    },
  });

  if (result.error) {
    console.error(`make-leaky-report-source-literal: failed to launch Playwright: ${result.error.message}`);
    process.exit(1);
  }

  // All four fixture tests are meant to fail — that IS the fixture. A clean
  // (exit 0) run means the spec stopped failing and produced none of the
  // artifacts this generator needs.
  if (result.status === 0) {
    console.error('make-leaky-report-source-literal: fixture-source-literal.spec.ts passed instead of failing — nothing to capture. Aborting.');
    console.error(result.stdout);
    process.exit(1);
  }

  copyFileSync(path.join(htmlReportDir, 'index.html'), path.join(here, 'leaky-report-source-literal-index.html'));
  copyFileSync(path.join(generatedDir, 'report.json'), path.join(here, 'leaky-report-source-literal.json'));

  const farErrorContextSource = findErrorContextFile(testResultsDir, 'large-codeframe');
  copyFileSync(farErrorContextSource, path.join(here, 'leaky-error-context-source-literal-far.md'));

  const unlabelledErrorContextSource = findErrorContextFile(testResultsDir, 'accessible-name');
  copyFileSync(unlabelledErrorContextSource, path.join(here, 'leaky-error-context-source-literal-unlabelled.md'));

  rmSync(generatedDir, { recursive: true, force: true });

  console.log(`wrote ${path.join(here, 'leaky-report-source-literal-index.html')}`);
  console.log(`wrote ${path.join(here, 'leaky-report-source-literal.json')}`);
  console.log(`wrote ${path.join(here, 'leaky-error-context-source-literal-far.md')}`);
  console.log(`wrote ${path.join(here, 'leaky-error-context-source-literal-unlabelled.md')}`);
}

main();
