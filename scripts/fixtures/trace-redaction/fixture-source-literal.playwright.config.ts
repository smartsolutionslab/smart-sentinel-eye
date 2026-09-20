// scripts/fixtures/trace-redaction/fixture-source-literal.playwright.config.ts
//
// Spec 186 / #2287, phase-6 reopen round 3. A throwaway Playwright config used
// ONLY by `make-leaky-report-source-literal.mjs` to run
// `fixture-source-literal.spec.ts` through the real Playwright Test runner
// and produce a real `index.html`, a real `report.json` and real
// `error-context.md` files. This is deliberately NOT `playwright.config.ts`
// and NOT `fixture.playwright.config.ts` — it is never referenced by
// `pnpm test:e2e`, `ci.yml`, or anything under `e2e/`. Kept as its own file
// (rather than extending `fixture.playwright.config.ts`) so regenerating this
// round's fixtures can never perturb the already-committed, already-green
// round-2 fixtures `fixture.playwright.config.ts` drives.
import { defineConfig } from '@playwright/test';
import path from 'node:path';

const here = __dirname;

export default defineConfig({
  testDir: here,
  testMatch: 'fixture-source-literal.spec.ts',
  fullyParallel: false,
  retries: 0,
  workers: 1,
  reporter: [
    ['html', { open: 'never', outputFolder: path.join(here, 'generated-source-literal', 'html-report') }],
    ['json', { outputFile: path.join(here, 'generated-source-literal', 'report.json') }],
  ],
  outputDir: path.join(here, 'generated-source-literal', 'test-results'),
  use: {
    trace: 'off',
    screenshot: 'off',
    video: 'off',
  },
});
