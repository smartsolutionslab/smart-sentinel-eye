import { test, expect, type Locator, type Page } from '@playwright/test';

// The file name must not contain "wall-" or "kiosk-": the config's patterns are unanchored, so either one sends a spec to a kiosk
// project and hides it from the console's.
test.use({ baseURL: 'http://localhost:5180' });

const OPERATOR = { user: 'operator', password: 'Cobalt-Meadow-Ripple-24' };
const WALL_DISPLAY = { user: 'wall-munich', password: 'Copper-Lantern-Drift-47' };

// The console's "Cameras" heading is drawn by a federated remote, so a missing heading alone cannot tell a broken sign-in from a broken remote.
const consoleShell = (page: Page) => page.getByRole('heading', { name: 'Cameras', exact: true });

// The picker has its heading only when a layout is published (the `seed` project does that for the kiosk projects, not for this one) and
// says so otherwise; either is the signed-in app. A failed kiosk or wall display shows neither: it sits on "Reconnecting".
const picker = (page: Page) =>
  page.getByRole('heading', { name: 'Pick a layout', exact: true }).or(page.getByText('No layouts published yet.'));

// The client the stored access token was issued to (`azp`), or null while there is none. The console keeps its grant in sessionStorage, the
// kiosks in localStorage. Only the claim leaves the page, never the token.
async function signedInClient(page: Page): Promise<string | null> {
  return page.evaluate(() => {
    for (const store of [window.sessionStorage, window.localStorage]) {
      const key = Object.keys(store).find((candidate) => candidate.startsWith('oidc.user:'));
      if (key === undefined) continue;
      const accessToken = (JSON.parse(store.getItem(key) ?? 'null') as { access_token?: unknown } | null)?.access_token;
      const payload = typeof accessToken === 'string' ? accessToken.split('.')[1] : undefined;
      if (payload === undefined) continue;
      const base64 = payload.replace(/-/g, '+').replace(/_/g, '/');
      const claims = JSON.parse(atob(base64.padEnd(Math.ceil(base64.length / 4) * 4, '='))) as { azp?: unknown };
      return typeof claims.azp === 'string' ? claims.azp : null;
    }
    return null;
  });
}

async function signInAt(
  page: Page,
  path: string,
  account: { user: string; password: string },
  client: string,
  firstScreen: (page: Page) => Locator,
) {
  await page.goto(path);
  await page.getByRole('button', { name: /sign in/i }).click();
  await page.locator('#username').fill(account.user);
  await page.locator('#password').fill(account.password);
  await page.locator('#kc-login').click();

  // Sign-in first, and as which client, so a red says which half broke: the token exchange, or what loads after it.
  await expect
    .poll(() => signedInClient(page), {
      message: `a token issued to '${client}' should be stored once sign-in completes`,
      timeout: 30_000,
    })
    .toBe(client);
  await expect(firstScreen(page)).toBeVisible({ timeout: 30_000 });
}

test('the operator console signs in at the one origin', async ({ page }) => {
  test.setTimeout(120_000);
  await signInAt(page, '/', OPERATOR, 'management-web', consoleShell);
});

test('the kiosk signs in at /kiosk/ on the one origin', async ({ page }) => {
  test.setTimeout(120_000);
  await signInAt(page, '/kiosk/', OPERATOR, 'kiosk-web', picker);
});

test('the wall display signs in at /wall/ on the one origin', async ({ page }) => {
  test.setTimeout(120_000);
  await signInAt(page, '/wall/', WALL_DISPLAY, 'kiosk-wall', picker);
});
