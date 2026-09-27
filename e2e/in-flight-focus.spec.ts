import { test, expect, type Page } from '@playwright/test';
import { signInAsOperator } from './support/sign-in';
import { FIRST_WRITE_TEST_TIMEOUT_MS, FIRST_WRITE_TIMEOUT_MS } from './support/cold-stack';

// Issue #2624 / ADR-0151 — spec 267. Eight in-flight-disable sites used native
// `disabled={isLoading}` / `disabled={pending}`, which drops keyboard focus to
// `<body>` the instant the control natively disables. jsdom cannot observe
// either half of the fix (the browser's disable-blur, or the real Enter-key
// implicit-submission path) — spec.md FR-006, mirroring
// `e2e/overlays.spec.ts:313-327`'s identical reasoning for spec 160.
//
// **Expected on `develop`**: every focus assertion below is red — a real
// browser blurs the natively-disabled control to `<body>`, and nothing
// restores it. Every `count() === 1` assertion is a green pin today (native
// `disabled` already suppresses the click/implicit-submission that would send
// a second request) — plan.md §6 records that its own discriminating power is
// proved by the T013 counterfactual once the guard exists, not by this file
// alone.

/**
 * Holds every matching request until `release()`, counting them at the route.
 *
 * Not shared: one caller (this file) does not earn a support module
 * (plan.md §6).
 */
async function holdWrites(
  page: Page,
  matches: (url: URL) => boolean,
  method: string,
): Promise<{ count: () => number; release: () => void }> {
  let count = 0;
  let release!: () => void;
  const gate = new Promise<void>((resolve) => (release = resolve));
  await page.route(matches, async (route) => {
    if (route.request().method() !== method) return route.fallback();
    count += 1;
    await gate;
    await route.continue();
  });
  return { count: () => count, release };
}

async function fillRuleForm(page: Page, name: string): Promise<void> {
  await page.locator('#rule-name').fill(name);
  await page.locator('#rule-source').fill('plc');
  await page.locator('#rule-kind').fill('PlcCycleStart');
  await page.locator('#rule-predicate').fill('$.payload.cycleTime <= 30');
  await page.locator('#rule-variable').fill('oeeLine1');
  await page.locator('#rule-value-expression').fill('100 - $.payload.cycleTime * 2');
}

