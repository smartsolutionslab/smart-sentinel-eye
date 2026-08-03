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

// Spec 012 T055 — `setVariableValue` is the cleanest lost update in the system
// and had no e2e coverage at all. This drives the write end to end: PUT
// /system-variables/system-variables/{name}/value carrying the If-Match the
// client read off the list row (ADR-0113), and the list refetching to show it.
//
// The server now *requires* that header, so a regression dropping it from
// `systemVariables.api.ts` returns 428 and fails here — which is the point.
// Nothing else in the suite would notice.
test('operator sets a variable value and the new value is reflected in the list', async ({ page }) => {
  await signInAsOperator(page);

  await page.getByRole('button', { name: /^system variables$/i }).click();
  await expect(page.getByRole('heading', { name: 'System variables', exact: true })).toBeVisible();

  const name = `E2E_Set_${Date.now()}`;
  await page.getByRole('button', { name: /new variable/i }).click();
  await page.locator('#variable-name').fill(name);
  await page.getByRole('button', { name: /^define$/i }).click();

  // Scope to this variable's row: the list is shared and every row carries the
  // same "New value" / "Set value" controls.
  const row = page.locator('li').filter({ hasText: name });
  await expect(row).toBeVisible();
  await expect(row.getByText('(unset)')).toBeVisible();

  await row.getByPlaceholder('New value').fill('Line 1 running');
  await row.getByRole('button', { name: /^set value$/i }).click();

  await expect(row.getByText('Line 1 running')).toBeVisible();
  // A 428 or 409 would render the page-level banner instead of updating.
  await expect(page.getByRole('alert')).toHaveCount(0);
});
