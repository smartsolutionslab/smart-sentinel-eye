import { describe, it, expect, vi, afterEach } from 'vitest';

const loadConfigWith = async (mode: string, base: string) => {
  vi.resetModules();
  vi.stubEnv('VITE_KEYCLOAK_URL', 'http://keycloak.test');
  vi.stubEnv('VITE_KIOSK_MODE', mode);
  vi.stubEnv('BASE_URL', base);

  const { oidcConfig } = await import('./auth');
  return oidcConfig;
};

describe('The callback follows the base and the client follows the mode flag', () => {
  afterEach(() => vi.unstubAllEnvs());

  it('Signs a wall display in as the wall client while the callback follows its base', async () => {
    const config = await loadConfigWith('wall', '/wall/');

    expect(config.client_id).toBe('kiosk-wall');
    expect(config.redirect_uri).toBe(`${window.location.origin}/wall/oidc/callback`);
  });

  it('Takes the callback from the base alone, not from the mode flag', async () => {
    const config = await loadConfigWith('wall', '/kiosk/');

    expect(config.client_id).toBe('kiosk-wall');
    expect(config.redirect_uri).toBe(`${window.location.origin}/kiosk/oidc/callback`);
  });
});
