import { test, expect, type Page } from '@playwright/test';
import { signInAsOperator } from './support/sign-in';
import { openSection } from './support/management-navigation';
import { FIRST_WRITE_TEST_TIMEOUT_MS, FIRST_WRITE_TIMEOUT_MS } from './support/cold-stack';

// Spec 441 (issue #3072) slice 1, T006. Not runnable locally (needs the Aspire
// stack); the blocking CI `e2e` job observes it.

interface SavedElement {
  kind: string;
  text?: string;
}

async function readElementsBack(page: Page, origin: string, name: string): Promise<SavedElement[]> {
  const token = await page.evaluate(() => {
    const key = Object.keys(window.sessionStorage).find((candidate) => candidate.startsWith('oidc.user:'));
    const user = JSON.parse(window.sessionStorage.getItem(key ?? '') ?? '{}') as Record<string, string>;
    return user['access_token'] ?? '';
  });
  expect(token, 'the operator should be holding an access token').not.toBe('');

  const overlays = await page.evaluate(
    async ([gatewayOrigin, accessToken]) => {
      const response = await fetch(`${gatewayOrigin}/overlay-designer/overlays`, {
        headers: { Authorization: `Bearer ${accessToken}` },
      });
      return (await response.json()) as { chains: { name: string; revisions: { elements: SavedElement[] }[] }[] };
    },
    [origin, token] as const,
  );

  const saved = overlays.chains.find((chain) => chain.name === name);
  expect(saved, `the saved overlay "${name}" should be readable back through the gateway`).toBeTruthy();
  return saved!.revisions[0]!.elements;
}

test('operator authors two labels, reorders them on the edit page, and the API returns the chosen order', async ({
  page,
}) => {
  test.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);

  // Origin taken from a real gateway request, never hardcoded (see overlays.spec.ts).
  const gatewayRequest = page.waitForRequest((request) => /\/overlay-designer\//.test(request.url()));

  await signInAsOperator(page);
  await openSection(page, 'Overlays');
  const origin = new URL((await gatewayRequest).url()).origin;

  await page.getByRole('button', { name: /new overlay/i }).click();
  const name = `E2E Layers ${Date.now()}`;
  await page.locator('#overlay-name').fill(name);

  await page.getByTestId('overlay-editor-text').fill('First label');
  await page.getByRole('button', { name: 'Add label' }).click();
  await page.getByTestId('overlay-editor-text').fill('Second label');

  // Front-most first: the label added last is at the top of the list.
  const items = page.getByRole('list', { name: 'Layers' }).getByRole('listitem');
  await expect(items).toHaveCount(2);
  await expect(items.nth(0)).toContainText('Second label');
  await expect(items.nth(1)).toContainText('First label');

  await page.getByRole('button', { name: /save as draft/i }).click();
  await expect(page.getByText(name)).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

  const created = await readElementsBack(page, origin, name);
  expect(created.map((element) => element.text)).toEqual(['First label', 'Second label']);

  const row = page.getByRole('listitem').filter({ hasText: name });
  await row.getByRole('button', { name: /^edit draft$/i }).click();
  await expect(page).toHaveURL(/\/overlays\/[^/]+\/revisions\/1\/edit$/);

  await page.getByRole('button', { name: 'First label', exact: true }).click();
  await page.getByRole('button', { name: 'Move First label up' }).click();

  const saveDraftButton = page.getByRole('button', { name: /^save draft$/i });
  await expect(saveDraftButton).toHaveAttribute('aria-disabled', 'false');
  await saveDraftButton.click();
  await expect(page).toHaveURL(/\/overlays$/, { timeout: FIRST_WRITE_TIMEOUT_MS });

  const reordered = await readElementsBack(page, origin, name);
  expect(reordered.map((element) => element.text)).toEqual(['Second label', 'First label']);
});

test('operator sees an advisory overlap line for two overlapping labels and can still save', async ({ page }) => {
  await signInAsOperator(page);
  await openSection(page, 'Overlays');

  await page.getByRole('button', { name: /new overlay/i }).click();
  await page.getByTestId('overlay-editor-text').fill('Left reading');
  await page.getByRole('button', { name: 'Add label' }).click();
  // The added label starts on the create-default geometry, which overlaps the first.
  await page.getByTestId('overlay-editor-text').fill('Right reading');

  const warning = page.getByTestId('overlay-editor-overlap-warning');
  await expect(warning).toHaveCount(1);
  await expect(warning).toHaveAttribute('role', 'status');
  await expect(warning).toContainText('Left reading');
  await expect(warning).toContainText('Right reading');
  await expect(page.getByRole('button', { name: /save as draft/i })).toHaveAttribute('aria-disabled', 'false');
});

test('operator cannot add a ninth element', async ({ page }) => {
  await signInAsOperator(page);
  await openSection(page, 'Overlays');

  await page.getByRole('button', { name: /new overlay/i }).click();
  const add = page.getByRole('button', { name: 'Add label' });
  for (let added = 0; added < 7; added++) {
    await add.click();
  }

  await expect(page.getByRole('list', { name: 'Layers' }).getByRole('listitem')).toHaveCount(8);
  await expect(add).toHaveAttribute('aria-disabled', 'true');
  await expect(page.getByText('Up to 8 elements')).toBeVisible();
});
