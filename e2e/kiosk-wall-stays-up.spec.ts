import { test, expect, type Page } from '@playwright/test';

/**
 * Spec 050 US1 — a wall stays up past the session limits that end it today.
 *
 * <p>
 * <b>Skipped unless the ceiling has been shortened.</b> Nothing in CI runs for
 * ten hours, so these need a realm whose session limits are seconds rather than
 * hours — set up outside the test and signalled by <c>SSE_SHORT_CEILING</c>.
 * They therefore <b>demonstrate the mechanism and not the production
 * configuration</b>, and any note that reads a green run here as "a wall was
 * watched for ten hours" is overstating it.
 * </p>
 *
 * <p>
 * Skipping rather than failing in CI is deliberate: a test that cannot run in an
 * environment should say so, not go red and teach everyone to ignore it.
 * </p>
 */

const SHORTENED = process.env['SSE_SHORT_CEILING'] === '1';
const IDLE_SECONDS = Number(process.env['SSE_SHORT_IDLE_SECONDS'] ?? '60');

const WALL_USER = 'wall-munich';
const WALL_PASSWORD = 'Wall-munich-1234';

async function signIn(page: Page): Promise<void> {
  await page.goto('/');
  await page.getByRole('button', { name: /sign in/i }).click();
  await page.locator('#username').fill(WALL_USER);
  await page.locator('#password').fill(WALL_PASSWORD);
  await page.locator('#kc-login').click();
  await expect(page.getByRole('heading', { name: 'Pick a layout' })).toBeVisible({ timeout: 60_000 });
}

test.describe('A wall stays up past the session limits (spec 050 US1)', () => {
  test.skip(!SHORTENED, 'needs a realm with a shortened ceiling — see verification.md');

  test('keeps showing its wall after the session that issued the grant has ended', async ({ page }) => {
    test.setTimeout(600_000);

    await signIn(page);
    await page.getByRole('listitem').first().getByRole('button').click();
    await expect(page.getByTestId('layout-grid')).toBeVisible();

    // **Wait past the ceiling, with margin.** Anything shorter passes with the
    // defect fully present, which is what makes a quick test worthless here.
    await page.waitForTimeout((IDLE_SECONDS + 30) * 1_000);

    // Force the app to use its grant: a read the wall needs, after the session
    // that issued the original token is gone.
    await page.reload();

    // **Asserted on the absence of a prompt, not the presence of the picker.**
    // The app restores the layout it was showing, so after a reload the screen
    // is on its wall rather than the picker — an earlier version of this test
    // looked for the picker and reported the feature broken when the wall was
    // right there on the page.
    await expect(page.getByRole('button', { name: /sign in/i })).toHaveCount(0);
    await expect(page.locator('#username')).toHaveCount(0);

    await expect(
      page.getByTestId('layout-grid'),
      'the wall must still be showing after the session that issued the grant ended',
    ).toBeVisible({ timeout: 60_000 });
  });

  test('comes back from an outage longer than the idle cut-off', async ({ page, context }) => {
    test.setTimeout(600_000);

    await signIn(page);

    const kept = await page.evaluate(() => {
      const key = Object.keys(window.localStorage).find((candidate) => candidate.includes('oidc.user:'));
      return key === undefined ? null : { key, value: window.localStorage.getItem(key) ?? '' };
    });
    expect(kept).not.toBeNull();
    const { key, value } = kept as { key: string; value: string };

    // The access token is expired before the restart. Spec 049 could recover a
    // restart only while the session behind the grant lived; this is the case it
    // explicitly could not do.
    const expired = JSON.stringify({
      ...(JSON.parse(value) as Record<string, unknown>),
      expires_at: Math.floor(Date.now() / 1000) - 3_600,
    });

    // Outlast the idle cut-off before restarting, so the original session is
    // genuinely gone rather than merely old.
    await page.waitForTimeout((IDLE_SECONDS + 30) * 1_000);

    const rebooted = await context.browser()?.newContext({
      baseURL: 'http://localhost:5174',
      // Not inherited from the project's `use` — the provider is served over
      // HTTPS with a development certificate, and without this the grant
      // exchange fails on certificate validation and reads exactly like the
      // product being broken.
      ignoreHTTPSErrors: true,
    });
    if (rebooted === undefined) throw new Error('could not simulate a restart');
    const screenAfterReboot = await rebooted.newPage();

    await screenAfterReboot.addInitScript(
      (stored: { key: string; value: string }) => {
        window.localStorage.setItem(stored.key, stored.value);
        window.localStorage.setItem('sse.auth.wasAuthenticated', 'true');
      },
      { key, value: expired },
    );

    await screenAfterReboot.goto('/');

    // A restarted screen lands where it was told to go. What matters is that
    // nobody was asked for credentials.
    await expect(
      screenAfterReboot.getByRole('button', { name: /sign in/i }),
      'a screen must come back from an outage that outlasted its session',
    ).toHaveCount(0, { timeout: 90_000 });
    await expect(screenAfterReboot.locator('#username')).toHaveCount(0);
    await expect(screenAfterReboot.getByRole('listitem').first()).toBeVisible({ timeout: 90_000 });

    await rebooted.close();
  });
});