test.describe('US1 — a keyboard operator submitting a dialog form keeps their place', () => {
  test('Register a camera: focus survives the in-flight window and implicit resubmission sends nothing more', async ({
    page,
  }) => {
    test.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);
    await signInAsOperator(page);

    await page.getByRole('button', { name: /register camera/i }).click();
    const name = `E2E Focus Register ${Date.now()}`;
    await page.locator('#register-camera-name').fill(name);
    const urlField = page.locator('#register-camera-url');
    await urlField.fill('rtsp://10.0.5.98/stream');

    const { count, release } = await holdWrites(
      page,
      (url) => url.pathname.endsWith('/camera-catalog/cameras'),
      'POST',
    );

    try {
      const submit = page.getByRole('button', { name: /^(register|registering…)$/i });
      await submit.focus();
      await expect(submit).toBeFocused();
      await page.keyboard.press('Enter');

      await expect.poll(() => count()).toBe(1);

      // The red assertion: a real browser blurs a natively-disabled Register
      // to <body> here, on unmodified `develop`.
      await expect(submit).toBeFocused();
      await expect(submit).toHaveAttribute('aria-disabled', 'true');

      // Implicit submission — Enter pressed in a text field, not on the button.
      await urlField.press('Enter');
    } finally {
      release();
    }

    // Settle point after the mutation's own response: the dialog closes and
    // the camera appears in the list.
    await expect(page.getByRole('link', { name })).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });
    expect(count()).toBe(1);
  });

  test('Correct the address: focus survives the in-flight window and implicit resubmission sends nothing more', async ({
    page,
  }) => {
    test.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);
    await signInAsOperator(page);

    await page.getByRole('button', { name: /register camera/i }).click();
    const name = `E2E Focus Address ${Date.now()}`;
    await page.locator('#register-camera-name').fill(name);
    await page.locator('#register-camera-url').fill('rtsp://10.0.5.98/stream');
    await page.getByRole('button', { name: /^register$/i }).click();
    await page.getByRole('link', { name }).click();
    await expect(page.getByRole('heading', { name })).toBeVisible();

    await page.getByRole('button', { name: /correct the address/i }).click();
    const urlField = page.locator('#edit-camera-url');
    await urlField.fill('rtsp://10.0.5.77/corrected');

    const { count, release } = await holdWrites(
      page,
      (url) => /\/camera-catalog\/cameras\/[^/]+$/.test(url.pathname),
      'PATCH',
    );

    try {
      const submit = page.getByRole('button', { name: /^(save|saving…)$/i });
      await submit.focus();
      await expect(submit).toBeFocused();
      await page.keyboard.press('Enter');

      await expect.poll(() => count()).toBe(1);

      await expect(submit).toBeFocused();
      await expect(submit).toHaveAttribute('aria-disabled', 'true');

      await urlField.press('Enter');
    } finally {
      release();
    }

    await expect(page.getByText('rtsp://10.0.5.77/corrected')).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });
    await expect(page.getByRole('alert')).toHaveCount(0);
    expect(count()).toBe(1);
  });

  test('Rename camera: focus survives the in-flight window and implicit resubmission sends nothing more', async ({
    page,
  }) => {
    test.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);
    await signInAsOperator(page);

    await page.getByRole('button', { name: /register camera/i }).click();
    const original = `E2E Focus Rename ${Date.now()}`;
    await page.locator('#register-camera-name').fill(original);
    await page.locator('#register-camera-url').fill('rtsp://10.0.5.98/stream');
    await page.getByRole('button', { name: /^register$/i }).click();
    await page.getByRole('link', { name: original }).click();
    await expect(page.getByRole('heading', { name: original })).toBeVisible();

    await page.getByRole('button', { name: /^rename$/i }).click();
    const nameField = page.locator('#rename-camera-name');
    const corrected = `${original} corrected`;
    await nameField.fill(corrected);

    const { count, release } = await holdWrites(
      page,
      (url) => /\/camera-catalog\/cameras\/[^/]+$/.test(url.pathname),
      'PATCH',
    );

    try {
      const submit = page.getByRole('button', { name: /^(save|saving…)$/i });
      await submit.focus();
      await expect(submit).toBeFocused();
      await page.keyboard.press('Enter');

      await expect.poll(() => count()).toBe(1);

      await expect(submit).toBeFocused();
      await expect(submit).toHaveAttribute('aria-disabled', 'true');

      await nameField.press('Enter');
    } finally {
      release();
    }

    await expect(page.getByRole('heading', { name: corrected })).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });
    await expect(page.getByRole('alert')).toHaveCount(0);
    expect(count()).toBe(1);
  });

  test('New rule: focus survives the in-flight window and implicit resubmission sends nothing more', async ({
    page,
  }) => {
    test.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);
    await signInAsOperator(page);

    await page.getByRole('link', { name: /^rules$/i }).click();
    await expect(page.getByRole('heading', { name: 'Rules', exact: true })).toBeVisible();

    await page.getByRole('button', { name: /new rule/i }).click();
    const name = `e2e-focus-rule-${Date.now()}`;
    await fillRuleForm(page, name);

    const { count, release } = await holdWrites(page, (url) => url.pathname.endsWith('/automation/rules'), 'POST');

    try {
      const submit = page.getByRole('button', { name: /^(create draft|creating…)$/i });
      await submit.focus();
      await expect(submit).toBeFocused();
      await page.keyboard.press('Enter');

      await expect.poll(() => count()).toBe(1);

      await expect(submit).toBeFocused();
      await expect(submit).toHaveAttribute('aria-disabled', 'true');

      await page.locator('#rule-predicate').press('Enter');
    } finally {
      release();
    }

    await expect(page.getByRole('row').filter({ hasText: name })).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });
    expect(count()).toBe(1);
  });

  test('New variable: focus survives the in-flight window and implicit resubmission sends nothing more', async ({
    page,
  }) => {
    test.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);
    await signInAsOperator(page);

    await page.getByRole('link', { name: /^system variables$/i }).click();
    await expect(page.getByRole('heading', { name: 'System variables', exact: true })).toBeVisible();

    await page.getByRole('button', { name: /new variable/i }).click();
    const name = `E2E_Focus_Variable_${Date.now()}`;
    const nameField = page.locator('#variable-name');
    await nameField.fill(name);

    const { count, release } = await holdWrites(
      page,
      (url) => url.pathname.endsWith('/system-variables/system-variables'),
      'POST',
    );

    try {
      const submit = page.getByRole('button', { name: /^(define|saving…)$/i });
      await submit.focus();
      await expect(submit).toBeFocused();
      await page.keyboard.press('Enter');

      await expect.poll(() => count()).toBe(1);

      await expect(submit).toBeFocused();
      await expect(submit).toHaveAttribute('aria-disabled', 'true');

      await nameField.press('Enter');
    } finally {
      release();
    }

    await expect(page.getByText(name)).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });
    expect(count()).toBe(1);
  });
});

