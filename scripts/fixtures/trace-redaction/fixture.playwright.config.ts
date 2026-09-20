// scripts/fixtures/trace-redaction/fixture.playwright.config.ts
//
// Spec 186 / #2287, phase-6 reopen. A throwaway Playwright config used ONLY
// by `make-leaky-report.mjs` to run `fixture.spec.ts` through the real
// Playwright Test runner and produce a real `index.html` and a real
// `error-context.md`. This is deliberately NOT `playwright.config.ts` — it
// is never referenced by `pnpm test:e2e`, `ci.yml`, or anything under `e2e/`.
import { defineConfig } from '@playwright/test';
import path from 'node:path';

const here = __dirname;

export default defineConfig({
  testDir: here,
  testMatch: 'fixture.spec.ts',
  fullyParallel: false,
  retries: 0,
  workers: 1,
  reporter: [
    ['html', { open: 'never', outputFolder: path.join(here, 'generated', 'html-report') }],
    ['json', { outputFile: path.join(here, 'generated', 'report.json') }],
  ],
  outputDir: path.join(here, 'generated', 'test-results'),
  use: {
    trace: 'off',
    screenshot: 'off',
    video: 'off',
  },
});
