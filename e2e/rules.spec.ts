import { test, expect } from '@playwright/test';
import { signInAsOperator } from './support/sign-in';

// Spec 013 T040 — Automation's first e2e coverage.
//
// Still skipped, but no longer for the reason first written here. #1298 — the
// 500 from GET /rules that stopped the screen loading at all — is fixed, and
// the listing is covered over HTTP by
// CrossFabEvaluationIntegrationTests.The_listing_omits_another_fabs_rules.
//
// What blocks these two tests is that the frontend half of spec 013 was never
// built: management-web renders no fab on a rule row and sends none when
// authoring, so `rule-fab` and the per-row 'munich' label below match nothing.
// The earlier claim that "nothing here should need rewriting" was wrong — the
// assertions describe a UI that does not exist yet.
//
// Equivalent behaviour is covered where it can run today:
//   - fab inference and refusal .... FabResolutionTests (all four rows)
//   - listing scoped to the caller . RuleQueryHandlerTests, and over HTTP in
//                                    CrossFabEvaluationIntegrationTests
//   - unreachable across fabs ...... CrossFabEvaluationIntegrationTests
test.describe.skip('rules — fab scoping (blocked on the missing UI)', () => {
  test('operator authors a rule and it lands in their own fab without naming it', async ({ page }) => {
    await signInAsOperator(page);

    await page.getByRole('button', { name: /^rules$/i }).click();
    await expect(page.getByRole('heading', { name: 'Rules', exact: true })).toBeVisible();
    await expect(page.getByRole('alert')).toHaveCount(0);

    const name = `e2e-rule-${Date.now()}`;
    await page.getByRole('button', { name: /new rule/i }).click();
    await page.locator('#rule-name').fill(name);
    await page.locator('#rule-trigger-source').fill('plc');
    await page.locator('#rule-trigger-kind').fill('PlcCycleStart');
    await page.locator('#rule-predicate').fill('$.payload.cycleTime <= 30');
    await page.locator('#rule-variable-name').fill('oeeLine1');
    await page.locator('#rule-value-expression').fill('100 - $.payload.cycleTime * 2');
    await page.getByRole('button', { name: /^create$/i }).click();

    // The operator never chose a fab. It is inferred from their single
    // assignment, which is the whole point of ADR-0114.
    const row = page.getByRole('listitem').filter({ hasText: name });
    await expect(row).toBeVisible();
    await expect(row.getByText('munich')).toBeVisible();
  });

  test('the list shows only rules from the operator’s own fab', async ({ page }) => {
    await signInAsOperator(page);

    await page.getByRole('button', { name: /^rules$/i }).click();
    await expect(page.getByRole('heading', { name: 'Rules', exact: true })).toBeVisible();

    // Every visible row belongs to a fab this operator holds. The seeded
    // operator is in munich only, so anything else is a scoping failure.
    const fabs = page.getByTestId('rule-fab');
    const count = await fabs.count();
    for (let i = 0; i < count; i++) {
      await expect(fabs.nth(i)).toHaveText('munich');
    }
  });
});
