import { afterEach, describe, expect, it, vi } from 'vitest';

/**
 * Spec 491 (#3245, ADR-0192): on the one origin the API base is the explicit relative path `/api`, so a request URL is
 * `/api/<context>/<route>`. `gateway.ts` reads its origin once, at module load, so each case stubs the environment and imports a
 * fresh module. The production guard (spec 011 FR-010) must keep refusing an empty value: a bundle that sets nothing still fails
 * loudly rather than quietly calling the page's own origin.
 */
describe('The API gateway origin on the one origin (spec 491, ADR-0192)', () => {
  afterEach(() => {
    vi.unstubAllEnvs();
    vi.resetModules();
  });

  it('Builds request URLs under the relative /api base', async () => {
    vi.stubEnv('PROD', true);
    vi.stubEnv('VITE_API_GATEWAY_URL', '/api');

    const { gatewayApiUrl } = await import('./gateway.js');

    expect(gatewayApiUrl('camera-catalog/cameras')).toBe('/api/camera-catalog/cameras');
  });

  it('Drops a trailing slash from the base, so the route is joined with exactly one', async () => {
    vi.stubEnv('PROD', true);
    vi.stubEnv('VITE_API_GATEWAY_URL', '/api/');

    const { gatewayApiUrl } = await import('./gateway.js');

    expect(gatewayApiUrl('system-variables/variables')).toBe('/api/system-variables/variables');
  });

  it('Still refuses a production build whose gateway origin is empty, naming the variable', async () => {
    vi.stubEnv('PROD', true);
    vi.stubEnv('VITE_API_GATEWAY_URL', '');

    await expect(import('./gateway.js')).rejects.toThrow(/VITE_API_GATEWAY_URL/);
  });
});
