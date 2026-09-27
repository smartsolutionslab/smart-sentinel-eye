import { test, expect, type Page, type Locator } from '@playwright/test';
import { signInAsOperator } from './support/sign-in';
import { FIRST_WRITE_TEST_TIMEOUT_MS, FIRST_WRITE_TIMEOUT_MS } from './support/cold-stack';

// Spec 268 (issue #2336), tasks.md T007 / plan.md §5.4 — the real interaction-
// state matrix (ADR-0146 item 5) proved in a real browser: jsdom (the vitest
// suites) computes neither `:hover`/`:active` nor CSS custom properties, so
// this is the only place any of it is actually observed. Mirrors
// `camera-detail.spec.ts` for sign-in and camera seeding (ADR-0108).
//
// Phase-4a colour is RED (spec §6): every scenario below is expected to fail
// on unmodified `develop`, except the two explicitly marked PIN — those are
// already true today (Tailwind 4.3.3's own `@media (hover: hover)` wrapping,
// and the ADR-0151 focus/onClick contract) and are declared green in advance,
// not phase-4a evidence.
//
// Waits are by condition throughout (ADR-0150): `expect(...).toHaveCSS(...)`
// and `expect.poll(...)` retry until the assertion holds or the suite's own
// timeout elapses, never a fixed `page.waitForTimeout`.

/** Registers a camera and returns its name, mirrored from camera-detail.spec.ts. */
async function registerCamera(page: Page, label: string): Promise<string> {
  const name = `${label} ${Date.now()}`;

  await page.getByRole('button', { name: /register camera/i }).click();
  await page.locator('#register-camera-name').fill(name);
  await page.locator('#register-camera-url').fill('rtsp://10.0.5.99/stream');
  await page.getByRole('button', { name: /^register$/i }).click();

  await expect(page.getByRole('link', { name })).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

  return name;
}

/**
 * Paints a throwaway probe element with `background-color: var(--color-…)`
 * (or `color:` for a text role) and reads back the resolved value — never a
 * hard-coded `rgb()`, so a token change moves both sides of any comparison
 * (plan.md §5.4; "an assertion must not check its own input" the other way
 * round: the SUBJECT is the control's computed style, the REFERENCE is the
 * token probe).
 */
async function probeToken(
  page: Page,
  cssVariableName: string,
  cssProperty: 'backgroundColor' | 'color' | 'outlineColor',
): Promise<string> {
  return page.evaluate(
    ([variableName, property]) => {
      const probe = document.createElement('div');
      probe.style.position = 'fixed';
      probe.style.top = '-9999px';
      if (property === 'backgroundColor') {
        probe.style.background = `var(${variableName})`;
      } else if (property === 'outlineColor') {
        probe.style.outline = `2px solid var(${variableName})`;
      } else {
        probe.style.color = `var(${variableName})`;
      }
      document.body.appendChild(probe);
      const resolved = getComputedStyle(probe)[property];
      probe.remove();
      return resolved;
    },
    [cssVariableName, cssProperty] as const,
  );
}

