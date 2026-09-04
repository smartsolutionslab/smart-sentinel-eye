import { test as setup, expect } from '@playwright/test';
import { signInAsOperator } from './sign-in';

/**
 * Spec 041 — a published layout for the kiosk to open.
 *
 * The kiosk cannot show a wall until one exists, and an e2e stack has none:
 * `ci.yml` boots the stack with `ScenarioSimulator=false`, so `camera-sim` and
 * `scenario-simulator` are not composed at all and CI starts on an empty
 * catalogue (#2013).
 *
 * This runs as its own Playwright project that the `kiosk` project depends on,
 * rather than leaning on `layouts.spec.ts` having happened to publish one
 * first. Depending on another spec's side effect is exactly the implicit
 * coupling that lets a broken kiosk look like a working one — which is the
 * defect this feature exists to correct.
 *
 * Drives management-web (`:5173`) rather than the API: publishing needs an
 * `If-Match` round-trip, and the UI path gets the contract right for free.
 */
setup('a published layout exists for the kiosk to open', async ({ page }) => {
  setup.setTimeout(120_000);

  await signInAsOperator(page);

  // Names are unique per fab, so a fixed name would collide on a second local
  // run against a surviving database. The kiosk opens whichever layout is
  // first — any published one proves the point — so the name is not shared.
  const stamp = Date.now();
  const cameraName = `Kiosk Seed Cam ${stamp}`;
  const layoutName = `Kiosk Seed Wall ${stamp}`;

  // A layout tile needs a camera; the dialog's submit stays disabled while the
  // catalogue is empty. It does NOT need an overlay — an unbound tile renders.
  await page.getByRole('button', { name: /register camera/i }).click();
  await page.locator('#register-camera-name').fill(cameraName);
  await page.locator('#register-camera-url').fill('rtsp://10.0.5.70/stream');
  await page.getByRole('button', { name: /^register$/i }).click();
  await expect(page.getByRole('cell', { name: cameraName })).toBeVisible();

  await page.getByRole('link', { name: /^layouts$/i }).click();
  await expect(page.getByRole('heading', { name: 'Layouts', exact: true })).toBeVisible();

  // The dialog opens on a 1×1 grid, which is all a kiosk needs to render a wall.
  await page.getByRole('button', { name: /new layout/i }).click();
  await page.locator('#layout-name').fill(layoutName);
  await page.locator('#tile-0-camera').selectOption({ label: cameraName });
  await page.getByRole('button', { name: /save as draft/i }).click();
  await expect(page.getByRole('heading', { name: layoutName })).toBeVisible();

  // Published, not draft: the kiosk picker lists Published revisions only.
  const row = page.getByRole('listitem').filter({ hasText: layoutName });
  await row.getByRole('button', { name: /^publish$/i }).click();
  await expect(row.getByText(/Published/)).toBeVisible();
});
