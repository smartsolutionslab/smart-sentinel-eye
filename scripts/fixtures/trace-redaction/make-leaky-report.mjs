#!/usr/bin/env node
// scripts/fixtures/trace-redaction/make-leaky-report.mjs
//
// Spec 186 / #2287, phase-6 reopen. Produces `leaky-report-index.html` and
// `leaky-error-context.md`: two REAL artifacts of the two shapes the phase-6
// security review found the scrubber never classifies as a candidate —
// Playwright's self-contained HTML report and its per-failure ARIA
// page-snapshot file.
//
// Unlike `make-leaky-trace.mjs` (which drives the raw `tracing` API
// directly), this runs an actual Playwright **Test runner** pass —
// `playwright test -c fixture.playwright.config.ts` — against the throwaway
// `fixture.spec.ts` in this same directory. That spec is never part of
// `e2e/` and is never run by `pnpm test:e2e` or CI; it exists only so this
// generator has two genuinely failing tests to point the real HTML + JSON
// reporters at. Every secret it plants is a `SSE_FAKE_*` sentinel
// (`sentinels.mjs`); nothing real enters either output file.
//
// **Requires no browser installation beyond what `pnpm test:e2e:install`
// already provides.** **Run by hand, not in CI** — regenerate these fixtures
// only when `@playwright/test` is version-bumped, then re-run
// `scripts/scrub-playwright-artifacts.test.mjs`'s presence assertions to
// confirm the regenerated files still carry the password sentinel in the
// places this file's header comment (and `spec.md`'s phase-6 reopen) names.
// This script is not part of `pnpm test:guards` and is never invoked by CI.
//
//   node scripts/fixtures/trace-redaction/make-leaky-report.mjs

import { spawnSync } from 'node:child_process';
import { copyFileSync, readdirSync, rmSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(here, '..', '..', '..');
const configPath = path.join(here, 'fixture.playwright.config.ts');

// Matches `fixture.playwright.config.ts`'s own `reporter`/`outputDir` paths —
// a working directory local to this fixture set, removed once the two
// committed files are copied out of it.
const generatedDir = path.join(here, 'generated');
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
    console.error(`make-leaky-report: failed to launch Playwright: ${result.error.message}`);
    process.exit(1);
  }

  // Both fixture tests are meant to fail — that IS the fixture. A clean
  // (exit 0) run means the spec stopped failing and produced neither
  // artifact this generator needs.
  if (result.status === 0) {
    console.error('make-leaky-report: fixture.spec.ts passed instead of failing — nothing to capture. Aborting.');
    console.error(result.stdout);
    process.exit(1);
  }

  copyFileSync(path.join(htmlReportDir, 'index.html'), path.join(here, 'leaky-report-index.html'));

  const errorContextSource = findErrorContextFile(testResultsDir, 'page-sna');
  copyFileSync(errorContextSource, path.join(here, 'leaky-error-context.md'));

  rmSync(generatedDir, { recursive: true, force: true });

  console.log(`wrote ${path.join(here, 'leaky-report-index.html')}`);
  console.log(`wrote ${path.join(here, 'leaky-error-context.md')}`);
}

main();