/** WCAG relative luminance from a `getComputedStyle` `rgb(r, g, b)`/`rgba(...)` string. */
function relativeLuminance(rgb: string): number {
  const match = rgb.match(/rgba?\((\d+),\s*(\d+),\s*(\d+)/);
  if (!match) {
    throw new Error(`not an rgb()/rgba() colour: ${rgb}`);
  }
  function linearize(component: string | undefined): number {
    const c = Number(component ?? 0) / 255;
    return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  }
  const [, r, g, b] = match;
  return 0.2126 * linearize(r) + 0.7152 * linearize(g) + 0.0722 * linearize(b);
}

async function backgroundColorOf(locator: Locator): Promise<string> {
  return locator.evaluate((el) => getComputedStyle(el).backgroundColor);
}

async function pressAndReadBackgroundColor(page: Page, locator: Locator): Promise<string> {
  const box = await locator.boundingBox();
  if (box === null) {
    throw new Error('element has no bounding box to press');
  }
  const centerX = box.x + box.width / 2;
  const centerY = box.y + box.height / 2;
  await page.mouse.move(centerX, centerY);
  await page.mouse.down();
  try {
    return await backgroundColorOf(locator);
  } finally {
    // Release off the target, not on it: a mousedown/mouseup pair on the SAME
    // element fires a real `click`, which would activate the control (open or
    // close a dialog) here rather than at the caller's own, later, explicit
    // `.click()` — the "pressed" read above only wants the `:active` fill,
    // not the activation.
    await page.mouse.move(centerX, centerY - (box.height + 20));
    await page.mouse.up();
  }
}

test.describe('Button interaction states (US1)', () => {
  test('primary, secondary, ghost and danger each have three distinct fills at rest, hover and pressed', async ({
    page,
  }) => {
    test.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);
    await signInAsOperator(page);
    const primaryCameraName = await registerCamera(page, 'E2E States Primary');

    // primary: "Register camera" on the Cameras list.
    const primary = page.getByRole('button', { name: /register camera/i });
    const primaryRest = await backgroundColorOf(primary);
    await primary.hover();
    await expect.poll(() => backgroundColorOf(primary)).not.toBe(primaryRest);
    const primaryHover = await backgroundColorOf(primary);
    const primaryPressed = await pressAndReadBackgroundColor(page, primary);
    expect(new Set([primaryRest, primaryHover, primaryPressed]).size).toBe(3);
    await expect(primary).toHaveCSS('opacity', '1');

    // primary's rest fill is the accent, not the triad's --color-accent-active.
    const accentProbe = await probeToken(page, '--color-accent', 'backgroundColor');
    const accentActiveProbe = await probeToken(page, '--color-accent-active', 'backgroundColor');
    expect(primaryRest).toBe(accentProbe);
    expect(primaryRest).not.toBe(accentActiveProbe);

    // secondary: "Cancel" in the Register dialog.
    await primary.click();
    const secondary = page.getByRole('dialog').getByRole('button', { name: /cancel/i });
    const secondaryRest = await backgroundColorOf(secondary);
    await secondary.hover();
    await expect.poll(() => backgroundColorOf(secondary)).not.toBe(secondaryRest);
    const secondaryHover = await backgroundColorOf(secondary);
    const secondaryPressed = await pressAndReadBackgroundColor(page, secondary);
    expect(new Set([secondaryRest, secondaryHover, secondaryPressed]).size).toBe(3);
    await secondary.click();

    // ghost: "Cancel" in the "Correct the address" dialog, opened from a
    // camera's detail page.
    await page.getByRole('link', { name: primaryCameraName }).click();
    await page.getByRole('button', { name: /correct the address/i }).click();
    const ghost = page.getByRole('dialog').getByRole('button', { name: /cancel/i });
    const ghostRest = await backgroundColorOf(ghost);
    await ghost.hover();
    await expect.poll(() => backgroundColorOf(ghost)).not.toBe(ghostRest);
    const ghostHover = await backgroundColorOf(ghost);
    const ghostPressed = await pressAndReadBackgroundColor(page, ghost);
    expect(new Set([ghostRest, ghostHover, ghostPressed]).size).toBe(3);
    await ghost.click();

    // danger: "Retire camera" opens the confirm dialog; its own "Retire
    // camera" button is the danger Button. Hover is lighter than rest,
    // pressed is darker than rest (opposite directions, plan.md §2.1).
    await page.getByRole('button', { name: /retire camera/i }).click();
    const danger = page.getByRole('alertdialog').getByRole('button', { name: /retire camera/i });
    const dangerRest = await backgroundColorOf(danger);
    await danger.hover();
    await expect.poll(() => backgroundColorOf(danger)).not.toBe(dangerRest);
    const dangerHover = await backgroundColorOf(danger);
    const dangerPressed = await pressAndReadBackgroundColor(page, danger);
    expect(new Set([dangerRest, dangerHover, dangerPressed]).size).toBe(3);

    expect(relativeLuminance(dangerHover)).toBeGreaterThan(relativeLuminance(dangerRest));
    expect(relativeLuminance(dangerRest)).toBeGreaterThan(relativeLuminance(dangerPressed));

    await page.getByRole('button', { name: /cancel/i }).click();
  });

  /**
   * PIN — already true on `develop` (Tailwind 4.3.3 wraps every `hover:`
   * utility in `@media (hover: hover)`, spec §1). Not phase-4a evidence.
   */
  test('a touch tap does not latch a hover fill', async ({ browser }) => {
    test.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);
    const context = await browser.newContext({ hasTouch: true, isMobile: true, viewport: { width: 390, height: 844 } });
    try {
      const page = await context.newPage();
      await signInAsOperator(page);

      const primary = page.getByRole('button', { name: /register camera/i });
      const restColor = await backgroundColorOf(primary);

      await primary.tap();
      // Dismiss the dialog the tap opened, back on the list, same button.
      await page.getByRole('button', { name: /cancel/i }).click();

      await expect.poll(() => backgroundColorOf(primary)).toBe(restColor);
    } finally {
      await context.close();
    }
  });

  test('focus-visible draws a stated, offset outline and survives forced-colors mode', async ({ page }) => {
    await signInAsOperator(page);

    const primary = page.getByRole('button', { name: /register camera/i });
    // `.focus()` moves focus the way keyboard navigation would (Chromium
    // marks it focus-visible, since it was not a pointer interaction) —
    // simpler and no less real than a chain of `Tab` presses from an
    // unspecified starting element.
    await primary.focus();

    const focusRingProbe = await probeToken(page, '--color-focus-ring', 'outlineColor');
    await expect(primary).toHaveCSS('outline-style', 'solid');
    await expect(primary).toHaveCSS('outline-width', '2px');
    await expect(primary).toHaveCSS('outline-offset', '2px');
    await expect(primary).toHaveCSS('outline-color', focusRingProbe);
    await expect(primary).toHaveCSS('box-shadow', 'none');

    await page.emulateMedia({ forcedColors: 'active' });
    await expect(primary).toHaveCSS('outline-style', 'solid');
    await page.emulateMedia({ forcedColors: null });
  });

  test('disabled reads as unavailable: no fill, a subtle border, a legible label, unresponsive to hover', async ({
    page,
  }) => {
    await signInAsOperator(page);

    const primary = page.getByRole('button', { name: /register camera/i });
    await primary.evaluate((el) => el.setAttribute('disabled', ''));

    const fgDisabledProbe = await probeToken(page, '--color-fg-disabled', 'color');
    const borderSubtleProbe = await probeToken(page, '--color-border-subtle', 'backgroundColor');
    await expect(primary).toHaveCSS('background-color', 'rgba(0, 0, 0, 0)');
    await expect(primary).toHaveCSS('color', fgDisabledProbe);
    expect(await primary.evaluate((el) => getComputedStyle(el).borderColor)).toBe(borderSubtleProbe);

    const beforeHover = await backgroundColorOf(primary);
    await primary.hover({ force: true });
    expect(await backgroundColorOf(primary)).toBe(beforeHover);

    await primary.evaluate((el) => el.removeAttribute('disabled'));
  });

  /**
   * ADR-0151's pin, from a live Button rather than jsdom: an `unavailable`
   * control still holds focus and its `onClick` still fires.
   */
  test('an unavailable Button (ADR-0151) still reads unavailable, keeps focus and fires onClick', async ({ page }) => {
    await signInAsOperator(page);

    // `LayoutEditorDialog.tsx`'s `saveBlocked` for a NEW layout is
    // `isLoading || knownCameras.size === 0` — grid validity plays no part
    // (`isEdit` is false here). Earlier tests in this file, and other specs
    // against a persistent stack, may already have registered cameras, so
    // relying on "zero cameras exist" would race. Holding the camera-choices
    // GET open keeps `knownCameras` at its initial empty Map deterministically,
    // the same `page.route`-hold pattern the busy-state test above uses.
    let releaseCameras: (() => void) | undefined;
    const held = new Promise<void>((resolve) => {
      releaseCameras = resolve;
    });
    const cameraChoicesPath = (url: URL): boolean => /\/camera-catalog\/cameras$/i.test(url.pathname);
    await page.route(cameraChoicesPath, async (route) => {
      if (route.request().method() !== 'GET') {
        return route.fallback();
      }
      await held;
      return route.fallback();
    });

    try {
      await page.getByRole('link', { name: /^layouts$/i }).click();
      await page.getByRole('button', { name: /new layout/i }).click();

      const save = page.getByRole('button', { name: /save as draft/i });
      await expect(save).toHaveAttribute('aria-disabled', 'true');

      const fgDisabledProbe = await probeToken(page, '--color-fg-disabled', 'color');
      await expect(save).toHaveCSS('color', fgDisabledProbe);

      await save.focus();
      await expect.poll(() => page.evaluate(() => document.activeElement?.getAttribute('aria-disabled'))).toBe('true');

      // `onClick` still fires: an `aria-disabled` control is not natively
      // disabled, so the click event reaches its listeners — unlike a native
      // `disabled` button, which never dispatches one at all. A probe
      // attribute (rather than an `ElementHandle`) both records the click and
      // gives `document.activeElement` something unique to compare against.
      await save.evaluate((element) => {
        element.setAttribute('data-e2e-save-probe', 'unclicked');
        element.addEventListener('click', () => element.setAttribute('data-e2e-save-probe', 'clicked'));
      });
      // `force: true`: Playwright's own actionability check treats
      // `aria-disabled="true"` as "not enabled" and refuses to click,
      // retrying until the test times out — that is Playwright's synthetic
      // click being stricter than a real browser, which is exactly the gap
      // ADR-0151 relies on (a real click still reaches an `aria-disabled`
      // control). Forcing bypasses that check without bypassing anything the
      // control itself does.
      await save.click({ force: true });
      await expect(save).toHaveAttribute('data-e2e-save-probe', 'clicked');

      // Still holds focus after the click — a natively `disabled` button
      // blurs to `<body>` the instant it disables, and nothing here does.
      expect(await page.evaluate(() => document.activeElement?.getAttribute('data-e2e-save-probe'))).toBe('clicked');
    } finally {
      releaseCameras?.();
      await page.unroute(cameraChoicesPath);
    }
  });
});

