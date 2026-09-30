import { test, expect } from '@playwright/test';
import { signInAsOperator } from './support/sign-in';
import { openSection } from './support/management-navigation';

/**
 * Spec 294 (issue #2353), plan.md §6 (4) — new behaviour, RED against
 * unmodified `develop` (ADR-0139/ADR-0144).
 *
 * <para>
 * <b>The editor half of the ratio-invariance proof</b> (with
 * `e2e/kiosk-label-scales-with-its-tile.spec.ts`'s wall half): both surfaces
 * call the same `overlayLabelSurfaceStyle()` (spec 146), so both must equal
 * the same `f / 1920` constant times their own container's width — the
 * editor's canvas (`OverlayEditor.tsx:610-620`, `container-type:
 * inline-size`, spec §5), not the viewport.
 * </para>
 *
 * <para>
 * `chromium` project, Desktop Chrome's default 1280×720 viewport — not
 * widened, because the point is that the type no longer depends on the
 * viewport at all once it is proportional to the (fixed-width) canvas. Red
 * today for exactly the reason the old formula was broken: at this
 * viewport the `vw` term is still live (`< 1600` px, spec §2), so the old
 * clamp resolves to `3vw` of 1280 = 38.4 px, not the capped 48 px and not
 * the new formula's 20 px either way.
 * </para>
 */
test('the editor preview is proportional to its own canvas, not the viewport', async ({ page }) => {
  await signInAsOperator(page);

  await openSection(page, 'Overlays');
  await page.getByRole('button', { name: /new overlay/i }).click();

  await page.getByTestId('overlay-editor-font-size').fill('48');

  await expect(page.getByText('Font size: 48px')).toBeVisible();

  const canvas = page.getByTestId('overlay-editor-canvas');
  const preview = page.getByTestId('overlay-editor-preview');

  const canvasWidthPx = await canvas.evaluate((element) => element.getBoundingClientRect().width);
  // 48 × canvasWidth / 1920 — spec §5's formula, at the editor's own,
  // fixed-width canvas rather than any literal restated here.
  const expectedFontSizePx = (48 * canvasWidthPx) / 1920;

  // The Rnd wrapper — `overlay-editor-preview`'s parent — carries the style
  // (the same traversal `OverlayLabelParity.test.tsx` uses).
  const previewFontSizePx = await preview.evaluate((element) => {
    const box = element.parentElement;
    if (box === null) {
      throw new Error('the preview label has no parent — it should carry overlayLabelSurfaceStyle()');
    }
    return parseFloat(getComputedStyle(box).fontSize);
  });
  const canvasContainerType = await canvas.evaluate((element) =>
    getComputedStyle(element).getPropertyValue('container-type').trim(),
  );

  expect(
    previewFontSizePx,
    `canvas width ${canvasWidthPx.toFixed(2)}px, expected font-size ${expectedFontSizePx.toFixed(3)}px, ` +
      `got ${previewFontSizePx}px`,
  ).toBeGreaterThanOrEqual(expectedFontSizePx - 0.5);
  expect(previewFontSizePx).toBeLessThanOrEqual(expectedFontSizePx + 0.5);

  expect(canvasContainerType, 'the editor canvas must establish an inline-size container').toBe('inline-size');
});
