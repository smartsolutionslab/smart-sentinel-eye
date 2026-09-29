import { expect, type Page } from '@playwright/test';
import { FIRST_WRITE_TIMEOUT_MS } from './cold-stack';

/**
 * Spec 288 US3 (ADR-0162 §3) — register-a-camera, the two arrival shapes the
 * suite actually uses. Kept as two functions rather than one with a mode
 * flag (ADR-0162's own rule against unifying different arrival locators): the
 * Cameras list renders each row as a `link` where a caller goes on to click
 * straight through it to the camera's own page (`camera-detail.spec.ts`,
 * `interaction-states.spec.ts`); every other caller here never navigates
 * through the row and only needs it to have arrived, read back as a `cell`
 * (`layouts.spec.ts`, the wall specs, the seeds).
 */

/**
 * Registers a camera and waits for its row `link` to appear.
 */
export async function registerCamera(page: Page, { name, url }: { name: string; url: string }): Promise<void> {
  await page.getByRole('button', { name: /register camera/i }).click();
  await page.locator('#register-camera-name').fill(name);
  await page.locator('#register-camera-url').fill(url);
  await page.getByRole('button', { name: /^register$/i }).click();

  await expect(page.getByRole('link', { name })).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });
}

/**
 * Registers a camera and waits for its row `cell` to appear.
 *
 * A caller registering several cameras in a loop passes the cold budget on
 * the first iteration only (`spanning-wall.spec.ts`,
 * `seed-live-video-wall.setup.ts`) and leaves it off the rest, which pay the
 * ordinary warm `expect` timeout instead — `toBeVisible()` called with no
 * argument at all, not with `{ timeout: undefined }`: passing an options
 * object at all, even an empty one, changes the recorded step's params (an
 * `expected: {}` the bare call never had), which the assertion-inventory
 * comparison (ADR-0162 §6) reads as a real difference.
 */
export async function registerCameraViaList(
  page: Page,
  { name, url }: { name: string; url: string },
  options?: { timeout?: number },
): Promise<void> {
  await page.getByRole('button', { name: /register camera/i }).click();
  await page.locator('#register-camera-name').fill(name);
  await page.locator('#register-camera-url').fill(url);
  await page.getByRole('button', { name: /^register$/i }).click();

  const locator = page.getByRole('cell', { name });
  if (options?.timeout === undefined) {
    await expect(locator).toBeVisible();
  } else {
    await expect(locator).toBeVisible({ timeout: options.timeout });
  }
}