test.describe('One focus indicator (US3)', () => {
  test('an Input, a DataTable sort header and a GridDesigner preset chip all draw focus in --color-focus-ring', async ({
    page,
  }) => {
    await signInAsOperator(page);
    const focusRingProbe = await probeToken(page, '--color-focus-ring', 'outlineColor');

    // An Input: the Register dialog's name field.
    await page.getByRole('button', { name: /register camera/i }).click();
    const input = page.locator('#register-camera-name');
    await input.focus();
    await expect(input).toHaveCSS('outline-style', 'solid');
    await expect(input).toHaveCSS('outline-color', focusRingProbe);
    await expect(input).toHaveCSS('box-shadow', 'none');
    await page.getByRole('button', { name: /cancel/i }).click();

    // A DataTable sort header button — the Cameras list's "Name" column.
    // The two real mouse clicks above (opening and cancelling the Register
    // dialog) left Chromium's input-modality tracker on "mouse", under which
    // a script-triggered `.focus()` on a button does not match
    // `:focus-visible` at all — a real browser heuristic, not a Button
    // defect. One keyboard event restores "keyboard" modality first.
    await page.keyboard.press('Tab');
    const sortHeader = page.getByRole('columnheader', { name: /name/i }).getByRole('button');
    await sortHeader.focus();
    await expect(sortHeader).toHaveCSS('outline-style', 'solid');
    await expect(sortHeader).toHaveCSS('outline-color', focusRingProbe);
    await expect(sortHeader).toHaveCSS('box-shadow', 'none');

    // A GridDesigner preset chip's wrapping <label> (has-[:focus-visible]).
    await page.getByRole('link', { name: /^layouts$/i }).click();
    await page.getByRole('button', { name: /new layout/i }).click();
    // Same mouse-modality reset as above: the two clicks just used would
    // otherwise leave the radio's `.focus()` without `:focus-visible`.
    await page.keyboard.press('Tab');
    const presetRadio = page.getByRole('radio', { name: '2×2' });
    const presetLabel = page.locator('label').filter({ has: presetRadio });
    await presetRadio.focus();
    await expect(presetLabel).toHaveCSS('outline-style', 'solid');
    await expect(presetLabel).toHaveCSS('outline-color', focusRingProbe);
    await expect(presetLabel).toHaveCSS('box-shadow', 'none');
  });

  test('a disabled Input is legible: fg-disabled text, no opacity applied', async ({ page }) => {
    await signInAsOperator(page);
    await page.getByRole('button', { name: /register camera/i }).click();

    const input = page.locator('#register-camera-name');
    await input.evaluate((el) => el.setAttribute('disabled', ''));

    const fgDisabledProbe = await probeToken(page, '--color-fg-disabled', 'color');
    await expect(input).toHaveCSS('color', fgDisabledProbe);
    await expect(input).toHaveCSS('opacity', '1');
  });
});

