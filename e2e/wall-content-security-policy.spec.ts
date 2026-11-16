import { test, expect } from '@playwright/test';
import { signInAsWallDisplay } from './support/wall-session';
import { ONE_ORIGIN, WALL_PATH } from './support/origins';
import { PolicyViolationRecorder } from './support/policy-violations';
import { readLiveVideoWall } from './support/live-video-wall';
import { everyTileDecoded } from './support/decoded-tiles';

test.use({ baseURL: ONE_ORIGIN });

const FIRST_FRAME_TIMEOUT_MS = process.env['CI'] !== undefined ? 90_000 : 60_000;

test('the wall display is served with a content security policy and raises no violation, signed in', async ({
  page,
}) => {
  test.setTimeout(180_000);
  const wall = readLiveVideoWall();
  const recorder = await PolicyViolationRecorder.attach(page);

  const policy = await recorder.goto(WALL_PATH);
  expect(policy, 'the wall display document should carry a Content-Security-Policy header').toBeTruthy();
  recorder.expectNone('on the signed-out wall display');

  await signInAsWallDisplay(page, { expectPopulatedPicker: true });
  recorder.expectNone('through sign-in');

  await page.getByRole('listitem').filter({ hasText: wall.layoutName }).getByRole('button').click();
  await expect(page.getByTestId('layout-grid')).toBeVisible();

  // Decoded frames prove the media (WHEP) connection was made; the label proves the hub delivered.
  await expect
    .poll(() => everyTileDecoded(page, wall.cameras.length), {
      timeout: FIRST_FRAME_TIMEOUT_MS,
      message: 'not every wall tile decoded a video frame under the policy',
    })
    .toBe(true);
  // Any resolved value: another spec in the shard may have left the shared variable at a value of its own.
  await expect(page.getByTestId('camera-viewer-overlay-label').first()).toHaveText(/\S/, { timeout: 30_000 });
  recorder.expectNone('while the wall decoded video and received its hub state');
});
