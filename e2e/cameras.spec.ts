import { test, expect, type Page } from '@playwright/test';

// ADR-0108 — cameras vertical slice against the live Aspire stack. An operator
// signs in through the real Keycloak login, then the management Cameras page
// talks to the camera-catalog service *through the API gateway* (ADR-0106).
// Any auth / route / CORS / scope failure surfaces an error, so these prove the
// authenticated path end to end: OIDC -> token -> cross-origin gateway ->
// service -> DB.

async function signInAsOperator(page: Page): Promise<void> {
  await page.goto('/');

  // Unauthenticated: the management shell shows the sign-in screen.
  await page.getByRole('button', { name: /sign in/i }).click();

  // Real Keycloak login form (standard login-theme element ids).
  await page.locator('#username').fill('operator');
  await page.locator('#password').fill('Operator1234');
  await page.locator('#kc-login').click();

  // Back in the app, authenticated — the Cameras heading renders.
  await expect(page.getByRole('heading', { name: 'Cameras', exact: true })).toBeVisible();
}

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
