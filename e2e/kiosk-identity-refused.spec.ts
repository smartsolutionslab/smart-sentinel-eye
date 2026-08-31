import { test, expect } from '@playwright/test';
import { signInToKiosk } from './support/kiosk-session';

/**
 * Spec 051 US2 — a screen the provider has shut out says so, rather than asking
 * a factory floor for a password.
 *
 * <p>
 * <b>Every assertion that matters here is an absence.</b> Today this case ends
 * on the identity provider's own login form: username and password boxes on a
 * wall-mounted display, inviting anyone walking past to type credentials into
 * it. A test asserting that a better heading appeared would pass with that form
 * still on the screen.
 * </p>
 */

test.describe('A shut-out screen says so (spec 051 US2)', () => {
  test('shows no credential prompt anywhere, and is not handed to the provider', async ({ page }) => {
    test.setTimeout(300_000);

    await signInToKiosk(page);

    // The provider answers, and what it says is that this screen is refused —
    // the shape a disabled account produces, observed against a running provider
    // as `invalid_grant` with "User disabled".
    let intercepted = 0;
    await page.route('**/protocol/openid-connect/token', async (route) => {
      intercepted += 1;
      await route.fulfill({
        status: 400,
        contentType: 'application/json',
        body: JSON.stringify({ error: 'invalid_grant', error_description: 'User disabled' }),
      });
    });

    await page.evaluate(() => {
      const key = Object.keys(window.localStorage).find((candidate) => candidate.startsWith('oidc.user:'));
      const user = JSON.parse(window.localStorage.getItem(key ?? '') ?? '{}') as Record<string, unknown>;
      user['expires_at'] = Math.floor(Date.now() / 1000) - 3_600;
      window.localStorage.setItem(key ?? '', JSON.stringify(user));
    });
    await page.reload();

    await expect(page.getByTestId('identity-not-authorized')).toBeVisible({ timeout: 60_000 });

    expect(intercepted, 'the interception must actually have fired').toBeGreaterThan(0);

    // **The load-bearing assertions.** Not "a message appeared" — no way to type
    // a credential into this screen, anywhere on it.
    await expect(page.locator('input[type="password"]')).toHaveCount(0);
    await expect(page.locator('#username')).toHaveCount(0);
    await expect(page.locator('#password')).toHaveCount(0);
    await expect(page.locator('input')).toHaveCount(0);

    // And it is not on the provider's pages, which is where the real form lives.
    expect(new URL(page.url()).port, 'a refused screen must not be redirected to the provider').toBe('5174');

    // It says what is wrong in words, and not in the library's.
    await expect(page.getByRole('heading', { name: /no longer authorized/i })).toBeVisible();
    await expect(page.getByText(/invalid_grant/i)).toHaveCount(0);
  });

  test('does not retry, because retrying cannot help', async ({ page }) => {
    test.setTimeout(300_000);

    await signInToKiosk(page);

    let intercepted = 0;
    await page.route('**/protocol/openid-connect/token', async (route) => {
      intercepted += 1;
      await route.fulfill({
        status: 400,
        contentType: 'application/json',
        body: JSON.stringify({ error: 'invalid_grant', error_description: 'User disabled' }),
      });
    });

    await page.evaluate(() => {
      const key = Object.keys(window.localStorage).find((candidate) => candidate.startsWith('oidc.user:'));
      const user = JSON.parse(window.localStorage.getItem(key ?? '') ?? '{}') as Record<string, unknown>;
      user['expires_at'] = Math.floor(Date.now() / 1000) - 3_600;
      window.localStorage.setItem(key ?? '', JSON.stringify(user));
    });
    await page.reload();

    await expect(page.getByTestId('identity-not-authorized')).toBeVisible({ timeout: 60_000 });
    const settled = intercepted;

    // A screen that kept trying would be telling whoever reads it that this
    // might clear. It will not.
    await page.waitForTimeout(30_000);

    expect(intercepted, 'a refused screen must stop asking').toBe(settled);
    await expect(page.getByTestId('identity-not-authorized')).toBeVisible();
  });
});
