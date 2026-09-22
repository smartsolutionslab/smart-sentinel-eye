import { test, expect, chromium, request as playwrightRequest, type BrowserContext, type Page } from '@playwright/test';
import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

/**
 * Spec 214 (#2509) US2, SC-6 — a running wall display keeps its wall while its
 * own account is locked out.
 *
 * <p>
 * Spec 207 turned on Keycloak's brute-force detector and left one question
 * unresolved: whether a lockout denies only *new* sign-ins or also revokes a
 * session that is already running. This is that question put to the thing the
 * issue actually cares about — a wall display holding a real offline grant on
 * `kiosk-wall` — rather than only to the code path it shares with
 * `management-web` (spec 214's integration suite, US1).
 * </p>
 *
 * <p>
 * <b>Predicted (benign) outcome: the wall keeps renewing straight through the
 * lock.</b> Keycloak's brute-force check runs inside the authentication flow,
 * which a `grant_type=refresh_token` exchange does not pass through. If the
 * live server disagrees, this test goes red by rendering
 * `NotAuthorizedScreen` — that is the severe finding (A3), not a defect to
 * adjust away.
 * </p>
 *
 * <p>
 * <b>The rig is copied from `wall-survives-a-process-death.spec.ts`</b>
 * (`signInAsWallDisplay`, `openFirstLayout`, `claimsOf`, `storedAccessToken`,
 * `expireStoredAccessToken`) rather than imported or extracted: that file
 * itself explains why — `e2e/support/*` is an ADR-0109 contention file and
 * extraction is a separate refactor (ADR-0036). That sibling file is not
 * edited by this one.
 * </p>
 *
 * <p>
 * Locking is done directly against `management-web`'s token endpoint, not
 * `kiosk-wall`'s: brute-force state is keyed on the user, not the client
 * (spec 214 A1), so this is the same lock every other test in this repository
 * uses, aimed at the account this file cares about.
 * </p>
 *
 * <p>
 * <b>`localStorage` is where the grant lives</b> (ADR-0131).
 * `expireStoredAccessToken` edits it in place and is the only sanctioned
 * touch; nothing here ever calls `localStorage.setItem` itself.
 * </p>
 *
 * <p>
 * <b>Teardown is mandatory, not conditional.</b> `wall-munich` is a seeded
 * realm account other e2e projects sign in as — deleting it is not an option,
 * and leaving it locked would fail the next project for a reason that reads
 * exactly like an unrelated regression. The attack-detection record is
 * cleared in a `finally` that runs whether the test passes or fails.
 * </p>
 *
 * <p>
 * <b>CI-only guarantee: `playwright.config.ts`'s `workers: isCI ? 1 : undefined`.</b>
 * `fullyParallel: false` serialises tests *within* a file, but files still run
 * across workers, and five other wall-*.spec.ts files sign in as
 * `wall-munich` too. CI always runs with one worker, so the lock window here
 * never overlaps a sibling's interactive sign-in. Running this file locally
 * alongside another `wall-*` spec (`--workers` above 1, or the default
 * multi-core value) can transiently fail that sibling's sign-in for a reason
 * that has nothing to do with it — pass `--workers=1` for a local run that
 * includes this file.
 * </p>
 */

const WALL = 'http://localhost:5175/';
const WALL_USER = 'wall-munich';
const WALL_PASSWORD = 'Wall-munich-1234';
const REALM = 'smart-sentinel-eye';
const MANAGEMENT_CLIENT_ID = 'management-web';

/**
 * The master realm's bootstrap admin-cli account. Same default and same
 * override as `wall-withdrawal.spec.ts` — copied, not imported, for the same
 * ADR-0109 contention reason as the rest of this file's helpers.
 */
const ADMIN_PASSWORD = process.env['SSE_KEYCLOAK_ADMIN_PASSWORD'] ?? 'dev-only-keycloak-admin';

/**
 * Copied from `wall-survives-a-process-death.spec.ts`: the URL is absolute —
 * a manually launched context inherits no `baseURL` from `playwright.config.ts`.
 */
