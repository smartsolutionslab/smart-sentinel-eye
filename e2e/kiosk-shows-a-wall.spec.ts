import { test, expect } from '@playwright/test';
import { openFirstLayout, signInToKiosk } from './support/kiosk-session';

/**
 * Spec 041 US1 (SC-001) — a kiosk lists published layouts and opens one.
 *
 * This is the first time that has been possible. The kiosk signed in as a
 * client carrying no fab claim, so every fab-scoped read was refused and the
 * picker rendered "Could not load layouts." for as long as the kiosk existed.
 *
 * **Asserted as a wall, never as the absence of an error.** An operator whose
 * token carries no fab gets an empty picker and no error at all, so "no error"
 * is satisfied by exactly the failure this feature fixes.
 *
 * **What this does NOT prove: that the tiles show a picture.** `openFirstLayout`
 * opens whichever published layout sorts first by name
 * (`ListLayoutsQueryHandler` orders `OrdinalIgnoreCase`), so *which* wall this
 * is depends on what the run has seeded — it is not `seed-published-layout`'s
 * wall by design. What is invariant: every wall this suite seeds **except spec
 * 056's** points its camera at an unserved `10.0.5.x` address, so no frame can
 * arrive, and a tile renders whether or not one does. Spec 056's wall is the
 * one exception and it is never reached from here — it is opened **by name** in
 * `kiosk-shows-a-label-over-video.spec.ts`, which is where a picture is
 * asserted. Both of the blockers spec 041 fixed — the missing `sub` claim and the
 * WHEP gate — are invisible here in either direction. They are verified by a
 * person, per the feature's quickstart.
 */
test('a kiosk opens a published wall and its tiles render', async ({ page }) => {
  await signInToKiosk(page);
  await openFirstLayout(page);

  await expect(page.getByTestId('layout-tile').first()).toBeVisible();
  expect(await page.getByTestId('layout-tile').count()).toBeGreaterThan(0);
});
