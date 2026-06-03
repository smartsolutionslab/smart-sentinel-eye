import { test, expect } from '@playwright/test';
import { signInAsOperator } from './support/sign-in';

// ADR-0108 — layouts "read" vertical slice. An operator signs in, opens the
// Layouts surface, and the list loads from the layout-composition service
// *through the API gateway* (ADR-0106). A 401 / 404 / CORS / scope failure
// surfaces the "Could not load layouts" alert, so asserting the heading renders
// with no alert proves the authenticated path: OIDC -> token -> cross-origin
// gateway -> service -> DB.
test('operator opens layouts and the list loads through the gateway', async ({ page }) => {
  await signInAsOperator(page);

  await page.getByRole('button', { name: /^layouts$/i }).click();

  // The Layouts surface renders and the authenticated GET
  // /layout-composition/layouts succeeded: no error alert.
  await expect(page.getByRole('heading', { name: 'Layouts', exact: true })).toBeVisible();
  await expect(page.getByRole('alert')).toHaveCount(0);
});

// ADR-0108 — layouts "create" write slice against the live Aspire stack. CI runs
// on a fresh, empty DB, so the layout dialog's camera picker and overlay select
// are empty until we seed them. This test therefore registers a camera and
// publishes an overlay first, then creates a layout that references both. A
// passing run proves the authenticated POST /layout-composition/layouts path end
// to end (Bearer; sse.management grandfathers sse.layouts.write), plus the
// cross-context reads the dialog depends on.
test('operator creates a layout referencing a camera and overlay and it appears in the list', async ({ page }) => {
  await signInAsOperator(page);

  const stamp = Date.now();
  const cameraName = `E2E Cam ${stamp}`;
  const overlayName = `E2E Overlay ${stamp}`;
  const layoutName = `E2E Layout ${stamp}`;

  // (1) Register a camera so the layout dialog's camera picker is non-empty
  // (the submit button stays disabled while no camera exists).
  await page.getByRole('button', { name: /register camera/i }).click();
  await page.locator('#register-camera-name').fill(cameraName);
  await page.locator('#register-camera-url').fill('rtsp://10.0.5.50/stream');
  await page.getByRole('button', { name: /^register$/i }).click();
  await expect(page.getByRole('cell', { name: cameraName })).toBeVisible();

  // (2) Create an overlay draft, then publish it: the layout dialog only lists
  // *published* overlays (useListOverlaysQuery('Published')).
  await page.getByRole('button', { name: /^overlays$/i }).click();
  await expect(page.getByRole('heading', { name: 'Overlays', exact: true })).toBeVisible();
  await page.getByRole('button', { name: /new overlay/i }).click();
  await page.locator('#overlay-name').fill(overlayName);
  await page.getByRole('button', { name: /save as draft/i }).click();

  // Publish from the newly created overlay's card so it becomes selectable.
  const overlayCard = page.getByRole('listitem').filter({ hasText: overlayName });
  await expect(overlayCard.getByRole('heading', { name: overlayName })).toBeVisible();
  await overlayCard.getByRole('button', { name: /^publish$/i }).click();
  await expect(overlayCard.getByText(/Published/)).toBeVisible();

  // (3) Open Layouts and create a draft referencing the camera and overlay.
  await page.getByRole('button', { name: /^layouts$/i }).click();
  await expect(page.getByRole('heading', { name: 'Layouts', exact: true })).toBeVisible();

  await page.getByRole('button', { name: /new layout/i }).click();
  await page.locator('#layout-name').fill(layoutName);
  // Options render the camera/overlay name as their visible text.
  await page.locator('#layout-camera').selectOption({ label: cameraName });
  await page.locator('#layout-overlay').selectOption({ label: overlayName });
  await page.getByRole('button', { name: /save as draft/i }).click();

  // The dialog closes and the new layout chain renders as an <h2>.
  await expect(page.getByRole('heading', { name: layoutName })).toBeVisible();
});
