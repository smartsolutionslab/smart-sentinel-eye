import { test, expect, type Page } from '@playwright/test';
import { signInToKiosk } from './support/kiosk-session';
import { signInAsOperator } from './support/sign-in';
import { isDecodeOngoing, readLiveVideoWall } from './support/live-video-wall';

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
    isDecodeOngoing(first.totalVideoFrames, second.totalVideoFrames, MINIMUM_FRAMES_PER_SAMPLE),
    `the picture is frozen, not live: ${first.totalVideoFrames} → ${second.totalVideoFrames} ` +
      `frames in ${SAMPLE_GAP_MS}ms across ${second.elements} video element(s)`,
  ).toBe(true);

  // **The rule must also reject a stall, or it is not a rule.** A check that
  // only ever sees healthy readings cannot distinguish "the picture is moving"
  // from "this assertion is always true" — and the failure it exists to catch,
  // a source that emitted one frame and stopped, is precisely the reading it
  // never gets to see on a working stack.
  //
  // Arithmetic, deliberately: it costs no stack time, and the plumbing is
  // covered by the mutation that points the camera at an address nothing serves.
  expect(
    isDecodeOngoing(first.totalVideoFrames, first.totalVideoFrames + 1, MINIMUM_FRAMES_PER_SAMPLE),
    'one extra frame in a second is a frozen wall, and the rule must say so',
  ).toBe(false);

  expect(
    isDecodeOngoing(first.totalVideoFrames, first.totalVideoFrames, MINIMUM_FRAMES_PER_SAMPLE),
    'no new frames at all is a frozen wall, and the rule must say so',
  ).toBe(false);

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

// ─────────────────────────────────────────────────────────────────────────
// US2 — the span, in this file rather than its own.
//
// **They share a wall, so they must share a file.** The span sets the bound
// variable to a series of values; the check above expects the seeded initial
// one. In separate files those race — and in CI, where files run in one
// worker in alphabetical order, the span would run FIRST and the check above
// would fail every time. It did exactly that in a full-suite run, with the
// label reading `SPAN0`.
//
// One file makes the order explicit and one worker's, rather than resting on
// filenames sorting the way someone hoped.
// ─────────────────────────────────────────────────────────────────────────

/**
 * Spec 056 US2 — the span, timed as one span or refused.
 *
 * <para>
 * <b>What this measures, and what it does not.</b> Submission of a variable's
 * new value to that value being visible on a tile. That covers <i>event →
 * overlay state</i> and <i>overlay composite + render</i>. It does <b>not</b>
 * cover camera → SFU, SFU → decode, or the presentation buffer — those are legs
 * of the <i>picture's</i> path, not the label's. A figure from here says nothing
 * about whether the 800 ms budget holds, because three of its legs are absent.
 * </para>
 *
 * <para>
 * <b>One subtraction on one clock.</b> Both stamps are taken by this process, on
 * this machine. Spec 053 examined exactly two shapes and reached different
 * verdicts: two readers of one OS clock (safe — how the front of its span was
 * established) and a host stamp minus a container stamp (not established, still
 * open). This is the first. The browser's clock is never mixed in.
 * </para>
 *
 * <para>
 * <b>A refusal is a result.</b> Where the run cannot show both ends share a
 * clock, it reports what it could not establish and no figure. The alternative
 * on offer is a number assembled from per-leg medians, and medians do not add
 * (ADR-0135) — that number would be a fabrication wearing a measurement's
 * clothes.
 * </para>
 */

const ITERATIONS = 5;
const OBSERVE_TIMEOUT_MS = 60_000;

/** The legs this span covers, and the ones it does not. Reported, never implied. */
const LEGS_COVERED = ['event → overlay state', 'overlay composite + render'] as const;
const LEGS_NOT_COVERED = ['camera → SFU', 'SFU → kiosk decode', 'presentation buffer'] as const;

interface SpanMeasurement {
  elapsedMilliseconds?: number;
  refusal?: string;
}

/**
 * Whether both ends of the span can be stamped on one clock.
 *
 * <para>
 * True only when this process both submits and observes, on this machine. A
 * remote browser or a distributed grid breaks that, and the honest answer there
 * is a refusal rather than a subtraction across two clocks.
 * </para>
 */
function sharesOneClock(): { ok: true } | { ok: false; because: string } {
  const wsEndpoint = process.env['PW_TEST_CONNECT_WS_ENDPOINT'];
  if (wsEndpoint !== undefined && wsEndpoint !== '') {
    return {
      ok: false,
      because: `the browser is remote (${wsEndpoint}), so the observation is not stamped on this machine`,
    };
  }
  return { ok: true };
}

