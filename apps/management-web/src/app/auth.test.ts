import { describe, it, expect, vi } from 'vitest';

vi.stubEnv('VITE_KEYCLOAK_URL', 'http://keycloak.test');
const { oidcConfig } = await import('./auth.js');

// `AuthProviderProps` does not itself type `client_id`/`scope` (they belong to
// the underlying `oidc-client-ts` UserManagerSettings, structurally accepted
// on the object literal in auth.ts but not exposed on its declared export
// type). Cast locally rather than widening auth.ts's own type or adding
// oidc-client-ts as a new direct dependency of this app for a test-only read.
const config = oidcConfig as typeof oidcConfig & { client_id?: string; scope?: string };

/**
 * Spec 200 (issue #2279) — the console signs in as `management-web`, never
 * the retired `smart-sentinel-eye-web` and its `sse.management` bundle.
 *
 * <p>
 * <b>Nothing else pins this cheaply.</b> The only other guard on `auth.ts`'s
 * shape is `e2e/management-identity.spec.ts` (SC-5), which needs a live
 * Aspire stack and runs in CI's `e2e` job alone. A repointed `client_id` or a
 * scope regression would go unnoticed by every faster suite until then.
 * </p>
 */
describe('The console names its own client, not the retired bundle client (spec 200)', () => {
  it('Signs in as management-web', () => {
    expect(config.client_id).toBe('management-web');
  });

  /**
   * **Exact, not a subset check** (mirrors kiosk-web's own auth test) — a
   * subset check bounds authority above and passes when `sse.management` is
   * added back, which is exactly the regression this spec exists to prevent.
   */
  it('Asks for nothing beyond signing in', () => {
    expect(config.scope?.split(' ')).toEqual(['openid']);
  });
});

/**
 * Spec 303 (#2524) security review: `useResetApiCachesOnSubjectChange`'s
 * defense-in-depth for a subject change over `userLoaded` only matters for
 * the OIDC flows that can still reach that event with a genuinely different
 * `sub` — `oidc-client-ts`'s silent-renewal path already rejects one via
 * `validateSubOnSilentRenew`, which defaults to `true`. This guards the
 * premise: a future change to `oidcConfig` that disables that default would
 * reopen the headline scenario the library closes today, silently.
 */
describe('The console never disables the library default subject check (spec 303, #2524)', () => {
  it('Does not set validateSubOnSilentRenew to false', () => {
    const settings = oidcConfig as typeof oidcConfig & { validateSubOnSilentRenew?: boolean };

    expect(settings.validateSubOnSilentRenew).not.toBe(false);
  });
});
