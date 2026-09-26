import { test, expect } from '@playwright/test';
import { signInAsOperator } from './support/sign-in';

/**
 * Spec 266 (issue #2335) US6 — the command palette's one e2e path.
 *
 * Every other US1–US3 consumer already has e2e coverage exercising it in a
 * real browser; the palette does not, and it is the only place that proves
 * Chromium's own Control+K does not win instead — jsdom cannot exercise the
 * browser's default keydown handling at all, so `ShellLayout.test.tsx`'s
 * `defaultPrevented` assertion is a proxy for this, not a replacement.
 *
 * Written in phase 4a (tasks.md T072) so the red is observable here too: it
 * must fail on the missing "Go to" dialog, never on sign-in.
 */
test.describe('command palette', () => {
  test('Control+K opens the palette, filters, and Enter navigates and focuses the nav link', async ({ page }) => {
    await signInAsOperator(page);

    await page.keyboard.press('Control+K');
    const dialog = page.getByRole('dialog', { name: 'Go to' });
    await expect(dialog).toBeVisible();

    await page.keyboard.type('rul');
    await page.keyboard.press('Enter');

    await expect(page).toHaveURL(/\/rules$/);
    await expect(page.getByRole('link', { name: /^rules$/i })).toBeFocused();
  });

  test('The "Go to…" button opens the palette, and Escape returns focus to it', async ({ page }) => {
    await signInAsOperator(page);

    const trigger = page.getByRole('button', { name: /go to…/i });
    await trigger.click();
    await expect(page.getByRole('dialog', { name: 'Go to' })).toBeVisible();

    await page.keyboard.press('Escape');

    await expect(page.getByRole('dialog')).toHaveCount(0);
    await expect(trigger).toBeFocused();
  });
});