test.describe('US2 — a keyboard operator running a dry run keeps their place', () => {
  test('Dry run: focus survives the in-flight window and a second activation sends nothing more', async ({ page }) => {
    test.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);
    await signInAsOperator(page);

    await page.getByRole('link', { name: /^rules$/i }).click();
    await expect(page.getByRole('heading', { name: 'Rules', exact: true })).toBeVisible();

    await page.getByRole('button', { name: /new rule/i }).click();
    const name = `e2e-focus-dry-run-${Date.now()}`;
    await fillRuleForm(page, name);
    await page.getByRole('button', { name: /^create draft$/i }).click();
    const row = page.getByRole('row').filter({ hasText: name });
    await expect(row).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

    await row.getByRole('button', { name: /^dry run$/i }).click();
    const panel = page.getByTestId('dry-run-panel');
    await expect(panel).toBeVisible();

    const { count, release } = await holdWrites(
      page,
      (url) => /\/automation\/rules\/[^/]+\/dry-run$/.test(url.pathname),
      'POST',
    );

    try {
      const run = panel.getByRole('button', { name: /^(run|running…)$/i });
      await run.focus();
      await expect(run).toBeFocused();
      await page.keyboard.press('Enter');

      await expect.poll(() => count()).toBe(1);

      // The red assertion.
      await expect(run).toBeFocused();
      await expect(run).toHaveAttribute('aria-disabled', 'true');

      // A second activation while in flight, focus still on Run.
      await page.keyboard.press('Enter');
    } finally {
      release();
    }

    await expect(panel.getByTestId('dry-run-result')).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });
    expect(count()).toBe(1);
  });
});

test.describe('US3 — a keyboard operator confirming a destructive action keeps their place', () => {
  test('Retire camera (confirm): focus survives the in-flight window and a second confirmation sends nothing more', async ({
    page,
  }) => {
    test.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);
    await signInAsOperator(page);

    await page.getByRole('button', { name: /register camera/i }).click();
    const name = `E2E Focus Retire Confirm ${Date.now()}`;
    await page.locator('#register-camera-name').fill(name);
    await page.locator('#register-camera-url').fill('rtsp://10.0.5.98/stream');
    await page.getByRole('button', { name: /^register$/i }).click();
    await page.getByRole('link', { name }).click();
    await expect(page.getByRole('heading', { name })).toBeVisible();

    await page.getByRole('button', { name: /retire camera/i }).click();
    const confirmation = page.getByRole('alertdialog');
    await expect(confirmation).toBeVisible();

    const { count, release } = await holdWrites(
      page,
      (url) => /\/camera-catalog\/cameras\/[^/]+\/retire$/.test(url.pathname),
      'POST',
    );

    try {
      const confirmButton = confirmation.getByRole('button', { name: /^retire camera$/i });
      await confirmButton.focus();
      await expect(confirmButton).toBeFocused();
      await page.keyboard.press('Enter');

      await expect.poll(() => count()).toBe(1);

      // The red assertion.
      await expect(confirmButton).toBeFocused();
      await expect(confirmButton).toHaveAttribute('aria-disabled', 'true');

      // A second activation while in flight, focus still on confirm.
      await page.keyboard.press('Enter');
    } finally {
      release();
    }

    await expect(page.getByRole('status')).toContainText(/retired/i, { timeout: FIRST_WRITE_TIMEOUT_MS });
    expect(count()).toBe(1);
  });

  test('Retire camera (Cancel): focus stays on Cancel and it refuses to close while a confirm is in flight', async ({
    page,
  }) => {
    test.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);
    await signInAsOperator(page);

    await page.getByRole('button', { name: /register camera/i }).click();
    const name = `E2E Focus Retire Cancel ${Date.now()}`;
    await page.locator('#register-camera-name').fill(name);
    await page.locator('#register-camera-url').fill('rtsp://10.0.5.98/stream');
    await page.getByRole('button', { name: /^register$/i }).click();
    await page.getByRole('link', { name }).click();
    await expect(page.getByRole('heading', { name })).toBeVisible();

    await page.getByRole('button', { name: /retire camera/i }).click();
    const confirmation = page.getByRole('alertdialog');
    await expect(confirmation).toBeVisible();

    const cancelButton = confirmation.getByRole('button', { name: /^cancel$/i });
    const confirmButton = confirmation.getByRole('button', { name: /^retire camera$/i });

    // Radix's own initial focus (FR-004) — not something this test drives.
    await expect(cancelButton).toBeFocused();

    const { count, release } = await holdWrites(
      page,
      (url) => /\/camera-catalog\/cameras\/[^/]+\/retire$/.test(url.pathname),
      'POST',
    );

    try {
      // Activates without moving focus — the WebKit pointer-activation path
      // (plan.md §6): the mechanism under test does not care how confirm was
      // activated, only that focus never moved off Cancel.
      await confirmButton.dispatchEvent('click');

      await expect.poll(() => count()).toBe(1);

      // The red assertion: Cancel, not confirm, is what must still hold focus.
      await expect(cancelButton).toBeFocused();
      await expect(cancelButton).toHaveAttribute('aria-disabled', 'true');

      await page.keyboard.press('Enter');
      await expect(page.getByRole('alertdialog')).toBeVisible();
    } finally {
      release();
    }

    // The held confirm request is released and succeeds; the dialog closes.
    await expect(page.getByRole('alertdialog')).toHaveCount(0, { timeout: FIRST_WRITE_TIMEOUT_MS });
  });
});

