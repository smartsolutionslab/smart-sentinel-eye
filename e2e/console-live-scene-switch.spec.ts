import { test, expect } from '@playwright/test';
import { signInAsOperator } from './support/sign-in';
import { openSection } from './support/management-navigation';
import { createPublishedLayout } from './support/management-layouts';
import { FIRST_WRITE_TEST_TIMEOUT_MS, FIRST_WRITE_TIMEOUT_MS } from './support/cold-stack';

// The file name must not contain "wall-" or "kiosk-": the config's patterns are unanchored, so either one sends it to a kiosk
// project and hides it from the console's.
test.use({ baseURL: 'http://localhost:5173' });

test('a wall page open in the console follows a scene switch made by another operator, without a reload', async ({
  page,
  browser,
}) => {
  test.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);

  const stamp = Date.now();
  const layoutAName = `E2E Layout Console Live A ${stamp}`;
  const layoutBName = `E2E Layout Console Live B ${stamp}`;
  const wallName = `E2E Console Live Wall ${stamp}`;

  await signInAsOperator(page);
  await openSection(page, 'Cameras');
  await createPublishedLayout(page, layoutAName, `E2E Console Live Cam A ${stamp}`);
  await openSection(page, 'Cameras');
  await createPublishedLayout(page, layoutBName, `E2E Console Live Cam B ${stamp}`);

  await openSection(page, 'Walls');
  await page.getByRole('button', { name: /new wall/i }).click();
  await page.locator('#wall-name').fill(wallName);
  await page.getByRole('checkbox', { name: layoutAName }).check();
  await page.getByRole('checkbox', { name: layoutBName }).check();
  await page.getByRole('button', { name: /^save$/i }).click();
  await expect(page.getByRole('heading', { name: wallName })).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

  const showing = page.getByTestId('wall-showing');
  await expect(showing).toContainText(layoutAName);

  // A second operator session switches the scene. This page does nothing: the only way it can learn of the switch is the hub.
  const otherContext = await browser.newContext({ baseURL: 'http://localhost:5173' });
  const other = await otherContext.newPage();
  try {
    await signInAsOperator(other);
    await openSection(other, 'Walls');
    await other.getByRole('button', { name: new RegExp(wallName) }).click();
    await expect(other.getByRole('heading', { name: wallName })).toBeVisible();
    await other.getByRole('button', { name: /^next$/i }).click();
    await expect(other.getByTestId('wall-showing')).toContainText(layoutBName);

    await expect(showing).toContainText(layoutBName, { timeout: 5_000 });
  } finally {
    await otherContext.close();
  }
});
