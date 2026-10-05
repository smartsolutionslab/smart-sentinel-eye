import { test, expect } from '@playwright/test';
import { signInAsOperator } from './support/sign-in';
import { signInToKiosk } from './support/kiosk-session';
import { clickSidebarLink, openSection } from './support/management-navigation';
import { registerCameraViaList } from './support/management-cameras';
import { FIRST_WRITE_TIMEOUT_MS, FIRST_WRITE_TEST_TIMEOUT_MS } from './support/cold-stack';

/**
 * Spec 300 (issue #2349) T022, ADR-0165 — the product's new primitives,
 * asserted for the first time end to end: a revision can mix Box, Text and
 * Ellipse elements, a tile renders every kind correctly (shapes bordered and
 * unfilled, labels painted with the correct computed ink), and the authored
 * colour — including its alpha — reaches the wall unchanged.
 *
 * <para>
 * Modelled on `kiosk-shows-two-labels-over-video.spec.ts` (spec 150 T023):
 * created through the gateway, not the dialog — `OverlayEditorDialog.tsx`
 * only ever edits the one Text element it finds by kind (FR-019), so a
 * mixed-kind overlay with more than one Text element, or any shape at all,
 * cannot currently be built by hand through either app.
 * </para>
 *
 * <para>
 * The set, in ordinal order: Box `#D32F2F` (opaque), Text `#D32F2F` (opaque
 * — legible, white/light ink over a dark surface), Text `#FFEB3B` (opaque —
 * legible, black/dark ink over a light surface), Text `#FFFFFF80`
 * (translucent white at its legibility floor's generous side — black/dark
 * ink), Ellipse `#FFA00080` (translucent orange stroke). Two shapes, three
 * labels — `camera-viewer-overlay-shape` and `camera-viewer-overlay-label`
 * each counted on their own testid (CameraViewer.tsx keeps them distinct on
 * purpose, plan.md §"CameraViewer.tsx" — a box must not change the label
 * count on fixtures that have no boxes, and vice versa).
 * </para>
 */
