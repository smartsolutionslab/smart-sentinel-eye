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
  // Spec 160 (issue #2387) §11 A3. Save is `aria-disabled` (spec 160
  // FR-001), not natively `disabled`, while the chain read that gates it is
  // still settling — whether Playwright 1.62's click actionability treats
  // `aria-disabled="true"` as "not enabled" is unverified, so this waits on
  // the attribute explicitly rather than depending on the answer; a click
  // that silently lands on a closed gate would otherwise still leave this
  // assertion green.
  const saveDraftButton = page.getByRole('button', { name: /^save draft$/i });
  await expect(saveDraftButton).not.toHaveAttribute('aria-disabled', 'true');
  await saveDraftButton.click();

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
  //
  // `force: true` — the same `getAriaDisabled` function behind the
  // `toBeEnabled()` fix above (`4d3b7c95`) also gates `locator.click()`'s
  // actionability pre-check, but here as a wait rather than a failure:
  // `aria-disabled="true"` never becomes `false` at the floor, so an
  // unforced click polls "element is not enabled" until
  // `FIRST_WRITE_TEST_TIMEOUT_MS` (300 s) fires — reproduced on a live
  // stack, 614+ retries, the exact shape of the CI hang this comment
  // replaces. `force: true` skips the pre-check and still dispatches a real
  // mouse click at the element's point, which is what this step is for.
  // Scoped to this one call — every other `.click()` on this button (there
  // is none while it is genuinely enabled in this file) should stay
  // unforced, since an unforced click is the thing that would actually
  // catch the button wrongly staying `aria-disabled` mid-sequence.
  await undoButton.click({ force: true });
  await expect(leftField).toHaveValue(savedLeft);
  const activeElementTag = await page.evaluate(() => document.activeElement?.tagName ?? null);
  expect(activeElementTag, 'focus should stay on a real control, not fall out to <body>').not.toBe('BODY');

  await page.keyboard.press('Control+z');

  // Step 7 — the label is back at the saved geometry.
  await expect(leftField).toHaveValue(savedLeft);
});

