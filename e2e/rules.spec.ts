import { test, expect, type Page } from '@playwright/test';
import { signInAsOperator } from './support/sign-in';

// Spec 013 T040 — Automation's e2e coverage for fab scoping.
//
// Previously skipped twice over: first against #1298 (GET /rules returned 500,
// so the screen never loaded), then against #1303 (the UI rendered no fab and
// sent none, so the assertions described something that did not exist). Both
// are fixed; these run.
//
// The seeded `operator` belongs to /fabs/munich only, so this covers the
// single-fab half of ADR-0114: the fab is inferred, never asked for, and shown
// on the row. The multi-fab half needs op-multi@smart-sentinel-eye.test and is
// covered over HTTP by RuleFabResolutionIntegrationTests — driving a second
// account through the browser would be testing Keycloak's login form, not fab
// resolution.
test.describe('rules — fab scoping', () => {
  test('operator authors a rule and it lands in their own fab without naming it', async ({ page }) => {
    await openRules(page);

    const name = `e2e-rule-${Date.now()}`;
    await page.getByRole('button', { name: /new rule/i }).click();

    // A single-fab operator is never asked which fab: it is inferred from the
    // one they hold, which is the whole point of ADR-0114.
    await expect(page.locator('#rule-fab-id')).toHaveCount(0);

    await fillRuleForm(page, name);
    await page.getByRole('button', { name: /^create draft$/i }).click();

    const row = page.getByRole('row').filter({ hasText: name });
    await expect(row).toBeVisible();
    await expect(row.getByTestId('rule-fab')).toHaveText('munich');
  });

  test('every listed rule shows a fab the operator holds', async ({ page }) => {
    await openRules(page);

    // Authors its own rule rather than relying on the test above: an empty
    // list would make the loop below assert nothing at all.
    const name = `e2e-scope-${Date.now()}`;
    await page.getByRole('button', { name: /new rule/i }).click();
    await fillRuleForm(page, name);
    await page.getByRole('button', { name: /^create draft$/i }).click();
    await expect(page.getByRole('row').filter({ hasText: name })).toBeVisible();

    // The seeded operator is in munich only, so anything else on screen is a
    // scoping failure — the server filtering by fab and the row rendering it,
    // observed together.
    const fabs = page.getByTestId('rule-fab');
    const count = await fabs.count();
    expect(count).toBeGreaterThan(0);
    for (let index = 0; index < count; index++) {
      await expect(fabs.nth(index)).toHaveText('munich');
    }
  });
});

async function openRules(page: Page) {
  await signInAsOperator(page);
  await page.getByRole('link', { name: /^rules$/i }).click();
  await expect(page.getByRole('heading', { name: 'Rules', exact: true })).toBeVisible();
  await expect(page.getByRole('alert')).toHaveCount(0);
}

async function fillRuleForm(page: Page, name: string) {
  await page.locator('#rule-name').fill(name);
  await page.locator('#rule-source').fill('plc');
  await page.locator('#rule-kind').fill('PlcCycleStart');
  await page.locator('#rule-predicate').fill('$.payload.cycleTime <= 30');
  await page.locator('#rule-variable').fill('oeeLine1');
  await page.locator('#rule-value-expression').fill('100 - $.payload.cycleTime * 2');
}
