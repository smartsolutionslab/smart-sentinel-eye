import { test, expect, type Page } from '@playwright/test';
import { signInAsOperator } from './support/sign-in';
import { KIOSK_PATH, ONE_ORIGIN, WALL_PATH } from './support/origins';

// The file name must not contain "wall-" or "kiosk-": the config's patterns are unanchored, so either one sends a spec to a kiosk
// project and hides it from the console's.
test.use({ baseURL: ONE_ORIGIN });

interface Violation {
  violatedDirective: string;
  blockedURI: string;
}

// Records every policy violation the page raises, from the first script of each document onwards.
async function recordViolations(page: Page): Promise<void> {
  await page.addInitScript(() => {
    const seen: Array<{ violatedDirective: string; blockedURI: string }> = [];
    (window as unknown as { __violations: typeof seen }).__violations = seen;
    document.addEventListener('securitypolicyviolation', (event) => {
      seen.push({ violatedDirective: event.violatedDirective, blockedURI: event.blockedURI });
    });
  });
}

async function violations(page: Page): Promise<Violation[]> {
  return page.evaluate(() => (window as unknown as { __violations?: Violation[] }).__violations ?? []);
}

async function documentPolicy(page: Page, path: string): Promise<string | undefined> {
  const response = await page.goto(path);
  return response?.headers()['content-security-policy'];
}

for (const [name, path] of [
  ['the console', '/'],
  ['the kiosk', KIOSK_PATH],
  ['the wall display', WALL_PATH],
] as const) {
  test(`${name} is served with a content security policy and raises no violation`, async ({ page }) => {
    await recordViolations(page);

    const policy = await documentPolicy(page, path);
    await page.waitForLoadState('load');

    expect(policy, `the document at ${path} should carry a Content-Security-Policy header`).toBeTruthy();
    expect(await violations(page)).toEqual([]);
  });
}

test('the signed-in console raises no violation on its cameras page', async ({ page }) => {
  test.setTimeout(120_000);
  await recordViolations(page);

  await signInAsOperator(page);
  await page.goto('/cameras');
  await expect(page.getByRole('heading', { name: 'Cameras', exact: true })).toBeVisible();

  expect(await violations(page)).toEqual([]);
});
