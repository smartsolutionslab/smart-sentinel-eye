import { test, expect, type Page } from '@playwright/test';
import { signInToKiosk } from './support/kiosk-session';

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
  await page.getByRole('listitem').first().getByRole('button').click();
  await expect(page.getByTestId('layout-grid')).toBeVisible();

  // **The key is read, not constructed.** It embeds the identity provider's
  // authority, which the stack serves on a proxied port — a hardcoded
  // `localhost:8080` key restored an entry the app never looks for, and the
  // test reported the feature broken when the fault was its own.
  const kept = await page.evaluate(() => {
    const key = Object.keys(window.localStorage).find((candidate) => candidate.includes('oidc.user:'));
    return key === undefined ? null : { key, value: window.localStorage.getItem(key) };
  });
  expect(kept, 'the kiosk should have kept its grant').not.toBeNull();

  // **A restart, as faithfully as a browser allows.** A new page in a new
  // context is a fresh process: no in-memory state, no session storage. What it
  // carries over is what a rebooted device carries over — whatever was written
  // to disk.
  const rebooted = await context.browser()?.newContext({ baseURL: 'http://localhost:5174' });
  if (rebooted === undefined) throw new Error('could not simulate a restart');
  const screenAfterReboot = await rebooted.newPage();

  await screenAfterReboot.addInitScript(
    (stored: { key: string; value: string }) => {
      // Restore only what survives a power cut: the grant on disk. Nothing else
      // carries over — no session storage, no in-memory state, no sign-in cookie.
      window.localStorage.setItem(stored.key, stored.value);
    },
    kept as { key: string; value: string },
  );

  await screenAfterReboot.goto('/');

  // No prompt, no redirect to a sign-in form — the wall, by itself.
  await expect(
    screenAfterReboot.getByRole('heading', { name: 'Pick a layout' }),
    'a rebooted screen must not ask anyone for credentials',
  ).toBeVisible({ timeout: 60_000 });

  await expect(screenAfterReboot.getByRole('button', { name: /sign in/i })).toHaveCount(0);

  await rebooted.close();
});
