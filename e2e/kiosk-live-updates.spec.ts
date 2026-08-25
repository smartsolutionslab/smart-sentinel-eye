import { test, expect } from '@playwright/test';
import { signInToKiosk } from './support/kiosk-session';

// Spec 011 US2 (FR-006/007) — the kiosk shows the discreet degraded badge
// while the layout hub is unreachable and clears it once the unbounded retry
// ladder reconnects. The outage is induced client-side by aborting every
// /hubs/** request (negotiate + transport), which the app must treat exactly
// like a network outage.
//
// Spec 041: the sign-in helper moved to e2e/support/kiosk-session.ts. The local
// copy accepted "could not load layouts" as a passing outcome, so this file
// went green for years against a kiosk that could never show a wall.

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
