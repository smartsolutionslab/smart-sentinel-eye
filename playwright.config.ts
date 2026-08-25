import { defineConfig, devices } from '@playwright/test';

// ADR-0108: browser e2e runs against a live `aspire run` stack — Aspire owns
// orchestration, so there is no Playwright-managed webServer here. baseURL is
// management-web; bring the stack up first (`aspire run`), then `pnpm test:e2e`.
const isCI = process.env.CI === 'true';

export default defineConfig({
  testDir: './e2e',
  timeout: 60_000,
  // CI cold-loads a freshly booted stack (services still warming, JWKS fetch),
  // so allow more slack and a couple of retries; local runs stay strict.
  expect: { timeout: isCI ? 30_000 : 15_000 },
  fullyParallel: false,
  retries: isCI ? 2 : 0,
  workers: isCI ? 1 : undefined,
  reporter: isCI ? [['list'], ['html', { open: 'never' }]] : [['list']],
  use: {
    baseURL: 'http://localhost:5173',
    trace: 'on-first-retry',
    video: 'retain-on-failure',
    ignoreHTTPSErrors: true,
  },
  projects: [
    // Management app (:5173) — everything except the kiosk specs.
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
      testIgnore: /kiosk-.*\.spec\.ts/,
    },
    // Spec 041 — the kiosk cannot show a wall until a layout is published, and
    // an e2e stack seeds none. Its own project so the kiosk specs do not depend
    // on another spec having happened to publish one first. `.setup.ts` is
    // outside Playwright's default testMatch, so no other project picks it up.
    {
      name: 'seed',
      use: { ...devices['Desktop Chrome'], baseURL: 'http://localhost:5173' },
      testMatch: /.*\.setup\.ts/,
    },
    // Kiosk app (:5174) — spec 011: kiosk-* specs only.
    {
      name: 'kiosk',
      use: { ...devices['Desktop Chrome'], baseURL: 'http://localhost:5174' },
      testMatch: /kiosk-.*\.spec\.ts/,
      dependencies: ['seed'],
    },
  ],
});
