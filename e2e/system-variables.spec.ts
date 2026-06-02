import { test, expect } from '@playwright/test';
import { signInAsOperator } from './support/sign-in';

// ADR-0108 — system-variables "read" vertical slice. An operator signs in, opens
// the System variables surface, and the list loads from the system-variables
// service *through the API gateway* (ADR-0106). A 401 / 404 / CORS / scope
// failure surfaces the "Could not load variables" alert, so asserting the
// heading renders with no alert proves the authenticated path end to end.
test('operator opens system variables and the list loads through the gateway', async ({ page }) => {
  await signInAsOperator(page);

  await page.getByRole('button', { name: /^system variables$/i }).click();

  await expect(page.getByRole('heading', { name: 'System variables', exact: true })).toBeVisible();
  await expect(page.getByRole('alert')).toHaveCount(0);
});
