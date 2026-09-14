import { test, expect } from '@playwright/test';
import { signInAsOperator } from './support/sign-in';
import { FIRST_WRITE_TEST_TIMEOUT_MS, FIRST_WRITE_TIMEOUT_MS } from './support/cold-stack';

// ADR-0108 — overlays "read" vertical slice. An operator signs in, opens the
// Overlays surface, and the list loads from the overlay-designer service
// *through the API gateway* (ADR-0106). A 401 / 404 / CORS / scope failure
// surfaces the "Could not load overlays" alert, so asserting the heading renders
// with no alert proves the authenticated path end to end.
test('operator opens overlays and the list loads through the gateway', async ({ page }) => {
  await signInAsOperator(page);

  await page.getByRole('link', { name: /^overlays$/i }).click();

  await expect(page.getByRole('heading', { name: 'Overlays', exact: true })).toBeVisible();
  await expect(page.getByRole('alert')).toHaveCount(0);
});

test('operator creates an overlay draft and it appears in the list', async ({ page }) => {
  test.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);

  await signInAsOperator(page);

  await page.getByRole('link', { name: /^overlays$/i }).click();
  await expect(page.getByRole('heading', { name: 'Overlays', exact: true })).toBeVisible();

  // POST /overlay-designer/overlays (Bearer; sse.management grandfathers
  // sse.overlays.write); the label uses the editor's default.
  await page.getByRole('button', { name: /new overlay/i }).click();
  const name = `E2E Overlay ${Date.now()}`;
  await page.locator('#overlay-name').fill(name);
  await page.getByRole('button', { name: /save as draft/i }).click();

  await expect(page.getByText(name)).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });
});

// Spec 151 (issue #2346) T002 — the one thing jsdom cannot prove
// (spec.md §Precision, §Independent end-to-end test procedure step 11): a
// typed percentage survives real form submission and the double -> decimal
// boundary to the server exactly — the client-side /100-vs-quantize
// discrimination is `OverlayGeometryFields.test.tsx`'s job (24.87 does not
// discriminate the two; it is used here only as an ordinary value). There is
// no edit dialog (`OverlayEditorDialog` is create-only), so the read-back is
// a gateway call, not a re-open.
test('operator types an exact geometry and the saved overlay carries it through the gateway', async ({ page }) => {
  test.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);

  // Origin taken from a real gateway request, never hardcoded — the gateway
  // is reached through Aspire's proxied endpoint, so a fixed port is a
  // different origin as far as CORS and the token's audience are concerned.
  const gatewayRequest = page.waitForRequest((request) => /\/overlay-designer\//.test(request.url()));

  await signInAsOperator(page);

  await page.getByRole('link', { name: /^overlays$/i }).click();
  await expect(page.getByRole('heading', { name: 'Overlays', exact: true })).toBeVisible();

  const origin = new URL((await gatewayRequest).url()).origin;
  // management-web sets no `userStore` in apps/management-web/src/app/auth.ts
  // (unlike apps/kiosk-web, which opts into `window.localStorage` explicitly),
  // so oidc-client-ts falls back to its default, sessionStorage. Reading
  // localStorage here — the pattern every kiosk spec uses because kiosk-web
  // genuinely is in localStorage — silently returns '', and every kiosk
  // helper this was copied from is a kiosk spec for exactly that reason.
  const token = await page.evaluate(() => {
    const key = Object.keys(window.sessionStorage).find((candidate) => candidate.startsWith('oidc.user:'));
    const user = JSON.parse(window.sessionStorage.getItem(key ?? '') ?? '{}') as Record<string, string>;
    return user['access_token'] ?? '';
  });
  expect(token, 'the operator should be holding an access token').not.toBe('');

  await page.getByRole('button', { name: /new overlay/i }).click();

  // FR-001/FR-003 — a percent field, type="text", reached by its visible
  // label, not by dragging. Tab commits the draft (FR-004's blur path).
  await page.getByLabel('Left', { exact: true }).fill('24.87');
  await page.keyboard.press('Tab');
  await page.getByLabel('Width', { exact: true }).fill('50');
  await page.keyboard.press('Tab');

  // FR-005/FR-006 — an exact, in-range value is accepted outright; nothing
  // on the panel refuses it.
  await expect(page.getByRole('alert')).toHaveCount(0);

  // The teardown's DISPOSABLE pattern (archive-e2e-overlays.teardown.ts) matches
  // "E2E " (space), not "E2E-" — every other disposable in this suite uses the
  // space form, and a hyphen here would leave one orphan draft per CI run.
  const name = `E2E Geometry ${Date.now()}`;
  await page.locator('#overlay-name').fill(name);
  await page.getByRole('button', { name: /save as draft/i }).click();

  await expect(page.getByText(name)).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

  const overlays = await page.evaluate(
    async ([gatewayOrigin, accessToken]) => {
      const response = await fetch(`${gatewayOrigin}/overlay-designer/overlays`, {
        headers: { Authorization: `Bearer ${accessToken}` },
      });
      return (await response.json()) as {
        chains: { name: string; revisions: { normalizedX: number; normalizedWidth: number }[] }[];
      };
    },
    [origin, token] as const,
  );

  const saved = overlays.chains.find((chain) => chain.name === name);
  expect(saved, `the saved overlay "${name}" should be readable back through the gateway`).toBeTruthy();
  const revision = saved!.revisions[0]!;
  // Exactly 0.2487 through the double -> decimal boundary, not merely close.
  expect(revision.normalizedX).toBe(0.2487);
  expect(revision.normalizedWidth).toBe(0.5);
});

