import { test, expect, type Page } from '@playwright/test';

/**
 * Spec 050 US2 — a wall-display account may view its own fab and do nothing
 * else.
 *
 * <p>
 * <b>The refusals are the assertions.</b> Testing that the wall renders proves
 * the account can read; it proves nothing about what else the account could do,
 * and "what else" is the entire reason this feature was refusable once. Spec 049
 * declined to escape the session ceiling because the privilege involved would
 * have reached operators — it is acceptable here only because it reaches an
 * account that can do very little.
 * </p>
 */

/**
 * <b>Skipped until the scope design is settled.</b> These need the kiosk client
 * to grant the offline scope, and the arrangement that did so locked every
 * account without the role out of the kiosk app — including operators and all
 * six existing kiosk specs. The scope has been withdrawn from the realm while
 * the design is reworked (ADR-0132, "What review found"), so these would fail
 * for a reason that is recorded rather than unknown.
 */
const WALL_SCOPE_GRANTED = process.env['SSE_WALL_SCOPE'] === '1';

const WALL_USER = 'wall-munich';
const WALL_PASSWORD = 'Wall-munich-1234';

/** Signs in as a named account through the real provider, as a screen would. */
async function signIn(page: Page, username: string, password: string): Promise<void> {
  await page.goto('/');
  await page.getByRole('button', { name: /sign in/i }).click();
  await page.locator('#username').fill(username);
  await page.locator('#password').fill(password);
  await page.locator('#kc-login').click();
  await expect(page.getByRole('heading', { name: 'Pick a layout' })).toBeVisible({ timeout: 60_000 });
}

/** The access token the app is holding, read the way the app itself would. */
async function bearer(page: Page): Promise<string> {
  const token = await page.evaluate(() => {
    const key = Object.keys(window.localStorage).find((candidate) => candidate.startsWith('oidc.user:'));
    if (key === undefined) return null;
    const stored = JSON.parse(window.localStorage.getItem(key) ?? 'null') as { access_token?: string } | null;
    return stored?.access_token ?? null;
  });
  expect(token, 'the screen should be holding a token').not.toBeNull();
  return token as string;
}

test.skip(!WALL_SCOPE_GRANTED, 'the offline scope is withdrawn pending rework — see ADR-0132');

test('a wall-display account can see its fab and change nothing', async ({ page }) => {
  test.setTimeout(240_000);

  const gatewayRequest = page.waitForRequest((request) =>
    /\/(layout-composition|stream-distribution|camera-catalog)\//.test(request.url()),
  );

  await signIn(page, WALL_USER, WALL_PASSWORD);

  const origin = new URL((await gatewayRequest).url()).origin;
  const token = await bearer(page);

  const call = async (method: string, path: string, body?: unknown) =>
    page.evaluate(
      async ([o, t, m, p, b]) => {
        const response = await fetch(`${o}${p}`, {
          method: m as string,
          headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${t}` },
          ...(b === null ? {} : { body: JSON.stringify(b) }),
        });
        return response.status;
      },
      [origin, token, method, path, body ?? null] as const,
    );

  // It can read its own fab's cameras — otherwise the wall would not work and
  // the refusals below would be vacuous.
  expect(await call('GET', '/camera-catalog/cameras?limit=1'), 'a screen must be able to see its cameras').toBe(200);

  // And it can change nothing. Each of these is a separate authority a wall
  // display has no business holding.
  expect(
    await call('POST', '/camera-catalog/cameras', { name: 'x', rtspUrl: 'rtsp://10.0.0.1/s' }),
  ).toBeGreaterThanOrEqual(400);
  expect(await call('POST', '/layout-composition/layouts', { name: 'x' })).toBeGreaterThanOrEqual(400);
  expect(await call('POST', '/overlay-designer/overlays', { name: 'x' })).toBeGreaterThanOrEqual(400);
});

test('a wall-display account holds a grant that outlives a session', async ({ page }) => {
  test.setTimeout(240_000);

  await signIn(page, WALL_USER, WALL_PASSWORD);

  const shape = await page.evaluate(() => {
    const key = Object.keys(window.localStorage).find((candidate) => candidate.startsWith('oidc.user:'));
    if (key === undefined) return null;
    const user = JSON.parse(window.localStorage.getItem(key) ?? '{}') as Record<string, string>;
    const decode = (jwt: string) => {
      const [, payload] = jwt.split('.');
      return JSON.parse(atob(payload.replace(/-/g, '+').replace(/_/g, '/'))) as Record<string, unknown>;
    };
    const refresh = decode(user['refresh_token'] ?? '');
    return { typ: refresh['typ'], hasExpiry: refresh['exp'] !== undefined };
  });

  // **Decoded, not counted.** Asserting that a token exists passes today, with
  // the screen still dropping to a prompt twice a day. The type is what says the
  // grant outlives the session that issued it.
  expect(shape?.typ, 'an ordinary refresh token dies with its session').toBe('Offline');
  expect(shape?.hasExpiry, 'and this one should carry no expiry at all').toBe(false);
});
