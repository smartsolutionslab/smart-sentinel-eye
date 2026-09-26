import { test as setup, expect } from '@playwright/test';
import { signInAsOperator } from './sign-in';
import { newLiveVideoWall, writeLiveVideoWall } from './live-video-wall';
import { FIRST_WRITE_TIMEOUT_MS } from './cold-stack';

/**
 * Spec 056 — a wall whose tiles have **both** halves: a camera whose video
 * actually arrives, and an overlay bound to a variable.
 *
 * <para>
 * <b>The one difference from the SC-004 seed is the cameras' address, and it is
 * the whole point.</b> That seed registers `rtsp://10.0.5.71/stream`, which
 * nothing serves, so its tiles render `WHEP returned 404` and never create an
 * <c>RTCRtpReceiver</c>. Every overlay assertion in this repository has run
 * against that wall — so a tile that draws its label <i>only when the video
 * fails</i> passes the entire suite. This one points at the fixture's video
 * source, so the SFU has something to pull.
 * </para>
 *
 * <para>
 * <b>Spec 225 US2, raised by spec 262 (ADR-0156, D1 option A) — nine tiles,
 * not one and not the old four.</b> The wall used to seed a single 1×1 tile
 * ("one tile is enough — the gap is that no check has both halves, not that
 * none has four"), then a 2×2 grid at spec 225. It now seeds the domain's
 * real ceiling, a 3×3 grid, so the composite-and-render measurement this
 * wall feeds (`kiosk-shows-a-label-over-video.spec.ts`'s span test) is read
 * against actual worst-case tile count rather than an under-read.
 * </para>
 *
 * <para>
 * Drives management-web rather than the API for the same reason the other seeds
 * do: publishing needs an `If-Match` round-trip and the UI path gets the
 * contract right for free.
 * </para>
 */
setup('a published wall exists whose tiles have both video and a bound overlay', async ({ page }) => {
  // Sized here rather than taken from `FIRST_WRITE_TEST_TIMEOUT_MS`: six
  // budgeted sites do not fit the shared ceiling. Five of them arriving cold at
  // ~40 s each leaves nothing for the sixth to spend its 90 s and report *which*
  // locator never resolved — 5 × 40 s + a sign-in + 90 s ≈ 320 s. `cold-stack.ts`
  // carries the rule.
  //
  // Phase-6 review (spec 225 US2, nit N1): US2 added camera registrations 2-4,
  // each a warm (not cold-budgeted) site paying the ordinary
  // `expect.timeout` — 30 s in CI (`playwright.config.ts:12`) — worst case.
  // The 320 s figure above did not account for that; +90 s covers it with
  // margin.
  //
  // Spec 262 (ADR-0156, D1 option A): the wall grew from four cameras to
  // nine, so registrations 2-4 above became registrations 2-9 — six more
  // warm sites at the same 30 s CI worst case, +180 s. Rounded up rather than
  // padded to the old ratio: this sweep has never actually run at nine, so
  // the risk is watched on the first PR run (spec 262 §7 D1) rather than
  // trusted from arithmetic alone.
  setup.setTimeout(600_000);

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

  // 3. Nine cameras — **at an address something actually serves**, and at the
  //    domain's real ceiling (`GridDimensions.MaxTiles` / `MaxCells`, now 9 —
  //    `GridDimensions.cs:26,29`, raised from 4 by ADR-0156/spec 262), never
  //    past it (spec 225 US2). The URL comes from the module that owns it,
  //    never composed here: a host and port written into a fixture is a
  //    second thing to keep true, and when it rots the wall looks like a
  //    broken product rather than a broken fixture.
  //
  //    Only the first registration is this test's cold FIRST_WRITE_TIMEOUT_MS
  //    site for this message kind — registrations 2-9 repeat it within the
  //    same test and pay the ordinary warm cost instead (`cold-stack.ts`:
  //    "not for a repeat write of the same kind inside one test").
  await page.getByRole('link', { name: /^cameras$/i }).click();
  for (const [index, camera] of wall.cameras.entries()) {
    await page.getByRole('button', { name: /register camera/i }).click();
    await page.locator('#register-camera-name').fill(camera.name);
    await page.locator('#register-camera-url').fill(camera.rtspUrl);
    await page.getByRole('button', { name: /^register$/i }).click();
    await expect(page.getByRole('cell', { name: camera.name })).toBeVisible(
      index === 0 ? { timeout: FIRST_WRITE_TIMEOUT_MS } : undefined,
    );
  }

  // 4. The wall: 3×3, nine tiles, one camera per tile, all bound to the same
  //    overlay (spec 225 US2, raised from 2×2/four by ADR-0156/spec 262) —
  //    so a per-tile render cost is distinguishable from fixed overhead,
  //    without a ninth overlay write to pay for it.
  await page.getByRole('link', { name: /^layouts$/i }).click();
  await expect(page.getByRole('heading', { name: 'Layouts', exact: true })).toBeVisible();

  await page.getByRole('button', { name: /new layout/i }).click();
  await page.locator('#layout-name').fill(wall.layoutName);

  // The dialog defaults to 1×1 (`LayoutEditorDialog.tsx:51`), so the grid
  // preset has to be driven rather than left at its default to reach nine
  // tiles. `GRID_PRESETS`' label is `${rows}×${cols}` (`gridDesignerModel.ts`).
  await page.getByRole('radio', { name: '3×3' }).click();

  for (const [index, camera] of wall.cameras.entries()) {
    await page.locator(`#tile-${index}-camera`).selectOption({ label: camera.name });
    await page.locator(`#tile-${index}-overlay`).selectOption({ label: wall.overlayName });
  }

  await page.getByRole('button', { name: /save as draft/i }).click();
  await expect(page.getByRole('heading', { name: wall.layoutName })).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

  const layoutRow = page.getByRole('listitem').filter({ hasText: wall.layoutName });
  await layoutRow.getByRole('button', { name: /^publish$/i }).click();
  await expect(layoutRow.getByText(/Published/)).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });
});