// Spec 152 T007, US1 — the issue's literal scenario. Every component test in
// this feature mocks the API hooks, so it is exactly how
// `useEditDraftOverlayRevisionMutation` stayed exported and uncalled for four
// specs (see spec 152 "Independent end-to-end test procedure"). Only this
// level proves the PATCH leaves the browser, crosses the gateway, and is
// answered 200 — the same PATCH /overlay-designer/overlays/{id}/revisions/1
// carrying If-Match that OverlayEditorDialog.test.tsx asserts is built from a
// server re-read, not client arithmetic (FR-011/FR-012).
test('operator edits a saved draft in place, onto the same revision', async ({ page }) => {
  test.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);

  await signInAsOperator(page);

  await page.getByRole('link', { name: /^overlays$/i }).click();
  await expect(page.getByRole('heading', { name: 'Overlays', exact: true })).toBeVisible();

  await page.getByRole('button', { name: /new overlay/i }).click();
  const name = `E2E Edit ${Date.now()}`;
  await page.locator('#overlay-name').fill(name);
  await page.getByRole('button', { name: /save as draft/i }).click();
  await expect(page.getByText(name)).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

  // Before this spec the row offers only Publish and Discard draft — this is
  // the observable that fails on develop.
  const row = page.getByRole('listitem').filter({ hasText: name });
  await row.getByRole('button', { name: /^edit draft$/i }).click();

  await expect(page.getByRole('dialog')).toBeVisible();
  await expect(page.getByLabel(/^name$/i)).toHaveCount(0);
  const textField = page.getByTestId('overlay-editor-text');
  await textField.fill('E2E Edited');
  await page.getByRole('button', { name: /^save draft$/i }).click();

  // Same revision, not a new one: the badge still reads v1 · Draft.
  await expect(row.getByText('E2E Edited')).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });
  await expect(row.getByText('v1 · Draft')).toBeVisible();
});

