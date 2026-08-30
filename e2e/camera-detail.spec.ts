import { test, expect } from '@playwright/test';
import { signInAsOperator } from './support/sign-in';

// Spec 030 T033–T036 — opening one camera and correcting it, against the live
// Aspire stack. Spec 029 built GET /cameras/{camera} and PATCH /cameras/{camera}
// and nothing called them; these prove the whole path an operator actually
// takes: OIDC -> token -> gateway -> camera-catalog -> DB, and back into a
// rendered page.

/** Registers a camera and returns its name, which is also the link to open it. */
async function registerCamera(page: import('@playwright/test').Page): Promise<string> {
  const name = `E2E Detail ${Date.now()}`;

  await page.getByRole('button', { name: /register camera/i }).click();
  await page.locator('#register-camera-name').fill(name);
  await page.locator('#register-camera-url').fill('rtsp://10.0.5.98/stream');
  await page.getByRole('button', { name: /^register$/i }).click();

  await expect(page.getByRole('link', { name })).toBeVisible();

  return name;
}

test('operator opens one camera from the list, and its location can be reloaded', async ({ page }) => {
  await signInAsOperator(page);
  const name = await registerCamera(page);

  await page.getByRole('link', { name }).click();

  await expect(page.getByRole('heading', { name })).toBeVisible();
  await expect(page.getByText('rtsp://10.0.5.98/stream')).toBeVisible();

  // FR-002, and the whole reason the shell got a router: the camera has a real
  // location. A panel driven by useState survives none of this.
  const opened = page.url();
  expect(opened).toMatch(/\/cameras\/[0-9a-f-]{36}$/i);

  await page.reload();
  await expect(page.getByRole('heading', { name })).toBeVisible();

  // Back returns to the list rather than out of the application.
  await page.goBack();
  await expect(page.getByRole('heading', { name: 'Cameras', exact: true })).toBeVisible();
});

test('operator corrects a camera address and sees the stored value', async ({ page }) => {
  await signInAsOperator(page);
  const name = await registerCamera(page);

  await page.getByRole('link', { name }).click();
  await page.getByRole('button', { name: /correct the address/i }).click();

  const field = page.locator('#edit-camera-url');
  await field.fill('rtsp://10.0.5.77/corrected');
  await page.getByRole('button', { name: /^save$/i }).click();

  // The PATCH carried If-Match with the version the read handed over; what is
  // shown afterwards is what came back from the server, not what was typed.
  await expect(page.getByText('rtsp://10.0.5.77/corrected')).toBeVisible();
  await expect(page.getByRole('alert')).toHaveCount(0);
});

/**
 * Spec 032 T016 / SC-005 — the test that used to be a comment here.
 *
 * Spec 030 left this uncovered on purpose. Nothing in the app could retire a
 * camera (#1860), so the test would have had to reach around the UI and call
 * the API to arrange its own state; the first attempt did exactly that and
 * failed twice over — a relative fetch resolves against the app's origin, not
 * the gateway's, and it carried no bearer token. Both were fixable, and fixing
 * them would have produced a test exercising the API while claiming to exercise
 * the application, hiding this very gap behind green.
 *
 * Spec 032 made retiring something an operator can do, so the test does it the
 * way an operator would. **No `fetch` appears below.** If arranging state ever
 * seems to need one, that is the signal spec 030 acted on.
 *
 * Three things in one run, because they are one claim: the camera is retired,
 * it leaves the default listing, and its record survives at its own address.
 */
test('operator retires a camera, and it leaves the listing but keeps its record', async ({ page }) => {
  await signInAsOperator(page);
  const name = await registerCamera(page);

  await page.getByRole('link', { name }).click();
  const location = page.url();

  await page.getByRole('button', { name: /retire camera/i }).click();

  // FR-005/006/007 — the three consequences, read before confirming. An
  // operator who is not told these is being asked to approve something they
  // cannot see: the stream loss and the name reuse happen elsewhere.
  const confirmation = page.getByRole('alertdialog');
  await expect(confirmation).toContainText(name);
  await expect(confirmation).toContainText(/permanent/i);
  await expect(confirmation).toContainText(/live stream will stop/i);
  await expect(confirmation).toContainText(/available again/i);

  await confirmation.getByRole('button', { name: /retire camera/i }).click();

  // FR-009 — the page reflects it without a reload, and FR-004: the control is
  // gone rather than disabled.
  await expect(page.getByRole('status')).toContainText(/retired/i);
  await expect(page.getByRole('button', { name: /retire camera/i })).toHaveCount(0);
  await expect(page.getByRole('button', { name: /correct the address/i })).toHaveCount(0);

  // FR-012 — nothing claims this operator caused it. Retiring is idempotent and
  // answers 204 either way, so a page announcing "Camera retired" would be
  // telling a second tab something false.
  await expect(page.locator('main')).not.toContainText(/camera retired/i);
  await expect(page.locator('main')).not.toContainText(/successfully/i);

  // FR-010 — gone from the default listing.
  await page.getByRole('link', { name: /back to cameras/i }).click();
  await expect(page.getByRole('heading', { name: 'Cameras', exact: true })).toBeVisible();
  await expect(page.getByRole('link', { name })).toHaveCount(0);

  // FR-011 — and yet still there, at its own address. Retirement takes a camera
  // out of the listing, not out of existence: the audit trail refers to it.
  await page.goto(location);
  await expect(page.getByRole('heading', { name })).toBeVisible();
  await expect(page.getByRole('status')).toContainText(/retired/i);
});

