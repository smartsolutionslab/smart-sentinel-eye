import { test, expect } from '@playwright/test';
import { signInAsOperator } from './support/sign-in';
import { signInToKiosk } from './support/kiosk-session';
import { clickSidebarLink, openSection } from './support/management-navigation';
import { registerCameraViaList } from './support/management-cameras';
import { FIRST_WRITE_TIMEOUT_MS, FIRST_WRITE_TEST_TIMEOUT_MS } from './support/cold-stack';

/**
 * Spec 150 (issue #2345) T023 — the product's whole point, asserted for the
 * first time end to end: a revision carries more than one label, a tile
 * renders every one of them in ordinal order, and a label other than the
 * first still resolves its text from a system variable.
 *
 * <para>
 * `kiosk-shows-a-label-over-video.spec.ts` seeds a nine-tile wall where every
 * tile carries exactly one label and proves that wall did not move — it
 * reads `.first()` deliberately and must keep passing unmodified. This file
 * is the second half spec 150 added: a tile whose overlay has two.
 * </para>
 *
 * <para>
 * <b>Created through the gateway, not the dialog.</b> `OverlayEditorDialog.tsx`
 * only ever binds `labels.0` (T021/FR-016 — the console gained no affordance
 * to add a second label this spec), so a two-label overlay cannot currently
 * be built by hand through either app. The only way to produce one is the
 * same POST the backend integration tests exercise (T013), issued here with
 * the operator's own bearer token exactly as the geometry-precision test in
 * `overlays.spec.ts` already reads results back through the gateway.
 * </para>
 */
