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
    await expect(page.getByRole('link', { name: /^cameras$/i })).toHaveAttribute('aria-current', 'page');
  });

  test('every shared singleton resolves to exactly one loaded instance in the federation share scope', async ({
    page,
  }) => {
    await signInAsOperator(page);

    // plan.md §3's singleton table, plus the shared package's own derived
    // keys (one per subpath under the trailing-slash shared key, T001
    // finding item 2) — checked by prefix rather than naming every subpath,
    // since the exact subpath list is an implementation detail of
    // `features/cameras` and `CameraDetailPage`, not this spec's contract.
    const singletonPackageNames = [
      'react',
      'react-dom',
      'react-router-dom',
      'react-redux',
      '@reduxjs/toolkit',
      'react-oidc-context',
    ];

    const loadedVersionCounts = await page.evaluate((packageNames: string[]) => {
      type SharedVersionEntry = { loaded?: boolean; lib?: unknown };
      type ShareScope = Record<string, Record<string, SharedVersionEntry>>;
      type FederationInstance = { shareScopeMap?: Record<string, ShareScope> };
      type FederationGlobal = { __INSTANCES__?: FederationInstance[] };

      const federation = (globalThis as unknown as { __FEDERATION__?: FederationGlobal }).__FEDERATION__;
      const instance = federation?.__INSTANCES__?.[0];
      const shareScope = instance?.shareScopeMap?.['default'] ?? {};

      function countLoaded(packageName: string): number {
        const matchingKeys = Object.keys(shareScope).filter(
          (key) => key === packageName || key.startsWith(`${packageName}/`),
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
        result[packageName] = countLoaded(packageName);
      }
      result['@smart-sentinel-eye/shared'] = countLoaded('@smart-sentinel-eye/shared');
      return result;
    }, singletonPackageNames);

    for (const packageName of singletonPackageNames) {
      expect(loadedVersionCounts[packageName], `${packageName} loaded version count`).toBe(1);
    }
    expect(
      loadedVersionCounts['@smart-sentinel-eye/shared'],
      '@smart-sentinel-eye/shared loaded version count',
    ).toBeGreaterThanOrEqual(1);
  });
});
