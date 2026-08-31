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
    // Wall display (:5175) — spec 052. The same application in wall mode, so
    // it signs in as `kiosk-wall` and asks for a grant that outlives the
    // session ceiling. A separate project because the mode is fixed when the
    // dev server starts: it cannot be toggled inside a test, and a test that
    // drove :5174 would silently exercise the ordinary kiosk instead.
    {
      name: 'wall',
      use: { ...devices['Desktop Chrome'], baseURL: 'http://localhost:5175' },
      testMatch: /wall-.*.spec.ts/,
      dependencies: ['seed'],
    },
    // Issue 1895 — retires the cameras this run registered, so a long-lived dev
    // database does not fill with rows pointing at addresses nothing serves.
    //
    // `dependencies` rather than a project-level `teardown`, and the ordering is
    // the reason: the seed camera is bound to the published layout the kiosk
    // opens, so cleaning it while `kiosk` is still running would pull a wall out
    // from under a test. Depending on both projects makes "after everything that
    // registers a camera" explicit instead of implied.
    //
    // A `--project=chromium` run therefore does NOT clean up. That is the
    // trade for not deleting the kiosk's layout mid-run; a full run does.
    {
      name: 'cleanup',
      use: { ...devices['Desktop Chrome'], baseURL: 'http://localhost:5173' },
      testMatch: /.*\.teardown\.ts/,
      dependencies: ['chromium', 'kiosk', 'wall'],
    },
  ],
});
