import { describe, it, expect, vi, afterEach } from 'vitest';

describe('The router serves its routes under the instance base', () => {
  afterEach(() => {
    vi.unstubAllEnvs();
    window.history.replaceState({}, '', '/');
  });

  it('Matches the layouts route for a path under the kiosk base', async () => {
    vi.stubEnv('BASE_URL', '/kiosk/');
    window.history.replaceState({}, '', '/kiosk/layouts/abc');
    vi.resetModules();

    const { router } = await import('./router');

    expect(router.basename).toBe('/kiosk');
    expect(router.state.matches.at(-1)?.route.path).toBe('/layouts/:layoutIdentifier');
  });
});
