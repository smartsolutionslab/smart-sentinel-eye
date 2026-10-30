import { test, expect, type Locator } from '@playwright/test';
import { signInToKiosk } from './support/kiosk-session';
import { signInAsOperator } from './support/sign-in';
import { openSection } from './support/management-navigation';
import { readLiveVideoWall } from './support/live-video-wall';

/**
 * Spec 466 (issue #3184), SC-002 — the tile view shows an overlay the way the wall does.
 *
 * <para>
 * The seeded nine-tile wall paints one overlay on every tile. This reads the first tile's
 * overlay on the kiosk, then reads the same overlay in the management console's tile view for
 * that tile, and compares what is observable about it: where its box sits as a fraction of the
 * 16:9 viewer, how large its type is as a fraction of the viewer's width, its colour and opacity,
 * its text and where the text wraps.
 * </para>
 *
 * <para>
 * Honest limit (research.md R10): this is computed style and bounding boxes, not rasterised
 * pixels. The wall's picture is live video and the view's is a still or a neutral field, so
 * pixel equality of the blend is not asserted. Text wrap is asserted by line count read from the
 * rendered text's client rects.
 * </para>
 *
 * <para>
 * Named `kiosk-…` so it runs in the `kiosk` project, which depends on the `seed` project that
 * publishes the wall this reads. The console is a second browser context on :5173.
 * </para>
 */
test.use({ viewport: { width: 1920, height: 1080 } });

/** Of the viewer's width/height. The view sizes its tile from a nominal display, not the kiosk's pixels. */
const TOLERANCE = 0.01;

interface OverlayReading {
  /** The label's box, each as a fraction of the 16:9 viewer it sits in. */
  box: { x: number; y: number; width: number; height: number };
  /** Type size as a fraction of the viewer's width. */
  fontSizeOfWidth: number;
  color: string;
  opacity: string;
  text: string;
  lines: number;
}

async function readOverlay(label: Locator): Promise<OverlayReading> {
  return label.evaluate((element) => {
    const viewer = element.parentElement;
    if (viewer === null) throw new Error('the overlay label has no viewer around it');
    const root = viewer.getBoundingClientRect();
    const box = element.getBoundingClientRect();
    const style = getComputedStyle(element);

    const range = document.createRange();
    range.selectNodeContents(element);
    const tops = new Set([...range.getClientRects()].map((rect) => Math.round(rect.top)));

    return {
      box: {
        x: (box.left - root.left) / root.width,
        y: (box.top - root.top) / root.height,
        width: box.width / root.width,
        height: box.height / root.height,
      },
      fontSizeOfWidth: parseFloat(style.fontSize) / root.width,
      color: style.color,
      opacity: style.opacity,
      text: (element.textContent ?? '').trim(),
      lines: tops.size,
    };
  });
}

test('the tile view shows the wall’s overlay with the same geometry, type, colour and wrap', async ({
  page,
  browser,
}) => {
  test.setTimeout(180_000);

  const wall = readLiveVideoWall();

  // The wall.
  await signInToKiosk(page);
  await page.getByRole('listitem').filter({ hasText: wall.layoutName }).getByRole('button').click();
  await expect(page.getByTestId('layout-grid')).toBeVisible();
  const wallLabel = page.getByTestId('camera-viewer-overlay-label').first();
  await expect(wallLabel).toBeVisible();
  await expect(wallLabel).not.toHaveText('');
  const onTheWall = await readOverlay(wallLabel);

  // The same tile in the console.
  const management = await browser.newContext({
    baseURL: 'http://localhost:5173',
    viewport: { width: 1920, height: 1080 },
    ignoreHTTPSErrors: true,
  });
  try {
    const managementPage = await management.newPage();
    await signInAsOperator(managementPage);
    await openSection(managementPage, 'Layouts');

    const row = managementPage.getByRole('listitem').filter({ hasText: wall.layoutName });
    await row.getByRole('button', { name: /edit \(new draft\)/i }).click();
    await managementPage
      .getByRole('button', { name: /open tile view/i })
      .first()
      .click();

    const frame = managementPage.getByTestId('tile-view-display').getByTestId('tile-frame');
    await expect(frame).toBeVisible();
    const viewLabel = frame.getByTestId('camera-viewer-overlay-label');
    await expect(viewLabel).toBeVisible();
    await expect(viewLabel).not.toHaveText('');
    const inTheView = await readOverlay(viewLabel);

    for (const side of ['x', 'y', 'width', 'height'] as const) {
      expect(
        Math.abs(inTheView.box[side] - onTheWall.box[side]),
        `box.${side}: wall ${onTheWall.box[side].toFixed(4)}, view ${inTheView.box[side].toFixed(4)}`,
      ).toBeLessThanOrEqual(TOLERANCE);
    }
    expect(
      Math.abs(inTheView.fontSizeOfWidth - onTheWall.fontSizeOfWidth),
      `type size as a share of the viewer's width: wall ${onTheWall.fontSizeOfWidth.toFixed(5)}, view ${inTheView.fontSizeOfWidth.toFixed(5)}`,
    ).toBeLessThanOrEqual(TOLERANCE * onTheWall.fontSizeOfWidth + 0.0005);
    expect(inTheView.color).toBe(onTheWall.color);
    expect(inTheView.opacity).toBe(onTheWall.opacity);
    expect(inTheView.text).toBe(onTheWall.text);
    expect(inTheView.lines, 'the text wraps onto the same number of lines').toBe(onTheWall.lines);
  } finally {
    await management.close();
  }
});
