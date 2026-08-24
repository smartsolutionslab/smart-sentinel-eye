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

/*
 * FR-007 — a retired camera opens, is marked, and offers no edit control — is
 * deliberately **not** covered here.
 *
 * Nothing in the app can retire a camera (#1860), so an end-to-end test would
 * have to reach around the UI and call the API to arrange its own state. The
 * first attempt did exactly that and failed for two reasons at once: a relative
 * fetch resolves against the app's origin rather than the gateway's, and it
 * carried no bearer token.
 *
 * Both were fixable. Fixing them would have produced a test that exercises the
 * API — which spec 028's integration tests already cover — while claiming to
 * exercise the application, and it would have hidden the actual gap behind
 * green.
 *
 * The rendering is covered by `CameraDetailPage.test.tsx`: a retired camera
 * opens, says it is retired, and offers no control, with a counterpart
 * asserting an active camera does offer one.
 *
 * This becomes writable honestly once #1860 lands and retiring is something an
 * operator can do — which is where the test belongs.
 */

/**
 * T036 / FR-008 — the property spec 029 could not test from the API alone,
 * because it lives in the last hop.
 *
 * The API answers a camera in another fab exactly as it answers one that never
 * existed. A UI can undo that with a single helpful sentence, so what is
 * compared is the rendered page for both causes — not merely that each showed
 * something.
 */
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
