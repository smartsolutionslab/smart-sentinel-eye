import { test, expect } from '@playwright/test';
import { signInToKiosk } from './support/kiosk-session';
import { readLiveVideoWall, LIVE_VIDEO_WALL_OVERLAY_FONT_SIZE_PX } from './support/live-video-wall';

/**
 * Spec 294 (issue #2353), plan.md §6 (3) — new behaviour, RED against
 * unmodified `develop` (ADR-0139/ADR-0144).
 *
 * <para>
 * <b>The type-to-plate ratio, proven on the real nine-tile wall.</b> The
 * label's box is `normalizedWidth × CameraViewer root width` by construction
 * (`CameraViewer.tsx:393`, `CameraViewer.tsx:524-540`); this spec's fix makes
 * its type the same proportion of that same root — `container-type:
 * inline-size` on the root, `fontSize` resolved in `cqw` against it
 * (`overlayLabelStyle.ts`, spec §5). So `font-size = containerWidth × f /
 * 1920` (subject to the `min(12, f/4)` px floor) should hold on <b>every</b>
 * tile of the seeded nine-tile wall, regardless of how the 3×3 grid divides
 * up 1920×1080 — and today it does not: the old `vw`-derived clamp caps out
 * flat at `f` px on a 1920-wide kiosk viewport (spec §2), so every tile
 * paints the same 32 px regardless of its own width.
 * </para>
 *
 * <para>
 * `f` is read from `live-video-wall.ts`'s
 * <c>LIVE_VIDEO_WALL_OVERLAY_FONT_SIZE_PX</c> — the seed's own documented
 * default — rather than restated as a literal beside the assertion (memory:
 * an assertion must not check its own input). The floor does not bind at
 * this `f` and this wall's tile widths (plan.md §6 (3)).
 * </para>
 *
 * <para>
 * Viewport pinned to 1920×1080 (spec §3's "Independent Test", spec.md §8
 * scenario 1) — the kiosk's canonical size, and the one every ratio in
 * spec.md §2's table is computed against.
 * </para>
 */
test.use({ viewport: { width: 1920, height: 1080 } });

/** The `min(12, f/4)` px legibility floor (spec §5) — restated from the
 * formula, not a magic number: at this wall's `f` and tile widths it never
 * binds (plan.md §6 (3)), and this constant is what proves that rather than
 * assuming it. */
const FLOOR_PX = Math.min(12, LIVE_VIDEO_WALL_OVERLAY_FONT_SIZE_PX / 4);

/** `getComputedStyle` rounds; the formula does not. */
const TOLERANCE_PX = 0.5;

interface TileReading {
  fontSizePx: number;
  containerWidthPx: number;
  containerType: string;
}

test('a label is proportional to its own tile container on every tile of the nine-tile wall', async ({ page }) => {
  test.setTimeout(120_000);

  const wall = readLiveVideoWall();

  await signInToKiosk(page);

  await page.getByRole('listitem').filter({ hasText: wall.layoutName }).getByRole('button').click();
  await expect(page.getByTestId('layout-grid')).toBeVisible();

  const labels = page.getByTestId('camera-viewer-overlay-label');
  // There are `wall.cameras.length` tiles by construction (nine, the
  // domain's ceiling — GridDimensions.cs:26,29, ADR-0156) — asserted, not
  // assumed, exactly as `kiosk-shows-a-label-over-video.spec.ts` does,
  // because the per-tile loop below is only as good as the count it reads.
  await expect(labels).toHaveCount(wall.cameras.length);

  for (let index = 0; index < wall.cameras.length; index += 1) {
    const label = labels.nth(index);

    const reading: TileReading = await label.evaluate((element) => {
      const container = element.parentElement;
      if (container === null) {
        throw new Error('the overlay label has no parent — it should be CameraViewer’s root');
      }
      const labelStyle = getComputedStyle(element);
      const containerStyle = getComputedStyle(container);
      return {
        fontSizePx: parseFloat(labelStyle.fontSize),
        containerWidthPx: parseFloat(containerStyle.width),
        containerType: containerStyle.getPropertyValue('container-type').trim(),
      };
    });

    const proportional = (reading.containerWidthPx * LIVE_VIDEO_WALL_OVERLAY_FONT_SIZE_PX) / 1920;
    const expectedFontSizePx = Math.max(FLOOR_PX, proportional);

    expect(
      reading.fontSizePx,
      `tile ${index}: container width ${reading.containerWidthPx.toFixed(2)}px, expected font-size ` +
        `max(${FLOOR_PX}px, ${proportional.toFixed(3)}px) = ${expectedFontSizePx.toFixed(3)}px, ` +
        `got ${reading.fontSizePx}px`,
    ).toBeGreaterThanOrEqual(expectedFontSizePx - TOLERANCE_PX);
    expect(reading.fontSizePx).toBeLessThanOrEqual(expectedFontSizePx + TOLERANCE_PX);

    expect(reading.containerType, `tile ${index}: CameraViewer's root must establish an inline-size container`).toBe(
      'inline-size',
    );
  }
});