test('a tile renders a mixed Box/Text/Ellipse element set, each kind correctly, in ordinal order', async ({
  page,
  browser,
}) => {
  test.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);

  const stamp = Date.now();
  const overlayName = `E2E Shapes ${stamp}`;
  const cameraName = `E2E Shapes Cam ${stamp}`;
  // The `E2E Layout ` prefix (not merely `E2E `) is what
  // `archive-e2e-layouts.teardown.ts`'s DISPOSABLE pattern matches.
  const layoutName = `E2E Layout Shapes ${stamp}`;

  const operatorContext = await browser.newContext({ baseURL: 'http://localhost:5173' });
  const operatorPage = await operatorContext.newPage();

  try {
    await signInAsOperator(operatorPage);

    const overlaysRequest = operatorPage.waitForRequest((request) => /\/overlay-designer\//.test(request.url()));
    await openSection(operatorPage, 'Overlays');
    const origin = new URL((await overlaysRequest).url()).origin;

    // management-web sets no `userStore` (unlike kiosk-web, which opts into
    // `window.localStorage`), so oidc-client-ts falls back to sessionStorage
    // — the same read `overlays.spec.ts` and the two-labels spec use for
    // this exact reason.
    const token = await operatorPage.evaluate(() => {
      const key = Object.keys(window.sessionStorage).find((candidate) => candidate.startsWith('oidc.user:'));
      const user = JSON.parse(window.sessionStorage.getItem(key ?? '') ?? '{}') as Record<string, string>;
      return user['access_token'] ?? '';
    });
    expect(token, 'the operator should be holding an access token').not.toBe('');

    // 1. The mixed-kind overlay, created directly against the gateway — the
    //    only way to produce one (see the file doc comment).
    const overlayIdentifier = await operatorPage.evaluate(
      async ([gatewayOrigin, accessToken, name]) => {
        const response = await fetch(`${gatewayOrigin}/overlay-designer/overlays`, {
          method: 'POST',
          headers: { Authorization: `Bearer ${accessToken}`, 'Content-Type': 'application/json' },
          body: JSON.stringify({
            name,
            elements: [
              {
                kind: 'Box',
                color: '#D32F2F',
                normalizedX: 0.02,
                normalizedY: 0.02,
                normalizedWidth: 0.2,
                normalizedHeight: 0.2,
              },
              {
                kind: 'Text',
                color: '#D32F2F',
                text: 'RED LABEL',
                normalizedX: 0.3,
                normalizedY: 0.02,
                normalizedWidth: 0.3,
                normalizedHeight: 0.12,
                fontSizePx: 16,
              },
              {
                kind: 'Text',
                color: '#FFEB3B',
                text: 'YELLOW LABEL',
                normalizedX: 0.3,
                normalizedY: 0.2,
                normalizedWidth: 0.3,
                normalizedHeight: 0.12,
                fontSizePx: 16,
              },
              {
                kind: 'Text',
                color: '#FFFFFF80',
                text: 'TRANSLUCENT LABEL',
                normalizedX: 0.3,
                normalizedY: 0.38,
                normalizedWidth: 0.3,
                normalizedHeight: 0.12,
                fontSizePx: 16,
              },
              {
                kind: 'Ellipse',
                color: '#FFA00080',
                normalizedX: 0.7,
                normalizedY: 0.02,
                normalizedWidth: 0.2,
                normalizedHeight: 0.2,
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
      [origin, token, overlayName] as const,
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

    expect(draft.revisions[0]?.elements.length, 'the created revision should carry all five elements').toBe(5);
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

    // 2. A camera, because a tile requires one. The `E2E ` prefix is what
    //    `retire-e2e-cameras.teardown.ts` matches on.
    await clickSidebarLink(operatorPage, 'Cameras');
    await registerCameraViaList(
      operatorPage,
      { name: cameraName, url: `rtsp://10.0.5.${Math.floor(Math.random() * 200) + 2}/stream` },
      { timeout: FIRST_WRITE_TIMEOUT_MS },
    );

    // 3. The wall: one tile, that camera, that mixed-kind overlay.
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

  // ---- the kiosk side: two shapes and three labels, each kind correct ----

  await signInToKiosk(page);
  await page.getByRole('listitem').filter({ hasText: layoutName }).getByRole('button').click();
  await expect(page.getByTestId('layout-grid')).toBeVisible();

  const shapes = page.getByTestId('camera-viewer-overlay-shape');
  const labels = page.getByTestId('camera-viewer-overlay-label');

  // Spec 150 FR-012 / spec 300 ADR-0165: the two testids are counted
  // independently, on purpose — a Box/Ellipse must not be counted as (or
  // alongside) a label.
  await expect(shapes, 'exactly the two shape elements (Box, Ellipse)').toHaveCount(2, { timeout: 30_000 });
  await expect(labels, 'exactly the three Text elements').toHaveCount(3, { timeout: 30_000 });

  // Ordinal order: shapes[0] is the Box (ordinal 0), shapes[1] the Ellipse
  // (ordinal 4) — `.nth()` is DOM order, which mirrors `CameraViewer.tsx`'s
  // `.map()` over the published, ordinal-ordered element set.
  const box = shapes.nth(0);
  const ellipse = shapes.nth(1);
  await expect(box).toHaveAttribute('data-kind', 'Box');
  await expect(ellipse).toHaveAttribute('data-kind', 'Ellipse');
  await expect(box).toHaveAttribute('aria-hidden', 'true');
  await expect(ellipse).toHaveAttribute('aria-hidden', 'true');

  // The authored stroke colour, including the Ellipse's alpha — read via
  // `toHaveCSS`, the browser's own computed-style matcher. `element.style.X`
  // is the live CSSOM `CSSStyleDeclaration`; reading it back re-serializes
  // the colour into `rgb()`/`rgba()` form rather than echoing the authored
  // eight-digit hex, confirmed live with a headless-Chromium probe of this
  // exact component style (`border-top-color`, the side the shorthand
  // normalizes into): `#D32F2F` → `rgb(211, 47, 47)`,
  // `#FFA00080` → `rgba(255, 160, 0, 0.5)`.
  await expect(box).toHaveCSS('border-top-color', 'rgb(211, 47, 47)');
  await expect(ellipse).toHaveCSS('border-top-color', 'rgba(255, 160, 0, 0.5)');
  await expect(ellipse).toHaveCSS('border-radius', '50%');

  // The three labels, in ordinal order (1, 2, 3 among the five elements —
  // the Box at ordinal 0 and the Ellipse at ordinal 4 are not labels, so
  // DOM order among `camera-viewer-overlay-label` nodes is exactly this).
  const redLabel = labels.nth(0);
  const yellowLabel = labels.nth(1);
  const translucentLabel = labels.nth(2);

  await expect(redLabel).toHaveText('RED LABEL');
  await expect(yellowLabel).toHaveText('YELLOW LABEL');
  await expect(translucentLabel).toHaveText('TRANSLUCENT LABEL');

  // The authored surface colour, read via `toHaveCSS` the same way as the
  // shapes' stroke above — `element.style.background`'s CSSOM read
  // re-serializes the colour into `rgb()`/`rgba()` form rather than echoing
  // the authored eight-digit hex, confirmed live: `#D32F2F` →
  // `rgb(211, 47, 47)`, `#FFEB3B` → `rgb(255, 235, 59)`, and the translucent
  // `#FFFFFF80` → `rgba(255, 255, 255, 0.5)`.
  await expect(redLabel).toHaveCSS('background-color', 'rgb(211, 47, 47)');
  await expect(yellowLabel).toHaveCSS('background-color', 'rgb(255, 235, 59)');
  await expect(translucentLabel).toHaveCSS('background-color', 'rgba(255, 255, 255, 0.5)');

  // The computed ink (ADR-0165 §2, textLegibility.ts `inkFor`): white ink on
  // red, black ink on yellow, black ink on the translucent white — read
  // from the browser-RESOLVED computed style, since the authored value here
  // is the CSS custom property reference (`var(--color-overlay-ink-...)`),
  // not a literal the inline-style read above would show usefully. Chromium
  // serialises these OKLCH primitives back out in `oklch()` notation rather
  // than `rgb()` (confirmed live for `--white` via `--color-bg-video-inverse`
  // in `overlays.spec.ts`'s `WHITE_FIELD_BACKGROUND_COLOR`), so the dark/light
  // inks are pinned the same way here.
  const LIGHT_INK_COLOR = 'oklch(1 0 0)';
  const DARK_INK_COLOR = 'oklch(0 0 0)';
  await expect(redLabel).toHaveCSS('color', LIGHT_INK_COLOR);
  await expect(yellowLabel).toHaveCSS('color', DARK_INK_COLOR);
  await expect(translucentLabel).toHaveCSS('color', DARK_INK_COLOR);
});