// Issue #2632 / ADR-0151 — spec 273. Ten more native-`disabled={...}` sites
// across seven files lose focus the same way #2624's did: SystemVariablesPage
// (S1 Set value, S2 Archive), LayoutsPage (L1 Publish, L3 More actions ->
// Revert), OverlaysPage (O1 Publish, O5 Archive), CamerasPage (C2 Next),
// AuditPage (A1 Next, terminal), WallDetailPage (W2 Show) and WallForm (W3
// Save). One test per **mechanism** per page (plan.md §4.2), not per control —
// L2/O2/O3/O4/W1 share a mechanism already proved by the test standing for it
// here.
//
// **Expected on `develop`**: every focus assertion below is red, for the same
// reason as the file header above — a real browser blurs the natively-disabled
// control to `<body>`, and nothing restores it. Every `count() === 1` /
// state-unchanged assertion is a green pin today; T014's counterfactuals (not
// this file) prove their discriminating power once the guard exists.

test.describe('US1 — an operator setting a system variable value keeps their place (spec 273 S1)', () => {
  test('System variables: Set value keeps focus through the in-flight window', async ({ page }) => {
    test.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);
    await signInAsOperator(page);

    await page.getByRole('link', { name: /^system variables$/i }).click();
    await expect(page.getByRole('heading', { name: 'System variables', exact: true })).toBeVisible();

    const name = `E2E_Focus_SetValue_${Date.now()}`;
    await page.getByRole('button', { name: /new variable/i }).click();
    await page.locator('#variable-name').fill(name);
    await page.getByRole('button', { name: /^define$/i }).click();

    const row = page.locator('li').filter({ hasText: name });
    await expect(row).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });
    await row.getByPlaceholder('New value').fill('E2E focus value');

    const { count, release } = await holdWrites(
      page,
      (url) => /\/system-variables\/system-variables\/[^/]+\/value$/.test(url.pathname),
      'PUT',
    );

    try {
      const setValue = row.getByRole('button', { name: /^set value$/i });
      await setValue.focus();
      await expect(setValue).toBeFocused();
      await page.keyboard.press('Enter');

      await expect.poll(() => count()).toBe(1);

      // The red assertion: a real browser blurs a natively-disabled Set value
      // to <body> here, on unmodified `develop`.
      await expect(setValue).toBeFocused();
      await expect(setValue).toHaveAttribute('aria-disabled', 'true');

      // A second activation while unavailable sends nothing more.
      await page.keyboard.press('Enter');
    } finally {
      release();
    }

    await expect(row.getByText('E2E focus value')).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });
    expect(count()).toBe(1);
  });
});

test.describe('US1 — an operator archiving a system variable keeps their place (spec 273 S2)', () => {
  test('System variables: Archive returns focus to the opener through the in-flight window', async ({ page }) => {
    test.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);
    await signInAsOperator(page);

    await page.getByRole('link', { name: /^system variables$/i }).click();
    await expect(page.getByRole('heading', { name: 'System variables', exact: true })).toBeVisible();

    const name = `E2E_Focus_Archive_${Date.now()}`;
    await page.getByRole('button', { name: /new variable/i }).click();
    await page.locator('#variable-name').fill(name);
    await page.getByRole('button', { name: /^define$/i }).click();

    const row = page.locator('li').filter({ hasText: name });
    await expect(row).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

    const archiveButton = row.getByRole('button', { name: /^archive$/i });
    await archiveButton.click();
    const confirmation = page.getByRole('alertdialog');
    await expect(confirmation).toBeVisible();

    const { count, release } = await holdWrites(
      page,
      (url) => /\/system-variables\/system-variables\/[^/]+\/archive$/.test(url.pathname),
      'POST',
    );

    try {
      await confirmation.getByRole('button', { name: /^archive$/i }).click();

      await expect.poll(() => count()).toBe(1);

      // The red assertion: the opener, not the closed dialog, must hold focus.
      await expect(archiveButton).toBeFocused();
      await expect(archiveButton).toHaveAttribute('aria-disabled', 'true');

      // Activating it again while unavailable opens no second confirmation.
      await page.keyboard.press('Enter');
      await expect(page.getByRole('alertdialog')).toHaveCount(0);
    } finally {
      release();
    }

    expect(count()).toBe(1);
  });
});

