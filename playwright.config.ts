import { defineConfig, devices } from '@playwright/test';

// ADR-0108: browser e2e runs against a live `aspire run` stack — Aspire owns
// orchestration, so there is no Playwright-managed webServer here. baseURL is
// management-web; bring the stack up first (`aspire run`), then `pnpm test:e2e`.
export default defineConfig({
  testDir: './e2e',
  timeout: 60_000,
  expect: { timeout: 15_000 },
  fullyParallel: false,
  retries: 0,
  reporter: [['list']],
  use: {
    baseURL: 'http://localhost:5173',
    trace: 'on-first-retry',
    video: 'retain-on-failure',
    ignoreHTTPSErrors: true,
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});