// Spec 154 (issue #2347) T010, US1 — step-wise undo/redo, observed in a real
// browser against the real stack. New behaviour, RED (ADR-0139/ADR-0144):
// `overlay-editor-label` and the Left field already exist on `develop`
// (spec 149, spec 151), so this fails at "press Ctrl+Z" — there is no Undo
// control yet and Ctrl+Z does nothing — not on a selector that cannot
// resolve at all. Modelled on "operator edits a saved draft in place"
// above, including `FIRST_WRITE_TEST_TIMEOUT_MS`. Only what jsdom cannot
// prove is exercised here — a real mouse drag through react-rnd and a real
// `Ctrl+Z` reaching the page — the coalescing algebra itself is
// `OverlayEditorUndo.test.tsx`'s job.
test('operator drags a label, undoes it, and undoes back to the saved geometry', async ({ page }) => {
  test.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);

  await signInAsOperator(page);

  await page.getByRole('link', { name: /^overlays$/i }).click();
  await expect(page.getByRole('heading', { name: 'Overlays', exact: true })).toBeVisible();

  await page.getByRole('button', { name: /new overlay/i }).click();
  const name = `E2E Undo ${Date.now()}`;
  await page.locator('#overlay-name').fill(name);
  await page.getByRole('button', { name: /save as draft/i }).click();
  await expect(page.getByText(name)).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

  const row = page.getByRole('listitem').filter({ hasText: name });
  await row.getByRole('button', { name: /^edit draft$/i }).click();
  await expect(page.getByRole('dialog')).toBeVisible();

  // Step 2 — the value the dialog opened with, read back from the field
  // rather than assumed, so the test pins whatever DEFAULT_INPUT actually
  // is today.
  const leftField = page.getByLabel('Left', { exact: true });
  const savedLeft = await leftField.inputValue();

  // Step 3 — a real mouse drag on the label, through react-rnd. No test id
  // exists for "drop the label 150px right, 60px down"; the drag is driven
  // by mouse position, exactly as an operator's would be.
  const label = page.getByTestId('overlay-editor-label');
  const box = await label.boundingBox();
  if (box === null) {
    throw new Error('the overlay label should have a bounding box once the dialog has rendered');
  }
  await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
  await page.mouse.down();
  await page.mouse.move(box.x + box.width / 2 + 150, box.y + box.height / 2 + 60, { steps: 10 });
  await page.mouse.up();

  await expect(leftField).not.toHaveValue(savedLeft);

  // Step 4 — Ctrl+Z, with focus inside the editor but not necessarily on the
  // field itself (FR-007: the binding is on the editor's root, not
  // conditioned on the event target).
  await label.click();
  await page.keyboard.press('Control+z');

  // Step 5 — the field reads its step-2 value again.
  await expect(leftField).toHaveValue(savedLeft);

  // Step 6 — Ctrl+Z until the Undo control refuses. One drag is one step, so
  // this is already true; pressed again to pin Decision 5's "further presses
  // change nothing" as well as reaching the floor in the first place.
  //
  // `aria-disabled`, not the native `disabled` attribute (phase 6 review
  // finding): a browser blurs a focused element the instant it becomes
  // natively disabled, so reaching the floor *by mouse* would push focus to
  // `<body>` — outside the editor root — and `Ctrl+Z` would stop working
  // until the operator clicked back in. jsdom does not implement
  // blur-on-disable, so only a real browser can prove this; that is exactly
  // what this test is for.
  const undoButton = page.getByRole('button', { name: /^undo$/i });
  await expect(undoButton).toHaveAttribute('aria-disabled', 'true');
  // Not `toBeEnabled()`: Playwright's own `getAriaDisabled` (pinned
  // playwright-core@1.62.1) is `isNativelyDisabled(el) || hasExplicitAriaDisabled(el)`
  // for a `role="button"` element, so `aria-disabled="true"` alone makes
  // `toBeEnabled()` fail regardless of the native `disabled` property —
  // confirmed against a bare `<button aria-disabled="true">` fixture with no
  // app code, so no implementation of this design can satisfy both
  // assertions at once. `toBeDisabled()` would be no better the other way:
  // it passes on `aria-disabled` alone even if the native property regressed
  // to `true`, which is exactly the regression this test exists to catch.
  // Both properties are read directly instead of through either matcher. The
  // element is cast through `unknown` to a structural `{ disabled: boolean }`
  // rather than typed `HTMLButtonElement` — `eslint.config.js`'s `e2e/**`
  // globals declare `document`/`window`/`Element` but not the per-tag DOM
  // lib types, and widening that list to reach green is the gate-weakening
  // ADR-0144 rules out; routing around it here is the same call already
  // made for `apps/shared` and `apps/management-web`'s own eslint configs.
  expect(await undoButton.evaluate((el) => (el as unknown as { disabled: boolean }).disabled)).toBe(false);
  await undoButton.focus();
  expect(await undoButton.evaluate((el) => document.activeElement === el)).toBe(true);

  // A mouse click on the refused control — not the keyboard shortcut — must
  // still no-op: aria-disabled does not stop the browser from dispatching
  // the click, so the handler itself has to refuse.
  await undoButton.click();
  await expect(leftField).toHaveValue(savedLeft);
  const activeElementTag = await page.evaluate(() => document.activeElement?.tagName ?? null);
  expect(activeElementTag, 'focus should stay on a real control, not fall out to <body>').not.toBe('BODY');

  await page.keyboard.press('Control+z');

  // Step 7 — the label is back at the saved geometry.
  await expect(leftField).toHaveValue(savedLeft);
});
