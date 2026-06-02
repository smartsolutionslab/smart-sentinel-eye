import { test, expect } from '@playwright/test';
import { signInAsOperator } from './support/sign-in';

// ADR-0108 — layouts "read" vertical slice. An operator signs in, opens the
// Layouts surface, and the list loads from the layout-composition service
// *through the API gateway* (ADR-0106). A 401 / 404 / CORS / scope failure
// surfaces the "Could not load layouts" alert, so asserting the heading renders
// with no alert proves the authenticated path: OIDC -> token -> cross-origin
// gateway -> service -> DB.
test('operator opens layouts and the list loads through the gateway', async ({ page }) => {
  await signInAsOperator(page);

  await page.getByRole('button', { name: /^layouts$/i }).click();

  // The Layouts surface renders and the authenticated GET
  // /layout-composition/layouts succeeded: no error alert.
  await expect(page.getByRole('heading', { name: 'Layouts', exact: true })).toBeVisible();
  await expect(page.getByRole('alert')).toHaveCount(0);
});
