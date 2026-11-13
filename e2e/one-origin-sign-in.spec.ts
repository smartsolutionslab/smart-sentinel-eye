import { test, expect, type Locator, type Page } from '@playwright/test';

// The file name must not contain "wall-" or "kiosk-": the config's patterns are unanchored, so either one sends a spec to a kiosk
// project and hides it from the console's.
test.use({ baseURL: 'http://localhost:5180' });

const OPERATOR = { user: 'operator', password: 'Cobalt-Meadow-Ripple-24' };
const WALL_DISPLAY = { user: 'wall-munich', password: 'Copper-Lantern-Drift-47' };

const consoleShell = (page: Page) => page.getByRole('heading', { name: 'Cameras', exact: true });

// The picker has its heading only when a layout is published (the `seed` project does that for the kiosk projects, not for this one) and
// says so otherwise; either is the signed-in app. A failed kiosk or wall display shows neither: it sits on "Reconnecting".
const picker = (page: Page) =>
  page.getByRole('heading', { name: 'Pick a layout', exact: true }).or(page.getByText('No layouts published yet.'));

async function signInAt(
  page: Page,
  path: string,
  account: { user: string; password: string },
  firstScreen: (page: Page) => Locator,
) {
  await page.goto(path);
  await page.getByRole('button', { name: /sign in/i }).click();
  await page.locator('#username').fill(account.user);
  await page.locator('#password').fill(account.password);
  await page.locator('#kc-login').click();

  await expect(firstScreen(page)).toBeVisible({ timeout: 30_000 });
  await expect(page.getByRole('heading', { name: /sign-in failed/i })).toHaveCount(0);
}

test('the operator console signs in at the one origin', async ({ page }) => {
  test.setTimeout(120_000);
  await signInAt(page, '/', OPERATOR, consoleShell);
});

test('the kiosk signs in at /kiosk/ on the one origin', async ({ page }) => {
  test.setTimeout(120_000);
  await signInAt(page, '/kiosk/', OPERATOR, picker);
});

test('the wall display signs in at /wall/ on the one origin', async ({ page }) => {
  test.setTimeout(120_000);
  await signInAt(page, '/wall/', WALL_DISPLAY, picker);
});