async function signInAsWallDisplay(page: Page): Promise<void> {
  await page.goto(WALL);
  await page.getByRole('button', { name: /sign in/i }).click();
  await page.locator('#username').fill(WALL_USER);
  await page.locator('#password').fill(WALL_PASSWORD);
  await page.locator('#kc-login').click();
  await expect(page.getByRole('heading', { name: 'Pick a layout' })).toBeVisible({ timeout: 90_000 });
  await expect(page.getByRole('listitem').first(), 'the seed project publishes a layout').toBeVisible({
    timeout: 90_000,
  });
}

/** A picker proves authentication; a `layout-grid` proves the wall. */
async function openFirstLayout(page: Page): Promise<void> {
  await page.getByRole('listitem').first().getByRole('button').click();
  await expect(page.getByTestId('layout-grid')).toBeVisible({ timeout: 90_000 });
}

/** Claims of a grant, read without verifying — this is a test, not a validator. */
function claimsOf(token: string): Record<string, unknown> {
  const [, payload] = token.split('.');
  if (payload === undefined) {
    throw new Error('a grant should have a payload segment');
  }
  return JSON.parse(Buffer.from(payload, 'base64url').toString('utf8')) as Record<string, unknown>;
}

/** The `access_token` of the stored grant, read and never written. */
async function storedAccessToken(page: Page): Promise<string> {
  const access = await page.evaluate(() => {
    const key = Object.keys(window.localStorage).find((candidate) => candidate.startsWith('oidc.user:'));
    if (key === undefined) return null;
    const user = JSON.parse(window.localStorage.getItem(key) ?? '{}') as Record<string, unknown>;
    return typeof user['access_token'] === 'string' ? user['access_token'] : null;
  });

  expect(access, 'the wall display should be holding a grant').not.toBeNull();
  return access as string;
}

/**
 * Ages the stored access token in place, so the next silent-renewal check
 * finds it already expired. Returns the token it just marked spent, which is
 * what the later assertion compares the renewed token against.
 */
async function expireStoredAccessToken(page: Page): Promise<string> {
  const spent = await page.evaluate(() => {
    const key = Object.keys(window.localStorage).find((candidate) => candidate.startsWith('oidc.user:'));
    if (key === undefined) return null;
    const user = JSON.parse(window.localStorage.getItem(key) ?? '{}') as Record<string, unknown>;
    const access = user['access_token'];
    user['expires_at'] = Math.floor(Date.now() / 1000) - 3_600;
    window.localStorage.setItem(key, JSON.stringify(user));
    return typeof access === 'string' ? access : null;
  });

  expect(spent, 'the wall display should be holding a grant to expire').not.toBeNull();
  return spent as string;
}

/**
 * The provider's own issuer, read off a live grant rather than hardcoded — the
 * host publishes Keycloak on a port it chooses, and a fixed one is a
 * different issuer as far as Keycloak is concerned (`wall-withdrawal.spec.ts`
 * already paid for this mistake once).
 */
function issuerOf(token: string): string {
  const issuer = claimsOf(token)['iss'];
  if (typeof issuer !== 'string' || issuer === '') {
    throw new Error('a grant should name its issuer');
  }
  return issuer;
}

