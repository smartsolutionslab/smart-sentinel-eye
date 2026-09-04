import { test as setup, expect } from '@playwright/test';
import { signInAsOperator } from './sign-in';
import { newLiveVideoWall, writeLiveVideoWall } from './live-video-wall';
import { FIRST_WRITE_TEST_TIMEOUT_MS, FIRST_WRITE_TIMEOUT_MS } from './cold-stack';

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
  setup.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);

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

  // The first write of the run, against a service that may still be cold; the
  // reasoning for the budget lives once, with the constant.
  await expect(page.getByRole('heading', { name: wall.variableName })).toBeVisible({
    timeout: FIRST_WRITE_TIMEOUT_MS,
  });

  // 2. An overlay whose text is a token, so the service holds a resolved
  //    snapshot for it — a static label has none, and the label could then be
  //    right without the binding working at all.
  await page.getByRole('link', { name: /^overlays$/i }).click();
  await expect(page.getByRole('heading', { name: 'Overlays', exact: true })).toBeVisible();

  await page.getByRole('button', { name: /new overlay/i }).click();
  await page.locator('#overlay-name').fill(wall.overlayName);
  await page.getByTestId('overlay-editor-text').fill(`{{${wall.variableName}}}`);
  await page.getByRole('button', { name: /save as draft/i }).click();
  await expect(page.getByText(wall.overlayName)).toBeVisible();

  const overlayRow = page.getByRole('listitem').filter({ hasText: wall.overlayName });
  await overlayRow.getByRole('button', { name: /^publish$/i }).click();
  await expect(overlayRow.getByText(/Published/)).toBeVisible();

  // 3. The camera — **at an address something actually serves**. The URL comes
  //    from the module that owns it, never composed here: a host and port
  //    written into a fixture is a second thing to keep true, and when it rots
  //    the wall looks like a broken product rather than a broken fixture.
  await page.getByRole('link', { name: /^cameras$/i }).click();
  await page.getByRole('button', { name: /register camera/i }).click();
  await page.locator('#register-camera-name').fill(wall.cameraName);
  await page.locator('#register-camera-url').fill(wall.cameraRtspUrl);
  await page.getByRole('button', { name: /^register$/i }).click();
  await expect(page.getByRole('cell', { name: wall.cameraName })).toBeVisible();

  // 4. The wall: one tile, that camera, that overlay. One tile is enough —
  //    the gap is that no check has both halves, not that none has four.
  await page.getByRole('link', { name: /^layouts$/i }).click();
  await expect(page.getByRole('heading', { name: 'Layouts', exact: true })).toBeVisible();

  await page.getByRole('button', { name: /new layout/i }).click();
  await page.locator('#layout-name').fill(wall.layoutName);
  await page.locator('#tile-0-camera').selectOption({ label: wall.cameraName });
  await page.locator('#tile-0-overlay').selectOption({ label: wall.overlayName });
  await page.getByRole('button', { name: /save as draft/i }).click();
  await expect(page.getByRole('heading', { name: wall.layoutName })).toBeVisible();

  const layoutRow = page.getByRole('listitem').filter({ hasText: wall.layoutName });
  await layoutRow.getByRole('button', { name: /^publish$/i }).click();
  await expect(layoutRow.getByText(/Published/)).toBeVisible();
});