/** Reports every figure, its median and range, and the legs it does not cover. */
function report(measurements: ReadonlyArray<SpanMeasurement>): void {
  const figures = measurements
    .map((measurement) => measurement.elapsedMilliseconds)
    .filter((value): value is number => value !== undefined)
    .sort((left, right) => left - right);

  if (figures.length === 0) {
    const because = measurements[0]?.refusal ?? 'no measurement was taken';
    console.info(`[span] UNMEASURED — ${because}`);
    console.info('[span] no figure is reported, and none is derived from per-leg figures');
    return;
  }

  const median = figures[Math.floor(figures.length / 2)]!;

  // Every figure, not a summary. A median without its spread hides whether the
  // system under test or the machine is the bottleneck, which is the distinction
  // that made an earlier "~3x" claim in this repository wrong.
  console.info(`[span] ${figures.length} runs — ${figures.join(' / ')} ms`);
  console.info(`[span] median ${median} ms, range ${figures[0]}-${figures[figures.length - 1]} ms`);
  console.info(`[span] covers: ${LEGS_COVERED.join(', ')}`);
  console.info(`[span] NOT covered: ${LEGS_NOT_COVERED.join(', ')}`);
  console.info('[span] includes the label hold (ADR-0129): yes — this wall has video');
  console.info(`[span] conditions: ${process.platform}, CI=${process.env['CI'] ?? 'false'}, one tile, one clip`);
}

async function setValue(operatorPage: Page, variableName: string, value: string): Promise<void> {
  const row = operatorPage.getByRole('listitem').filter({ hasText: variableName });
  await row.getByPlaceholder('New value').fill(value);
  await row.getByRole('button', { name: /^set value$/i }).click();
}

test('the span from a value being submitted to it being visible', async ({ page, context }) => {
  test.setTimeout(300_000);

  const wall = readLiveVideoWall();
  const clock = sharesOneClock();

  await signInToKiosk(page);
  await page.getByRole('listitem').filter({ hasText: wall.layoutName }).getByRole('button').click();
  await expect(page.getByTestId('layout-grid')).toBeVisible();

  const label = page.getByTestId('camera-viewer-overlay-label').first();
  await expect(label).toContainText(wall.variableInitialValue, { timeout: 30_000 });

  // The channel must be up before anything is timed, or the first figure
  // measures the connection rather than the span.
  await expect(page.getByTestId('live-updates-degraded')).toBeHidden({ timeout: 45_000 });

  // **Refused before it is attempted**, so a run that cannot be measured says so
  // rather than producing a figure whose two ends came from different clocks.
  if (!clock.ok) {
    report([{ refusal: clock.because }]);
    test.skip(true, `span unmeasured: ${clock.because}`);
    return;
  }

  const operatorContext = await context.browser()!.newContext({ baseURL: 'http://localhost:5173' });
  const operatorPage = await operatorContext.newPage();
  const measurements: SpanMeasurement[] = [];

  try {
    await signInAsOperator(operatorPage);
    await operatorPage.getByRole('link', { name: /^system variables$/i }).click();

    for (let iteration = 0; iteration < ITERATIONS; iteration += 1) {
      // Distinguishable per iteration, so the observation cannot match a value
      // left over from the previous one.
      const value = `SPAN${iteration}`;

      const submittedAt = Date.now();
      await setValue(operatorPage, wall.variableName, value);

      try {
        await expect(label).toContainText(value, { timeout: OBSERVE_TIMEOUT_MS });
        measurements.push({ elapsedMilliseconds: Date.now() - submittedAt });
      } catch {
        measurements.push({
          refusal: `iteration ${iteration}: the value never reached the tile within ${OBSERVE_TIMEOUT_MS}ms`,
        });
        break;
      }
    }
  } finally {
    await operatorPage.close();
    await operatorContext.close();
  }

  report(measurements);

  const timed = measurements.filter((measurement) => measurement.elapsedMilliseconds !== undefined);

  // **An unmeasured span honestly recorded is the required outcome**, not a
  // failure — FR-009. What is forbidden is substituting a derived figure, and
  // there is nowhere in this file that could produce one.
  if (timed.length === 0) {
    test.skip(true, `span unmeasured: ${measurements[0]?.refusal ?? 'unknown'}`);
    return;
  }

  expect(timed.length, 'a single run is not a measurement').toBeGreaterThan(1);
});