test.describe('US2 — an operator publishing a layout keeps their place (spec 273 L1)', () => {
  test('Layouts: Publish keeps focus through the in-flight window', async ({ page }) => {
    test.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);
    await signInAsOperator(page);

    const cameraName = `E2E Focus Publish Cam ${Date.now()}`;
    await page.getByRole('button', { name: /register camera/i }).click();
    await page.locator('#register-camera-name').fill(cameraName);
    await page.locator('#register-camera-url').fill('rtsp://10.0.5.90/stream');
    await page.getByRole('button', { name: /^register$/i }).click();
    await expect(page.getByRole('cell', { name: cameraName })).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

    await page.getByRole('link', { name: /^layouts$/i }).click();
    await expect(page.getByRole('heading', { name: 'Layouts', exact: true })).toBeVisible();

    const layoutName = `E2E Focus Publish Layout ${Date.now()}`;
    await page.getByRole('button', { name: /new layout/i }).click();
    await page.locator('#layout-name').fill(layoutName);
    await page.locator('#tile-0-camera').selectOption({ label: cameraName });
    await page.getByRole('button', { name: /save as draft/i }).click();
    await expect(page.getByRole('heading', { name: layoutName })).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

    const row = page.getByRole('listitem').filter({ hasText: layoutName });

    const { count, release } = await holdWrites(
      page,
      (url) => /\/layout-composition\/layouts\/[^/]+\/revisions\/\d+\/publish$/.test(url.pathname),
      'POST',
    );

    try {
      const publish = row.getByRole('button', { name: /^publish$/i });
      await publish.focus();
      await expect(publish).toBeFocused();
      await page.keyboard.press('Enter');

      await expect.poll(() => count()).toBe(1);

      await expect(publish).toBeFocused();
      await expect(publish).toHaveAttribute('aria-disabled', 'true');

      await page.keyboard.press('Enter');
    } finally {
      release();
    }

    await expect(row.getByText(/Published/)).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });
    expect(count()).toBe(1);
  });
});

test.describe('US2 — an operator reverting a layout through More actions keeps their place (spec 273 L3)', () => {
  test('Layouts: More actions -> Revert keeps focus on the trigger through the in-flight window', async ({ page }) => {
    test.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);
    await signInAsOperator(page);

    const cameraName = `E2E Focus Revert Cam ${Date.now()}`;
    await page.getByRole('button', { name: /register camera/i }).click();
    await page.locator('#register-camera-name').fill(cameraName);
    await page.locator('#register-camera-url').fill('rtsp://10.0.5.91/stream');
    await page.getByRole('button', { name: /^register$/i }).click();
    await expect(page.getByRole('cell', { name: cameraName })).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

    await page.getByRole('link', { name: /^layouts$/i }).click();
    await expect(page.getByRole('heading', { name: 'Layouts', exact: true })).toBeVisible();

    const layoutName = `E2E Focus Revert Layout ${Date.now()}`;
    await page.getByRole('button', { name: /new layout/i }).click();
    await page.locator('#layout-name').fill(layoutName);
    await page.locator('#tile-0-camera').selectOption({ label: cameraName });
    await page.getByRole('button', { name: /save as draft/i }).click();
    await expect(page.getByRole('heading', { name: layoutName })).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

    const row = page.getByRole('listitem').filter({ hasText: layoutName });
    await row.getByRole('button', { name: /^publish$/i }).click();
    await expect(row.getByText(/Published/)).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

    const { count, release } = await holdWrites(
      page,
      (url) => /\/layout-composition\/layouts\/[^/]+\/revisions\/\d+\/revert$/.test(url.pathname),
      'POST',
    );

    try {
      const trigger = row.getByRole('button', { name: /more actions/i });
      await trigger.click();
      const menu = page.getByRole('menu');
      await expect(menu).toBeVisible();
      await menu.getByRole('menuitem', { name: /^revert$/i }).click();

      await expect.poll(() => count()).toBe(1);

      // The red assertion: the trigger, not the menu, must hold focus.
      await expect(trigger).toBeFocused();
      await expect(trigger).toHaveAttribute('aria-disabled', 'true');

      // Reopening while unavailable: the trigger still opens (plan.md §3.3 /
      // J2), but every entry already refuses — selecting one sends nothing
      // more. Enter, not .click(), because Playwright will not click an
      // aria-disabled element.
      await page.keyboard.press('Enter');
      const reopened = page.getByRole('menu');
      await expect(reopened).toBeVisible();
      await reopened.getByRole('menuitem', { name: /^revert$/i }).click({ force: true });
    } finally {
      release();
    }

    expect(count()).toBe(1);
  });
});

test.describe('US3 — an operator publishing an overlay keeps their place (spec 273 O1)', () => {
  test('Overlays: Publish keeps focus through the in-flight window', async ({ page }) => {
    test.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);
    await signInAsOperator(page);

    await page.getByRole('link', { name: /^overlays$/i }).click();
    await expect(page.getByRole('heading', { name: 'Overlays', exact: true })).toBeVisible();

    const overlayName = `E2E Focus Publish Overlay ${Date.now()}`;
    await page.getByRole('button', { name: /new overlay/i }).click();
    await page.locator('#overlay-name').fill(overlayName);
    await page.getByRole('button', { name: /save as draft/i }).click();
    await expect(page.getByText(overlayName)).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

    const row = page.getByRole('listitem').filter({ hasText: overlayName });

    const { count, release } = await holdWrites(
      page,
      (url) => /\/overlay-designer\/overlays\/[^/]+\/revisions\/\d+\/publish$/.test(url.pathname),
      'POST',
    );

    try {
      const publish = row.getByRole('button', { name: /^publish$/i });
      await publish.focus();
      await expect(publish).toBeFocused();
      await page.keyboard.press('Enter');

      await expect.poll(() => count()).toBe(1);

      await expect(publish).toBeFocused();
      await expect(publish).toHaveAttribute('aria-disabled', 'true');

      await page.keyboard.press('Enter');
    } finally {
      release();
    }

    await expect(row.getByText(/Published/)).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });
    expect(count()).toBe(1);
  });
});

