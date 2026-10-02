import { test, expect, type Page } from '@playwright/test';
import { signInAsOperator } from './support/sign-in';
import { signInToKiosk } from './support/kiosk-session';
import { openSection } from './support/management-navigation';
import { createPublishedLayout } from './support/management-layouts';
import { createSwitchWallRule } from './support/management-rules';
import { FIRST_WRITE_TEST_TIMEOUT_MS, FIRST_WRITE_TIMEOUT_MS } from './support/cold-stack';

/**
 * Spec 296 (#2618) US2, T117. **RED today.** `RuleDialog.tsx` offers only
 * "Set a system variable" / "Highlight an overlay" (T124 has not landed), so
 * `createSwitchWallRule`'s very first `getByRole('option', { name: "Switch a
 * wall's scene" })` click has nothing to find — this is expected and is the
 * correct failure for a feature that does not exist yet (ADR-0139/0144).
 *
 * Authors a `SwitchWallScene` rule **through the management-web dialog**
 * (not the API directly — this is a UI test, spec.md US2's independent
 * test), publishes it, then ingests a matching event directly against
 * EventIngestion's manual endpoint (`POST /event-ingestion/events/manual`,
 * the same shape `verification.md` §2 recorded for PR-A's own walk) and
 * asserts the kiosk — already watching the wall — renders the target
 * layout's grid. No kiosk-web code is touched or needs to be: the kiosk has
 * followed `WallSceneChanged` since spec 258 (plan.md §4.2); this test is
 * what *observes* that for the rule path specifically.
 *
 * **This file's name is deliberately not `wall-*.spec.ts` or
 * `kiosk-*.spec.ts`.** `playwright.config.ts`'s `chromium` project excludes
 * `/(kiosk|wall)-.*\.spec\.ts/` — unanchored, so the exclusion is "`wall-` or
 * `kiosk-` anywhere in the name", not only as a prefix. `rule-switches-a-
 * wall.spec.ts` has `wall` as its last word, with no trailing `-` before
 * `.spec.ts`, so neither branch of that alternation matches it; it is picked
 * up by `chromium` (management-web, :5173) like an ordinary spec.
 */

/** Creates a wall named `wallName` with the given ordered scene (layout) names. Leaves the page on the wall's detail view. */
async function createWall(page: Page, wallName: string, sceneLayoutNames: string[]) {
  await openSection(page, 'Walls');
  await page.getByRole('button', { name: /new wall/i }).click();
  await page.locator('#wall-name').fill(wallName);
  for (const layoutName of sceneLayoutNames) {
    await page.getByRole('checkbox', { name: layoutName }).check();
  }
  await page.getByRole('button', { name: /^save$/i }).click();

  await expect(page.getByRole('heading', { name: wallName })).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });
}

// See the file header: this spec's own `page`/`context` must be management-web
// (:5173) no matter which Playwright project's testMatch happens to sweep the
// file in (mirrors wall-changes-its-scene.spec.ts's own `test.use`).
test.use({ baseURL: 'http://localhost:5173' });

test('authoring a SwitchWallScene rule through the dialog switches the wall, and the kiosk follows (US2-2)', async ({
  page,
  browser,
}) => {
  test.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);

  const stamp = Date.now();
  const layoutAName = `E2E Rule Scene A ${stamp}`;
  const layoutBName = `E2E Rule Scene B ${stamp}`;
  const wallName = `E2E Rule Wall ${stamp}`;
  const ruleName = `e2e-rule-switches-wall-${stamp}`;
  const cameraAName = `E2E Rule Cam A ${stamp}`;
  const cameraBName = `E2E Rule Cam B ${stamp}`;

  await signInAsOperator(page);
  await openSection(page, 'Cameras');
  const cameraAIdentifier = await createPublishedLayout(page, layoutAName, cameraAName);
  await openSection(page, 'Cameras');
  const cameraBIdentifier = await createPublishedLayout(page, layoutBName, cameraBName);
  await createWall(page, wallName, [layoutAName, layoutBName]);

  // The kiosk, watching the wall before any switch — the ordinary kiosk
  // (:5174), not the wall display (:5175), and explicit because
  // `browser.newContext()` does not inherit a project's `use.baseURL`.
  const kioskContext = await browser.newContext({ baseURL: 'http://localhost:5174' });
  const kiosk = await kioskContext.newPage();
  try {
    await signInToKiosk(kiosk);
    await kiosk.getByRole('listitem').filter({ hasText: wallName }).getByRole('button').click();
    await expect(kiosk.getByTestId('layout-grid')).toBeVisible();
    await expect(kiosk.getByTestId('layout-tile').first()).toHaveAttribute('data-camera-identifier', cameraAIdentifier);

    // Author the rule through the dialog, targeting layout B specifically.
    const gatewayRequest = page.waitForRequest((request) => /\/automation\//.test(request.url()));
    await openSection(page, 'Rules');
    const gatewayOrigin = new URL((await gatewayRequest).url()).origin;

    await page.getByRole('button', { name: /new rule/i }).click();
    await createSwitchWallRule(page, ruleName, wallName, layoutBName);
    await page.getByRole('button', { name: /^create draft$/i }).click();

    const row = page.getByRole('row').filter({ hasText: ruleName });
    await expect(row).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

    // Publish it — a Draft rule never evaluates (spec.md US2).
    await row.getByRole('button', { name: /^publish$/i }).click();
    await expect(row.getByText('Active')).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

    // Ingest a matching event directly against EventIngestion's manual
    // endpoint — this is the trigger, not the thing under test (that is the
    // dialog-authored rule above and the kiosk assertion below). Reads the
    // raw access token out of the console's own session storage, mirroring
    // `management-session.ts`'s `readManagementAccessToken` (which decodes
    // the claims, not the bearer string this fetch needs).
    const token = await page.evaluate(() => {
      const key = Object.keys(window.sessionStorage).find((candidate) => candidate.startsWith('oidc.user:'));
      const user = JSON.parse(window.sessionStorage.getItem(key ?? '') ?? '{}') as Record<string, string>;
      return user['access_token'] ?? '';
    });
    expect(token, 'the operator should be holding an access token').not.toBe('');

    const status = await page.evaluate(
      async ([origin, bearer]) => {
        const response = await fetch(`${origin}/event-ingestion/events/manual`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${bearer}` },
          body: JSON.stringify({
            deviceId: 'e2e-rule-switches-a-wall',
            kind: 'LineStop',
            occurredAt: new Date().toISOString(),
            payload: { line: 3 },
          }),
        });
        return response.status;
      },
      [gatewayOrigin, token] as const,
    );
    expect(status, 'POST /event-ingestion/events/manual should accept the matching event').toBe(201);

    // The kiosk follows: no kiosk-web code change needed (plan.md §4.2) —
    // it has subscribed to WallSceneChanged since spec 258. Generous budget:
    // the chain is ingest → Automation → LayoutComposition → hub → kiosk.
    await expect(kiosk.getByTestId('layout-tile').first()).toHaveAttribute(
      'data-camera-identifier',
      cameraBIdentifier,
      { timeout: FIRST_WRITE_TIMEOUT_MS },
    );
  } finally {
    await kioskContext.close();
  }
});
