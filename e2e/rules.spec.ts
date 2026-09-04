import { test, expect, type Page } from '@playwright/test';
import { signInAsOperator } from './support/sign-in';
import { FIRST_WRITE_TEST_TIMEOUT_MS, FIRST_WRITE_TIMEOUT_MS } from './support/cold-stack';

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
    test.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);

    await openRules(page);

    const name = `e2e-rule-${Date.now()}`;
    await page.getByRole('button', { name: /new rule/i }).click();

    // A single-fab operator is never asked which fab: it is inferred from the
    // one they hold, which is the whole point of ADR-0114.
    await expect(page.locator('#rule-fab-id')).toHaveCount(0);

    await fillRuleForm(page, name);
    await page.getByRole('button', { name: /^create draft$/i }).click();

    const row = page.getByRole('row').filter({ hasText: name });
    await expect(row).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });
    await expect(row.getByTestId('rule-fab')).toHaveText('munich');
  });

  test('every listed rule shows a fab the operator holds', async ({ page }) => {
    test.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);

    await openRules(page);

    // Authors its own rule rather than relying on the test above: an empty
    // list would make the loop below assert nothing at all.
    const name = `e2e-scope-${Date.now()}`;
    await page.getByRole('button', { name: /new rule/i }).click();
    await fillRuleForm(page, name);
    await page.getByRole('button', { name: /^create draft$/i }).click();
    await expect(page.getByRole('row').filter({ hasText: name })).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

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

// #1952. Both of the page's mutations discarded their result, so a refused
// publish told the operator nothing — and worse than nothing: the list still
// refetched, the row flipped to Published (by the *other* operator), and the
// refusal read as success.
//
// The twin of the layouts race in layouts.spec.ts, and deliberately shaped the
// same way: two contexts share the seeded `operator`, because concurrency is
// per-aggregate rather than per-user. An assertion on the *absence* of a
// conflict would have passed on the broken build, so this asserts the conflict
// is surfaced.
test('a second operator publishing the same rule is refused, not silently applied', async ({ browser }) => {
  test.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);

  const name = `e2e-race-rule-${Date.now()}`;

  const first = await browser.newContext();
  const second = await browser.newContext();

  try {
    const pageOne = await first.newPage();
    await openRules(pageOne);

    await pageOne.getByRole('button', { name: /new rule/i }).click();
    await fillRuleForm(pageOne, name);
    await pageOne.getByRole('button', { name: /^create draft$/i }).click();

    const rowOne = pageOne.getByRole('row').filter({ hasText: name });
    await expect(rowOne).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

    // The second context loads the list *now*, so it holds the same version the
    // first one does. Loading it after the publish below would hand it the
    // current version and prove nothing.
    const pageTwo = await second.newPage();
    await openRules(pageTwo);
    const rowTwo = pageTwo.getByRole('row').filter({ hasText: name });
    await expect(rowTwo.getByRole('button', { name: /^publish$/i })).toBeVisible();

    // First writer wins.
    await rowOne.getByRole('button', { name: /^publish$/i }).click();
    await expect(rowOne.getByText('Active')).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

    // Second writer is acting on the version it read before that publish.
    await rowTwo.getByRole('button', { name: /^publish$/i }).click();

    // The refusal is surfaced, and the advice is to reload rather than retry —
    // retrying is what replays the stale intent over the first writer.
    const alert = pageTwo.getByRole('alert');
    await expect(alert).toBeVisible();
    await expect(alert).toContainText(/changed|reload|re-read/i);
    await expect(alert).not.toContainText(/try again/i);
  } finally {
    await first.close();
    await second.close();
  }
});