test.describe('US3 — an operator archiving an overlay keeps their place (spec 273 O5)', () => {
  test('Overlays: Archive returns focus to the opener through the in-flight window', async ({ page }) => {
    test.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);
    await signInAsOperator(page);

    await page.getByRole('link', { name: /^overlays$/i }).click();
    await expect(page.getByRole('heading', { name: 'Overlays', exact: true })).toBeVisible();

    const overlayName = `E2E Focus Archive Overlay ${Date.now()}`;
    await page.getByRole('button', { name: /new overlay/i }).click();
    await page.locator('#overlay-name').fill(overlayName);
    await page.getByRole('button', { name: /save as draft/i }).click();
    await expect(page.getByText(overlayName)).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

    const row = page.getByRole('listitem').filter({ hasText: overlayName });
    await row.getByRole('button', { name: /^publish$/i }).click();
    await expect(row.getByText(/Published/)).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

    const archiveButton = row.getByRole('button', { name: /^archive$/i });
    await archiveButton.click();
    const confirmation = page.getByRole('alertdialog');
    await expect(confirmation).toBeVisible();

    const { count, release } = await holdWrites(
      page,
      (url) => /\/overlay-designer\/overlays\/[^/]+\/revisions\/\d+\/archive$/.test(url.pathname),
      'POST',
    );

    try {
      await confirmation.getByRole('button', { name: /^archive$/i }).click();

      await expect.poll(() => count()).toBe(1);

      await expect(archiveButton).toBeFocused();
      await expect(archiveButton).toHaveAttribute('aria-disabled', 'true');

      await page.keyboard.press('Enter');
      await expect(page.getByRole('alertdialog')).toHaveCount(0);
    } finally {
      release();
    }

    expect(count()).toBe(1);
  });
});

test.describe('US4 — an operator paging through cameras keeps their place (spec 273 C2)', () => {
  test('Cameras: Next stays focused while the next page loads', async ({ page }) => {
    test.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);

    // Fulfilled rather than seeded: a fresh CI stack cannot be relied on for
    // more than 50 cameras (plan.md §4.2), and this test is about the
    // in-flight window, not the real catalogue's contents.
    function cameraPage(offset: number, name: string) {
      return {
        items: [
          {
            cameraIdentifier: `e2e-focus-camera-${offset}`,
            version: 1,
            fab: 'munich',
            name,
            rtspUrl: 'rtsp://10.0.5.1/stream',
            registeredAt: '2026-05-24T10:00:00Z',
            status: 'Registered',
          },
        ],
        count: 60,
        offset,
        limit: 50,
      };
    }

    let release!: () => void;
    const gate = new Promise<void>((resolve) => (release = resolve));
    // Counted at the route, not inferred from the page-2 marker text alone:
    // without this, a second activation's request would still eventually
    // resolve to the same page-2 payload, and the test could not tell a
    // present guard from an absent one (mirroring `holdWrites` above).
    let secondPageRequests = 0;

    // Registered BEFORE sign-in: Cameras is the landing page, so its very
    // first GET must already be caught by this route.
    await page.route(
      (url) => url.pathname.endsWith('/camera-catalog/cameras'),
      async (route) => {
        if (route.request().method() !== 'GET') return route.fallback();
        const offset = Number(new URL(route.request().url()).searchParams.get('offset') ?? '0');
        if (offset === 0) {
          return route.fulfill({ json: cameraPage(0, 'E2E Focus Page1 Cam') });
        }
        secondPageRequests += 1;
        await gate;
        return route.fulfill({ json: cameraPage(50, 'E2E Focus Page2 Cam') });
      },
    );

    await signInAsOperator(page);
    await expect(page.getByText('E2E Focus Page1 Cam')).toBeVisible();

    const next = page.getByRole('button', { name: /^next$/i });
    await next.focus();
    await expect(next).toBeFocused();
    await page.keyboard.press('Enter');

    await expect.poll(() => secondPageRequests).toBe(1);

    // The red assertion.
    await expect(next).toBeFocused();
    await expect(next).toHaveAttribute('aria-disabled', 'true');

    // A second activation while in flight — the request count is what
    // discriminates it (T014), same as every other direct control in this file.
    await page.keyboard.press('Enter');

    release();
    await expect(page.getByText('E2E Focus Page2 Cam')).toBeVisible();
    expect(secondPageRequests).toBe(1);
  });
});

