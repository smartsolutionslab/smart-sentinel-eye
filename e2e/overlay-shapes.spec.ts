import { test, expect, type Page } from '@playwright/test';
import { signInAsOperator } from './support/sign-in';
import { openSection } from './support/management-navigation';
import { FIRST_WRITE_TEST_TIMEOUT_MS, FIRST_WRITE_TIMEOUT_MS } from './support/cold-stack';

// Spec 457 (issue #3107), independent end-to-end procedure steps 2-4 and 6. Not runnable
// locally (needs the Aspire stack); the blocking CI `e2e` job observes it.

interface SavedElement {
  kind: string;
  color: string;
  normalizedX: number;
  normalizedY: number;
  normalizedWidth: number;
  normalizedHeight: number;
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

async function add(page: Page, kind: 'Text' | 'Box' | 'Ellipse'): Promise<void> {
  await page.getByRole('button', { name: 'Add', exact: true }).click();
  await page.getByRole('menuitem', { name: kind, exact: true }).click();
}

test('operator adds a box and an ellipse, edits them, and the API returns the chosen kinds, order and geometry', async ({
  page,
}) => {
  test.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);

  // Origin taken from a real gateway request, never hardcoded (see overlays.spec.ts).
  const gatewayRequest = page.waitForRequest((request) => /\/overlay-designer\//.test(request.url()));

  await signInAsOperator(page);
  await openSection(page, 'Overlays');
  const origin = new URL((await gatewayRequest).url()).origin;

  // Step 2: the Add menu offers the three kinds; a Box is added on top and selected.
  await page.getByRole('button', { name: /new overlay/i }).click();
  const name = `E2E Shapes ${Date.now()}`;
  await page.locator('#overlay-name').fill(name);

  await page.getByRole('button', { name: 'Add', exact: true }).click();
  await expect(page.getByRole('menuitem')).toHaveText(['Text', 'Box', 'Ellipse']);
  await page.getByRole('menuitem', { name: 'Box', exact: true }).click();

  const items = page.getByRole('list', { name: 'Layers' }).getByRole('listitem');
  await expect(items).toHaveCount(2);
  await expect(items.nth(0)).toContainText('Box 1');
  await expect(page.getByRole('button', { name: 'Box 1', exact: true })).toHaveAttribute('aria-pressed', 'true');
  await expect(page.getByLabel('Left', { exact: true })).toBeVisible();
  await expect(page.getByTestId('overlay-editor-text')).toHaveCount(0);

  await page.getByLabel('Left', { exact: true }).fill('40');
  await page.getByLabel('Left', { exact: true }).blur();
  await page.getByRole('button', { name: /save as draft/i }).click();
  await expect(page.getByText(name)).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

  // Step 3: the API returns [Text, Box] with the Box at the chosen geometry and the default colour.
  const created = await readElementsBack(page, origin, name);
  expect(created.map((element) => element.kind)).toEqual(['Text', 'Box']);
  expect(created[1]).toMatchObject({ kind: 'Box', color: '#FFFFFFD9', normalizedX: 0.4, normalizedY: 0.1 });
  expect(created[1]!.text).toBeUndefined();

  // Step 4: edit the draft, add an Ellipse, nudge it, undo, nudge again, send it one layer back.
  const row = page.getByRole('listitem').filter({ hasText: name });
  await row.getByRole('button', { name: /^edit draft$/i }).click();
  await expect(page).toHaveURL(/\/overlays\/[^/]+\/revisions\/1\/edit$/);

  await add(page, 'Ellipse');
  await expect(page.getByRole('button', { name: 'Ellipse 1', exact: true })).toHaveAttribute('aria-pressed', 'true');
  const canvasShape = page.getByTestId('overlay-editor-label');
  await canvasShape.focus();
  await page.keyboard.press('ArrowRight');
  await expect(page.getByLabel('Left', { exact: true })).toHaveValue('10.5');
  await page.keyboard.press('Control+z');
  await expect(page.getByLabel('Left', { exact: true })).toHaveValue('10');
  await page.keyboard.press('ArrowRight');
  await expect(page.getByLabel('Left', { exact: true })).toHaveValue('10.5');

  await page.getByRole('button', { name: 'Move Ellipse 1 down' }).click();

  const saveDraftButton = page.getByRole('button', { name: /^save draft$/i });
  await expect(saveDraftButton).toHaveAttribute('aria-disabled', 'false');
  await saveDraftButton.click();
  await expect(page).toHaveURL(/\/overlays$/, { timeout: FIRST_WRITE_TIMEOUT_MS });

  const edited = await readElementsBack(page, origin, name);
  expect(edited.map((element) => element.kind)).toEqual(['Text', 'Ellipse', 'Box']);
  expect(edited[1]).toMatchObject({ kind: 'Ellipse', color: '#FFFFFFD9', normalizedX: 0.105 });
  expect(edited[2]).toMatchObject({ kind: 'Box', normalizedX: 0.4 });
});

test('operator cannot add a ninth element of any kind', async ({ page }) => {
  await signInAsOperator(page);
  await openSection(page, 'Overlays');

  await page.getByRole('button', { name: /new overlay/i }).click();
  const kinds = ['Box', 'Ellipse', 'Text', 'Box', 'Ellipse', 'Text', 'Box'] as const;
  for (const kind of kinds) {
    await add(page, kind);
  }

  await expect(page.getByRole('list', { name: 'Layers' }).getByRole('listitem')).toHaveCount(8);
  const trigger = page.getByRole('button', { name: 'Add', exact: true });
  await expect(trigger).toHaveAttribute('aria-disabled', 'true');
  await expect(page.getByText('Up to 8 elements')).toBeVisible();

  await trigger.click();
  await expect(page.getByRole('menu')).toHaveCount(0);
  await expect(page.getByRole('list', { name: 'Layers' }).getByRole('listitem')).toHaveCount(8);
});