test('a tile renders two overlay labels in ordinal order, the second resolved from a variable', async ({
  page,
  browser,
}) => {
  test.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);

  const stamp = Date.now();
  // Lowercase — the variable grammar rejects anything else (spec 005).
  const variableName = `twolabel${stamp}`;
  const variableValue = `LBL2VALUE${stamp}`;
  const overlayName = `E2E Two Labels ${stamp}`;
  const cameraName = `E2E Two Labels Cam ${stamp}`;
  // The `E2E Layout ` prefix (not merely `E2E `) is what
  // `archive-e2e-layouts.teardown.ts`'s DISPOSABLE pattern matches.
  const layoutName = `E2E Layout Two Labels ${stamp}`;
  const firstLabelText = 'FIRST LABEL';

  // A second context/baseURL rather than navigating `page` there and back:
  // `kiosk-shows-a-label-over-video.spec.ts`'s span test uses the identical
  // shape for the same reason — this file's own `page` stays on kiosk-web
  // (:5174, this project's baseURL) throughout.
  const operatorContext = await browser.newContext({ baseURL: 'http://localhost:5173' });
  const operatorPage = await operatorContext.newPage();

  try {
    await signInAsOperator(operatorPage);

    // 1. The variable the second label resolves from, defined before the
    //    overlay that references it — same ordering
    //    `seed-bound-overlay-wall.setup.ts` uses, so the reverse index has
    //    something to resolve against once the overlay publishes.
    const variableRequest = operatorPage.waitForRequest((request) => /\/system-variables\//.test(request.url()));

    await openSection(operatorPage, 'System variables');
    await operatorPage.getByRole('button', { name: /new variable/i }).click();
    await operatorPage.locator('#variable-name').fill(variableName);
    await operatorPage.locator('#variable-initial-value').fill(variableValue);
    await operatorPage.getByRole('button', { name: /^define$/i }).click();

    await expect(operatorPage.getByRole('heading', { name: variableName })).toBeVisible({
      timeout: FIRST_WRITE_TIMEOUT_MS,
    });

    // Origin taken from a real gateway request, never hardcoded — the
    // gateway is reached through Aspire's proxied endpoint
    // (`aspire-keycloak-issuer-must-match-proxied-port`-shaped gotcha), so a
    // fixed port is a different origin as far as CORS and the token's
    // audience are concerned.
    const origin = new URL((await variableRequest).url()).origin;

    // management-web sets no `userStore` (unlike kiosk-web, which opts into
    // `window.localStorage`), so oidc-client-ts falls back to sessionStorage
    // — the same read `overlays.spec.ts` uses for this exact reason.
    const token = await operatorPage.evaluate(() => {
      const key = Object.keys(window.sessionStorage).find((candidate) => candidate.startsWith('oidc.user:'));
      const user = JSON.parse(window.sessionStorage.getItem(key ?? '') ?? '{}') as Record<string, string>;
      return user['access_token'] ?? '';
    });
    expect(token, 'the operator should be holding an access token').not.toBe('');

    // 2. The two-label overlay itself, created directly against the
    //    gateway. Label 0 carries a literal; label 1 carries a placeholder
    //    naming the variable just defined — the whole point of this test is
    //    that placeholder resolution fires for a label that is NOT the
    //    first, which a one-label fixture could never exercise.
    const overlayIdentifier = await operatorPage.evaluate(
      async ([gatewayOrigin, accessToken, name, label0Text, label1Text]) => {
        const response = await fetch(`${gatewayOrigin}/overlay-designer/overlays`, {
          method: 'POST',
          headers: { Authorization: `Bearer ${accessToken}`, 'Content-Type': 'application/json' },
          body: JSON.stringify({
            name,
            elements: [
              {
                kind: 'Text',
                color: '#FFFFFFD9',
                text: label0Text,
                normalizedX: 0.05,
                normalizedY: 0.05,
                normalizedWidth: 0.3,
                normalizedHeight: 0.15,
                fontSizePx: 16,
              },
              {
                kind: 'Text',
                color: '#FFFFFFD9',
                text: label1Text,
                normalizedX: 0.05,
                normalizedY: 0.25,
                normalizedWidth: 0.3,
                normalizedHeight: 0.15,
                fontSizePx: 16,
              },
            ],
          }),
        });
        if (!response.ok) {
          throw new Error(`overlay create failed: ${response.status} ${await response.text()}`);
        }
        // `Results.Created(location, identifier)` serialises a bare `Guid`
        // as a JSON string — not an envelope object.
        return (await response.json()) as string;
      },
      [origin, token, overlayName, firstLabelText, `{{${variableName}}}`] as const,
    );

    const draft = await operatorPage.evaluate(
      async ([gatewayOrigin, accessToken, identifier]) => {
        const response = await fetch(`${gatewayOrigin}/overlay-designer/overlays/${identifier}`, {
          headers: { Authorization: `Bearer ${accessToken}` },
        });
        return (await response.json()) as {
          version: number;
          revisions: { revisionNumber: number; elements: unknown[] }[];
        };
      },
      [origin, token, overlayIdentifier] as const,
    );

    expect(draft.revisions[0]?.elements.length, 'the created revision should carry both labels').toBe(2);
    const revisionNumber = draft.revisions[0]!.revisionNumber;

    // A tile can only bind a PUBLISHED overlay (`LayoutEditorDialog.tsx`'s
    // `useListOverlaysQuery('Published')`), exactly as the UI-driven seeds
    // publish before the layout step.
    await operatorPage.evaluate(
      async ([gatewayOrigin, accessToken, identifier, revNumber, version]) => {
        const response = await fetch(
          `${gatewayOrigin}/overlay-designer/overlays/${identifier}/revisions/${revNumber}/publish`,
          {
            method: 'POST',
            headers: { Authorization: `Bearer ${accessToken}`, 'If-Match': `"${version}"` },
          },
        );
        if (!response.ok) {
          throw new Error(`overlay publish failed: ${response.status} ${await response.text()}`);
        }
      },
      [origin, token, overlayIdentifier, revisionNumber, draft.version] as const,
    );

    // 3. A camera, because a tile requires one. The `E2E ` prefix is what
    //    `retire-e2e-cameras.teardown.ts` matches on.
    await clickSidebarLink(operatorPage, 'Cameras');
    await registerCameraViaList(
      operatorPage,
      { name: cameraName, url: `rtsp://10.0.5.${Math.floor(Math.random() * 200) + 2}/stream` },
      { timeout: FIRST_WRITE_TIMEOUT_MS },
    );

    // 4. The wall: one tile, that camera, that two-label overlay.
    await openSection(operatorPage, 'Layouts');
    await operatorPage.getByRole('button', { name: /new layout/i }).click();
    await operatorPage.locator('#layout-name').fill(layoutName);
    await operatorPage.locator('#tile-0-camera').selectOption({ label: cameraName });
    await operatorPage.locator('#tile-0-overlay').selectOption({ label: overlayName });
    await operatorPage.getByRole('button', { name: /save as draft/i }).click();

    await expect(operatorPage.getByRole('heading', { name: layoutName })).toBeVisible({
      timeout: FIRST_WRITE_TIMEOUT_MS,
    });

    const layoutRow = operatorPage.getByRole('listitem').filter({ hasText: layoutName });
    await layoutRow.getByRole('button', { name: /^publish$/i }).click();
    await expect(layoutRow.getByText(/Published/)).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });
  } finally {
    await operatorContext.close();
  }

  // ---- the kiosk side: two labels, in order ------------------------------

  await signInToKiosk(page);
  await page.getByRole('listitem').filter({ hasText: layoutName }).getByRole('button').click();
  await expect(page.getByTestId('layout-grid')).toBeVisible();

  const labels = page.getByTestId('camera-viewer-overlay-label');

  // Exactly two — neither the single-label count the existing characterisation
  // pins nor a third from some other tile leaking in.
  await expect(labels, 'the tile should render exactly one label per ordinal').toHaveCount(2, { timeout: 30_000 });

  // Ordinal order, not merely "both texts appear somewhere": `.nth(0)` is the
  // first DOM child (CameraViewer.tsx's `.map()`, keyed by ordinal), so a
  // reversed render order would fail this exact pairing.
  await expect(labels.nth(0), 'the first label (ordinal 0) should carry its literal text').toHaveText(firstLabelText, {
    timeout: 30_000,
  });

  // The whole point of this test: placeholder resolution fires for the
  // SECOND label, not only the first.
  await expect(
    labels.nth(1),
    `the second label (ordinal 1) should carry the variable's resolved value "${variableValue}", not the raw placeholder`,
  ).toHaveText(variableValue, { timeout: 30_000 });
});