test.describe('Button busy state (US2)', () => {
  /**
   * Holds the rename request open with `page.route`, submits, and reads
   * `aria-busy`, the cursor and the fill while it is pending — released in
   * `finally` so a failing assertion never leaves the route hanging.
   */
  test('busy announces and keeps the rest fill while a rename is in flight', async ({ page }) => {
    test.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);
    await signInAsOperator(page);
    const original = await registerCamera(page, 'E2E States Busy');

    await page.getByRole('link', { name: original }).click();
    await expect(page.getByRole('heading', { name: original })).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });
    await page.getByRole('button', { name: /^rename$/i }).click();

    let releaseRename: (() => void) | undefined;
    const held = new Promise<void>((resolve) => {
      releaseRename = resolve;
    });

    const renamePath = (url: URL): boolean => /\/camera-catalog\/cameras\/[0-9a-f-]{36}$/i.test(url.pathname);
    await page.route(renamePath, async (route) => {
      if (route.request().method() !== 'PATCH') {
        return route.fallback();
      }
      await held;
      return route.fallback();
    });

    try {
      const field = page.locator('#rename-camera-name');
      await field.fill(`${original} corrected`);
      // Not `getByRole('button', { name: /^save$/i })`: the label swaps to
      // "Saving…" the instant the click lands, and a lazy role-by-name
      // locator re-resolves on every use, so it would stop matching anything
      // right when the assertions below need to find it.
      const submit = page.getByRole('dialog').locator('button[type="submit"]');
      await submit.click();

      await expect(submit).toHaveAttribute('aria-busy', 'true');
      await expect(submit).toHaveCSS('cursor', 'progress');
      const accentProbe = await probeToken(page, '--color-accent', 'backgroundColor');
      await expect(submit).toHaveCSS('background-color', accentProbe);
    } finally {
      releaseRename?.();
      await page.unroute(renamePath);
    }

    await expect(page.getByRole('heading', { name: `${original} corrected` })).toBeVisible({
      timeout: FIRST_WRITE_TIMEOUT_MS,
    });
  });
});