/**
 * T036 / FR-008 — the property spec 029 could not test from the API alone,
 * because it lives in the last hop.
 *
 * The API answers a camera in another fab exactly as it answers one that never
 * existed. A UI can undo that with a single helpful sentence, so what is
 * compared is the rendered page for both causes — not merely that each showed
 * something.
 */
/**
 * Spec 035 T016 / SC-005 — the endpoint spec 033 built and nothing called.
 *
 * Registers and renames entirely through the app. **No `fetch` appears in this
 * file**, and that is deliberate: spec 030 *removed* a test that reached around
 * the UI to arrange its own state, because repairing it would have produced a
 * test exercising the API while claiming to exercise the application. Spec 032's
 * retire test above was written after that lesson, and this follows it.
 */
test('operator renames a camera and the new name follows it into the listing', async ({ page }) => {
  await signInAsOperator(page);
  const original = await registerCamera(page);

  await page.getByRole('link', { name: original }).click();
  await expect(page.getByRole('heading', { name: original })).toBeVisible();

  await page.getByRole('button', { name: /^rename$/i }).click();

  // Pre-filled with what the camera is called now — a correction is an edit,
  // not a retype (FR-003).
  const field = page.locator('#rename-camera-name');
  await expect(field).toHaveValue(original);

  const corrected = `${original} corrected`;
  await field.fill(corrected);
  await page.getByRole('button', { name: /^save$/i }).click();

  // FR-012: the page reflects it without a reload, and no refusal appears.
  await expect(page.getByRole('heading', { name: corrected })).toBeVisible();
  await expect(page.getByRole('alert')).toHaveCount(0);

  const location = page.url();

  // And the listing, whose row carried the old name until the same invalidation
  // refreshed it.
  await page.getByRole('link', { name: /back to cameras/i }).click();
  await expect(page.getByRole('link', { name: corrected })).toBeVisible();
  await expect(page.getByRole('link', { name: original, exact: true })).toHaveCount(0);

  // Same camera throughout — the identifier never moved, which is the whole
  // difference between correcting a name and registering a replacement.
  await page.goto(location);
  await expect(page.getByRole('heading', { name: corrected })).toBeVisible();
});

test('a camera the operator may not see reads exactly as one that does not exist', async ({ page }) => {
  await signInAsOperator(page);

  // Two identifiers this operator cannot resolve: one well-formed and unknown,
  // one likewise. Neither may be distinguishable from the other.
  await page.goto('/cameras/00000000-0000-4000-8000-000000000001');
  await expect(page.getByRole('heading', { name: /no such camera/i })).toBeVisible();
  const first = await page.locator('main').innerText();

  await page.goto('/cameras/00000000-0000-4000-8000-000000000002');
  await expect(page.getByRole('heading', { name: /no such camera/i })).toBeVisible();
  const second = await page.locator('main').innerText();

  expect(second).toBe(first);

  // The sentence that would reintroduce the enumeration, and the one most
  // likely to be added later as a kindness.
  expect(first).not.toMatch(/access|permission|not yours|another fab/i);
});

/**
 * Spec 043 T011 / FR-001 — the camera's page reaches the viewer, against the
 * live stack.
 *
 * **What this does NOT prove: that a picture appears.** `camera-sim` and
 * `scenario-simulator` sit inside `if (isRunMode && !isE2ETests)`, so a
 * Playwright run produces no video at all — the `<video>` element is mounted
 * whether or not a frame ever arrives, and `CameraViewer` will be sitting in
 * Connecting… or Stream is offline the whole time. A `<video>` is a viewer,
 * not a picture.
 *
 * It is still worth having, and for a specific reason: it is the only check
 * that fails if the page stops mounting the viewer *in the real app*. The unit
 * test stubs the composite, so it cannot tell a working import from a broken
 * one. Between them: the unit test proves the wiring, this proves the mount.
 * A person proves the picture (quickstart §5).
 */
test('an opened camera has a viewer, and a retired one explains why it does not', async ({ page }) => {
  await signInAsOperator(page);
  const name = await registerCamera(page);

  await page.getByRole('link', { name }).click();
  await expect(page.getByRole('heading', { name })).toBeVisible();

  await expect(page.locator('video')).toHaveCount(1);

  // FR-004. The absence is deliberate and explained — an unexplained one lets
  // an operator conclude the video is broken, which is the whole ambiguity.
  await page.getByRole('button', { name: /retire camera/i }).click();
  await page
    .getByRole('alertdialog')
    .getByRole('button', { name: /retire camera/i })
    .click();

  await expect(page.locator('video')).toHaveCount(0);
  await expect(page.getByRole('status')).toContainText(/stream/i);
});
