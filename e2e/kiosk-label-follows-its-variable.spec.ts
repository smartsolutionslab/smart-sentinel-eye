import { test, expect, type Page } from '@playwright/test';
import { signInToKiosk } from './support/kiosk-session';
import { signInAsOperator } from './support/sign-in';
import { readLiveVideoWall } from './support/live-video-wall';

/**
 * Spec 056 US1 — the binding is live, not a coincidence.
 *
 * <para>
 * <b>Why this is separate from the both-halves check.</b> That one can be
 * satisfied by a label that happens to carry the right text on first paint: an
 * overlay whose resolved value was correct when the page loaded proves nothing
 * about the value ever changing. Only a change followed by the tile
 * distinguishes a live binding from a lucky one.
 * </para>
 *
 * <para>
 * <b>And video must keep decoding across the change.</b> A label that updates
 * because the tile fell back to a no-video state would satisfy a naive
 * assertion while being precisely the failure this feature exists to catch.
 * </para>
 */

const SAMPLE_GAP_MS = 1_000;
const MINIMUM_FRAMES_PER_SAMPLE = 10;

async function totalVideoFrames(page: Page): Promise<number> {
  return page.evaluate(() => {
    let total = 0;
    for (const video of Array.from(document.querySelectorAll('video'))) {
      if (typeof video.getVideoPlaybackQuality !== 'function') continue;
      total += video.getVideoPlaybackQuality().totalVideoFrames;
    }
    return total;
  });
}

/**
 * **Held back, not passing, and the reason is written down rather than
 * discovered again by the next person.**
 *
 * <para>
 * This does not pass today, and the investigation did not reach a cause. What
 * is established, by running it:
 * </para>
 *
 * <ul>
 *   <li>The change reaches the server — the operator page asserts the new value
 *       is visible before the kiosk is ever checked.</li>
 *   <li>The kiosk's live-update channel is up — the degraded badge is hidden
 *       before the change is made.</li>
 *   <li>The tile keeps its old text for 60 s. On a wall <b>with</b> video and on
 *       one <b>without</b>, so the label hold (ADR-0129) is not the cause.
 *       Asserting on <c>layout-tile</c> instead of the overlay label does not
 *       change it either, so it is not the locator.</li>
 *   <li><b>Nothing in this repository has ever tested this path.</b> The only
 *       existing check that changes a variable and expects a tile to follow —
 *       the reconciliation spec — does it while the kiosk is <i>offline</i> and
 *       asserts after reconnect. That path is green; the online one was never
 *       covered.</li>
 * </ul>
 *
 * <para>
 * Marked <c>fixme</c> rather than deleted: the expectation is real and belongs
 * in the suite, and a check quietly removed is one nobody comes back to. It is
 * not marked <c>fail</c>, because that would assert the failure is understood —
 * and it is not.
 * </para>
 */
test.fixme('the label follows its variable while the picture keeps moving', async ({ page, context }) => {
  test.setTimeout(240_000);

  const wall = readLiveVideoWall();

  await signInToKiosk(page);
  await page.getByRole('listitem').filter({ hasText: wall.layoutName }).getByRole('button').click();
  await expect(page.getByTestId('layout-grid')).toBeVisible();

  const label = page.getByTestId('camera-viewer-overlay-label').first();
  await expect(label).toContainText(wall.variableInitialValue, { timeout: 30_000 });

  // Video is decoding before the change, so a later stall is attributable to
  // the change rather than to the tile never having worked.
  await expect
    .poll(async () => totalVideoFrames(page), { timeout: 30_000 })
    .toBeGreaterThan(0);
  const beforeChange = await totalVideoFrames(page);

  // **Wait for the live channel before changing anything.** A value set while
  // the hub is still connecting is pushed to nobody, and the tile then sits on
  // its old text for reasons that have nothing to do with the binding — which
  // is exactly how this first failed, with the label stuck on its initial value
  // for a full minute.
  await expect(
    page.getByTestId('live-updates-degraded'),
    'the kiosk never established its live-update channel, so no change could reach it',
  ).toBeHidden({ timeout: 45_000 });

  // Change the value from a second session, the way an operator would — the
  // kiosk is a display and cannot set variables.
  //
  // **Its own context with management-web's base URL.** A page opened from this
  // one inherits the kiosk project's `:5174`, so `signInAsOperator` lands on the
  // kiosk's layout picker instead of the management shell — which is exactly how
  // this failed first time. Same shape as the reconciliation spec's admin page.
  const operatorContext = await context.browser()!.newContext({ baseURL: 'http://localhost:5173' });
  const operatorPage = await operatorContext.newPage();
  try {
    await signInAsOperator(operatorPage);
    await operatorPage.getByRole('link', { name: /^system variables$/i }).click();

    // The value input is inline in the row, not in a dialog — same controls the
    // reconciliation and system-variables specs drive.
    const row = operatorPage.getByRole('listitem').filter({ hasText: wall.variableName });
    await row.getByPlaceholder('New value').fill(wall.variableChangedValue);
    await row.getByRole('button', { name: /^set value$/i }).click();
    await expect(row.getByText(wall.variableChangedValue)).toBeVisible();
  } finally {
    await operatorPage.close();
    await operatorContext.close();
  }

  // The tile follows. Generous, because the label is deliberately HELD back to
  // match the age of the picture (ADR-0129) — a wait tuned to a video-less wall
  // would be too short here, and that difference is the point.
  await expect(
    label,
    `the variable changed to "${wall.variableChangedValue}" but the tile's label did not follow`,
  ).toContainText(wall.variableChangedValue, { timeout: 60_000 });

  // And the picture is still moving. Without this, a tile that lost its video
  // and fell back to a label-only state would pass.
  const afterChange = await totalVideoFrames(page);
  await page.waitForTimeout(SAMPLE_GAP_MS);
  const settled = await totalVideoFrames(page);

  expect(
    afterChange - beforeChange,
    'video stopped advancing while the label was updating',
  ).toBeGreaterThan(0);

  expect(
    settled - afterChange,
    `the label updated but the picture froze: ${afterChange} → ${settled} frames in ${SAMPLE_GAP_MS}ms`,
  ).toBeGreaterThanOrEqual(MINIMUM_FRAMES_PER_SAMPLE);
});
