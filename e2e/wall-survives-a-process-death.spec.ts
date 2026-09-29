import { test, expect, chromium, type BrowserContext, type TestInfo } from '@playwright/test';
import { mkdtemp, readdir, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { openFirstLayout } from './support/kiosk-session';
import { claimsOf, expireStoredAccessToken, signInAsWallDisplay, storedAccessToken } from './support/wall-session';

/**
 * Spec 107 — a wall display returns after its **browser process actually died**.
 *
 * <p>
 * Every other "restart" in this suite is a reconstruction: state is read out of
 * one context in JavaScript and written into a fresh one before the page loads
 * (`kiosk-comes-back.spec.ts:47`, `wall-outlives-its-session.spec.ts:110`). That
 * proves the app can spend a grant it is <i>handed</i>. It does not prove a
 * grant reaches disk and is read back by a <b>different OS process</b>, which is
 * what a power cut requires and the only thing ADR-0131 actually rests on.
 * </p>
 *
 * <p>
 * So this file launches a persistent Chromium profile, signs in, expires the
 * access token in place, ends the process, and relaunches on the same directory.
 * <b>Nothing is written into the second process.</b> If a `localStorage.setItem`
 * ever appears here, the test has become the reconstruction it exists to
 * replace.
 * </p>
 */

const WALL = 'http://localhost:5175/';

/**
 * Where the boot-time grant is stashed, before any application code runs.
 *
 * <p>
 * A file-scoped shape, cast at each use, rather than a `declare global` on
 * `Window`: a project-wide augmentation from a spec file collides with any
 * other branch that adds the same member, which is the ADR-0109 contention
 * shape this file otherwise avoids.
 * </p>
 */
type BootWindow = { __grantAtBoot?: string | null };

/**
 * Stops a hand-started trace, keeping it **only when something failed**.
 *
 * <p>
 * `trace: 'on-first-retry'` never fires for a context the fixtures did not
 * create, and a Linux-runner failure with no trace is the worst place this test
 * can end up — but a file under `outputPath` is not attached to the HTML report,
 * so a trace nobody links to helps nobody. Written *and* attached on failure;
 * discarded on green.
 * </p>
 *
 * <p>
 * The decision reads a caller-held flag rather than `testInfo.status`: that
 * field is only meaningful once the test has finished, and these contexts are
 * closed while it is still running, so consulting it here would write a trace on
 * every green run.
 * </p>
 */
async function stopTracing(context: BrowserContext, name: string, failed: boolean, testInfo: TestInfo): Promise<void> {
  if (!failed) {
    await context.tracing.stop();
    return;
  }

  const trace = testInfo.outputPath(`${name}.zip`);
  await context.tracing.stop({ path: trace });
  await testInfo.attach(name, { path: trace, contentType: 'application/zip' });
}

test.describe('A wall survives a process death (spec 107 US1)', () => {
  // `{}` rather than a named parameter, and Playwright requires it: the runner
  // parses the first parameter to work out which fixtures a test needs, and
  // rejects anything that is not an object destructuring pattern ("First
  // argument must use the object destructuring pattern"). This test needs no
  // fixture, only `testInfo`, so the pattern has to be present and empty.
  // eslint-disable-next-line no-empty-pattern
  test('a wall display comes back after its browser process dies', async ({}, testInfo) => {
    test.setTimeout(300_000);

    // **Outside `test-results/`, deliberately.** `testInfo.outputPath` is
    // uploaded by `ci.yml`'s `if: always()` artifact step with 14-day retention
    // on a public repository, and this profile's LevelDB holds `wall-munich`'s
    // *offline* refresh token — a grant a sibling test asserts has no `exp`
    // (`wall-outlives-its-session.spec.ts:68`). `mkdtemp` is unique by
    // construction, so a CI retry still starts from a profile that has never
    // been used; it is removed below whatever happens.
    const profile = await mkdtemp(join(tmpdir(), 'sse-wall-'));

    try {
      // --- process #1 -----------------------------------------------------
      // `ignoreHTTPSErrors` is NOT inherited by a manually launched context, and
      // Keycloak runs on a development certificate. Without it the refresh
      // exchange fails on certificate validation and the screen falls to a login
      // form — indistinguishable from the defect under test
      // (`kiosk-comes-back.spec.ts:70-78` records that incident).
      const first: BrowserContext = await chromium.launchPersistentContext(profile, { ignoreHTTPSErrors: true });
      await first.tracing.start({ screenshots: true, snapshots: true });

      let spentAccessToken: string;
      let firstFailed = false;
      try {
        const page = first.pages()[0] ?? (await first.newPage());
        await signInAsWallDisplay(page, { url: WALL, timeout: 90_000, expectPopulatedPicker: true });
        await openFirstLayout(page, { timeout: 90_000 });
        spentAccessToken = await expireStoredAccessToken(page, 'the first process should be holding a grant to expire');
      } catch (error) {
        firstFailed = true;
        throw error;
      } finally {
        await stopTracing(first, 'process-one', firstFailed, testInfo);
        // Awaited: two Chromiums on one user-data directory is undefined
        // behaviour, and closing is also what flushes Local Storage to disk.
        await first.close();
      }

      // The flush the comment above claims, checked on disk before anything
      // depends on it. Chromium's `SingletonLock`/`SingletonSocket` are
      // POSIX-only, so on Linux a `close()` that returns before the process is
      // reaped leaves launch #2 either blocking to the 300 s timeout or quietly
      // re-creating the profile. A named failure here beats both.
      // The listing is folded into the message because this check fires at the
      // one moment nothing can be looked at afterwards: process #1's trace has
      // just been discarded (green so far) and process #2 does not exist yet.
      const leveldb = await readdir(join(profile, 'Default', 'Local Storage', 'leveldb')).catch(() => null);
      const onDisk = await readdir(profile).catch(() => ['<profile unreadable>']);
      expect(
        leveldb,
        `the dead process must have flushed Local Storage to disk; the profile holds: ${onDisk.join(', ')}`,
      ).not.toBeNull();

      // --- process #2 — same directory, nothing handed over ----------------
      const second: BrowserContext = await chromium.launchPersistentContext(profile, { ignoreHTTPSErrors: true });
      await second.tracing.start({ screenshots: true, snapshots: true });

      // **The positive discriminator, registered before anything navigates.**
      // Recovery through the stored grant is a `grant_type=refresh_token` token
      // call and nothing else; a screen that reached the picker via the provider
      // would have gone through `/protocol/openid-connect/auth` on the way.
      //
      // Both halves were shown capable of failing rather than assumed to be
      // (spec §8): C2b leaves `grantTypes` **empty** and reddens the first, and a
      // probe that attached this same collector to process #1 — which does sign
      // in interactively — caught its one `/auth` request, so the second is
      // watching a live wire and not an empty one.
      const grantTypes: string[] = [];
      const providerPrompts: string[] = [];
      second.on('request', (request) => {
        const url = request.url();
        if (request.method() === 'POST' && url.includes('/protocol/openid-connect/token')) {
          grantTypes.push(new URLSearchParams(request.postData() ?? '').get('grant_type') ?? '(none)');
        }
        if (url.includes('/protocol/openid-connect/auth')) {
          providerPrompts.push(url);
        }
      });

      let secondFailed = false;
      try {
        const revived = second.pages()[0] ?? (await second.newPage());

        // Read at document start, before any application code. A read after the
        // page settles returns the *renewed* grant, whose expiry is in the
        // future — the assertion would then be unfailable, which is the #2054
        // shape this test exists to avoid.
        await revived.addInitScript(() => {
          const key = Object.keys(window.localStorage).find((candidate) => candidate.startsWith('oidc.user:'));
          (window as unknown as BootWindow).__grantAtBoot = key === undefined ? null : window.localStorage.getItem(key);
        });

        // Control — the restart is genuinely cookie-less. Read before the app
        // has a chance to set anything, so what is seen is what survived the
        // profile.
        //
        // **Spec §10 A2 was half wrong, and this is where it was caught.**
        // `KEYCLOAK_IDENTITY` — the httpOnly cookie that *is* the SSO session —
        // does not survive, as A2 predicted. `KEYCLOAK_SESSION` does: it carries
        // an explicit expiry, so Chromium persists it across the process death.
        // Spec §3.1 scenario 2 pre-committed the answer for exactly this
        // observation — record it and clear the cookie, never drop the control —
        // so the wall below recovers with no provider cookie of any kind.
        // Removing state can only make recovery harder, so this is not the
        // hand-over the file forbids.
        //
        // **The network assertion below is what actually excludes the
        // provider-session path**; this is the cheap corroborating control.
        const survived = (await second.cookies()).map((cookie) => cookie.name);
        expect(survived, 'the SSO identity cookie must not outlive the process that held it').not.toContain(
          'KEYCLOAK_IDENTITY',
        );
        // The A2 correction, asserted where a reader meets it rather than only in
        // the spec — and it is what makes the clear below real work. Re-reading
        // the jar *after* the clear on the same regex would assert only that
        // Playwright honours its own contract.
        expect(survived, 'KEYCLOAK_SESSION does survive a process death (spec §10 A2, corrected)').toContain(
          'KEYCLOAK_SESSION',
        );

        await second.clearCookies({ name: /^KEYCLOAK_|AUTH_SESSION_ID/ });

        await revived.goto(WALL);

        // `goto` resolves on `load`, and the failure this test exists to detect
        // is a `signinRedirect` — which can land *between* that and the read
        // below, destroying the execution context or re-running the init script
        // on the provider's document. Either way the boot-state control would
        // fire and blame profile persistence for a redirect. Name the redirect
        // instead.
        let atBoot: string | null;
        try {
          await revived.waitForLoadState('domcontentloaded');
          atBoot = await revived.evaluate(() => (window as unknown as BootWindow).__grantAtBoot ?? null);
        } catch (error) {
          expect(
            revived.url(),
            `the relaunched wall was sent to the provider instead of spending its grant: ${String(error)}`,
          ).toContain(WALL);
          throw error;
        }
        expect(revived.url(), 'the relaunched wall must not have been sent to the provider').toContain(WALL);

        // Control — the profile really was reused, and the token really was spent.
        expect(atBoot, 'the relaunched profile must carry the grant the first process wrote').not.toBeNull();
        const expiresAt = (JSON.parse(atBoot as string) as Record<string, number>)['expires_at'];
        expect(expiresAt, 'and its access token must already be spent').toBeLessThan(Math.floor(Date.now() / 1000));

        // THE CLAIM — what the wall shows, with nobody touching it.
        await expect(
          revived.getByRole('button', { name: /sign in/i }),
          'a wall display must come back without a person',
        ).toHaveCount(0, { timeout: 90_000 });
        await expect(revived.locator('#username'), 'and must never be asked for a credential').toHaveCount(0);
        // The picker, not the wall process #1 was showing: today the only
        // persisted UI state is `sse.auth.wasAuthenticated`
        // (`useSessionExpiry.ts:9`), so nothing restores the open layout. If
        // deep-link restoration is ever persisted, process #1's `openFirstLayout`
        // makes this the assertion that reports it.
        await expect(revived.getByRole('heading', { name: 'Pick a layout' })).toBeVisible({ timeout: 90_000 });
        // Populated, not merely present: an empty picker is what a fab-less token
        // produces and it renders without an error.
        await expect(revived.getByRole('listitem').first(), 'the picker must be populated').toBeVisible({
          timeout: 90_000,
        });

        await openFirstLayout(revived, { timeout: 90_000 });

        // The recovery was a renewal, not a re-use. The spent JWT is still valid
        // for an hour by the realm's clock, so only a *different* access token
        // proves `expires_at` was consulted and the grant was exchanged.
        const renewed = await storedAccessToken(revived, 'the recovered wall display should be holding a grant');
        expect(renewed, 'the wall must have exchanged its grant, not re-sent the token it marked spent').not.toBe(
          spentAccessToken,
        );
        // And renewed on the same narrowed client: a grant swapped for a wider
        // one renders identically (`wall-outlives-its-session.spec.ts:83`).
        expect(claimsOf(renewed)['azp'], 'and on the wall client, not a wider one').toBe('kiosk-wall');

        expect(grantTypes, 'the wall must come back by spending its stored grant').toContain('refresh_token');
        expect(providerPrompts, 'and must never be sent to the provider for a person to sign in').toHaveLength(0);
      } catch (error) {
        secondFailed = true;
        throw error;
      } finally {
        await stopTracing(second, 'process-two', secondFailed, testInfo);
        await second.close();
      }
    } finally {
      // The profile holds a live offline grant; nothing keeps it after the run.
      await rm(profile, { recursive: true, force: true, maxRetries: 10, retryDelay: 250 });
    }
  });
});
