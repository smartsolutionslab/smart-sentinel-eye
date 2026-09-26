import { test, expect } from '@playwright/test';
import { signInAsOperator } from './support/sign-in';
import { signInToKiosk } from './support/kiosk-session';
import { FIRST_WRITE_TEST_TIMEOUT_MS, FIRST_WRITE_TIMEOUT_MS } from './support/cold-stack';

/**
 * Spec 258 (#2608) US1, T015. Two independent scenarios:
 *
 * - **US1-3** — an admin creates a wall from two Published layouts, a kiosk
 *   opens it, and clicking **Next** in management-web moves the kiosk from
 *   the first scene's grid to the second's within about a second.
 * - **US1-17** — while the kiosk's SignalR connection is down, a switch
 *   happens behind its back; on reconnect the kiosk re-reads the wall and
 *   shows the current scene, with no page reload.
 *
 * **What this does NOT prove.** It does not measure FR-V1's settle-time
 * budget (Phase 5's job, spec.md §7) — the "within ~1s" wait below is a test
 * timeout, not a recorded figure. It does not prove the audit trail (that is
 * `WallEndpointsTests.cs`, T013, over the real Postgres row). It does not
 * prove multi-tile grids specifically — each layout here is the smallest
 * legal one (a single tile), because what changes between scenes is *which*
 * layout is showing, not how many tiles it has.
 *
 * **This file's name collides with the `[wall]` Playwright project.**
 * `playwright.config.ts`'s `wall` project (kiosk-web's *wall-display* mode,
 * :5175 — a different "wall" from this spec's `Wall` aggregate) matches any
 * `wall-*.spec.ts`, and its `chromium` project (management-web, :5173)
 * excludes the same pattern for exactly the reason this file tripped over:
 * a `wall-*`-named spec run against the wrong app fails at the very first
 * `signInAsOperator` line, before any of its own code runs, because the page
 * it navigates is the kiosk wall-display screen, which has no "sign in"
 * button leading to a "Cameras" heading. `test.use` below pins this file's
 * default `page`/`context` fixtures back to management-web regardless of
 * which project sweeps the file in; the kiosk side already uses its own
 * `browser.newContext()`, which — per `kiosk-comes-back.spec.ts` — does not
 * inherit a project's `use.baseURL` either, so it needs the same explicit
 * value pointed at the ordinary kiosk (:5174, not the :5175 wall display).
 *
 * **RED today.** Nothing under this spec exists yet: no "Walls" nav entry, no
 * wall create dialog, no `/walls/:wallIdentifier` kiosk route. The
 * `data-testid`s and button/heading copy below are this test's own contract
 * for T061 (management-web) and T064 (kiosk-web) to satisfy — mirroring how
 * `layouts.spec.ts` and `CellPage.tsx`'s existing `layout-grid`/`layout-tile`
 * testids already work, not inventing a new naming style.
 */

/**
 * Registers a camera and authors + publishes a one-tile layout named `name`.
 * Leaves the page on the Layouts list. Returns the registered camera's
 * identifier (a GUID) — extracted from its Cameras-page row link — because
 * that, not the camera's name, is what ends up embedded in the kiosk's
 * layout-tile content (see the file header for why).
 */
async function createPublishedLayout(
  page: import('@playwright/test').Page,
  name: string,
  cameraName: string,
): Promise<string> {
  // Navigate to Cameras explicitly rather than assuming the caller is already
  // there — the second call in a row starts on the Layouts list left by the
  // first call's own ending, where "Register camera" does not exist.
  await page.getByRole('link', { name: /^cameras$/i }).click();
  await expect(page.getByRole('heading', { name: 'Cameras', exact: true })).toBeVisible();
  await page.getByRole('button', { name: /register camera/i }).click();
  await page.locator('#register-camera-name').fill(cameraName);
  await page.locator('#register-camera-url').fill(`rtsp://10.0.5.${Math.floor(Math.random() * 200) + 2}/stream`);
  await page.getByRole('button', { name: /^register$/i }).click();
  const cameraRow = page.getByRole('cell', { name: cameraName });
  await expect(cameraRow).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });
  const cameraHref = await cameraRow.getByRole('link').getAttribute('href');
  const cameraIdentifier = cameraHref!.split('/').pop()!;

  await page.getByRole('link', { name: /^layouts$/i }).click();
  await expect(page.getByRole('heading', { name: 'Layouts', exact: true })).toBeVisible();
  await page.getByRole('button', { name: /new layout/i }).click();
  await page.locator('#layout-name').fill(name);
  await page.locator('#tile-0-camera').selectOption({ label: cameraName });
  await page.getByRole('button', { name: /save as draft/i }).click();
  await expect(page.getByRole('heading', { name })).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

  const row = page.getByRole('listitem').filter({ hasText: name });
  await row.getByRole('button', { name: /^publish$/i }).click();
  await expect(row.getByText(/Published/)).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

  return cameraIdentifier;
}

/** Creates a wall named `wallName` with the given ordered scene (layout) names. Leaves the page on the wall's detail view. */
async function createWall(page: import('@playwright/test').Page, wallName: string, sceneLayoutNames: string[]) {
  await page.getByRole('link', { name: /^walls$/i }).click();
  await expect(page.getByRole('heading', { name: 'Walls', exact: true })).toBeVisible();
  await page.getByRole('button', { name: /new wall/i }).click();
  await page.locator('#wall-name').fill(wallName);
  for (const layoutName of sceneLayoutNames) {
    await page.getByRole('checkbox', { name: layoutName }).check();
  }
  await page.getByRole('button', { name: /^save$/i }).click();

  await expect(page.getByRole('heading', { name: wallName })).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });
}

