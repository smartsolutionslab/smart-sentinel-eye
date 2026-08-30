import { test, expect, request as playwrightRequest, type Page } from '@playwright/test';

/**
 * Spec 050 US3 — withdrawing one screen must not take down the wall.
 *
 * <p>
 * <b>This is the claim the research could only reason about.</b> R4 argued that
 * because the provider tracks offline sessions individually, one screen's grant
 * can be ended while a sibling keeps running. Reasoning is exactly what this
 * feature exists to distrust, so this tests it — and if it fails, US3 is
 * re-scoped in the open rather than reinterpreted into whatever the mechanism
 * happens to do.
 * </p>
 *
 * <p>
 * It matters more here than it would elsewhere: the grant never expires, so
 * being able to end one is the only thing that ends it. If the unit of
 * withdrawal is the account rather than the session, withdrawing one screen
 * stops an entire fab's wall.
 * </p>
 */

const SHORTENED = process.env['SSE_WITHDRAWAL_PROBE'] === '1';
const KEYCLOAK = process.env['SSE_KEYCLOAK_URL'] ?? 'https://127.0.0.1:10756';
const ADMIN_PASSWORD = process.env['SSE_KEYCLOAK_ADMIN_PASSWORD'] ?? '';

const WALL_USER = 'wall-munich';
const WALL_PASSWORD = 'Wall-munich-1234';
const REALM = 'smart-sentinel-eye';

async function signIn(page: Page): Promise<string> {
  await page.goto('/');
  await page.getByRole('button', { name: /sign in/i }).click();
  await page.locator('#username').fill(WALL_USER);
  await page.locator('#password').fill(WALL_PASSWORD);
  await page.locator('#kc-login').click();
  await expect(page.getByRole('heading', { name: 'Pick a layout' })).toBeVisible({ timeout: 60_000 });

  const refresh = await page.evaluate(() => {
    const key = Object.keys(window.localStorage).find((candidate) => candidate.includes('oidc.user:'));
    if (key === undefined) return null;
    const user = JSON.parse(window.localStorage.getItem(key) ?? '{}') as Record<string, string>;
    return user['refresh_token'] ?? null;
  });
  expect(refresh, 'a screen should hold a grant it can refresh with').not.toBeNull();
  return refresh as string;
}

/** Claims of a grant, read without verifying — this is a test, not a validator. */
function claimsOf(token: string): Record<string, string> {
  const [, payload] = token.split('.');
  return JSON.parse(Buffer.from(payload, 'base64url').toString('utf8')) as Record<string, string>;
}

/**
 * Where the grant was issued, taken from the grant.
 *
 * <p>
 * <b>Not a constant.</b> The provider is reached through a proxied endpoint, so
 * a hardcoded host is a different issuer as far as the provider is concerned and
 * every exchange comes back `invalid_grant`. That failure reads exactly like a
 * withdrawn session, and it briefly had this test reporting that withdrawing one
 * screen took down its sibling — a control with nothing withdrawn is what caught
 * it.
 * </p>
 */
function issuerOf(token: string): string {
  const issuer = claimsOf(token)['iss'];
  expect(issuer, 'a grant should name its issuer').toBeTruthy();
  return issuer;
}

/** The session a grant belongs to, which is what withdrawal has to target. */
function sessionOf(refreshToken: string): string {
  return claimsOf(refreshToken)['sid'] ?? '';
}

test.describe('Withdrawing one screen (spec 050 US3)', () => {
  test.skip(!SHORTENED, 'needs the wall account mirrored into the running realm — see verification.md');

  /**
   * **The control.** If two grants cannot both be exchanged when nothing has
   * been withdrawn, then a failure in the test above says nothing about
   * withdrawal — it says the arrangement never worked. Running this first is
   * what makes the other result mean something.
   */
  test('control: two screens can both refresh when nothing is withdrawn', async ({ browser }) => {
    test.setTimeout(600_000);

    const first = await browser.newContext({ baseURL: 'http://localhost:5174', ignoreHTTPSErrors: true });
    const second = await browser.newContext({ baseURL: 'http://localhost:5174', ignoreHTTPSErrors: true });
    const grantOne = await signIn(await first.newPage());
    const grantTwo = await signIn(await second.newPage());

    expect(sessionOf(grantOne)).not.toBe(sessionOf(grantTwo));

    const api = await playwrightRequest.newContext({ ignoreHTTPSErrors: true });
    const exchange = async (refreshToken: string) => {
      const response = await api.post(`${issuerOf(refreshToken)}/protocol/openid-connect/token`, {
        form: { client_id: 'kiosk-web', grant_type: 'refresh_token', refresh_token: refreshToken },
      });
      return response.status();
    };

    expect(await exchange(grantOne), 'both screens should refresh with nothing withdrawn').toBe(200);
    expect(await exchange(grantTwo), 'both screens should refresh with nothing withdrawn').toBe(200);

    await first.close();
    await second.close();
    await api.dispose();
  });

  test('ends one screen and leaves its sibling running', async ({ browser }) => {
    test.setTimeout(600_000);

    // Two screens in the same fab, signed in as the same wall-display account —
    // which is the arrangement a real wall has.
    const first = await browser.newContext({ baseURL: 'http://localhost:5174', ignoreHTTPSErrors: true });
    const second = await browser.newContext({ baseURL: 'http://localhost:5174', ignoreHTTPSErrors: true });
    const grantOne = await signIn(await first.newPage());
    const grantTwo = await signIn(await second.newPage());

    const sessionOne = sessionOf(grantOne);
    const sessionTwo = sessionOf(grantTwo);
    expect(sessionOne, 'two screens must not share a session, or nothing can be withdrawn alone').not.toBe(sessionTwo);

    const admin = await playwrightRequest.newContext({ ignoreHTTPSErrors: true });
    const tokenResponse = await admin.post(`${KEYCLOAK}/realms/master/protocol/openid-connect/token`, {
      form: { client_id: 'admin-cli', username: 'admin', password: ADMIN_PASSWORD, grant_type: 'password' },
    });
    const adminToken = ((await tokenResponse.json()) as { access_token: string }).access_token;

    // Withdraw exactly one screen's session.
    const deleted = await admin.delete(`${KEYCLOAK}/admin/realms/${REALM}/sessions/${sessionOne}?isOffline=true`, {
      headers: { Authorization: `Bearer ${adminToken}` },
    });
    expect(deleted.status(), 'the provider should be able to end a single session').toBeLessThan(300);

    // Now the load-bearing part: exchange each grant directly. The withdrawn one
    // must fail and the sibling must not.
    const exchange = async (refreshToken: string) => {
      const response = await admin.post(`${issuerOf(refreshToken)}/protocol/openid-connect/token`, {
        form: { client_id: 'kiosk-web', grant_type: 'refresh_token', refresh_token: refreshToken },
      });
      // The provider's reason is the evidence here: a withdrawn session answers
      // "Offline user session not found", which distinguishes an ended session
      // from a grant that never worked. Both look like 400 from outside, and
      // that ambiguity briefly had this test reporting the wrong finding.
      if (response.status() !== 200) {
        console.log('EXCHANGE', response.status(), (await response.text()).slice(0, 220));
      }
      return response.status();
    };

    expect(await exchange(grantOne), 'the withdrawn screen must stop').toBeGreaterThanOrEqual(400);
    expect(await exchange(grantTwo), 'and the other nineteen must not').toBe(200);

    await first.close();
    await second.close();
    await admin.dispose();
  });
});
