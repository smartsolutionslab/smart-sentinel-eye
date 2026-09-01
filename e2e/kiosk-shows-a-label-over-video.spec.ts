import { test, expect, type Page } from '@playwright/test';
import { signInToKiosk } from './support/kiosk-session';
import { readLiveVideoWall } from './support/live-video-wall';

/**
 * Spec 056 US1 — the product's central behaviour, asserted for the first time.
 *
 * <para>
 * <b>A label over live video is what the kiosk is for, and nothing in this
 * repository has ever checked one.</b> The overlay fixtures register a camera
 * at an address nothing serves, so their tiles render `WHEP returned 404` and
 * create no receiver; the scenario simulator has real video but nothing asserts
 * against it. A tile that draws its label <i>only when the video fails</i>
 * therefore passes the entire suite.
 * </para>
 *
 * <para>
 * <b>Both halves, on the same tile, in one test.</b> Split across two, the
 * suite could stay green while the product does not work — which is exactly the
 * state this file exists to end.
 * </para>
 */

/** How long to wait for the first decoded frame. Stated, not discovered. */
const FIRST_FRAME_TIMEOUT_MS = 30_000;

/** The gap between decode samples, and the frames the second must add. */
const SAMPLE_GAP_MS = 1_000;

/**
 * The clip runs at 25 fps, so a healthy second delivers about 25 frames. Ten
 * clears a slow runner comfortably while still rejecting a stall.
 */
const MINIMUM_FRAMES_PER_SAMPLE = 10;

interface DecodeReading {
  /** Frames the decoder has produced for this element, over the session. */
  totalVideoFrames: number;
  /** How many video elements were found — 0 means no picture at all. */
  elements: number;
}

/**
 * Reads decoded-frame counts off the tile's own `<video>` element.
 *
 * <para>
 * <c>getVideoPlaybackQuality()</c> counts frames the decoder actually produced.
 * Deliberately <b>not</b> <c>currentTime</c>, which can advance over a stalled
 * track and would report a frozen picture as healthy.
 * </para>
 *
 * <para>
 * Deliberately not a second reader of the WebRTC <c>inbound-rtp</c> statistics
 * either: the application already owns that reading, and duplicating it here
 * would be a second thing to keep true. This asks the element what it drew.
 * </para>
 *
 * <para>
 * Reports the element count so <i>no picture at all</i> stays distinguishable
 * from <i>a picture that is not advancing</i>. They need different fixes, and a
 * single number cannot tell them apart.
 * </para>
 */
async function readDecode(page: Page): Promise<DecodeReading> {
  return page.evaluate(() => {
    const videos = Array.from(document.querySelectorAll('video'));
    let totalVideoFrames = 0;

    for (const video of videos) {
      if (typeof video.getVideoPlaybackQuality !== 'function') continue;
      totalVideoFrames += video.getVideoPlaybackQuality().totalVideoFrames;
    }

    return { totalVideoFrames, elements: videos.length };
  });
}

test('a tile shows an overlay label over video that is actually decoding', async ({ page }) => {
  test.setTimeout(180_000);

  const wall = readLiveVideoWall();

  await signInToKiosk(page);

  // This wall specifically — the picker also lists the other seeds' layouts.
  await page.getByRole('listitem').filter({ hasText: wall.layoutName }).getByRole('button').click();
  await expect(page.getByTestId('layout-grid')).toBeVisible();

  // ---- half one: the picture, and it must be MOVING ----------------------

  await expect
    .poll(async () => (await readDecode(page)).totalVideoFrames, {
      timeout: FIRST_FRAME_TIMEOUT_MS,
      message:
        'no video frame ever decoded on this tile — either the SFU has no path for the ' +
        'camera, or the fixture video source is not serving',
    })
    .toBeGreaterThan(0);

  // **The delta is the assertion, not the count.** A source that emitted one
  // frame and stopped satisfies "frames have been decoded" while showing
  // something an operator cannot tell from a frozen wall — and neither can a
  // screenshot, which is why this is the check that had to exist.
  const first = await readDecode(page);
  await page.waitForTimeout(SAMPLE_GAP_MS);
  const second = await readDecode(page);

  const framesAdvanced = second.totalVideoFrames - first.totalVideoFrames;

  // Printed on success as well as failure. A passing assertion says the delta
  // cleared the threshold; it does not say by how much, and the margin is what
  // tells a reader whether the picture is healthy or barely moving.
  console.info(
    `[decode] ${first.totalVideoFrames} → ${second.totalVideoFrames} frames in ${SAMPLE_GAP_MS}ms ` +
      `(+${framesAdvanced}, threshold ${MINIMUM_FRAMES_PER_SAMPLE}) across ${second.elements} element(s)`,
  );

  expect(
    framesAdvanced,
    `the picture is frozen, not live: ${first.totalVideoFrames} → ${second.totalVideoFrames} ` +
      `frames in ${SAMPLE_GAP_MS}ms across ${second.elements} video element(s)`,
  ).toBeGreaterThanOrEqual(MINIMUM_FRAMES_PER_SAMPLE);

  // ---- half two: the label, over that picture ----------------------------

  await expect(
    page.getByTestId('camera-viewer-overlay-label').first(),
    'the tile decodes video but renders no overlay label at all',
  ).toBeVisible({ timeout: 30_000 });

  await expect(
    page.getByTestId('camera-viewer-overlay-label').first(),
    `the overlay label is present but does not carry the variable's resolved value ` +
      `"${wall.variableInitialValue}"`,
  ).toContainText(wall.variableInitialValue, { timeout: 30_000 });
});
