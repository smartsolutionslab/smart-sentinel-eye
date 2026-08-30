import { test, expect, type Page } from '@playwright/test';
import { openFirstLayout, signInToKiosk } from './support/kiosk-session';

/**
 * Spec 049 US1/US2 — a wall comes back on its own (ADR-0131).
 *
 * <p>
 * <b>Why these are e2e and not unit tests.</b> Both claims are about state that
 * outlives a page: a grant surviving the process, and a grant outliving the
 * sign-in session the identity provider keeps. Neither exists inside a
 * component, and a unit test asserting the configuration proves the intent
 * rather than the effect.
 * </p>
 *
 * <p>
 * <b>Every case starts from a browser that has never signed in.</b> Beginning
 * from a signed-in state proves nothing about coming back, which is the whole
 * subject — and is how the previous two features each shipped a defect.
 * </p>
 */

/** What the kiosk kept, read the way the app would find it after a restart. */
async function storedGrant(page: Page): Promise<string | null> {
  return page.evaluate(() => {
    const key = Object.keys(window.localStorage).find((candidate) => candidate.includes('oidc.user:'));
    return key === null || key === undefined ? null : window.localStorage.getItem(key);
  });
}

test('a kiosk keeps its grant where a restart cannot destroy it', async ({ page }) => {
  test.setTimeout(180_000);

  await signInToKiosk(page);

  // The grant is on disk, not in storage the browser process takes with it.
  expect(await storedGrant(page), 'the kiosk should have kept its grant').not.toBeNull();

  const inProcessOnly = await page.evaluate(() =>
    Object.keys(window.sessionStorage).filter((key) => key.includes('oidc.user:')),
  );
  expect(inProcessOnly, 'nothing that matters may live only with the process').toHaveLength(0);
});

test('a restarted kiosk shows its wall again without anyone touching it', async ({ page, context }) => {
  test.setTimeout(240_000);

  await signInToKiosk(page);
  await openFirstLayout(page);

  // **The key is read, not constructed.** It embeds the identity provider's
  // authority, which the stack serves on a proxied port — a hardcoded
  // `localhost:8080` key restored an entry the app never looks for, and the test
  // reported the feature broken when the fault was its own.
  const kept = await page.evaluate(() => {
    const key = Object.keys(window.localStorage).find((candidate) => candidate.includes('oidc.user:'));
    return key === undefined ? null : { key, value: window.localStorage.getItem(key) ?? '' };
  });
  expect(kept, 'the kiosk should have kept its grant').not.toBeNull();

  const { key, value } = kept as { key: string; value: string };

  // **The access token is expired before the restart, and this is the whole
  // point of the test.** A power cut outlasts the token: restoring a *fresh*
  // grant and restarting instantly passes against the defect this covers, where
  // nothing exchanged the refresh token and the screen went to a login form.
  const withExpiredToken = JSON.stringify({
    ...(JSON.parse(value) as Record<string, unknown>),
    expires_at: Math.floor(Date.now() / 1000) - 3_600,
  });

  // **A restart, as faithfully as a browser allows.** A new context is a fresh
  // process: no in-memory state, no session storage, and — because the context
  // is new — no sign-in cookie either, which is what a rebooted device lacks.
  // `ignoreHTTPSErrors` is repeated here on purpose: a context created through
  // `browser.newContext()` does NOT inherit the project's `use` settings, and
  // the identity provider is served over HTTPS with a development certificate.
  // Without it the refresh exchange fails on certificate validation and the
  // screen falls to a login form — which reads exactly like the product defect
  // this test exists to catch.
  const rebooted = await context.browser()?.newContext({
    baseURL: 'http://localhost:5174',
    ignoreHTTPSErrors: true,
  });
  if (rebooted === undefined) throw new Error('could not simulate a restart');
  const screenAfterReboot = await rebooted.newPage();

  await screenAfterReboot.addInitScript(
    (stored: { key: string; value: string }) => {
      // Restore only what survives a power cut: what was written to disk.
      window.localStorage.setItem(stored.key, stored.value);
      window.localStorage.setItem('sse.auth.wasAuthenticated', 'true');
    },
    { key, value: withExpiredToken },
  );

  await screenAfterReboot.goto('/');

  // No prompt and no sign-in button — the screen recovers on its own by
  // spending the grant it kept.
  await expect(
    screenAfterReboot.getByRole('heading', { name: 'Pick a layout' }),
    'a rebooted screen must not ask anyone for credentials',
  ).toBeVisible({ timeout: 60_000 });
  await expect(screenAfterReboot.getByRole('button', { name: /sign in/i })).toHaveCount(0);

  // And the wall itself renders, which is what the operator is there for. The
  // picker alone would prove authentication and not the thing the story claims.
  await openFirstLayout(screenAfterReboot);

  await rebooted.close();
});
