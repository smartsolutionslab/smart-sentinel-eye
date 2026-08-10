import { test, expect } from '@playwright/test';
import { signInAsOperator } from './support/sign-in';

// ADR-0108 — cameras vertical slice against the live Aspire stack. An operator
// signs in through the real Keycloak login, then the management Cameras page
// talks to the camera-catalog service *through the API gateway* (ADR-0106).
// Any auth / route / CORS / scope failure surfaces an error, so these prove the
// authenticated path end to end: OIDC -> token -> cross-origin gateway ->
// service -> DB.

test('operator signs in and the cameras list loads through the gateway', async ({ page }) => {
  await signInAsOperator(page);

  // The authenticated GET /camera-catalog/cameras succeeded: no error alert.
  await expect(page.getByRole('alert')).toHaveCount(0);
});

test('operator registers a camera and it appears in the list', async ({ page }) => {
  await signInAsOperator(page);

  // POST /camera-catalog/cameras (Bearer; sse.management grandfathers
  // sse.cameras.write), then the list invalidates and refetches.
  await page.getByRole('button', { name: /register camera/i }).click();

  const name = `E2E Cam ${Date.now()}`;
  await page.locator('#register-camera-name').fill(name);
  await page.locator('#register-camera-url').fill('rtsp://10.0.5.99/stream');
  await page.getByRole('button', { name: /^register$/i }).click();

  // The dialog closes and the newly registered camera shows up in the table.
  await expect(page.getByRole('cell', { name })).toBeVisible();
});

// Spec 015 T029 — the fab half of the cameras surface.
//
// The seeded `operator` belongs to /fabs/munich only, so this covers the
// single-fab half of ADR-0114: the fab is inferred, never asked for, and shown
// on the row. The multi-fab half needs op-multi@smart-sentinel-eye.test and is
// covered over HTTP by CameraFabResolutionIntegrationTests — driving a second
// account through the browser would be testing Keycloak's login form, not fab
// resolution.
test.describe('cameras — fab scoping', () => {
  test('operator registers a camera and it lands in their own fab without naming it', async ({ page }) => {
    await signInAsOperator(page);

    await page.getByRole('button', { name: /register camera/i }).click();

    // A single-fab operator is never asked which fab: it is inferred from the
    // one they hold, which is the whole point of ADR-0114.
    await expect(page.locator('#camera-fab-id')).toHaveCount(0);

    const name = `E2E Fab ${Date.now()}`;
    await page.locator('#register-camera-name').fill(name);
    await page.locator('#register-camera-url').fill('rtsp://10.0.5.98/stream');
    await page.getByRole('button', { name: /^register$/i }).click();

    // The row carries the fab, so a multi-fab operator could tell two
    // same-named rows apart — the gap #1303 was for rules.
    const row = page.getByRole('row').filter({ hasText: name });
    await expect(row).toBeVisible();
    await expect(row).toContainText('munich');
  });
});
