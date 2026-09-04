import { test, expect } from '@playwright/test';
import { signInAsOperator } from './support/sign-in';
import { FIRST_WRITE_TEST_TIMEOUT_MS, FIRST_WRITE_TIMEOUT_MS } from './support/cold-stack';

// Spec 066 / #2014 — how long the injected-delay test below holds the define
// `POST`. 20 s is chosen against both `expect` budgets in `playwright.config.ts`:
// above the 15 s local default and below CI's 30 s, so the same test says the
// same thing in both places, and well below the 60 s per-test timeout so the
// failure is an *assertion* timeout rather than a test timeout.
const SLOW_WRITE_DELAY_MS = 20_000;

// ADR-0108 — system-variables "read" vertical slice. An operator signs in, opens
// the System variables surface, and the list loads from the system-variables
// service *through the API gateway* (ADR-0106). A 401 / 404 / CORS / scope
// failure surfaces the "Could not load variables" alert, so asserting the
// heading renders with no alert proves the authenticated path end to end.
test('operator opens system variables and the list loads through the gateway', async ({ page }) => {
  await signInAsOperator(page);

  await page.getByRole('link', { name: /^system variables$/i }).click();

  await expect(page.getByRole('heading', { name: 'System variables', exact: true })).toBeVisible();
  await expect(page.getByRole('alert')).toHaveCount(0);
});

test('operator defines a system variable and it appears in the list', async ({ page }) => {
  test.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);

  await signInAsOperator(page);

  await page.getByRole('link', { name: /^system variables$/i }).click();
  await expect(page.getByRole('heading', { name: 'System variables', exact: true })).toBeVisible();

  // POST /system-variables/system-variables (Bearer; sse.management grandfathers
  // sse.variables.write); the list invalidates and refetches.
  await page.getByRole('button', { name: /new variable/i }).click();
  const name = `E2E_Var_${Date.now()}`;
  await page.locator('#variable-name').fill(name);
  // Type defaults to String, so the name alone is a valid definition.
  await page.getByRole('button', { name: /^define$/i }).click();

  await expect(page.getByText(name)).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });
});

test('operator defines a Boolean system variable and it appears in the list', async ({ page }) => {
  test.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);

  await signInAsOperator(page);

  await page.getByRole('link', { name: /^system variables$/i }).click();
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

  await expect(page.getByText(name)).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });
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
  test.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);

  await signInAsOperator(page);

  await page.getByRole('link', { name: /^system variables$/i }).click();
  await expect(page.getByRole('heading', { name: 'System variables', exact: true })).toBeVisible();

  const name = `E2E_Set_${Date.now()}`;
  await page.getByRole('button', { name: /new variable/i }).click();
  await page.locator('#variable-name').fill(name);
  await page.getByRole('button', { name: /^define$/i }).click();

  // Scope to this variable's row: the list is shared and every row carries the
  // same "New value" / "Set value" controls.
  const row = page.locator('li').filter({ hasText: name });
  await expect(row).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });
  await expect(row.getByText('(unset)')).toBeVisible();

  await row.getByPlaceholder('New value').fill('Line 1 running');
  await row.getByRole('button', { name: /^set value$/i }).click();

  await expect(row.getByText('Line 1 running')).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });
  // A 428 or 409 would render the page-level banner instead of updating.
  await expect(page.getByRole('alert')).toHaveCount(0);
});

// Spec 066 / #2014 — the first write of a run against a cold service is served
// slowly (specs/023-first-event-cold-start/verification.md §3 measured ~5 s per
// message type, still unexplained), and every write assertion in this file
// waits at the shared `expect` budget — 15 s locally. This test makes that
// arrangement observable on a warm stack by holding the define `POST` for
// SLOW_WRITE_DELAY_MS: the write succeeds, it is merely late, and the assertion
// gives up before it lands.
test('a define the service is slow to answer still appears in the list', async ({ page }) => {
  await signInAsOperator(page);

  // **Only the POST.** The list `GET` is the *same* URL — `systemVariables.api.ts`
  // gives both `url: ''` against the `system-variables/system-variables` base —
  // so a route that did not discriminate by method would delay the read as well,
  // and the test would be red for the wrong reason and stay red after the fix.
  // A URL predicate rather than a glob, because the `GET` carries query
  // parameters and the `POST` may carry `?fabId=`.
  await page.route(
    (url) => url.pathname.endsWith('/system-variables/system-variables'),
    async (route) => {
      if (route.request().method() !== 'POST') {
        return route.fallback();
      }
      await new Promise((resolve) => setTimeout(resolve, SLOW_WRITE_DELAY_MS));
      await route.continue();
    },
  );

  await page.getByRole('link', { name: /^system variables$/i }).click();
  // The read is NOT delayed: this heading follows the list `GET` through the
  // same route handler and still resolves at the default budget.
  await expect(page.getByRole('heading', { name: 'System variables', exact: true })).toBeVisible();

  await page.getByRole('button', { name: /new variable/i }).click();
  const name = `E2E_Slow_${Date.now()}`;
  await page.locator('#variable-name').fill(name);
  await page.getByRole('button', { name: /^define$/i }).click();

  // No explicit timeout — the shape every write assertion in this file uses
  // today, and the reason a late-but-successful write reads as a product
  // failure.
  await expect(page.getByText(name)).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });
});

// Spec 014 T041 — the fab half of the variables surface.
//
// The seeded `operator` belongs to /fabs/munich only, so this covers the
// single-fab half of ADR-0114: the fab is inferred, never asked for, and shown
// on the row. The multi-fab half needs op-multi@smart-sentinel-eye.test and is
// covered over HTTP by VariableFabResolutionIntegrationTests — driving a second
// account through the browser would be testing Keycloak's login form, not fab
// resolution.
test.describe('system variables — fab scoping', () => {
  test('operator defines a variable and it lands in their own fab without naming it', async ({ page }) => {
    test.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);

    await signInAsOperator(page);
    await page.getByRole('link', { name: /^system variables$/i }).click();
    await expect(page.getByRole('heading', { name: 'System variables', exact: true })).toBeVisible();

    await page.getByRole('button', { name: /new variable/i }).click();

    // A single-fab operator is never asked which fab: it is inferred from the
    // one they hold (ADR-0114). The selector must not even render.
    await expect(page.locator('#variable-fab-id')).toHaveCount(0);

    const name = `E2E_Fab_${Date.now()}`;
    await page.locator('#variable-name').fill(name);
    await page.getByRole('button', { name: /^define$/i }).click();

    // The row carries the fab, so a multi-fab operator could tell two
    // same-named rows apart — the gap #1303 was for rules.
    const row = page.getByRole('listitem').filter({ hasText: name });
    await expect(row).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });
    await expect(row).toContainText('munich');
  });
});
