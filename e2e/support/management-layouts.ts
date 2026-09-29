import { expect, type Page } from '@playwright/test';
import { registerCameraViaList } from './management-cameras';
import { openSection } from './management-navigation';
import { FIRST_WRITE_TIMEOUT_MS } from './cold-stack';

/**
 * Spec 288 US3 (ADR-0162 §3) — registers a camera and authors + publishes a
 * one-tile layout referencing it. Leaves the page on the Layouts list.
 *
 * Deliberately does **not** navigate to Cameras itself: `seed-published-
 * layout.setup.ts` calls this straight after sign-in, while it is already on
 * the Cameras landing page, and `wall-changes-its-scene.spec.ts` /
 * `in-flight-focus.spec.ts` call `openSection(page, 'Cameras')` themselves
 * immediately before each call (its second call in a row otherwise starts on
 * the Layouts list the first call's own ending left it on). Baking the nav
 * click in here would add an assertion the seed's caller never had
 * (ADR-0162 §6 — the inventory must not gain a step no caller asked for).
 *
 * The camera's address is a random, unserved `10.0.5.x` — nothing in this
 * function's callers asserts on it, only on the camera's name, so varying it
 * from caller to caller (the seed used a fixed `.70`) carries no observable
 * difference.
 *
 * Returns the registered camera's identifier (a GUID), extracted from its
 * Cameras-page row link — `wall-changes-its-scene.spec.ts` uses it because
 * that, not the camera's name, is what ends up embedded in the kiosk's
 * layout-tile content; the seed and `in-flight-focus.spec.ts`'s callers
 * discard it.
 */
export async function createPublishedLayout(page: Page, name: string, cameraName: string): Promise<string> {
  await registerCameraViaList(
    page,
    { name: cameraName, url: `rtsp://10.0.5.${Math.floor(Math.random() * 200) + 2}/stream` },
    { timeout: FIRST_WRITE_TIMEOUT_MS },
  );
  const cameraHref = await page.getByRole('cell', { name: cameraName }).getByRole('link').getAttribute('href');
  const cameraIdentifier = cameraHref!.split('/').pop()!;

  await openSection(page, 'Layouts');
  await page.getByRole('button', { name: /new layout/i }).click();
  await page.locator('#layout-name').fill(name);
  await page.locator('#tile-0-camera').selectOption({ label: cameraName });
  await page.getByRole('button', { name: /save as draft/i }).click();
  await expect(page.getByRole('heading', { name })).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

  const row = page.getByRole('listitem').filter({ hasText: name });
  await row.getByRole('button', { name: /^publish$/i }).click();
  await expect(row.getByText(/Published/)).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

  return cameraIdentifier;
}
