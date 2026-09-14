import { test, expect } from '@playwright/test';
import { signInAsOperator } from './support/sign-in';
import { FIRST_WRITE_TEST_TIMEOUT_MS, FIRST_WRITE_TIMEOUT_MS } from './support/cold-stack';

// ADR-0108 — overlays "read" vertical slice. An operator signs in, opens the
// Overlays surface, and the list loads from the overlay-designer service
// *through the API gateway* (ADR-0106). A 401 / 404 / CORS / scope failure
// surfaces the "Could not load overlays" alert, so asserting the heading renders
// with no alert proves the authenticated path end to end.
test('operator opens overlays and the list loads through the gateway', async ({ page }) => {
  await signInAsOperator(page);

  await page.getByRole('link', { name: /^overlays$/i }).click();

  await expect(page.getByRole('heading', { name: 'Overlays', exact: true })).toBeVisible();
  await expect(page.getByRole('alert')).toHaveCount(0);
});

test('operator creates an overlay draft and it appears in the list', async ({ page }) => {
  test.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);

  await signInAsOperator(page);

  await page.getByRole('link', { name: /^overlays$/i }).click();
  await expect(page.getByRole('heading', { name: 'Overlays', exact: true })).toBeVisible();

  // POST /overlay-designer/overlays (Bearer; sse.management grandfathers
  // sse.overlays.write); the label uses the editor's default.
  await page.getByRole('button', { name: /new overlay/i }).click();
  const name = `E2E Overlay ${Date.now()}`;
  await page.locator('#overlay-name').fill(name);
  await page.getByRole('button', { name: /save as draft/i }).click();

  await expect(page.getByText(name)).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });
});

// Spec 151 (issue #2346) T002 — the one thing jsdom cannot prove
// (spec.md §Precision, §Independent end-to-end test procedure step 11): a
// typed percentage survives real form submission and the double -> decimal
// boundary to the server exactly — the client-side /100-vs-quantize
// discrimination is `OverlayGeometryFields.test.tsx`'s job (24.87 does not
// discriminate the two; it is used here only as an ordinary value). There is
// no edit dialog (`OverlayEditorDialog` is create-only), so the read-back is
// a gateway call, not a re-open.
test('operator types an exact geometry and the saved overlay carries it through the gateway', async ({ page }) => {
  test.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);

  // Origin taken from a real gateway request, never hardcoded — the gateway
  // is reached through Aspire's proxied endpoint, so a fixed port is a
  // different origin as far as CORS and the token's audience are concerned.
  const gatewayRequest = page.waitForRequest((request) => /\/overlay-designer\//.test(request.url()));

  await signInAsOperator(page);

  await page.getByRole('link', { name: /^overlays$/i }).click();
  await expect(page.getByRole('heading', { name: 'Overlays', exact: true })).toBeVisible();

  const origin = new URL((await gatewayRequest).url()).origin;
  const token = await page.evaluate(() => {
    const key = Object.keys(window.localStorage).find((candidate) => candidate.startsWith('oidc.user:'));
    const user = JSON.parse(window.localStorage.getItem(key ?? '') ?? '{}') as Record<string, string>;
    return user['access_token'] ?? '';
  });
  expect(token, 'the operator should be holding an access token').not.toBe('');

  await page.getByRole('button', { name: /new overlay/i }).click();

  // FR-001/FR-003 — a percent field, type="text", reached by its visible
  // label, not by dragging. Tab commits the draft (FR-004's blur path).
  await page.getByLabel('Left', { exact: true }).fill('24.87');
  await page.keyboard.press('Tab');
  await page.getByLabel('Width', { exact: true }).fill('50');
  await page.keyboard.press('Tab');

  // FR-005/FR-006 — an exact, in-range value is accepted outright; nothing
  // on the panel refuses it.
  await expect(page.getByRole('alert')).toHaveCount(0);

  // The teardown's DISPOSABLE pattern (archive-e2e-overlays.teardown.ts) matches
  // "E2E " (space), not "E2E-" — every other disposable in this suite uses the
  // space form, and a hyphen here would leave one orphan draft per CI run.
  const name = `E2E Geometry ${Date.now()}`;
  await page.locator('#overlay-name').fill(name);
  await page.getByRole('button', { name: /save as draft/i }).click();

  await expect(page.getByText(name)).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

  const overlays = await page.evaluate(
    async ([gatewayOrigin, accessToken]) => {
      const response = await fetch(`${gatewayOrigin}/overlay-designer/overlays`, {
        headers: { Authorization: `Bearer ${accessToken}` },
      });
      return (await response.json()) as {
        chains: { name: string; revisions: { normalizedX: number; normalizedWidth: number }[] }[];
      };
    },
    [origin, token] as const,
  );

  const saved = overlays.chains.find((chain) => chain.name === name);
  expect(saved, `the saved overlay "${name}" should be readable back through the gateway`).toBeTruthy();
  const revision = saved!.revisions[0]!;
  // Exactly 0.2487 through the double -> decimal boundary, not merely close.
  expect(revision.normalizedX).toBe(0.2487);
  expect(revision.normalizedWidth).toBe(0.5);
});
