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
      teardown: 'cleanup',
      use: { ...devices['Desktop Chrome'] },
      // **Both exclusions, and the second was learned the hard way.** Adding a
      // project does not take its files out of this one: the wall specs ran
      // here too, against the management app, and failed every time — eight
      // failures and nine minutes of retries for a suite that passed perfectly
      // in its own project on the same run.
      testIgnore: /(kiosk|wall)-.*\.spec\.ts/,
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
      testMatch: /kiosk-.*.spec.ts/,
      dependencies: ['seed'],
      teardown: 'cleanup',
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
      teardown: 'cleanup',
    },
    // Retires the cameras this run registered and archives the layouts it
    // published, so a long-lived dev database does not fill with rows pointing
    // at addresses nothing serves.
    //
    // **Referenced as each test project's `teardown` rather than depending on
    // them.** This was the other way round, for a reason that turned out not to
    // apply: the fear was that cleaning up while `kiosk` still ran would delete
    // the layout a test was watching. A `teardown` project does not do that — it
    // runs after the projects that name it have finished, and once, not per
    // project.
    //
    // What `dependencies` cost was the partial run. `--project=kiosk` pulled in
    // `seed` (a dependency) but never `cleanup` (which depended on projects the
    // run did not select), so every partial run registered two cameras and
    // retired none. The dev stack reached seventy that way. Now every project
    // that registers a camera names the cleanup that removes it, and
    // `--project=kiosk --list` shows `cleanup` scheduled.
    {
      name: 'cleanup',
      use: { ...devices['Desktop Chrome'], baseURL: 'http://localhost:5173' },
      testMatch: /.*.teardown.ts/,
    },
  ],
});
