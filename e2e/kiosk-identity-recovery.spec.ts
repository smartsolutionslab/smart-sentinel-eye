import { test, expect, type Page, type Route } from '@playwright/test';
import { openFirstLayout, signInToKiosk } from './support/kiosk-session';

/**
 * Spec 051 US1 — a wall that can come back does, without anybody walking to it.
 *
 * <p>
 * <b>The defect these replace was observed, not theorised.</b> The provider was
 * stopped, the screen went to "Sign-in failed / Failed to fetch", the provider
 * was started again and became healthy — and ninety seconds later, with nobody
 * touching it, the screen was still dark. That branch had no retry in it at all.
 * </p>
 */

/**
 * Cuts the token endpoint off, the way a provider that is down does.
 *
 * <p>
 * <b>The handler counts its own calls, and every test asserts that count.</b>
 * Silent renewal can run in a hidden iframe, so a pattern registered on the page
 * alone can match nothing at all — and a test that intercepts nothing behaves
 * exactly like a test that passes. Spec 050 shipped this in a different
 * disguise, posting to a hardcoded host while the tokens came from somewhere
 * else, and only a deliberate control caught it.
 * </p>
 *
 * <p>
 * <c>abort('failed')</c> produces the same <c>TypeError: Failed to fetch</c> a
 * stopped provider produces — checked against a real one, not assumed.
 * </p>
 */
function cutOffTheProvider(page: Page) {
  const state = { calls: 0, cutOff: true };

  const handler = async (route: Route) => {
    state.calls += 1;
    if (state.cutOff) {
      await route.abort('failed');
      return;
    }
    await route.fallback();
  };

  return {
    state,
    install: () => page.route('**/protocol/openid-connect/token', handler),
    restore: () => {
      state.cutOff = false;
    },
  };
}

/** Forces the next renewal to actually happen, by ageing the grant on disk. */
async function expireTheStoredGrant(page: Page): Promise<void> {
  const aged = await page.evaluate(() => {
    const key = Object.keys(window.localStorage).find((candidate) => candidate.startsWith('oidc.user:'));
    if (key === undefined) return false;
    const user = JSON.parse(window.localStorage.getItem(key) ?? '{}') as Record<string, unknown>;
    user['expires_at'] = Math.floor(Date.now() / 1000) - 3_600;
    window.localStorage.setItem(key, JSON.stringify(user));
    return true;
  });

  expect(aged, 'the screen should be holding a grant to age').toBe(true);
}

test.describe('A wall comes back on its own (spec 051 US1)', () => {
  test('returns to its wall after the provider recovers, with nothing touched', async ({ page }) => {
    test.setTimeout(300_000);

    await signInToKiosk(page);
    await openFirstLayout(page);

    const provider = cutOffTheProvider(page);
    await provider.install();
    await expireTheStoredGrant(page);
    await page.reload();

    // The recoverable screen, not the old dead one.
    await expect(page.getByTestId('identity-reconnecting')).toBeVisible({ timeout: 60_000 });
    await expect(page.getByText(/no action is needed/i)).toBeVisible();

    // **Nothing from the identity library reaches the wall** (FR-010).
    await expect(page.getByText(/failed to fetch/i)).toHaveCount(0);

    expect(
      provider.state.calls,
      'the interception must actually have fired — a pattern matching nothing looks exactly like a passing test',
    ).toBeGreaterThan(0);

    // It is trying on its own, before anyone has done anything.
    const attemptsBefore = provider.state.calls;
    await page.waitForTimeout(15_000);
    expect(provider.state.calls, 'a recoverable failure must retry without being asked').toBeGreaterThan(
      attemptsBefore,
    );

    // The provider comes back. **Nothing is clicked from here on** — that is the
    // whole claim, and the manual button already worked before this feature.
    provider.restore();

    await expect(page.getByTestId('layout-grid'), 'the wall must return with nobody touching the screen').toBeVisible({
      timeout: 120_000,
    });
    await expect(page.getByTestId('identity-reconnecting')).toHaveCount(0);
  });

  test('keeps showing its wall when a renewal fails in the background', async ({ page }) => {
    test.setTimeout(300_000);

    await signInToKiosk(page);
    await openFirstLayout(page);

    // The provider goes away, but this screen still holds a working token. A
    // wall with something valid to show must not be blanked for a failure that
    // has not cost it anything yet.
    const provider = cutOffTheProvider(page);
    await provider.install();

    await page.waitForTimeout(10_000);

    await expect(page.getByTestId('layout-grid')).toBeVisible();
    await expect(page.getByTestId('identity-reconnecting')).toHaveCount(0);
  });
});
