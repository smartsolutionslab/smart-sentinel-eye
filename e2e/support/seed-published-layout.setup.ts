import { test as setup } from '@playwright/test';
import { signInAsOperator } from './sign-in';
import { createPublishedLayout } from './management-layouts';
import { FIRST_WRITE_TEST_TIMEOUT_MS } from './cold-stack';

/**
 * Spec 041 — a published layout for the kiosk to open.
 *
 * The kiosk cannot show a wall until one exists, and an e2e stack has none:
 * `ci.yml` boots the stack with `ScenarioSimulator=false`, so `camera-sim` and
 * `scenario-simulator` are not composed at all and CI starts on an empty
 * catalogue (#2013).
 *
 * This runs as its own Playwright project that the `kiosk` project depends on,
 * rather than leaning on `layouts.spec.ts` having happened to publish one
 * first. Depending on another spec's side effect is exactly the implicit
 * coupling that lets a broken kiosk look like a working one — which is the
 * defect this feature exists to correct.
 *
 * Drives management-web (`:5173`) rather than the API: publishing needs an
 * `If-Match` round-trip, and the UI path gets the contract right for free.
 */
setup('a published layout exists for the kiosk to open', async ({ page }) => {
  setup.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);

  await signInAsOperator(page);

  // Names are unique per fab, so a fixed name would collide on a second local
  // run against a surviving database. The kiosk opens whichever layout is
  // first — any published one proves the point — so the name is not shared.
  const stamp = Date.now();
  const cameraName = `Kiosk Seed Cam ${stamp}`;
  const layoutName = `Kiosk Seed Wall ${stamp}`;

  // A layout tile needs a camera; the dialog's submit stays disabled while the
  // catalogue is empty. It does NOT need an overlay — an unbound tile renders.
  //
  // No `openSection(page, 'Cameras')` here: sign-in already lands on Cameras
  // (spec.md's own comment above), and `createPublishedLayout` deliberately
  // does not navigate there itself (see that function's own doc comment).
  await createPublishedLayout(page, layoutName, cameraName);
});