// Spec 160 (issue #2387) US1 — the one thing jsdom cannot prove
// (`OverlayEditorDialogSaveGate.test.tsx`'s doc comment): jsdom does not
// implement the browser's disable-blur ("focus fixup") algorithm, so a
// vitest assertion on `document.activeElement` can never fail there
// regardless of whether Save uses `aria-disabled` or native `disabled`. This
// repo already answered the identical question for a structurally identical
// control — the Undo button test above (spec 154, :213-220) — and this test
// carries the claim here for the same reason.
//
// New behaviour, RED (ADR-0139/ADR-0144) against unmodified `develop`: Save
// is rendered natively `disabled`, so the instant the edit mutation goes
// pending a real browser drops focus to `<body>` and it never returns —
// Radix's FocusScope cannot rescue a disable-blur (`relatedTarget: null` on
// the `focusout`, and its MutationObserver watches `childList`/`subtree`,
// never `attributes`, spec.md §1.1).
//
// Two browser contexts sharing one overlay, modelled on
// `e2e/layouts.spec.ts`'s "a second operator publishing the same revision is
// refused" test: the second context opens the editor on the version the
// first is about to move past, so its own Save is the one that earns the
// 409.
test('a stale-version conflict does not cost the keyboard operator their place at Save', async ({ browser }) => {
  test.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);

  const name = `E2E Save Focus ${Date.now()}`;

  const first = await browser.newContext();
  const second = await browser.newContext();

  try {
    const pageOne = await first.newPage();
    await signInAsOperator(pageOne);
    await pageOne.getByRole('link', { name: /^overlays$/i }).click();
    await pageOne.getByRole('button', { name: /new overlay/i }).click();
    await pageOne.locator('#overlay-name').fill(name);
    await pageOne.getByRole('button', { name: /save as draft/i }).click();
    await expect(pageOne.getByText(name)).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });

    // The second context opens the editor *now*, before the first writer's
    // edit lands, so it holds the version that edit is about to supersede.
    // Opening it after would hand it the current version and prove nothing —
    // the same ordering the layouts conflict test uses.
    const pageTwo = await second.newPage();
    await signInAsOperator(pageTwo);
    await pageTwo.getByRole('link', { name: /^overlays$/i }).click();
    const rowTwo = pageTwo.getByRole('listitem').filter({ hasText: name });
    await expect(rowTwo.getByRole('button', { name: /^edit draft$/i })).toBeVisible({
      timeout: FIRST_WRITE_TIMEOUT_MS,
    });
    await rowTwo.getByRole('button', { name: /^edit draft$/i }).click();
    await expect(pageTwo.getByRole('dialog')).toBeVisible();
    // Accessible-name pattern, not a plain `/^save draft$/i`: this dialog's
    // submit button relabels itself `Saving…` for exactly as long as
    // `isLoading` is true (`OverlayEditorDialog.tsx:360`) — the PATCH-pending
    // window this test exists to observe. A locator that only matches the
    // settled label resolves to nothing during that window, so a retrying
    // assertion built on it silently skips past the window instead of
    // sampling inside it. Still Save-specific within this dialog: neither
    // `Cancel` nor create mode's `Save as draft` matches either branch.
    const saveButtonTwo = pageTwo.getByRole('button', { name: /^(save draft|saving…)$/i });

    // The first writer moves the version out from under the second.
    const rowOne = pageOne.getByRole('listitem').filter({ hasText: name });
    await rowOne.getByRole('button', { name: /^edit draft$/i }).click();
    await expect(pageOne.getByRole('dialog')).toBeVisible();
    await pageOne.getByTestId('overlay-editor-text').fill('E2E First Writer');
    await pageOne.getByRole('button', { name: /^save draft$/i }).click();
    await expect(pageOne.getByRole('dialog')).toHaveCount(0);

    // Slow only the second context's PATCH, so the pending window is
    // observable rather than a single-frame flicker on a warm local stack —
    // `system-variables.spec.ts`'s pattern. The chain re-read GET is a
    // different path (`/{id}`, no `/revisions/`), so it is untouched by this
    // route — delaying it too would make its own in-flight window
    // unobservable for the identical reason that file discriminates by
    // method.
    await pageTwo.route(
      (url) => /\/overlay-designer\/overlays\/[^/]+\/revisions\/\d+$/.test(url.pathname),
      async (route) => {
        if (route.request().method() !== 'PATCH') return route.fallback();
        await new Promise((resolve) => setTimeout(resolve, 1000));
        await route.continue();
      },
    );

    // Programmatic focus, not a real Tab traversal: the mechanism under test
    // (a browser un-focusing a control the instant it natively disables)
    // does not care how the control became focused, only that it was —
    // spec.md §8.1 step 3's own parenthetical ("a click would focus it
    // anyway; tabbing proves the keyboard path" — this proves the same thing
    // more directly, without depending on the form's current tab order).
    await pageTwo.getByTestId('overlay-editor-text').fill('E2E Second Writer');
    await saveButtonTwo.focus();
    await expect(saveButtonTwo).toBeFocused();
    await pageTwo.keyboard.press('Enter');

    // The PATCH is now in flight (held by the route above). This is the
    // assertion that must be RED on unmodified `develop`: real browser, real
    // native `disabled`, real blur to `<body>`.
    await expect(saveButtonTwo).toBeFocused();
    await expect(saveButtonTwo).toHaveAttribute('aria-disabled', 'true');

    // The conflict lands and the chain re-read starts.
    const alertTwo = pageTwo.getByRole('alert');
    await expect(alertTwo).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });
    await expect(saveButtonTwo).toBeFocused();

    // The re-read answers with the corrected version; Save re-opens, still
    // under the operator's finger.
    await expect(saveButtonTwo).not.toHaveAttribute('aria-disabled', 'true', {
      timeout: FIRST_WRITE_TIMEOUT_MS,
    });
    await expect(saveButtonTwo).toBeFocused();

    // Gate-open pairing (rule 2, tasks.md): pressing it now actually saves,
    // onto the recovered version.
    await pageTwo.keyboard.press('Enter');
    await expect(pageTwo.getByRole('dialog')).toHaveCount(0, { timeout: FIRST_WRITE_TIMEOUT_MS });
  } finally {
    await first.close();
    await second.close();
  }
});
