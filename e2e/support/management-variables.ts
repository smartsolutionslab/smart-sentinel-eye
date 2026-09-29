import type { Page } from '@playwright/test';

/**
 * Spec 288 US3 (ADR-0162 §3) — defines a system variable with no type beyond
 * the dialog's own String default. Fill only, no arrival assertion: every
 * caller reads its own row back afterward with its own locator shape, so
 * asserting here would duplicate, not replace, that read. Needs no
 * deliberate break for the same reason as `fillRuleForm` — it asserts
 * nothing at all.
 *
 * Callers that also drive the Type selector (`system-variables.spec.ts`'s
 * Boolean and toggle-back tests) or check the fab selector's absence
 * mid-dialog stay inline — a different, longer step sequence, not this one
 * (ADR-0162 §3).
 */
export async function defineVariable(page: Page, name: string): Promise<void> {
  await page.getByRole('button', { name: /new variable/i }).click();
  await page.locator('#variable-name').fill(name);
  await page.getByRole('button', { name: /^define$/i }).click();
}
