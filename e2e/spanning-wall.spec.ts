import { test, expect } from '@playwright/test';
import { signInAsOperator } from './support/sign-in';
import { signInToKiosk } from './support/kiosk-session';
import { FIRST_WRITE_TIMEOUT_MS } from './support/cold-stack';

/**
 * Spec 262 US2 + US3, T024 (ADR-0156) — a hero-and-thumbnails wall, authored
 * in the console and drawn at its real size on the kiosk.
 *
 * <para>
 * <b>Geometry, not decode.</b> The six cameras point at addresses nothing
 * serves, the same fixture-less convention every other authoring spec in this
 * suite uses (`layouts.spec.ts`, `seed-published-layout.setup.ts`) — a tile
 * renders whether or not a picture arrives, and this spec asserts what the
 * grid places, not what a `<video>` decodes.
 * </para>
 *
 * <para>
 * <b>This is one file spanning two apps</b>, unlike every other kiosk
 * assertion in this suite (which lives in its own `kiosk-*.spec.ts`, run by
 * the `kiosk` Playwright project against `:5174`, fed by a `.setup.ts` seed).
 * This file's name matches neither the `kiosk-*` nor `wall-*` exclusion in
 * `playwright.config.ts`, so it runs under the default `chromium` project
 * (`:5173`, management-web) like `layouts.spec.ts` — the console half needs
 * nothing else. The kiosk half opens its own browser context with an explicit
 * `baseURL` override, mirroring how `layouts.spec.ts`'s lost-update test opens
 * a second context for its own reason.
 * </para>
 *
 * <para>
 * <b>Why the first and last tile, not a position lookup.</b>
 * `wallGrid.ts#buildGridItems` emits items in row-major order <i>by origin</i>,
 * and a cell another tile's span covers emits nothing at all — no tile, no
 * placeholder. With the hero at (0,0) spanning 2×2 and five 1×1 tiles at
 * (0,2), (1,2), (2,0), (2,1), (2,2), the three covered cells — (0,1), (1,0),
 * (1,1) — never appear, so the DOM order of `layout-tile` elements is exactly
 * hero, (0,2), (1,2), (2,0), (2,1), (2,2). The hero is always first and
 * (2,2) is always last, which this spec's own "0 layout-empty-cell" and
 * "6 layout-tile" assertions confirm before the geometry comparison relies
 * on it.
 * </para>
 */
