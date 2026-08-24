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

  await page.getByRole('link', { name: /^layouts$/i }).click();

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
test('operator authors a 2×2 wall referencing a camera and overlay and it appears in the list', async ({ page }) => {
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
  await page.getByRole('link', { name: /^overlays$/i }).click();
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
  await page.getByRole('link', { name: /^layouts$/i }).click();
  await expect(page.getByRole('heading', { name: 'Layouts', exact: true })).toBeVisible();

  await page.getByRole('button', { name: /new layout/i }).click();
  await page.locator('#layout-name').fill(layoutName);
  // Author a 2×2 wall (spec 010): pick the grid size, assign the camera to
  // every tile (a camera may be reused across tiles, ADR-0112 §2), and bind
  // the overlay to tile 0. Options render the camera/overlay name as text.
  await page.getByRole('radio', { name: '2×2' }).click();
  for (const tile of [0, 1, 2, 3]) {
    await page.locator(`#tile-${tile}-camera`).selectOption({ label: cameraName });
  }
  await page.locator('#tile-0-overlay').selectOption({ label: overlayName });
  await page.getByRole('button', { name: /save as draft/i }).click();

  // The dialog closes and the new layout chain renders as an <h2>.
  await expect(page.getByRole('heading', { name: layoutName })).toBeVisible();
});

// Spec 012 T054 — the lost update, end to end, against the real stack.
//
// Two browser contexts share the same `operator` user: concurrency is
// per-aggregate, not per-user, so a second Keycloak identity would prove
// nothing extra. Both load the Layouts list and therefore both hold the same
// revision version. The first publish moves it; the second is built on a view
// that has since gone, and must be refused rather than silently overwriting.
//
// Before spec 012 both writes committed and the first operator's publish was
// lost with no indication to either of them. That is the regression this
// guards: an assertion on the *absence* of a conflict here would have passed
// on the broken build, so the test asserts the conflict is surfaced.
test('a second operator publishing the same revision is refused, not silently applied', async ({
  browser,
}) => {
  const stamp = Date.now();
  const cameraName = `E2E Race Cam ${stamp}`;
  const layoutName = `E2E Race Layout ${stamp}`;

  const first = await browser.newContext();
  const second = await browser.newContext();

  try {
    const pageOne = await first.newPage();
    await signInAsOperator(pageOne);

    // Seed a camera and a draft layout from the first context.
    await pageOne.getByRole('button', { name: /register camera/i }).click();
    await pageOne.locator('#register-camera-name').fill(cameraName);
    await pageOne.locator('#register-camera-url').fill('rtsp://10.0.5.60/stream');
    await pageOne.getByRole('button', { name: /^register$/i }).click();
    await expect(pageOne.getByRole('cell', { name: cameraName })).toBeVisible();

    await pageOne.getByRole('link', { name: /^layouts$/i }).click();
    await pageOne.getByRole('button', { name: /new layout/i }).click();
    await pageOne.locator('#layout-name').fill(layoutName);
    await pageOne.locator('#tile-0-camera').selectOption({ label: cameraName });
    await pageOne.getByRole('button', { name: /save as draft/i }).click();
    await expect(pageOne.getByRole('heading', { name: layoutName })).toBeVisible();

    // The second context loads the list *now*, so it holds the same version the
    // first one does. This ordering is the whole test — loading it after the
    // publish below would hand it the current version and prove nothing.
    const pageTwo = await second.newPage();
    await signInAsOperator(pageTwo);
    await pageTwo.getByRole('link', { name: /^layouts$/i }).click();
    const rowTwo = pageTwo.getByRole('listitem').filter({ hasText: layoutName });
    await expect(rowTwo.getByRole('button', { name: /^publish$/i })).toBeVisible();

    // First writer wins.
    const rowOne = pageOne.getByRole('listitem').filter({ hasText: layoutName });
    await rowOne.getByRole('button', { name: /^publish$/i }).click();
    await expect(rowOne.getByText(/Published/)).toBeVisible();

    // Second writer is acting on the version it read before that publish.
    await rowTwo.getByRole('button', { name: /^publish$/i }).click();

    // The conflict is surfaced, and the advice is to reload rather than retry —
    // retrying is what replays the stale intent over the first writer.
    const alert = pageTwo.getByRole('alert');
    await expect(alert).toBeVisible();
    await expect(alert).toContainText(/changed|reload|re-read/i);
    await expect(alert).not.toContainText(/try again/i);
  } finally {
    await first.close();
    await second.close();
  }
});
