import { test, expect, type Page } from '@playwright/test';

// Spec 011 US2 (FR-006/007) — the kiosk shows the discreet degraded badge
// while the layout hub is unreachable and clears it once the unbounded retry
// ladder reconnects. The outage is induced client-side by aborting every
// /hubs/** request (negotiate + transport), which the app must treat exactly
// like a network outage.
//
// e2e/support/sign-in.ts drives the management shell (it asserts the Cameras
// heading), so the kiosk repeats the same seeded-operator Keycloak form flow
// here and asserts arrival on the picker instead.
async function signInToKiosk(page: Page): Promise<void> {
  await page.goto('/');

  await page.getByRole('button', { name: /sign in/i }).click();

  await page.locator('#username').fill('operator');
  await page.locator('#password').fill('Operator1234');
  await page.locator('#kc-login').click();

  // Back in the kiosk, authenticated — the picker renders (list or empty state).
  await expect(
    page.getByText(/pick a layout|no layouts published yet|could not load layouts/i).first(),
  ).toBeVisible();
}

test('kiosk shows the degraded badge while the hub is unreachable and clears it after recovery', async ({
  page,
}) => {
  await signInToKiosk(page);

  // Kill the hub and reload so even the INITIAL connect fails (FR-006:
  // initial-connect failures must retry indefinitely, never give up).
  await page.route('**/hubs/**', (route) => route.abort());
  await page.reload();
  await expect(page.getByTestId('live-updates-degraded')).toBeVisible();

  // Restore the network. The retry ladder tops out at 30 s ±20 % jitter, so
  // the next attempt lands within ~36 s of recovery — allow 45 s.
  await page.unroute('**/hubs/**');
  await expect(page.getByTestId('live-updates-degraded')).toBeHidden({ timeout: 45_000 });
});
