import { test, expect } from '@playwright/test';
import { signInAsOperator } from './support/sign-in';

// ADR-0108 — overlays "read" vertical slice. An operator signs in, opens the
// Overlays surface, and the list loads from the overlay-designer service
// *through the API gateway* (ADR-0106). A 401 / 404 / CORS / scope failure
// surfaces the "Could not load overlays" alert, so asserting the heading renders
// with no alert proves the authenticated path end to end.
test('operator opens overlays and the list loads through the gateway', async ({ page }) => {
  await signInAsOperator(page);

  await page.getByRole('button', { name: /^overlays$/i }).click();

  await expect(page.getByRole('heading', { name: 'Overlays', exact: true })).toBeVisible();
  await expect(page.getByRole('alert')).toHaveCount(0);
});
