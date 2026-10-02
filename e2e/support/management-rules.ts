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

/**
 * Spec 296 (#2618) US2 (ADR-0162 §3) — fills the rule-authoring form for a
 * `SwitchWallScene` action: the base fields `fillRuleForm` already fills
 * (with a `manual`/`LineStop` trigger, matching the manual-ingest event this
 * spec's rule is meant to react to), then the Action/Wall/Target selects this
 * action type adds (plan.md §4.2). Fill only: no submit, no assertion, the
 * same contract as `fillRuleForm` above — the caller clicks "Create draft"
 * and asserts arrival itself.
 *
 * `targetLabel` is the target select's option name: either a Published
 * layout's name (targets that scene) or `'Next scene'` (targets Next).
 */
export async function createSwitchWallRule(
  page: Page,
  name: string,
  wallName: string,
  targetLabel: string,
): Promise<void> {
  await page.locator('#rule-name').fill(name);
  await page.locator('#rule-source').fill('manual');
  await page.locator('#rule-kind').fill('LineStop');
  await page.locator('#rule-predicate').fill('$.payload.line == 3');

  await page.getByRole('combobox', { name: /^action$/i }).click();
  await page.getByRole('option', { name: "Switch a wall's scene" }).click();

  await page.getByRole('combobox', { name: /^wall$/i }).click();
  await page.getByRole('option', { name: wallName }).click();

  await page.getByRole('combobox', { name: /target/i }).click();
  await page.getByRole('option', { name: targetLabel }).click();
}
