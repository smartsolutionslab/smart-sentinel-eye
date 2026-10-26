import { test, expect, type Page } from '@playwright/test';
import { signInAsOperator } from './support/sign-in';
import { openSection } from './support/management-navigation';
import { FIRST_WRITE_TEST_TIMEOUT_MS, FIRST_WRITE_TIMEOUT_MS } from './support/cold-stack';

// Spec 463 (issue #3108), "Independent end-to-end test (US2)": pick a swatch, set the opacity to
// its floor, save, read the colour back through the API; a too-bright hex sends no request.
// Not runnable locally (needs the Aspire stack); the blocking CI `e2e` job observes it. The
// publish-and-kiosk-tile step needs a wall binding and is left to the verification run.

interface SavedElement {
  kind: string;
  color: string;
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

test('operator colours a label from a swatch at its opacity floor, and a too-bright hex sends nothing', async ({
  page,
}) => {
  test.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);

  const gatewayRequest = page.waitForRequest((request) => /\/overlay-designer\//.test(request.url()));

  await signInAsOperator(page);
  await openSection(page, 'Overlays');
  const origin = new URL((await gatewayRequest).url()).origin;

  await page.getByRole('button', { name: /new overlay/i }).click();
  const name = `E2E Colour ${Date.now()}`;
  await page.locator('#overlay-name').fill(name);

  // The label is the only element and is selected: pick a swatch and observe its hex.
  await page.getByRole('button', { name: 'cyan-700', exact: true }).click();
  const hexField = page.getByLabel('Colour', { exact: true });
  await expect(hexField).toHaveValue('#0F707DFF');

  // Opacity to its floor.
  const opacity = page.getByLabel('Opacity', { exact: true });
  const floor = Number(await opacity.getAttribute('min'));
  await opacity.fill(String(floor));
  const expected = `#0F707D${floor.toString(16).toUpperCase().padStart(2, '0')}`;
  await expect(hexField).toHaveValue(expected);

  // A too-bright hex is refused before any request.
  const requests: string[] = [];
  page.on('request', (request) => {
    if (request.method() !== 'GET') requests.push(`${request.method()} ${request.url()}`);
  });
  await hexField.fill('#FFFFFF');
  await hexField.press('Enter');
  await expect(page.getByRole('alert').filter({ hasText: /brighter than the wall allows/i })).toBeVisible();
  expect(requests, 'a refused colour must not reach the API').toEqual([]);
  await hexField.fill(expected);
  await hexField.press('Enter');

  await page.getByRole('button', { name: /save as draft/i }).click();
  await expect(page.getByText(name)).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

  const elements = await readElementsBack(page, origin, name);
  expect(elements[0]!.color).toBe(expected);
});
