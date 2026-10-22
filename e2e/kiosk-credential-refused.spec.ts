import { test, expect, type Page, type Route } from '@playwright/test';
import { signInToKiosk } from './support/kiosk-session';
import { readLiveVideoWall } from './support/live-video-wall';

/**
 * Spec 439 (issue #2781) — a wall whose every tile was refused at once says the
 * display's own access failed, tries one renewal, and tells someone what to ask
 * for if that fails.
 *
 * <p>
 * <b>Every test counts its own interceptions</b> (the spec 051 pattern): a
 * route that never fired would let each assertion below pass for the wrong
 * reason, so the count is asserted before anything that depends on it.
 * </p>
 */

const RENEWING_TEXT = "This display's access was refused. Reconnecting with a new credential.";
// Shown only when the provider answered the renewal with a user holding no
// credential, which these tests cannot provoke; asserted absent below.
const REFUSED_TEXT = 'Access refused. Ask an administrator to re-enrol this display.';

const WHEP_POST = (url: URL): boolean => /\/whep(\/|$)/.test(url.pathname);
const TOKEN_ENDPOINT = '**/protocol/openid-connect/token';

/** Counts the `wall-credential→<phase>` resilience lines the page logs. */
function countResilienceLines(page: Page): (phase: string) => number {
  const phases: string[] = [];
  page.on('console', (message) => {
    const [tag, detail] = message.args();
    if (tag === undefined || detail === undefined) return;
    void Promise.all([tag.jsonValue(), detail.jsonValue()]).then(([first, second]) => {
      const transition = (second as { transition?: unknown } | null)?.transition;
      if (first === '[resilience]' && typeof transition === 'string') phases.push(transition);
    });
  });
  return (phase) => phases.filter((transition) => transition === `wall-credential→${phase}`).length;
}

/**
 * Opens the seeded wall from the picker the sign-in lands on. Callers install
 * their token-endpoint route between `signInToKiosk` and this: the SPA's own
 * authorization-code exchange also posts to that endpoint, and an interceptor
 * installed earlier would break the sign-in itself.
 */
async function openTheLiveWall(page: Page): Promise<void> {
  const wall = readLiveVideoWall();
  await page.getByRole('listitem').filter({ hasText: wall.layoutName }).getByRole('button').click();
  await expect(page.getByTestId('layout-grid')).toBeVisible();
}

test.describe('The wall knows its own credential (spec 439)', () => {
  test('recovers: every tile refused 401 shows the renewing state, renews once, and the tiles reconnect', async ({
    page,
  }) => {
    test.setTimeout(240_000);

    let renewed = false;
    let whepRefusals = 0;
    let renewalCalls = 0;
    const lines = countResilienceLines(page);

    await signInToKiosk(page);
    await page.route(WHEP_POST, async (route: Route) => {
      if (route.request().method() !== 'POST' || renewed) {
        await route.continue();
        return;
      }
      whepRefusals += 1;
      await route.fulfill({ status: 401, body: 'unauthorized' });
    });
    // Only calls after the first refusal count: that is when the wall's own
    // renewal can start, so a routine silent renew earlier cannot inflate it.
    await page.route(TOKEN_ENDPOINT, async (route) => {
      if (whepRefusals > 0) renewalCalls += 1;
      renewed = true;
      await route.continue();
    });

    await openTheLiveWall(page);

    await expect(page.getByText(RENEWING_TEXT)).toBeVisible({ timeout: 60_000 });
    await expect(page.getByTestId('wall-credential-state')).toHaveCount(0, { timeout: 60_000 });

    expect(whepRefusals, 'the 401 interception must actually have fired').toBeGreaterThan(1);
    expect(renewalCalls, 'exactly one renewal is attempted').toBe(1);
    expect(lines('renewing')).toBe(1);
    await expect(page.getByText('Access refused').first()).toHaveCount(0);
    await expect(page.locator('video').first()).toBeVisible();
  });

  test('refused: a provider that answers invalid_grant ends on the not-authorized screen, not the wall notice', async ({
    page,
  }) => {
    test.setTimeout(240_000);

    let tokenCalls = 0;
    let whepRefusals = 0;
    await signInToKiosk(page);
    await page.route(WHEP_POST, async (route) => {
      whepRefusals += 1;
      await route.fulfill({ status: 401, body: 'unauthorized' });
    });
    await page.route(TOKEN_ENDPOINT, async (route) => {
      tokenCalls += 1;
      await route.fulfill({
        status: 400,
        contentType: 'application/json',
        body: JSON.stringify({ error: 'invalid_grant', error_description: 'User disabled' }),
      });
    });

    await openTheLiveWall(page);

    await expect(page.getByTestId('identity-not-authorized')).toBeVisible({ timeout: 60_000 });
    expect(whepRefusals, 'the 401 interception must actually have fired').toBeGreaterThan(1);
    expect(tokenCalls, 'the renewal reached the provider').toBeGreaterThan(0);
    await expect(page.getByTestId('wall-credential-state')).toHaveCount(0);
  });

  test('unreachable: a renewal the provider never answers keeps the first state and never says re-enrol', async ({
    page,
  }) => {
    test.setTimeout(240_000);

    let tokenCalls = 0;
    let whepRefusals = 0;
    const lines = countResilienceLines(page);
    await signInToKiosk(page);
    await page.route(WHEP_POST, async (route) => {
      whepRefusals += 1;
      await route.fulfill({ status: 401, body: 'unauthorized' });
    });
    await page.route(TOKEN_ENDPOINT, async (route) => {
      if (whepRefusals > 0) tokenCalls += 1;
      await route.abort();
    });

    await openTheLiveWall(page);

    await expect(page.getByText(RENEWING_TEXT)).toBeVisible({ timeout: 60_000 });
    expect(whepRefusals, 'the 401 interception must actually have fired').toBeGreaterThan(1);
    await expect
      .poll(() => tokenCalls, { message: 'the abort interception must actually have fired' })
      .toBeGreaterThan(0);

    await page.waitForTimeout(15_000);

    // The session machine's own retry may ask again; the wall itself asked once.
    expect(lines('renewing')).toBe(1);
    await expect(page.getByText(RENEWING_TEXT)).toBeVisible();
    await expect(page.getByText(REFUSED_TEXT)).toHaveCount(0);
  });

  test('mixed: a 403 tile keeps its own message and the wall shows no credential state', async ({ page }) => {
    test.setTimeout(240_000);

    let renewalCalls = 0;
    let unauthorized = 0;
    let forbidden = 0;
    await signInToKiosk(page);
    await page.route(WHEP_POST, async (route) => {
      if (unauthorized === 0) {
        unauthorized += 1;
        await route.fulfill({ status: 401, body: 'unauthorized' });
        return;
      }
      forbidden += 1;
      await route.fulfill({ status: 403, body: 'forbidden' });
    });
    await page.route(TOKEN_ENDPOINT, async (route) => {
      if (unauthorized > 0) renewalCalls += 1;
      await route.continue();
    });

    await openTheLiveWall(page);

    await expect(page.getByText('Access refused').first()).toBeVisible({ timeout: 60_000 });
    expect(unauthorized, 'the 401 interception must actually have fired').toBe(1);
    expect(forbidden, 'the 403 interception must actually have fired').toBeGreaterThan(0);

    await page.waitForTimeout(12_000);

    await expect(page.getByTestId('wall-credential-state')).toHaveCount(0);
    expect(renewalCalls, 'a 403 never triggers a renewal').toBe(0);
  });
});
