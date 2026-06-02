import { test, expect } from '@playwright/test';

// ADR-0108 — cameras "read" vertical slice. An operator signs in through the
// real Keycloak login, and the management Cameras page loads the list from the
// camera-catalog service *through the API gateway* (ADR-0106). A 401 / 404 /
// CORS / scope failure anywhere on that path surfaces the "Could not load
// cameras" alert, so asserting the heading renders with no alert proves the
// authenticated vertical end to end: OIDC redirect -> token -> cross-origin
// gateway -> service -> DB.
test('operator signs in and the cameras list loads through the gateway', async ({ page }) => {
  await page.goto('/');

  // Unauthenticated: the management shell shows the sign-in screen.
  await page.getByRole('button', { name: /sign in/i }).click();

  // Real Keycloak login form (standard login-theme element ids).
  await page.locator('#username').fill('operator');
  await page.locator('#password').fill('Operator1234');
  await page.locator('#kc-login').click();

  // Back in the app, authenticated — the Cameras heading renders.
  await expect(page.getByRole('heading', { name: 'Cameras', exact: true })).toBeVisible();

  // The authenticated GET /camera-catalog/cameras succeeded: no error alert.
  await expect(page.getByRole('alert')).toHaveCount(0);
});
