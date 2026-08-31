import { describe, it, expect, vi, afterEach } from 'vitest';

/**
 * Spec 052 — how a screen that should hold a long-lived grant is told from one
 * that should not.
 *
 * <p>
 * <b>The module reads its mode once, at import.</b> So each case here imports a
 * fresh copy with the environment already set; stubbing afterwards would assert
 * against a configuration the application never had.
 * </p>
 */

const loadConfigWith = async (mode: string | undefined) => {
  vi.resetModules();
  vi.stubEnv('VITE_KEYCLOAK_URL', 'http://keycloak.test');
  if (mode === undefined) {
    vi.stubEnv('VITE_KIOSK_MODE', '');
  } else {
    vi.stubEnv('VITE_KIOSK_MODE', mode);
  }

  const { oidcConfig } = await import('./auth.js');
  return { clientId: oidcConfig.client_id, scope: oidcConfig.scope ?? '' };
};

describe('A screen signs in as what it is configured to be (spec 052)', () => {
  afterEach(() => vi.unstubAllEnvs());

  it('Signs a wall display in as the wall client, asking for a grant that outlives the session', async () => {
    const { clientId, scope } = await loadConfigWith('wall');

    expect(clientId).toBe('kiosk-wall');
    expect(scope.split(' ')).toContain('offline_access');
  });

  it.each([undefined, '', 'operator', 'Wall-ish', 'kiosk'])(
    'Signs everything else in as the ordinary kiosk client with no long-lived grant (%s)',
    async (mode) => {
      const { clientId, scope } = await loadConfigWith(mode);

      expect(clientId).toBe('kiosk-web');
      expect(scope.split(' ')).not.toContain('offline_access');
    },
  );

  it('Accepts the mode however it is capitalised, because a deployment flag will be', async () => {
    const { clientId } = await loadConfigWith('WALL');

    expect(clientId).toBe('kiosk-wall');
  });

  /**
   * **The combination that must never exist**, stated as its own case.
   *
   * <p>
   * An optional scope refuses nobody only while nobody asks for it. An account
   * without the privilege that <i>requests</i> it is refused the entire sign-in
   * — so <c>kiosk-web</c> asking for <c>offline_access</c> locks out every
   * operator. That is spec 050's failure by another route, and the reason one
   * flag decides both halves rather than two flags decided separately.
   * </p>
   */
  it.each(['wall', 'WALL', undefined, '', 'operator'])(
    'Never asks the ordinary kiosk client for a long-lived grant (%s)',
    async (mode) => {
      const { clientId, scope } = await loadConfigWith(mode);

      const asksForLongLivedGrant = scope.split(' ').includes('offline_access');

      expect(
        clientId === 'kiosk-web' && asksForLongLivedGrant,
        'kiosk-web asking for offline_access refuses every account that lacks the privilege — which is every operator',
      ).toBe(false);
    },
  );

  /**
   * The narrowing only holds if the two clients are genuinely different. A mode
   * that selected the same client for both would satisfy every assertion above
   * about scopes and still leave a wall display carrying write authority.
   */
  it('Uses a different client for a wall display than for anything else', async () => {
    const wall = await loadConfigWith('wall');
    const ordinary = await loadConfigWith(undefined);

    expect(wall.clientId).not.toBe(ordinary.clientId);
  });
});
