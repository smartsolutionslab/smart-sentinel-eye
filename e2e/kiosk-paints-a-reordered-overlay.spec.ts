import { test, expect, type Locator, type Page } from '@playwright/test';
import { signInAsOperator } from './support/sign-in';
import { signInToKiosk } from './support/kiosk-session';
import { clickSidebarLink, openSection } from './support/management-navigation';
import { registerCameraViaList } from './support/management-cameras';
import { FIRST_WRITE_TIMEOUT_MS } from './support/cold-stack';

/**
 * Spec 301 (issue #2348) US3, T004 — characterisation, not new behaviour.
 * Spec 150/300 already made the ordinal both the stored key and the paint
 * order, so a wholesale `PATCH` that only permutes an existing element set
 * (spec 150 FR-006, "a reorder is the existing wholesale PATCH with a
 * permuted body") should already republish and paint in the new order, end
 * to end. The backend half of that premise is
 * `OverlayRevisionLifecycleIntegrationTests
 * .Reordering_through_a_branch_PATCH_republishes_elements_in_the_new_order`;
 * this is the kiosk half. **This spec changes no production code.** A red
 * result here means US1/US3's premise about the backend is false — that is
 * a finding to report, not something to "fix" by editing this file.
 *
 * <para>
 * **Overlapping geometry is deliberate** (FR-002, spec 301 decision 1):
 * every element below shares the same normalized box, and the create must
 * still answer 201 — overlap is legal, not a validation error. This is also
 * why `OverlayEditorDialog.tsx` cannot build this fixture by hand (it edits
 * only the one Text element it finds by kind, per
 * `kiosk-shows-shapes-over-video.spec.ts`'s own doc comment), so the overlay
 * is created directly against the gateway, exactly as that spec and
 * `kiosk-shows-two-labels-over-video.spec.ts` do.
 * </para>
 *
 * <para>
 * **No z-index.** Spec 301 explicitly adds no stacking field — paint order
 * alone determines stacking (plan.md "US1: the hold carries the paired
 * set"). Every overlay node's computed `z-index` must read `auto`,
 * both before and after the reorder.
 * </para>
 */
