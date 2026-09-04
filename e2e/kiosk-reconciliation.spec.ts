import { test, expect, type Page } from '@playwright/test';
import { signInToKiosk } from './support/kiosk-session';
import { readBoundOverlayWall } from './support/bound-overlay-wall';
import { FIRST_WRITE_TIMEOUT_MS } from './support/cold-stack';

/**
 * Issue 1921 — SC-004's reconciliation half: a kiosk that missed events while
 * the hub was down catches up on reconnect, within 10 s and with no reload.
 *
 * `kiosk-live-updates.spec.ts` covers the *badge* — degraded appears, then
 * clears. It deliberately changes no data, so nothing there proves the kiosk
 * applies what it missed. That gap is why every `@microsoft/signalr` bump has
 * carried a manual quickstart run (#1129, #1920).
 *
 * **The outage is a real network drop, not an aborted route.** The badge spec
 * aborts the hub route and then *reloads*, so the initial connect fails. That
 * trick is unavailable here: this test's whole subject is state that survives
 * in place, so it must not reload — and route interception does nothing to an
 * already-established WebSocket, which simply stays open. `setOffline` severs
 * it, which is what stopping the hub host does in the manual quickstart.
 *
 * The management side therefore lives in its **own browser context**: offline
 * is a context-wide setting, and the writes below have to succeed while the
 * kiosk is cut off.
 *
 * Two phases in one test rather than two tests, because the wall is one tile:
 * once the overlay is archived the tile renders "Overlay unavailable", which
 * would mask any later assertion about the resolved value.
 */

/** Cut the kiosk off, do something behind its back, let it recover. */
async function withKioskOffline(kiosk: Page, whileOffline: () => Promise<void>): Promise<void> {
  await kiosk.context().setOffline(true);
  await expect(kiosk.getByTestId('live-updates-degraded')).toBeVisible({ timeout: 60_000 });

  await whileOffline();

  await kiosk.context().setOffline(false);
  // The retry ladder tops out at 30 s ±20 %, so recovery lands within ~36 s.
  await expect(kiosk.getByTestId('live-updates-degraded')).toBeHidden({ timeout: 45_000 });
}

test('kiosk reconciles the overlay state it missed while the hub was down', async ({ page, browser }) => {
  test.setTimeout(300_000);

  const wall = readBoundOverlayWall();

  await signInToKiosk(page);

  // This wall specifically — the picker also lists the other seed's layout.
  await page.getByRole('listitem').filter({ hasText: wall.layoutName }).getByRole('button').click();
  await expect(page.getByTestId('layout-grid')).toBeVisible();

  const tile = page.getByTestId('layout-tile').first();
  await expect(tile).toContainText(wall.variableInitialValue);

  // Its own context, so taking the kiosk offline does not take management with
  // it. The kiosk page itself must never navigate: SC-004 is "no page reload".
  const adminContext = await browser.newContext({ baseURL: 'http://localhost:5173' });
  const admin = await adminContext.newPage();
  await admin.goto('/');
  await admin.getByRole('button', { name: /sign in/i }).click();
  await admin.locator('#username').fill('operator');
  await admin.locator('#password').fill('Operator1234');
  await admin.locator('#kc-login').click();
  await expect(admin.getByRole('heading', { name: 'Cameras', exact: true })).toBeVisible();

  try {
    // Phase 1 — a value changed during the outage arrives after reconnect.
    await withKioskOffline(page, async () => {
      await admin.getByRole('link', { name: /^system variables$/i }).click();
      const row = admin.getByRole('listitem').filter({ hasText: wall.variableName });
      await row.getByPlaceholder('New value').fill(wall.variableChangedValue);
      await row.getByRole('button', { name: /^set value$/i }).click();
      await expect(row.getByText(wall.variableChangedValue)).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });
    });

    await expect(tile).toContainText(wall.variableChangedValue, { timeout: 10_000 });

    // Phase 2 — an overlay archived during the outage leaves the tile unbound.
    await withKioskOffline(page, async () => {
      await admin.getByRole('link', { name: /^overlays$/i }).click();
      const row = admin.getByRole('listitem').filter({ hasText: wall.overlayName });
      await row.getByRole('button', { name: /^archive$/i }).click();
      await admin
        .getByRole('alertdialog')
        .getByRole('button', { name: /^archive$/i })
        .click();
      await expect(row.getByText(/Archived/)).toBeVisible();
    });

    await expect(page.getByText('Overlay unavailable')).toBeVisible({ timeout: 10_000 });
  } finally {
    await adminContext.close();
  }
});
