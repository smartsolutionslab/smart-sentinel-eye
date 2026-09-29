import { expect, type Page } from '@playwright/test';
import { FIRST_WRITE_TIMEOUT_MS } from './cold-stack';

/**
 * Spec 288 US3 (ADR-0162 §3) — the minimal overlay draft: no custom label
 * text, no geometry. Callers whose test needs either (the geometry specs,
 * the seeds' token-bound overlays) stay inline — a different, longer step
 * sequence, not this one with an extra parameter (ADR-0162 §3/§4).
 */
export async function createOverlayDraft(page: Page, name: string): Promise<void> {
  await page.getByRole('button', { name: /new overlay/i }).click();
  await page.locator('#overlay-name').fill(name);
  await page.getByRole('button', { name: /save as draft/i }).click();

  await expect(page.getByText(name)).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });
}
