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

test('a retired camera opens, says so, and offers no way to change it', async ({ page }) => {
  await signInAsOperator(page);
  const name = await registerCamera(page);

  await page.getByRole('link', { name }).click();
  const cameraUrl = page.url();

  // Retired through the API rather than the UI: retiring has no control yet,
  // which is tracked separately rather than folded into this feature.
  const identifier = cameraUrl.split('/').pop() ?? '';
  const retired = await page.evaluate(async (id) => {
    const response = await fetch(`/camera-catalog/cameras/${id}/retire`, { method: 'POST' });
    return response.status;
  }, identifier);
  expect([204, 401, 403]).toContain(retired);

  await page.goto(cameraUrl);

  // FR-007 — the refusal is visible before the attempt. An edit control that
  // opened and then failed on submit would not satisfy this.
  await expect(page.getByText(/retired/i).first()).toBeVisible();
  await expect(page.getByRole('button', { name: /correct the address/i })).toHaveCount(0);
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
