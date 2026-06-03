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

test('operator defines a system variable and it appears in the list', async ({ page }) => {
  await signInAsOperator(page);

  await page.getByRole('button', { name: /^system variables$/i }).click();
  await expect(page.getByRole('heading', { name: 'System variables', exact: true })).toBeVisible();

  // POST /system-variables/system-variables (Bearer; sse.management grandfathers
  // sse.variables.write); the list invalidates and refetches.
  await page.getByRole('button', { name: /new variable/i }).click();
  const name = `E2E_Var_${Date.now()}`;
  await page.locator('#variable-name').fill(name);
  // Type defaults to String, so the name alone is a valid definition.
  await page.getByRole('button', { name: /^define$/i }).click();

  await expect(page.getByText(name)).toBeVisible();
});

test('operator defines a Boolean system variable and it appears in the list', async ({ page }) => {
  await signInAsOperator(page);

  await page.getByRole('button', { name: /^system variables$/i }).click();
  await expect(page.getByRole('heading', { name: 'System variables', exact: true })).toBeVisible();

  // Selecting Boolean reveals the conditional truthy/falsy label fields, which the
  // schema requires for the Boolean code path (else the Define POST never fires).
  await page.getByRole('button', { name: /new variable/i }).click();
  const name = `E2E_Bool_${Date.now()}`;
  await page.locator('#variable-name').fill(name);
  await page.locator('#variable-type').selectOption('Boolean');
  await page.locator('#variable-truthy').fill('On');
  await page.locator('#variable-falsy').fill('Off');
  await page.getByRole('button', { name: /^define$/i }).click();

  await expect(page.getByText(name)).toBeVisible();
});
