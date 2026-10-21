import { test, expect } from '@playwright/test';

/**
 * Spec 425 (issue #2667) US2 — the kiosk's first-boot "Sign in" button is a touch target
 * and does not animate when hovered.
 *
 * **This spec is GREEN IN ADVANCE and is not Phase 4a red evidence (ADR-0139).** Today the
 * raw button is `px-6 py-3` = 48 px with no border, and a raw button with no transition
 * starts no animation on hover, so both assertions already hold on `develop`. It pins the
 * real-browser outcome of SC-1/SC-2 on the kiosk after the button becomes
 * `<Button size="touch">` (50 px with its border, no motion utility): the migration must not
 * shrink the target or introduce a transition. CI-only — Playwright needs the full stack and
 * was not run locally.
 *
 * Starts from a context that has never signed in (no storage), the way a first boot does;
 * a fresh context does not inherit the project's `use` settings (see kiosk-comes-back.spec.ts).
 */
test('the first-boot Sign in button is at least 48px tall and starts no animation on hover', async ({ browser }) => {
  const context = await browser.newContext({
    baseURL: 'http://localhost:5174',
    ignoreHTTPSErrors: true,
  });

  try {
    const page = await context.newPage();
    await page.goto('/');

    const signIn = page.getByRole('button', { name: /^sign in$/i });
    await expect(signIn).toBeVisible();

    const box = await signIn.boundingBox();
    expect(box, 'the Sign in button has a layout box').not.toBeNull();
    expect(box?.height ?? 0, 'a finger needs at least 48px').toBeGreaterThanOrEqual(48);

    await signIn.hover();

    const animations = await signIn.evaluate((element) => element.getAnimations().length);
    expect(animations, 'hovering the button starts no animation').toBe(0);
  } finally {
    await context.close();
  }
});
