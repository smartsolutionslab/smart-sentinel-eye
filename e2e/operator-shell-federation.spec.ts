import { test, expect } from '@playwright/test';
import { signInAsOperator } from './support/sign-in';

/**
 * Spec 316 (issue superseding #1008) T005 — RED (ADR-0139/0144), plan.md §6
 * tests 12(a) and 12(b). Nothing here is scaffolded yet: there is no
 * `management-cameras` Aspire resource on `:5176` (T008), no remote package
 * (T007), and no `RemoteSurface`/federation host config in the shell (T009).
 * Both cases below are expected to fail for that reason — a timed-out
 * response wait for (a), and an absent `__FEDERATION__` global for (b) — not
 * on an assertion mismatch inside a working federation.
 *
 * 12(c) (the narrowed-scope case) belongs to T010/US2, not this file — this
 * file's two cases are US1/US3's loading and singleton-sharing coverage
 * only, per the task split in tasks.md T005 vs T010.
 *
 * 12(c), added by T010: a session narrowed to drop "sse.cameras.read",
 * **through the token response alone** — `User.scope` changes, the real
 * access token used for API calls does not (FR-009 reads only `User.scope`;
 * API-level enforcement for a narrower token is spec 200's territory, not
 * this one's). No `<Gated>` wrapper and no `NavigationProvider` exist yet
 * (T010/T011), so today this is expected to fail exactly like (a)/(b) above:
 * on a request that never arrives / a heading that never disappears, not on
 * an assertion mismatch against a working gate.
 */