test.describe('A wall survives a lockout (spec 214 US2, SC-6)', () => {
  // `{}` rather than a named parameter: Playwright requires the first
  // parameter to be an object-destructuring pattern to work out which
  // fixtures a test needs, even when none are used.
  // eslint-disable-next-line no-empty-pattern
  test('a running wall display keeps its wall while wall-munich is locked out', async ({}) => {
    test.setTimeout(300_000);

    // Outside `test-results/`, deliberately — CI uploads that directory on
    // this public repo, and this profile's Local Storage holds
    // `wall-munich`'s *offline* refresh token.
    const profile = await mkdtemp(join(tmpdir(), 'sse-wall-lockout-'));
    const context: BrowserContext = await chromium.launchPersistentContext(profile, { ignoreHTTPSErrors: true });
    const api = await playwrightRequest.newContext({ ignoreHTTPSErrors: true });

    // Declared here so they're in scope for the lockout-clearing `finally`
    // nested inside the try block below (around the lock/assert steps). They
    // stay resolved before anything is locked, so if signing in, opening the
    // layout, or minting the admin token throws first, that inner `finally`
    // is never entered — correctly, since nothing has been locked yet to
    // clear.
    let provider = '';
    let adminToken = '';
    let userId = '';

    try {
      const page = context.pages()[0] ?? (await context.newPage());
      await signInAsWallDisplay(page);
      await openFirstLayout(page);

      const issuer = issuerOf(await storedAccessToken(page));
      provider = new URL(issuer).origin;

      // Master-realm admin token and wall-munich's id, minted and resolved
      // *before* the lock loop — so if either of these throws, nothing has
      // been locked yet and the outer `finally` has nothing to clear.
      const tokenResponse = await api.post(`${provider}/realms/master/protocol/openid-connect/token`, {
        form: { client_id: 'admin-cli', username: 'admin', password: ADMIN_PASSWORD, grant_type: 'password' },
      });
      expect(tokenResponse.status(), 'minting a master-realm admin token must succeed before anything is locked').toBe(
        200,
      );
      adminToken = ((await tokenResponse.json()) as { access_token: string }).access_token;

      const users = await api.get(`${provider}/admin/realms/${REALM}/users`, {
        headers: { Authorization: `Bearer ${adminToken}` },
        params: { username: WALL_USER, exact: 'true' },
      });
      const matches = (await users.json()) as Array<{ id: string }>;
      expect(matches.length, `'${WALL_USER}' should exist in the realm as a seeded account`).toBeGreaterThan(0);
      userId = matches[0]!.id;

      try {
        // --- lock wall-munich, directly against management-web's token endpoint ---
        // Brute-force state is keyed on the user, not the client (spec 214
        // A1), so a wrong guess posted at `management-web` locks this
        // account for every client, `kiosk-wall` included.
        //
        // failureFactor + 1 wrong guesses, read off the realm rather than a
        // fixed count of three: quickLoginCheckMilliSeconds: 1000 trips
        // first today, but a human raising it (one of spec.md's own listed
        // candidate remedies) would silently stop three guesses from
        // locking anything, and a fixed count would then fail this test for
        // a reason that reads like a wall regression rather than a config
        // change (plan.md's reasoning for the C# suite's own LockProbeAsync
        // applies identically here).
        const realmRepresentation = await api.get(`${provider}/admin/realms/${REALM}`, {
          headers: { Authorization: `Bearer ${adminToken}` },
        });
        const { failureFactor } = (await realmRepresentation.json()) as { failureFactor?: number };
        expect(failureFactor, `'admin/realms/${REALM}' should report its own failureFactor`).not.toBeUndefined();

        for (let attempt = 1; attempt <= failureFactor! + 1; attempt++) {
          const wrong = await api.post(`${issuer}/protocol/openid-connect/token`, {
            form: {
              grant_type: 'password',
              client_id: MANAGEMENT_CLIENT_ID,
              username: WALL_USER,
              password: `WrongPassword${attempt}`,
              scope: 'openid',
            },
          });
          expect(
            wrong.status(),
            `wrong-password grant #${attempt} of ${failureFactor! + 1} must never be accepted`,
          ).toBeGreaterThanOrEqual(400);
        }

        // --- confirm locked: the correct password is now refused too ---
        const stillLocked = await api.post(`${issuer}/protocol/openid-connect/token`, {
          form: {
            grant_type: 'password',
            client_id: MANAGEMENT_CLIENT_ID,
            username: WALL_USER,
            password: WALL_PASSWORD,
            scope: 'openid',
          },
        });
        const stillLockedBody = (await stillLocked.json()) as { error?: string };
        expect(
          stillLocked.status(),
          `'${WALL_USER}' should be locked out after ${failureFactor! + 1} rapid wrong guesses`,
        ).toBe(400);
        expect(
          stillLockedBody.error,
          `expected the refusal to read 'invalid_grant', got error: '${stillLockedBody.error}'`,
        ).toBe('invalid_grant');

        // Control on the lock itself (mirrors spec 214 SC-2): the
        // attack-detection record, not only the endpoint's own refusal.
        const attackDetection = await api.get(
          `${provider}/admin/realms/${REALM}/attack-detection/brute-force/users/${userId}`,
          { headers: { Authorization: `Bearer ${adminToken}` } },
        );
        const record = (await attackDetection.json()) as { disabled?: boolean };
        expect(
          record.disabled,
          `'${WALL_USER}'s attack-detection record should report disabled == true once locked`,
        ).toBe(true);

        // --- register the network collector before forcing renewal ---
        // Registered here, not earlier: it must never see the interactive
        // sign-in above, or `providerPrompts` would be non-empty for a reason
        // that has nothing to do with the lockout.
        const grantTypes: string[] = [];
        const providerPrompts: string[] = [];
        page.on('request', (request) => {
          const url = request.url();
          if (request.method() === 'POST' && url.includes('/protocol/openid-connect/token')) {
            grantTypes.push(new URLSearchParams(request.postData() ?? '').get('grant_type') ?? '(none)');
          }
          if (url.includes('/protocol/openid-connect/auth')) {
            providerPrompts.push(url);
          }
        });

        const spentAccessToken = await expireStoredAccessToken(page);

        // A bare `localStorage` edit changes nothing until something re-reads
        // it: `automaticSilentRenew`'s timer is scheduled off the User object
        // the SDK already holds in memory, not off storage it does not poll.
        // `page.reload()` is the same mechanism `kiosk-identity-recovery.spec.ts`
        // uses to force the same re-read, on the same URL — which is also why
        // this stays on the layout instead of falling back to the picker.
        await page.reload();

        // Settle first, discriminate second. Immediately after a reload
        // neither `layout-grid` nor `identity-not-authorized` has mounted
        // yet, and `toHaveCount(0)` on either one is satisfied on its very
        // first poll regardless of which branch the app is actually on —
        // asserting it directly here would pass unconditionally rather than
        // discriminate anything. Waiting for *either* terminal state first
        // is what makes the assertion below capable of failing.
        await expect(
          page.getByTestId('layout-grid').or(page.getByTestId('identity-not-authorized')),
          'the wall should settle on either its layout or the not-authorized screen, not hang between them',
        ).toBeVisible({ timeout: 90_000 });

        // THE SEVERE-BRANCH DISCRIMINATOR, now that the page has settled.
        await expect(
          page.getByTestId('identity-not-authorized'),
          'a locked-out account must not refuse its own live session (A3 — the severe finding, if this fails)',
        ).toHaveCount(0);

        // No sign-in prompt anywhere on it.
        await expect(page.getByRole('button', { name: /sign in/i })).toHaveCount(0);
        await expect(page.locator('#username')).toHaveCount(0);

        // Still on the wall.
        await expect(
          page.getByTestId('layout-grid'),
          'a locked-out account must not blank a display that was already showing its wall',
        ).toBeVisible();

        // It renewed silently, without being handed to the identity provider.
        expect(grantTypes, 'the wall must renew through its stored grant while its account is locked out').toContain(
          'refresh_token',
        );
        expect(providerPrompts, 'and must never be sent to the provider for a person to sign in').toHaveLength(0);

        // The renewal was real: a different token than the one just marked
        // spent, still on the narrowed wall client.
        const renewedAccessToken = await storedAccessToken(page);
        expect(renewedAccessToken, 'the wall must have exchanged its grant, not kept the one it marked spent').not.toBe(
          spentAccessToken,
        );
        expect(claimsOf(renewedAccessToken)['azp'], 'and renewed on the wall client, not a wider one').toBe(
          'kiosk-wall',
        );
      } finally {
        // MANDATORY, unconditional. `wall-munich` is a seeded realm account
        // other e2e projects sign in as — deleting it is not an option, and
        // leaving it locked fails the next project for a reason that reads
        // exactly like an unrelated regression.
        const cleared = await api.delete(
          `${provider}/admin/realms/${REALM}/attack-detection/brute-force/users/${userId}`,
          { headers: { Authorization: `Bearer ${adminToken}` } },
        );
        expect(
          cleared.status(),
          `clearing '${WALL_USER}'s lockout must succeed — it is a seeded account other e2e projects depend on`,
        ).toBeLessThan(300);
      }
    } finally {
      await context.close();
      await api.dispose();
      // The profile holds a live offline grant; nothing keeps it after the run.
      await rm(profile, { recursive: true, force: true, maxRetries: 10, retryDelay: 250 });
    }
  });
});