test('a tile paints a reordered overlay in its new order, overlap and all', async ({ page, browser }) => {
  // More first-of-kind writes than kiosk-shows-shapes-over-video.spec.ts's
  // three (overlay create+publish, camera register, layout create+publish):
  // this test adds a branch, a PATCH-reorder and a second publish on top —
  // six distinct write types in total, each potentially paying the ~5 s cold
  // first-of-kind cost cold-stack.ts documents. 450 s covers six full
  // FIRST_WRITE_TIMEOUT_MS budgets plus the sign-in/navigation preamble.
  test.setTimeout(450_000);

  const stamp = Date.now();
  const overlayName = `E2E Reorder ${stamp}`;
  const cameraName = `E2E Reorder Cam ${stamp}`;
  // The `E2E Layout ` prefix (not merely `E2E `) is what
  // `archive-e2e-layouts.teardown.ts`'s DISPOSABLE pattern matches.
  const layoutName = `E2E Layout Reorder ${stamp}`;

  // Fully overlapping — the same normalized box for all three elements.
  const OVERLAP = { normalizedX: 0.3, normalizedY: 0.3, normalizedWidth: 0.3, normalizedHeight: 0.3 } as const;
  const boxElement = { kind: 'Box', color: '#1565C0FF', ...OVERLAP };
  const textElement = { kind: 'Text', color: '#FFFFFFD9', text: 'Zone A', ...OVERLAP, fontSizePx: 16 };
  const ellipseElement = { kind: 'Ellipse', color: '#2E7D32FF', ...OVERLAP };

  const operatorContext = await browser.newContext({ baseURL: 'http://localhost:5173' });
  const operatorPage = await operatorContext.newPage();

  try {
    await signInAsOperator(operatorPage);

    const overlaysRequest = operatorPage.waitForRequest((request) => /\/overlay-designer\//.test(request.url()));
    await openSection(operatorPage, 'Overlays');
    const origin = new URL((await overlaysRequest).url()).origin;

    // management-web sets no `userStore` (unlike kiosk-web, which opts into
    // `window.localStorage`), so oidc-client-ts falls back to sessionStorage
    // — the same read the shapes and two-labels specs use for this exact
    // reason.
    const token = await operatorPage.evaluate(() => {
      const key = Object.keys(window.sessionStorage).find((candidate) => candidate.startsWith('oidc.user:'));
      const user = JSON.parse(window.sessionStorage.getItem(key ?? '') ?? '{}') as Record<string, string>;
      return user['access_token'] ?? '';
    });
    expect(token, 'the operator should be holding an access token').not.toBe('');

    // 1. The overlapping, mixed-kind overlay — created directly against the
    //    gateway. A 201 here is FR-002's own point: overlap is accepted.
    const createResult = await postJson(operatorPage, `${origin}/overlay-designer/overlays`, token, {
      name: overlayName,
      elements: [boxElement, textElement, ellipseElement],
    });
    expect(createResult.status, `overlay create should accept overlapping geometry: ${createResult.body}`).toBe(201);
    const overlayIdentifier = JSON.parse(createResult.body) as string;

    let chain = await getOverlay(operatorPage, origin, token, overlayIdentifier);
    expect(chain.revisions[0]?.elements.length, 'the created revision should carry all three elements').toBe(3);
    const firstRevisionNumber = chain.revisions[0]!.revisionNumber;

    await publishRevision(operatorPage, origin, token, overlayIdentifier, firstRevisionNumber, chain.version);

    // 2. A camera, because a tile requires one. The `E2E ` prefix is what
    //    `retire-e2e-cameras.teardown.ts` matches on.
    await clickSidebarLink(operatorPage, 'Cameras');
    await registerCameraViaList(
      operatorPage,
      { name: cameraName, url: `rtsp://10.0.5.${Math.floor(Math.random() * 200) + 2}/stream` },
      { timeout: FIRST_WRITE_TIMEOUT_MS },
    );

    // 3. The wall: one tile, that camera, that overlapping overlay.
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

    // ---- the kiosk side: bind, assert the initial DOM order ----------------

    await signInToKiosk(page);
    await page.getByRole('listitem').filter({ hasText: layoutName }).getByRole('button').click();
    await expect(page.getByTestId('layout-grid')).toBeVisible();

    const tile = page.getByTestId('layout-tile').first();
    const overlayNodes = tile.locator(
      '[data-testid="camera-viewer-overlay-label"], [data-testid="camera-viewer-overlay-shape"]',
    );
    await expect(overlayNodes, 'all three elements should be painted, overlap notwithstanding').toHaveCount(3, {
      timeout: FIRST_WRITE_TIMEOUT_MS,
    });

    await expect(async () => {
      expect(await kindsInDomOrder(overlayNodes)).toEqual(['Box', 'Text', 'Ellipse']);
    }, 'the initial DOM order should be the published ordinal order').toPass({ timeout: FIRST_WRITE_TIMEOUT_MS });

    // ---- republish, reordered: [Ellipse, Text, Box] -------------------------

    chain = await getOverlay(operatorPage, origin, token, overlayIdentifier);
    const draftNumber = await branchDraft(operatorPage, origin, token, overlayIdentifier, chain.version);

    chain = await getOverlay(operatorPage, origin, token, overlayIdentifier);
    await patchElements(operatorPage, origin, token, overlayIdentifier, draftNumber, chain.version, [
      ellipseElement,
      textElement,
      boxElement,
    ]);

    chain = await getOverlay(operatorPage, origin, token, overlayIdentifier);
    await publishRevision(operatorPage, origin, token, overlayIdentifier, draftNumber, chain.version);

    // ---- the kiosk side, again: the new order, pushed live ------------------

    await expect(overlayNodes, 'still all three elements after the reorder').toHaveCount(3, {
      timeout: FIRST_WRITE_TIMEOUT_MS,
    });
    await expect(async () => {
      expect(await kindsInDomOrder(overlayNodes)).toEqual(['Ellipse', 'Text', 'Box']);
    }, 'the DOM order should follow the republished, reordered ordinal order').toPass({
      timeout: FIRST_WRITE_TIMEOUT_MS,
    });

    // ---- paint order alone determines stacking: no z-index anywhere --------

    const nodeCount = await overlayNodes.count();
    for (let index = 0; index < nodeCount; index++) {
      await expect(overlayNodes.nth(index), `overlay node ${index} should carry no explicit z-index`).toHaveCSS(
        'z-index',
        'auto',
      );
    }
  } finally {
    await operatorContext.close();
  }
});

interface OverlayElementBody {
  kind: string;
  color: string;
  text?: string;
  normalizedX: number;
  normalizedY: number;
  normalizedWidth: number;
  normalizedHeight: number;
  fontSizePx?: number;
}

interface OverlayChain {
  version: number;
  revisions: { revisionNumber: number; elements: unknown[] }[];
}

/** Fetches the overlay chain through the gateway, using the operator's bearer token. */
async function getOverlay(
  operatorPage: Page,
  origin: string,
  token: string,
  overlayIdentifier: string,
): Promise<OverlayChain> {
  return operatorPage.evaluate(
    async ([gatewayOrigin, accessToken, identifier]) => {
      const response = await fetch(`${gatewayOrigin}/overlay-designer/overlays/${identifier}`, {
        headers: { Authorization: `Bearer ${accessToken}` },
      });
      return (await response.json()) as OverlayChain;
    },
    [origin, token, overlayIdentifier] as const,
  );
}

async function postJson(
  operatorPage: Page,
  url: string,
  token: string,
  body: unknown,
): Promise<{ status: number; body: string }> {
  return operatorPage.evaluate(
    async ([requestUrl, accessToken, requestBody]) => {
      const response = await fetch(requestUrl, {
        method: 'POST',
        headers: { Authorization: `Bearer ${accessToken}`, 'Content-Type': 'application/json' },
        body: JSON.stringify(requestBody),
      });
      return { status: response.status, body: await response.text() };
    },
    [url, token, body] as const,
  );
}

/** `POST .../revisions/{revisionNumber}/publish`, conditional on the chain's current version. */
async function publishRevision(
  operatorPage: Page,
  origin: string,
  token: string,
  overlayIdentifier: string,
  revisionNumber: number,
  version: number,
): Promise<void> {
  await operatorPage.evaluate(
    async ([gatewayOrigin, accessToken, identifier, revNumber, chainVersion]) => {
      const response = await fetch(
        `${gatewayOrigin}/overlay-designer/overlays/${identifier}/revisions/${revNumber}/publish`,
        { method: 'POST', headers: { Authorization: `Bearer ${accessToken}`, 'If-Match': `"${chainVersion}"` } },
      );
      if (!response.ok) {
        throw new Error(`overlay publish failed: ${response.status} ${await response.text()}`);
      }
    },
    [origin, token, overlayIdentifier, revisionNumber, version] as const,
  );
}

/** `POST .../draft` (branch), conditional on the chain's current version. Returns the new draft's revision number. */
async function branchDraft(
  operatorPage: Page,
  origin: string,
  token: string,
  overlayIdentifier: string,
  version: number,
): Promise<number> {
  return operatorPage.evaluate(
    async ([gatewayOrigin, accessToken, identifier, chainVersion]) => {
      const response = await fetch(`${gatewayOrigin}/overlay-designer/overlays/${identifier}/draft`, {
        method: 'POST',
        headers: { Authorization: `Bearer ${accessToken}`, 'If-Match': `"${chainVersion}"` },
      });
      if (!response.ok) {
        throw new Error(`branch failed: ${response.status} ${await response.text()}`);
      }
      return (await response.json()) as number;
    },
    [origin, token, overlayIdentifier, version] as const,
  );
}

/** `PATCH .../revisions/{revisionNumber}`, replacing the whole element set — a reorder is this with a permuted body (spec 150 FR-006). */
async function patchElements(
  operatorPage: Page,
  origin: string,
  token: string,
  overlayIdentifier: string,
  revisionNumber: number,
  version: number,
  elements: OverlayElementBody[],
): Promise<void> {
  await operatorPage.evaluate(
    async ([gatewayOrigin, accessToken, identifier, revNumber, chainVersion, elementBodies]) => {
      const response = await fetch(`${gatewayOrigin}/overlay-designer/overlays/${identifier}/revisions/${revNumber}`, {
        method: 'PATCH',
        headers: {
          Authorization: `Bearer ${accessToken}`,
          'Content-Type': 'application/json',
          'If-Match': `"${chainVersion}"`,
        },
        body: JSON.stringify({ elements: elementBodies }),
      });
      if (!response.ok) {
        throw new Error(`reorder PATCH failed: ${response.status} ${await response.text()}`);
      }
    },
    [origin, token, overlayIdentifier, revisionNumber, version, elements] as const,
  );
}

/**
 * Both overlay node kinds (`camera-viewer-overlay-label`, `-shape`) are
 * direct siblings with no wrapper (`CameraViewer.tsx`'s own comment, spec
 * 150 FR-012), so a comma-selector locator already yields them in DOM
 * order — the same order `.map()` painted them in. A label carries no
 * `data-kind` (only shapes do), so its kind is inferred from its testid.
 */
async function kindsInDomOrder(nodes: Locator): Promise<string[]> {
  return nodes.evaluateAll((elements) =>
    elements.map((element) =>
      element.getAttribute('data-testid') === 'camera-viewer-overlay-label'
        ? 'Text'
        : (element.getAttribute('data-kind') ?? 'unknown'),
    ),
  );
}