test('a hero-and-thumbnails wall is authored, published and drawn at its real size', async ({ page, browser }) => {
  // Sized per-site rather than copying `FIRST_WRITE_TEST_TIMEOUT_MS` (that
  // constant's own arithmetic is for a single-app test): three cold-budgeted
  // write kinds (first camera registration, save-as-draft, publish —
  // `cold-stack.ts`) at 90 s each = 270 s.
  //
  // Phase-6 review (spec 262 nit S3): that figure alone missed camera
  // registrations 2-6 — five more warm (not cold-budgeted) sites paying the
  // ordinary `expect.timeout` ceiling of 30 s each in CI
  // (`playwright.config.ts:12`) = 150 s — plus two full Keycloak sign-ins
  // (operator and kiosk) and the kiosk's own warm expects (`layout-grid`
  // visible, the `Layouts` heading, the published listitem). 270 s + 150 s
  // already exceeds the old 360 s budget before a single sign-in is counted;
  // rounded up to 480 s for margin on the sign-ins and remaining warm
  // expects. Same gap already found once in this repo
  // (`seed-live-video-wall.setup.ts`, spec 225 phase-6 nit N1) — mirror that
  // arithmetic rather than re-deriving it from scratch next time.
  test.setTimeout(480_000);

  const stamp = Date.now();
  const cameraNames = Array.from({ length: 6 }, (_, index) => `E2E Span Cam ${index + 1} ${stamp}`);
  const layoutName = `E2E Layout Span ${stamp}`;

  await signInAsOperator(page);

  // (1) Six cameras — unserved addresses (see file doc above). Only the first
  // registration is this test's cold `FIRST_WRITE_TIMEOUT_MS` site for this
  // message kind; registrations 2-6 repeat it within the same test and pay
  // the ordinary warm cost instead (`cold-stack.ts`).
  for (const [index, name] of cameraNames.entries()) {
    await page.getByRole('button', { name: /register camera/i }).click();
    await page.locator('#register-camera-name').fill(name);
    await page.locator('#register-camera-url').fill(`rtsp://10.0.5.${90 + index}/stream`);
    await page.getByRole('button', { name: /^register$/i }).click();
    await expect(page.getByRole('cell', { name })).toBeVisible(
      index === 0 ? { timeout: FIRST_WRITE_TIMEOUT_MS } : undefined,
    );
  }

  // (2) The hero wall: a 3×3 grid, tile (0,0) spanning 2×2, five 1×1 tiles at
  // (0,2), (1,2), (2,0), (2,1), (2,2). Tile ids are the dense grid index,
  // row-major (`GridDesigner.tsx`): (0,0)=0, (0,2)=2, (1,2)=5, (2,0)=6,
  // (2,1)=7, (2,2)=8.
  await page.getByRole('link', { name: /^layouts$/i }).click();
  await expect(page.getByRole('heading', { name: 'Layouts', exact: true })).toBeVisible();

  await page.getByRole('button', { name: /new layout/i }).click();
  await page.locator('#layout-name').fill(layoutName);
  await page.getByRole('radio', { name: '3×3' }).click();

  const tileAssignments: ReadonlyArray<readonly [number, string]> = [
    [0, cameraNames[0]!],
    [2, cameraNames[1]!],
    [5, cameraNames[2]!],
    [6, cameraNames[3]!],
    [7, cameraNames[4]!],
    [8, cameraNames[5]!],
  ];
  for (const [index, name] of tileAssignments) {
    await page.locator(`#tile-${index}-camera`).selectOption({ label: name });
  }

  // The hero's span selects only appear once the cell is populated
  // (`GridDesigner.tsx`), which the camera assignment above already did.
  await page.locator('#tile-0-row-span').selectOption('2');
  await page.locator('#tile-0-col-span').selectOption('2');

  await page.getByRole('button', { name: /save as draft/i }).click();
  await expect(page.getByRole('heading', { name: layoutName })).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

  const layoutRow = page.getByRole('listitem').filter({ hasText: layoutName });
  await layoutRow.getByRole('button', { name: /^publish$/i }).click();
  await expect(layoutRow.getByText(/Published/)).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

  // (3) Open it BY NAME on the kiosk — a second context, its own sign-in, its
  // own baseURL (see file doc above).
  const kioskContext = await browser.newContext({ baseURL: 'http://localhost:5174' });
  try {
    const kioskPage = await kioskContext.newPage();
    await signInToKiosk(kioskPage);

    await kioskPage.getByRole('listitem').filter({ hasText: layoutName }).getByRole('button').click();
    await expect(kioskPage.getByTestId('layout-grid')).toBeVisible();
    await expect(kioskPage.getByRole('alert')).toHaveCount(0);

    const tiles = kioskPage.getByTestId('layout-tile');
    const emptyCells = kioskPage.getByTestId('layout-empty-cell');

    // The hero's span covers the three otherwise-empty cells entirely, so a
    // 3×3 grid with 6 tiles has no placeholder at all.
    await expect(tiles).toHaveCount(6);
    await expect(emptyCells).toHaveCount(0);

    // Row-major-by-origin DOM order (see file doc above): the hero is first,
    // the lone bottom-right 1×1 tile is last.
    const heroBox = await tiles.first().boundingBox();
    const thumbnailBox = await tiles.last().boundingBox();

    expect(heroBox, 'the hero tile has no bounding box — it did not render').not.toBeNull();
    expect(thumbnailBox, 'the (2,2) tile has no bounding box — it did not render').not.toBeNull();

    const widthRatio = heroBox!.width / thumbnailBox!.width;
    const heightRatio = heroBox!.height / thumbnailBox!.height;

    // ~2×, with tolerance for the grid's 1-unit gap crossing the span
    // (plan.md §4.4): a 15% band comfortably covers a few pixels of gap
    // against a few-hundred-pixel cell while still catching a wall that
    // rendered the hero as a plain 1×1 tile (ratio ~1) or mis-sized entirely.
    expect(widthRatio, `hero width ${heroBox!.width} vs thumbnail width ${thumbnailBox!.width}`).toBeGreaterThan(1.85);
    expect(widthRatio, `hero width ${heroBox!.width} vs thumbnail width ${thumbnailBox!.width}`).toBeLessThan(2.15);
    expect(heightRatio, `hero height ${heroBox!.height} vs thumbnail height ${thumbnailBox!.height}`).toBeGreaterThan(
      1.85,
    );
    expect(heightRatio, `hero height ${heroBox!.height} vs thumbnail height ${thumbnailBox!.height}`).toBeLessThan(
      2.15,
    );
  } finally {
    await kioskContext.close();
  }
});
