import { test as setup, expect } from '@playwright/test';
import { signInAsOperator } from './sign-in';
import { newLiveVideoWall, writeLiveVideoWall } from './live-video-wall';
import { FIRST_WRITE_TIMEOUT_MS } from './cold-stack';

/**
 * Spec 056 — a wall whose tile has **both** halves: a camera whose video
 * actually arrives, and an overlay bound to a variable.
 *
 * <para>
 * <b>The one difference from the SC-004 seed is the camera's address, and it is
 * the whole point.</b> That seed registers `rtsp://10.0.5.71/stream`, which
 * nothing serves, so its tiles render `WHEP returned 404` and never create an
 * <c>RTCRtpReceiver</c>. Every overlay assertion in this repository has run
 * against that wall — so a tile that draws its label <i>only when the video
 * fails</i> passes the entire suite. This one points at the fixture's video
 * source, so the SFU has something to pull.
 * </para>
 *
 * <para>
 * Drives management-web rather than the API for the same reason the other seeds
 * do: publishing needs an `If-Match` round-trip and the UI path gets the
 * contract right for free.
 * </para>
 */
setup('a published wall exists whose tile has both video and a bound overlay', async ({ page }) => {
  // Sized here rather than taken from `FIRST_WRITE_TEST_TIMEOUT_MS`: six
  // budgeted sites do not fit the shared ceiling. Five of them arriving cold at
  // ~40 s each leaves nothing for the sixth to spend its 90 s and report *which*
  // locator never resolved — 5 × 40 s + a sign-in + 90 s ≈ 320 s. `cold-stack.ts`
  // carries the rule.
  setup.setTimeout(360_000);

  const wall = newLiveVideoWall();
  writeLiveVideoWall(wall);

  await signInAsOperator(page);

  // 1. The variable the label resolves from, and which the span measurement
  //    later changes.
  await page.getByRole('link', { name: /^system variables$/i }).click();
  await expect(page.getByRole('heading', { name: 'System variables', exact: true })).toBeVisible();

  await page.getByRole('button', { name: /new variable/i }).click();
  await page.locator('#variable-name').fill(wall.variableName);
  await page.locator('#variable-initial-value').fill(wall.variableInitialValue);
  await page.getByRole('button', { name: /^define$/i }).click();

  // **Every write in this file gets the budget, not just this one.** Six kinds
  // of write across four services follow, and the cold cost attaches to the
  // message *type* — so the camera register below is exactly as cold as this
  // define. This project is a dependency of `kiosk` and `wall`, so under
  // `--project=kiosk` these are the first writes of the entire run, and a seed
  // failure fails every dependent project. The reasoning for the number lives
  // once, with the constant.
  await expect(page.getByRole('heading', { name: wall.variableName })).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

  // 2. An overlay whose text is a token, so the service holds a resolved
  //    snapshot for it — a static label has none, and the label could then be
  //    right without the binding working at all.
  await page.getByRole('link', { name: /^overlays$/i }).click();
  await expect(page.getByRole('heading', { name: 'Overlays', exact: true })).toBeVisible();

  await page.getByRole('button', { name: /new overlay/i }).click();
  await page.locator('#overlay-name').fill(wall.overlayName);
  await page.getByTestId('overlay-editor-text').fill(`{{${wall.variableName}}}`);
  await page.getByRole('button', { name: /save as draft/i }).click();
  await expect(page.getByText(wall.overlayName)).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

  const overlayRow = page.getByRole('listitem').filter({ hasText: wall.overlayName });
  await overlayRow.getByRole('button', { name: /^publish$/i }).click();
  await expect(overlayRow.getByText(/Published/)).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

  // 3. The camera — **at an address something actually serves**. The URL comes
  //    from the module that owns it, never composed here: a host and port
  //    written into a fixture is a second thing to keep true, and when it rots
  //    the wall looks like a broken product rather than a broken fixture.
  await page.getByRole('link', { name: /^cameras$/i }).click();
  await page.getByRole('button', { name: /register camera/i }).click();
  await page.locator('#register-camera-name').fill(wall.cameraName);
  await page.locator('#register-camera-url').fill(wall.cameraRtspUrl);
  await page.getByRole('button', { name: /^register$/i }).click();
  await expect(page.getByRole('cell', { name: wall.cameraName })).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

  // 4. The wall: one tile, that camera, that overlay. One tile is enough —
  //    the gap is that no check has both halves, not that none has four.
  await page.getByRole('link', { name: /^layouts$/i }).click();
  await expect(page.getByRole('heading', { name: 'Layouts', exact: true })).toBeVisible();

  await page.getByRole('button', { name: /new layout/i }).click();
  await page.locator('#layout-name').fill(wall.layoutName);
  await page.locator('#tile-0-camera').selectOption({ label: wall.cameraName });
  await page.locator('#tile-0-overlay').selectOption({ label: wall.overlayName });
  await page.getByRole('button', { name: /save as draft/i }).click();
  await expect(page.getByRole('heading', { name: wall.layoutName })).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

  const layoutRow = page.getByRole('listitem').filter({ hasText: wall.layoutName });
  await layoutRow.getByRole('button', { name: /^publish$/i }).click();
  await expect(layoutRow.getByText(/Published/)).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });
});
