import { test, expect, type Page } from '@playwright/test';
import { signInToKiosk } from './support/kiosk-session';
import { signInAsOperator } from './support/sign-in';
import { clickSidebarLink } from './support/management-navigation';
import { readLiveVideoWall } from './support/live-video-wall';
import { FIRST_WRITE_TIMEOUT_MS } from './support/cold-stack';
import { KIOSK_PATH, ONE_ORIGIN } from './support/origins';
import { PolicyViolationRecorder } from './support/policy-violations';

test.use({ baseURL: ONE_ORIGIN });

const FIRST_FRAME_TIMEOUT_MS = process.env['CI'] !== undefined ? 90_000 : 60_000;

async function everyTileDecoded(page: Page, tiles: number): Promise<boolean> {
  return page.evaluate((expected) => {
    const videos = Array.from(document.querySelectorAll('video'));
    return videos.length === expected && videos.every((video) => video.getVideoPlaybackQuality().totalVideoFrames > 0);
  }, tiles);
}

test('the kiosk shows live video and takes a hub push under its content security policy with no violation', async ({
  page,
  browser,
}) => {
  test.setTimeout(240_000);

  const wall = readLiveVideoWall();
  const recorder = await PolicyViolationRecorder.attach(page);

  const policy = await recorder.goto(KIOSK_PATH);
  expect(policy, 'the kiosk document should carry a Content-Security-Policy header').toBeTruthy();
  recorder.expectNone('on the signed-out kiosk');

  await signInToKiosk(page);
  recorder.expectNone('through sign-in');

  await page.getByRole('listitem').filter({ hasText: wall.layoutName }).getByRole('button').click();
  await expect(page.getByTestId('layout-grid')).toBeVisible();

  await expect
    .poll(() => everyTileDecoded(page, wall.cameras.length), {
      timeout: FIRST_FRAME_TIMEOUT_MS,
      message: 'not every tile decoded a video frame under the policy',
    })
    .toBe(true);

  const label = page.getByTestId('camera-viewer-overlay-label').first();
  await expect(label).toContainText(wall.variableInitialValue, { timeout: 30_000 });

  // A push over the layout hub, from a context that is not recorded.
  const operatorContext = await browser.newContext({ baseURL: ONE_ORIGIN });
  const operator = await operatorContext.newPage();
  try {
    await signInAsOperator(operator);
    await clickSidebarLink(operator, 'System variables');
    const row = operator.getByRole('listitem').filter({ hasText: wall.variableName });

    await row.getByPlaceholder('New value').fill(wall.variableChangedValue);
    await row.getByRole('button', { name: /^set value$/i }).click();

    try {
      await expect(label, 'the hub push should reach the kiosk').toContainText(wall.variableChangedValue, {
        timeout: FIRST_WRITE_TIMEOUT_MS,
      });
    } finally {
      // The other kiosk specs expect the seeded value; put it back.
      await row.getByPlaceholder('New value').fill(wall.variableInitialValue);
      await row.getByRole('button', { name: /^set value$/i }).click();
      await expect(label).toContainText(wall.variableInitialValue, { timeout: FIRST_WRITE_TIMEOUT_MS });
    }
  } finally {
    await operatorContext.close();
  }

  recorder.expectNone('while the kiosk decoded video and took a hub push');
});