test.describe('US5 — an operator paging through audit keeps their place (spec 273 A1)', () => {
  test('Audit: Next to the last page keeps focus and does not reset to the first page', async ({ page }) => {
    function auditRow(auditIdentifier: string, eventKind: string) {
      return {
        auditIdentifier,
        occurredAt: '2026-05-30T10:00:00Z',
        receivedAt: '2026-05-30T10:00:00Z',
        fab: 'munich',
        eventKind,
        resourceKind: 'camera',
        resourceIdentifier: '33333333-3333-3333-3333-333333333333',
        actorIdentifier: '22222222-2222-2222-2222-222222222222',
        actorIsSystem: false,
        actorUsername: 'operator',
        eventIdentifier: auditIdentifier,
        payload: '{}',
        payloadSizeBytes: 2,
        schemaVersion: 1,
      };
    }

    let release!: () => void;
    const gate = new Promise<void>((resolve) => (release = resolve));

    await signInAsOperator(page);

    // Fulfilled, not seeded (plan.md §4.2): a fresh CI stack cannot be relied
    // on for more than 50 rows, so the "last page" is fabricated here rather
    // than paged to for real.
    await page.route(
      (url) => url.pathname.endsWith('/audit-observability/audit'),
      async (route) => {
        const cursor = new URL(route.request().url()).searchParams.get('cursor');
        if (cursor === null) {
          return route.fulfill({
            json: { rows: [auditRow('e2e-focus-first', 'E2EFocusFirstPageV1')], nextCursor: 'e2e-focus-cursor' },
          });
        }
        await gate;
        return route.fulfill({
          json: { rows: [auditRow('e2e-focus-last', 'E2EFocusLastPageV1')], nextCursor: null },
        });
      },
    );

    await page.getByRole('link', { name: /^audit$/i }).click();
    await expect(page.getByRole('heading', { name: 'Audit', exact: true })).toBeVisible();
    await expect(page.getByText('E2EFocusFirstPageV1')).toBeVisible();

    const next = page.getByRole('button', { name: /^next$/i });
    await next.focus();
    await expect(next).toBeFocused();
    await page.keyboard.press('Enter');

    // The red assertion: a real browser blurs a natively-disabled Next to
    // <body> here, on unmodified `develop`.
    await expect(next).toBeFocused();
    await expect(next).toHaveAttribute('aria-disabled', 'true');

    release();
    await expect(page.getByText('E2EFocusLastPageV1')).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

    // Terminal: nextCursor is now null, so Next stays unavailable.
    await expect(next).toBeFocused();
    await expect(next).toHaveAttribute('aria-disabled', 'true');

    // The real consequence the guard exists for (spec.md §3): an unguarded
    // activation here sets `cursor: undefined`, which is the FIRST page.
    await page.keyboard.press('Enter');
    await expect(page.getByText('E2EFocusLastPageV1')).toBeVisible();
    await expect(page.getByText('E2EFocusFirstPageV1')).toHaveCount(0);
  });
});

test.describe('US6 — an operator switching a wall scene keeps their place (spec 273 W2)', () => {
  test('Walls: Show keeps focus through the in-flight window', async ({ page }) => {
    test.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);
    await signInAsOperator(page);

    const stamp = Date.now();
    const cameraAName = `E2E Focus Wall Cam A ${stamp}`;
    const cameraBName = `E2E Focus Wall Cam B ${stamp}`;
    const layoutAName = `E2E Focus Wall Scene A ${stamp}`;
    const layoutBName = `E2E Focus Wall Scene B ${stamp}`;
    const wallName = `E2E Focus Wall ${stamp}`;

    async function publishSimpleLayout(cameraName: string, layoutName: string) {
      await page.getByRole('link', { name: /^cameras$/i }).click();
      await expect(page.getByRole('heading', { name: 'Cameras', exact: true })).toBeVisible();
      await page.getByRole('button', { name: /register camera/i }).click();
      await page.locator('#register-camera-name').fill(cameraName);
      await page.locator('#register-camera-url').fill(`rtsp://10.0.5.${Math.floor(Math.random() * 200) + 2}/stream`);
      await page.getByRole('button', { name: /^register$/i }).click();
      await expect(page.getByRole('cell', { name: cameraName })).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

      await page.getByRole('link', { name: /^layouts$/i }).click();
      await expect(page.getByRole('heading', { name: 'Layouts', exact: true })).toBeVisible();
      await page.getByRole('button', { name: /new layout/i }).click();
      await page.locator('#layout-name').fill(layoutName);
      await page.locator('#tile-0-camera').selectOption({ label: cameraName });
      await page.getByRole('button', { name: /save as draft/i }).click();
      await expect(page.getByRole('heading', { name: layoutName })).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

      const row = page.getByRole('listitem').filter({ hasText: layoutName });
      await row.getByRole('button', { name: /^publish$/i }).click();
      await expect(row.getByText(/Published/)).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });
    }

    await publishSimpleLayout(cameraAName, layoutAName);
    await publishSimpleLayout(cameraBName, layoutBName);

    await page.getByRole('link', { name: /^walls$/i }).click();
    await expect(page.getByRole('heading', { name: 'Walls', exact: true })).toBeVisible();
    await page.getByRole('button', { name: /new wall/i }).click();
    await page.locator('#wall-name').fill(wallName);
    await page.getByRole('checkbox', { name: layoutAName }).check();
    await page.getByRole('checkbox', { name: layoutBName }).check();
    await page.getByRole('button', { name: /^save$/i }).click();
    await expect(page.getByRole('heading', { name: wallName })).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

    const { count, release } = await holdWrites(
      page,
      (url) => /\/layout-composition\/walls\/[^/]+\/switch$/.test(url.pathname),
      'POST',
    );

    try {
      const show = page
        .getByRole('listitem')
        .filter({ hasText: layoutBName })
        .getByRole('button', { name: /^show$/i });
      await show.focus();
      await expect(show).toBeFocused();
      await page.keyboard.press('Enter');

      await expect.poll(() => count()).toBe(1);

      await expect(show).toBeFocused();
      await expect(show).toHaveAttribute('aria-disabled', 'true');

      await page.keyboard.press('Enter');
    } finally {
      release();
    }

    await expect(page.getByTestId('wall-showing')).toContainText(layoutBName, { timeout: FIRST_WRITE_TIMEOUT_MS });
    expect(count()).toBe(1);
  });
});

