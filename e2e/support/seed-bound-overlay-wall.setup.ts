import { test as setup, expect } from '@playwright/test';
import { signInAsOperator } from './sign-in';
import { newBoundOverlayWall, writeBoundOverlayWall } from './bound-overlay-wall';
import { FIRST_WRITE_TIMEOUT_MS } from './cold-stack';

/**
 * Issue 1921 — a published wall whose tile binds a published overlay whose
 * label binds a system variable.
 *
 * `seed-published-layout.setup.ts` deliberately publishes a layout with an
 * *unbound* tile, because that is all the kiosk needs to render a wall. SC-004
 * needs the opposite: something on screen whose value can go stale while the
 * hub is down, so that reconciliation after reconnect is observable at all.
 *
 * Nothing else seeds this. The scenario simulator builds richer walls but is
 * gated out of the e2e stack — the same reason the other seed exists.
 *
 * Drives management-web rather than the API: publishing needs an `If-Match`
 * round-trip, and the UI path gets the contract right for free.
 */
setup('a published wall exists whose tile binds an overlay bound to a variable', async ({ page }) => {
  // Sized here rather than taken from `FIRST_WRITE_TEST_TIMEOUT_MS`: six
  // budgeted sites do not fit the shared ceiling. Five of them arriving cold at
  // ~40 s each leaves nothing for the sixth to spend its 90 s and report *which*
  // locator never resolved — 5 × 40 s + a sign-in + 90 s ≈ 320 s. `cold-stack.ts`
  // carries the rule.
  setup.setTimeout(360_000);

  const wall = newBoundOverlayWall();
  writeBoundOverlayWall(wall);

  await signInAsOperator(page);

  // 1. The variable the overlay label will resolve from.
  await page.getByRole('link', { name: /^system variables$/i }).click();
  await expect(page.getByRole('heading', { name: 'System variables', exact: true })).toBeVisible();

  await page.getByRole('button', { name: /new variable/i }).click();
  await page.locator('#variable-name').fill(wall.variableName);
  await page.locator('#variable-initial-value').fill(wall.variableInitialValue);
  await page.getByRole('button', { name: /^define$/i }).click();

  // **Every write in this file gets the budget, not just this one.** Six kinds
  // of write across four services follow, and the cold cost attaches to the
  // message *type* — so the camera register below is exactly as cold as this
  // define. This project is a dependency of `kiosk` and `wall`, so under
  // `--project=kiosk` these are the first writes of the entire run, and a seed
  // failure fails every dependent project. The reasoning for the number lives
  // once, with the constant.
  await expect(page.getByRole('heading', { name: wall.variableName })).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

  // 2. An overlay whose label is a token (spec 005) so the service holds a
  //    resolved snapshot for it — a static label has none.
  await page.getByRole('link', { name: /^overlays$/i }).click();
  await expect(page.getByRole('heading', { name: 'Overlays', exact: true })).toBeVisible();

  await page.getByRole('button', { name: /new overlay/i }).click();
  await page.locator('#overlay-name').fill(wall.overlayName);
  await page.getByTestId('overlay-editor-text').fill(`{{${wall.variableName}}}`);
  await page.getByRole('button', { name: /save as draft/i }).click();
  await expect(page.getByText(wall.overlayName)).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

  // Published, not draft: a tile can only bind a published overlay.
  const overlayRow = page.getByRole('listitem').filter({ hasText: wall.overlayName });
  await overlayRow.getByRole('button', { name: /^publish$/i }).click();
  await expect(overlayRow.getByText(/Published/)).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

  // 3. A camera, because a tile requires one. The `E2E ` prefix is what the
  //    cleanup teardown matches on (issue 1895).
  await page.getByRole('link', { name: /^cameras$/i }).click();
  await page.getByRole('button', { name: /register camera/i }).click();
  await page.locator('#register-camera-name').fill(wall.cameraName);
  await page.locator('#register-camera-url').fill('rtsp://10.0.5.71/stream');
  await page.getByRole('button', { name: /^register$/i }).click();
  await expect(page.getByRole('cell', { name: wall.cameraName })).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

  // 4. The wall: one tile, that camera, that overlay.
  await page.getByRole('link', { name: /^layouts$/i }).click();
  await expect(page.getByRole('heading', { name: 'Layouts', exact: true })).toBeVisible();

  await page.getByRole('button', { name: /new layout/i }).click();
  await page.locator('#layout-name').fill(wall.layoutName);
  await page.locator('#tile-0-camera').selectOption({ label: wall.cameraName });
  await page.locator('#tile-0-overlay').selectOption({ label: wall.overlayName });
  await page.getByRole('button', { name: /save as draft/i }).click();
  await expect(page.getByRole('heading', { name: wall.layoutName })).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

  const layoutRow = page.getByRole('listitem').filter({ hasText: wall.layoutName });
  await layoutRow.getByRole('button', { name: /^publish$/i }).click();
  await expect(layoutRow.getByText(/Published/)).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });
});