function showingText(page: import('@playwright/test').Page) {
  return page.getByTestId('wall-showing');
}

// See the file header: this spec's own `page`/`context` must be management-web
// (:5173) no matter which Playwright project's testMatch happens to sweep the
// file in.
test.use({ baseURL: 'http://localhost:5173' });

test('an admin switches a wall by hand and the kiosk follows within about a second (US1-3)', async ({
  page,
  browser,
}) => {
  test.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);

  const stamp = Date.now();
  const layoutAName = `E2E Wall Scene A ${stamp}`;
  const layoutBName = `E2E Wall Scene B ${stamp}`;
  const wallName = `E2E Rotating Wall ${stamp}`;
  const cameraAName = `E2E Wall Cam A ${stamp}`;
  const cameraBName = `E2E Wall Cam B ${stamp}`;

  await signInAsOperator(page);
  const cameraAIdentifier = await createPublishedLayout(page, layoutAName, cameraAName);
  const cameraBIdentifier = await createPublishedLayout(page, layoutBName, cameraBName);
  await createWall(page, wallName, [layoutAName, layoutBName]);

  await expect(showingText(page)).toContainText(layoutAName);

  // The ordinary kiosk (:5174), not the wall display (:5175) — and explicit,
  // because `browser.newContext()` does not inherit a project's `use.baseURL`.
  const kioskContext = await browser.newContext({ baseURL: 'http://localhost:5174' });
  const kiosk = await kioskContext.newPage();
  try {
    await signInToKiosk(kiosk);
    await kiosk.getByRole('listitem').filter({ hasText: wallName }).getByRole('button').click();

    await expect(kiosk.getByTestId('layout-grid')).toBeVisible();
    await expect(kiosk.getByTestId('layout-tile').first()).toHaveAttribute('data-camera-identifier', cameraAIdentifier);

    await page.getByRole('button', { name: /^next$/i }).click();
    await expect(showingText(page)).toContainText(layoutBName);

    // "Within about a second" — this is a generous end-to-end wait, not the
    // FR-V1 settle-time measurement (that is a Phase-5 concern, spec.md §7).
    await expect(kiosk.getByTestId('layout-tile').first()).toHaveAttribute('data-camera-identifier', cameraBIdentifier, {
      timeout: 5_000,
    });
  } finally {
    await kioskContext.close();
  }
});

test('a kiosk that missed a switch while its hub connection was down reconciles on reconnect (US1-17)', async ({
  page,
  browser,
}) => {
  test.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);

  const stamp = Date.now();
  const layoutAName = `E2E Reconcile Scene A ${stamp}`;
  const layoutBName = `E2E Reconcile Scene B ${stamp}`;
  const wallName = `E2E Reconcile Wall ${stamp}`;
  const cameraAName = `E2E Reconcile Cam A ${stamp}`;
  const cameraBName = `E2E Reconcile Cam B ${stamp}`;

  await signInAsOperator(page);
  const cameraAIdentifier = await createPublishedLayout(page, layoutAName, cameraAName);
  const cameraBIdentifier = await createPublishedLayout(page, layoutBName, cameraBName);
  await createWall(page, wallName, [layoutAName, layoutBName]);

  // Its own browser context: management-web must keep working while only the
  // kiosk's connection is severed (mirrors kiosk-reconciliation.spec.ts). The
  // ordinary kiosk (:5174), not the wall display (:5175) — and explicit,
  // because `browser.newContext()` does not inherit a project's `use.baseURL`.
  const kioskContext = await browser.newContext({ baseURL: 'http://localhost:5174' });
  const kiosk = await kioskContext.newPage();
  try {
    await signInToKiosk(kiosk);
    await kiosk.getByRole('listitem').filter({ hasText: wallName }).getByRole('button').click();
    await expect(kiosk.getByTestId('layout-grid')).toBeVisible();
    await expect(kiosk.getByTestId('layout-tile').first()).toHaveAttribute('data-camera-identifier', cameraAIdentifier);

    // Sever the kiosk's own connection — a real network drop, not an aborted
    // route (kiosk-reconciliation.spec.ts's own lesson: route interception
    // does nothing to an already-established WebSocket).
    await kioskContext.setOffline(true);
    await expect(kiosk.getByTestId('live-updates-degraded')).toBeVisible({ timeout: 60_000 });

    // The switch happens behind the kiosk's back.
    await page.getByRole('button', { name: /^next$/i }).click();
    await expect(showingText(page)).toContainText(layoutBName);

    // The kiosk must still be showing the old scene — it never received the
    // frame — until it reconnects and re-reads.
    await expect(kiosk.getByTestId('layout-tile').first()).toHaveAttribute('data-camera-identifier', cameraAIdentifier);

    await kioskContext.setOffline(false);
    await expect(kiosk.getByTestId('live-updates-degraded')).toBeHidden({ timeout: 45_000 });

    // Reconciled by re-reading GET /walls/{id} on reconnect (FR-008), not by
    // a page reload.
    await expect(kiosk.getByTestId('layout-tile').first()).toHaveAttribute('data-camera-identifier', cameraBIdentifier, {
      timeout: 45_000,
    });
  } finally {
    await kioskContext.close();
  }
});