test.describe('operator shell federation (spec 316)', () => {
  test('navigating to /cameras fetches remoteEntry.js from :5176, and the camera list renders under the shell', async ({
    page,
  }) => {
    // Set up the expectation before signing in: the operator shell opens on
    // Cameras today (sign-in.ts's own assertion), so the first navigation
    // into the federated surface happens as part of landing there, not a
    // later click.
    const remoteEntryResponse = page.waitForResponse((response) => response.url().includes(':5176/remoteEntry.js'), {
      timeout: 15_000,
    });

    await signInAsOperator(page);

    const response = await remoteEntryResponse;
    expect(response.ok()).toBe(true);

    await expect(page.getByRole('heading', { name: 'Cameras', exact: true })).toBeVisible();
    // No aria-current assertion here: router.tsx:48-51 renders the cameras
    // remote directly at the index route rather than redirecting to
    // `/cameras`, by deliberate design (avoids a `<Navigate>` render-cycle
    // flash on cold load). The URL stays `/`, so the "Cameras" nav link
    // (which points at `/cameras`) never gets aria-current="page" there —
    // a known, accepted consequence, not something this test's scope (the
    // remote entry loading and rendering) covers.
  });

  test('every shared singleton resolves to exactly one loaded instance in the federation share scope', async ({
    page,
  }) => {
    await signInAsOperator(page);

    // plan.md §3's singleton table. Checked by exact key, not prefix: a
    // prefix match would wrongly fold in independently-correct singletons
    // that merely share a name prefix, e.g. `react/jsx-runtime` and
    // `react/jsx-dev-runtime` under `react`.
    const singletonPackageNames = [
      'react',
      'react-dom',
      'react-router-dom',
      'react-redux',
      '@reduxjs/toolkit',
      'react-oidc-context',
    ];

    const sharedPackageName = '@smart-sentinel-eye/shared';

    const { loadedVersionCounts, sharedLoadedCount } = await page.evaluate(
      ({ packageNames, sharedName }: { packageNames: string[]; sharedName: string }) => {
        type SharedVersionEntry = { loaded?: boolean; lib?: unknown };
        type ShareScope = Record<string, Record<string, SharedVersionEntry>>;
        type FederationInstance = { shareScopeMap?: Record<string, ShareScope> };
        type FederationGlobal = { __INSTANCES__?: FederationInstance[] };

        const federation = (globalThis as unknown as { __FEDERATION__?: FederationGlobal }).__FEDERATION__;
        const instance = federation?.__INSTANCES__?.[0];
        const shareScope = instance?.shareScopeMap?.['default'] ?? {};

        // S2 fix (spec 316 phase-6 review): the share scope is keyed by
        // version, so two copies loaded at the SAME version would already
        // collapse into one entry — the previous count summed across every
        // subpath key for every name, which could never catch that (the
        // counterfactual this test exists to catch: a `singleton: false`
        // drift). Counting per exact key instead catches it, since a second,
        // independently-loaded copy at the same version still shows up as a
        // second *loaded* entry under that one key.
        //
        // `useIn`/`from` looked like a stronger, per-consumer signal and an
        // earlier version of this test asserted on them — dropped after
        // dumping the real `shareScopeMap` live (phase-6 review follow-up):
        // for every genuine cross-app singleton (react, react-router-dom, …)
        // both apps load, `useIn` is `["shell"]` and `from` is `"shell"`,
        // never `"cameras"`, even though cameras is provably using it (this
        // test's own count, and test 1's rendered page, both confirm
        // sharing works). The only entries ever showing `"cameras"` are
        // modules ONLY cameras declares (e.g. its own schema, never shared
        // with the shell) — so these fields record whichever host first
        // registered a share key, not "every app that consumed it". There is
        // no field in this runtime version that proves "the remote used the
        // host's copy" more directly than the loaded-count already does.
        function countLoaded(packageName: string, allowSubpaths: boolean): number {
          const matchingKeys = Object.keys(shareScope).filter(
            (key) => key === packageName || (allowSubpaths && key.startsWith(`${packageName}/`)),
          );
          let loaded = 0;
          for (const key of matchingKeys) {
            const versions = shareScope[key] ?? {};
            for (const version of Object.values(versions)) {
              if (version.loaded === true || version.lib !== undefined) loaded += 1;
            }
          }
          return loaded;
        }

        const result: Record<string, number> = {};
        for (const packageName of packageNames) {
          result[packageName] = countLoaded(packageName, false);
        }

        // `@smart-sentinel-eye/shared` is the one name with real subpath
        // exports meant to collapse into several keys (one per subpath,
        // T001 finding item 2, e.g. `@smart-sentinel-eye/shared/api/cameras.api`)
        // — each subpath is its own singleton, so this name keeps the
        // weaker "at least one loaded, across every subpath" check, by
        // design, rather than "exactly one" against a single key.
        const sharedLoadedCount = countLoaded(sharedName, true);

        return { loadedVersionCounts: result, sharedLoadedCount };
      },
      { packageNames: singletonPackageNames, sharedName: sharedPackageName },
    );

    for (const packageName of singletonPackageNames) {
      // Exactly one loaded version at this exact key — not merely "at least
      // one" — is the actual singleton guarantee: two copies at different
      // versions, or (the counterfactual this test exists to catch) a
      // second, independently-loaded copy at the SAME version, both fail
      // this, where the old subpath-summing count could not.
      expect(loadedVersionCounts[packageName], `${packageName} loaded version count`).toBe(1);
    }
    expect(sharedLoadedCount, `${sharedPackageName} loaded version count`).toBeGreaterThanOrEqual(1);
  });

  test('a session narrowed to drop sse.cameras.read via the token response sees no Cameras, and the remote’s code is never fetched', async ({
    page,
  }) => {
    // Rewrites only the token response's "scope" field — the real access
    // token used for every API call is untouched (FR-009; spec.md's "Also
    // binding" note on this scenario). Set up before sign-in: the token POST
    // happens as part of the Keycloak redirect flow below.
    await page.route('**/protocol/openid-connect/token', async (route) => {
      const response = await route.fetch();
      const body = (await response.json()) as Record<string, unknown>;
      if (typeof body.scope === 'string') {
        body.scope = body.scope
          .split(' ')
          .filter((scope) => scope !== 'sse.cameras.read')
          .join(' ');
      }
      await route.fulfill({ response, json: body });
    });

    // Judgment call (flagged): spec.md's own acceptance scenario for this
    // case says "no request is made for the cameras remote's entry or
    // modules" — the remote's *code*. `nav-manifest.json` is static,
    // unauthenticated data the shell reads from every registered remote
    // regardless of entitlement, to learn each entry's requiredScopes in the
    // first place (FR-015: "Gating decides what is fetched as code, not what
    // is known") — the same port (:5176) serves both files. So this asserts
    // no request for the remote's federation entry specifically, not a
    // blanket zero requests to :5176, which would contradict FR-015's own
    // manifest-fetch behaviour (exercised unconditionally by every other
    // NavigationProvider case in this spec's unit-test coverage).
    const remoteEntryRequests: string[] = [];
    page.on('request', (request) => {
      if (request.url().includes(':5176/remoteEntry.js')) remoteEntryRequests.push(request.url());
    });

    // Not `signInAsOperator`: it asserts the Cameras heading at the end,
    // which is exactly what a narrowed session must NOT show.
    await page.goto('/');
    await page.getByRole('button', { name: /sign in/i }).click();
    await page.locator('#username').fill('operator');
    await page.locator('#password').fill('Cobalt-Meadow-Ripple-24');
    await page.locator('#kc-login').click();

    // A positive assertion first: proves the shell actually rendered (not
    // merely that it hasn't gotten around to showing Cameras yet, e.g. still
    // on the Keycloak redirect) before the absence checks below are read as
    // the gate having refused something, rather than as nothing having
    // rendered at all.
    await expect(page.getByRole('link', { name: /^layouts$/i })).toBeVisible();

    await expect(page.getByRole('heading', { name: 'Cameras', exact: true })).toHaveCount(0);
    await expect(page.getByRole('link', { name: /^cameras$/i })).toHaveCount(0);

    await page.keyboard.press('Control+K');
    const dialog = page.getByRole('dialog', { name: 'Go to' });
    await expect(dialog).toBeVisible();
    await expect(dialog.getByText('Cameras', { exact: true })).toHaveCount(0);
    await page.keyboard.press('Escape');

    // Waits for the cameras remote's manifest response before asserting
    // "not available": without this, the assertion could pass merely
    // because the manifest fetch (unconditional, FR-015) had not resolved
    // yet — exactly the same gap `Gated`'s own loading state covers now
    // (plan.md §4.3) — rather than because the gate actually refused this
    // path once it knew.
    const manifestResponse = page.waitForResponse((response) => response.url().includes(':5176/nav-manifest.json'));
    await page.goto('/cameras/11111111-1111-1111-1111-111111111111');
    await manifestResponse;

    await expect(page.getByText(/not available/i)).toBeVisible();

    expect(remoteEntryRequests, 'the cameras remote’s code must never be fetched for an unentitled session').toEqual(
      [],
    );
  });
});