test.describe('US7 — an operator creating a wall keeps their place (spec 273 W3)', () => {
  test('New wall: Save survives the in-flight window and implicit resubmission sends nothing more', async ({
    page,
  }) => {
    test.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);
    await signInAsOperator(page);

    const stamp = Date.now();
    const cameraAName = `E2E Focus NewWall Cam A ${stamp}`;
    const cameraBName = `E2E Focus NewWall Cam B ${stamp}`;
    const layoutAName = `E2E Focus NewWall Scene A ${stamp}`;
    const layoutBName = `E2E Focus NewWall Scene B ${stamp}`;
    const wallName = `E2E Focus New Wall ${stamp}`;

    async function publishSimpleLayout(cameraName: string, layoutName: string) {
      await page.getByRole('link', { name: /^cameras$/i }).click();
      await expect(page.getByRole('heading', { name: 'Cameras', exact: true })).toBeVisible();
      await page.getByRole('button', { name: /register camera/i }).click();
      await page.locator('#register-camera-name').fill(cameraName);
      await page.locator('#register-camera-url').fill(`rtsp://10.0.5.${Math.floor(Math.random() * 200) + 2}/stream`);
      await page.getByRole('button', { name: /^register$/i }).click();
      await expect(page.getByRole('cell', { name: cameraName })).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

      await page.getByRole('link', { name: /^layouts$/i }).click();
      await expect(page.getByRole('heading', { name: 'Layouts', exact: true })).toBeVisible();
      await page.getByRole('button', { name: /new layout/i }).click();
      await page.locator('#layout-name').fill(layoutName);
      await page.locator('#tile-0-camera').selectOption({ label: cameraName });
      await page.getByRole('button', { name: /save as draft/i }).click();
      await expect(page.getByRole('heading', { name: layoutName })).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

      const row = page.getByRole('listitem').filter({ hasText: layoutName });
      await row.getByRole('button', { name: /^publish$/i }).click();
      await expect(row.getByText(/Published/)).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });
    }

    await publishSimpleLayout(cameraAName, layoutAName);
    await publishSimpleLayout(cameraBName, layoutBName);

    await page.getByRole('link', { name: /^walls$/i }).click();
    await expect(page.getByRole('heading', { name: 'Walls', exact: true })).toBeVisible();
    await page.getByRole('button', { name: /new wall/i }).click();

    const nameField = page.locator('#wall-name');
    await nameField.fill(wallName);
    await page.getByRole('checkbox', { name: layoutAName }).check();
    await page.getByRole('checkbox', { name: layoutBName }).check();

    const { count, release } = await holdWrites(
      page,
      (url) => url.pathname.endsWith('/layout-composition/walls'),
      'POST',
    );

    try {
      const save = page.getByRole('button', { name: /^(save|saving…)$/i });
      await save.focus();
      await expect(save).toBeFocused();
      await page.keyboard.press('Enter');

      await expect.poll(() => count()).toBe(1);

      // The red assertion.
      await expect(save).toBeFocused();
      await expect(save).toHaveAttribute('aria-disabled', 'true');
      await expect(save).toHaveAttribute('aria-busy', 'true');

      // Implicit submission — Enter pressed in the Name field, not on the button.
      await nameField.press('Enter');
    } finally {
      release();
    }

    await expect(page.getByRole('heading', { name: wallName })).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });
    expect(count()).toBe(1);
  });
});
