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
