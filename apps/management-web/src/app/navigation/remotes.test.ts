import { afterEach, describe, expect, it, vi } from 'vitest';

/**
 * Plan 316 §4.4 / §6 test 7 — `remotes.ts` is the shell's remote registry.
 * Issue #2780 added the second entry (`eventIngestion`); both prod-build
 * guards mirror `auth.ts`'s `VITE_KEYCLOAK_URL` check line for line: a prod
 * bundle with no remote URL would try to fetch `remoteEntry.js` from
 * localhost on the operator's machine — fail loudly instead, at module
 * load, naming the variable.
 *
 * Follows `apps/kiosk-web/src/app/wallMode.test.ts`'s pattern for a
 * module that reads its environment once, at import: each case resets the
 * module registry and stubs the environment before a fresh dynamic import.
 */
describe('The shell remote registry (plan 316 §4.4, issue #2780, mirrors auth.ts)', () => {
  afterEach(() => {
    vi.unstubAllEnvs();
    vi.resetModules();
  });

  it('Throws at module load, naming VITE_CAMERAS_REMOTE_URL, when it is unset in a production build', async () => {
    vi.stubEnv('PROD', true);
    vi.stubEnv('VITE_CAMERAS_REMOTE_URL', '');
    vi.stubEnv('VITE_EVENT_INGESTION_REMOTE_URL', 'http://localhost:5177');

    await expect(import('./remotes.js')).rejects.toThrow(/VITE_CAMERAS_REMOTE_URL/);
  });

  it('Throws at module load, naming VITE_EVENT_INGESTION_REMOTE_URL, when it is unset in a production build', async () => {
    vi.stubEnv('PROD', true);
    vi.stubEnv('VITE_CAMERAS_REMOTE_URL', 'http://localhost:5176');
    vi.stubEnv('VITE_EVENT_INGESTION_REMOTE_URL', '');

    await expect(import('./remotes.js')).rejects.toThrow(/VITE_EVENT_INGESTION_REMOTE_URL/);
  });

  it('Does not throw outside a production build, even when both URLs are unset', async () => {
    vi.stubEnv('PROD', false);
    vi.stubEnv('VITE_CAMERAS_REMOTE_URL', '');
    vi.stubEnv('VITE_EVENT_INGESTION_REMOTE_URL', '');

    await expect(import('./remotes.js')).resolves.toBeDefined();
  });

  it('Registers both remotes at their configured base URLs', async () => {
    vi.stubEnv('PROD', true);
    vi.stubEnv('VITE_CAMERAS_REMOTE_URL', 'http://localhost:5176');
    vi.stubEnv('VITE_EVENT_INGESTION_REMOTE_URL', 'http://localhost:5177');

    const { remotes } = await import('./remotes.js');

    expect(remotes).toEqual([
      { name: 'cameras', baseUrl: 'http://localhost:5176' },
      { name: 'eventIngestion', baseUrl: 'http://localhost:5177' },
    ]);
  });
});
