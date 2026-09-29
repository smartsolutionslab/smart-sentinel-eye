import { expect, type Page } from '@playwright/test';

/**
 * Spec 288 US1 (ADR-0162 §1-2) — the wall session rig, extracted from the
 * two-to-four copies each wall spec carried (spec 288 §1.5): sign-in, claim
 * decoding, issuer lookup, and stored-grant reading/expiry.
 *
 * Every export here takes the `Page` it drives, so specs that launch their
 * own persistent context (`wall-survives-a-lockout`,
 * `wall-survives-a-process-death`) use it exactly like specs that drive the
 * test's own `page` fixture (FR-003) — nothing here is a fixture itself
 * (ADR-0162 §1).
 *
 * The grant lives in `localStorage` (ADR-0131) under an `oidc.user:...` key
 * that embeds the identity provider's authority — never a hardcoded host,
 * because the stack publishes it on a port it chooses.
 */

export const WALL_USER = 'wall-munich';
export const WALL_PASSWORD = 'Wall-munich-1234';

export interface SignInAsWallDisplayOptions {
  /**
   * Defaults to `'/'` (the project's own `baseURL`). A context launched
   * outside the fixtures (`wall-survives-a-lockout`,
   * `wall-survives-a-process-death`) inherits no `baseURL` and passes the
   * absolute wall URL instead.
   */
  url?: string;
  /**
   * Defaults to `60_000`. The two manually-launched-context specs pass
   * `90_000`.
   */
  timeout?: number;
  /**
   * Defaults to `false`. When `true`, also asserts the picker's first item
   * is visible (`'the seed project publishes a layout'`) — the two
   * manually-launched-context specs pass `true`.
   */
  expectPopulatedPicker?: boolean;
}

/**
 * Signs in as the seeded `wall-munich` display account through the real
 * Keycloak form (ADR-0108) and waits for the layout picker.
 *
 * Every caller's variant — URL, timeout budget, whether the picker must
 * already be populated — is preserved as an option rather than unified away
 * (spec 288 §1.6, FR-005): a consolidation that picked one copy's numbers
 * would silently change behaviour for the others.
 */
export async function signInAsWallDisplay(page: Page, options: SignInAsWallDisplayOptions = {}): Promise<void> {
  const { url = '/', timeout = 60_000, expectPopulatedPicker = false } = options;

  await page.goto(url);
  await page.getByRole('button', { name: /sign in/i }).click();
  await page.locator('#username').fill(WALL_USER);
  await page.locator('#password').fill(WALL_PASSWORD);
  await page.locator('#kc-login').click();
  await expect(page.getByRole('heading', { name: 'Pick a layout' })).toBeVisible({ timeout });

  if (expectPopulatedPicker) {
    await expect(page.getByRole('listitem').first(), 'the seed project publishes a layout').toBeVisible({ timeout });
  }
}

/** Claims of a grant, read without verifying — this is a test, not a validator. */
export function claimsOf(token: string): Record<string, unknown> {
  const [, payload] = token.split('.');
  if (payload === undefined) {
    throw new Error('a grant should have a payload segment');
  }
  return JSON.parse(Buffer.from(payload, 'base64url').toString('utf8')) as Record<string, unknown>;
}

/**
 * The provider's own issuer, read off a live grant rather than hardcoded —
 * the host publishes the provider on a port it chooses, so a fixed one is a
 * different issuer as far as the provider is concerned and every exchange
 * comes back refused.
 */
export function issuerOf(token: string): string {
  const issuer = claimsOf(token)['iss'];
  if (typeof issuer !== 'string' || issuer === '') {
    throw new Error('a grant should name its issuer');
  }
  return issuer;
}

/** The `access_token` of the stored grant, read and never written. */
export async function storedAccessToken(page: Page, message: string): Promise<string> {
  const access = await page.evaluate(() => {
    const key = Object.keys(window.localStorage).find((candidate) => candidate.startsWith('oidc.user:'));
    if (key === undefined) return null;
    const user = JSON.parse(window.localStorage.getItem(key) ?? '{}') as Record<string, unknown>;
    return typeof user['access_token'] === 'string' ? user['access_token'] : null;
  });

  expect(access, message).not.toBeNull();
  return access as string;
}

/**
 * Ages the stored access token in place, so the next silent-renewal check
 * finds it already expired. Returns the token it just marked spent, which is
 * what a later assertion compares the renewed token against.
 */
export async function expireStoredAccessToken(page: Page, message: string): Promise<string> {
  const spent = await page.evaluate(() => {
    const key = Object.keys(window.localStorage).find((candidate) => candidate.startsWith('oidc.user:'));
    if (key === undefined) return null;
    const user = JSON.parse(window.localStorage.getItem(key) ?? '{}') as Record<string, unknown>;
    const access = user['access_token'];
    user['expires_at'] = Math.floor(Date.now() / 1000) - 3_600;
    window.localStorage.setItem(key, JSON.stringify(user));
    return typeof access === 'string' ? access : null;
  });

  expect(spent, message).not.toBeNull();
  return spent as string;
}
