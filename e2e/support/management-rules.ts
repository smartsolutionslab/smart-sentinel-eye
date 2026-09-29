import type { Page } from '@playwright/test';

/**
 * Spec 288 US3 (ADR-0162 §3) — fills the rule-authoring form's required
 * fields. Fill only: no submit, no assertion, so it needs no deliberate break
 * (ADR-0162 §6's "arrival-asserting helper" rule does not apply — this one
 * asserts nothing at all).
 */
export async function fillRuleForm(page: Page, name: string): Promise<void> {
  await page.locator('#rule-name').fill(name);
  await page.locator('#rule-source').fill('plc');
  await page.locator('#rule-kind').fill('PlcCycleStart');
  await page.locator('#rule-predicate').fill('$.payload.cycleTime <= 30');
  await page.locator('#rule-variable').fill('oeeLine1');
  await page.locator('#rule-value-expression').fill('100 - $.payload.cycleTime * 2');
}
