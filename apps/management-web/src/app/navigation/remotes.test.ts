import { afterEach, describe, expect, it, vi } from 'vitest';

/**
 * Plan 316 §4.4 / §6 test 7 (tasks.md T006, US3) — `remotes.ts` is the shell's
 * remote registry: `[{ name: 'cameras', baseUrl: VITE_CAMERAS_REMOTE_URL }]`
 * (plan.md §1). Its prod-build guard mirrors `auth.ts`'s `VITE_KEYCLOAK_URL`
 * check line for line (plan.md §4.4): a prod bundle with no remote URL would
 * try to fetch `remoteEntry.js` from localhost on the operator's machine —
 * fail loudly instead, at module load, naming the variable.
 *
 * **Red**: `apps/management-web/src/app/navigation/remotes.ts` does not exist
 * yet (US3 is new behaviour, tasks.md's colour table). T009 (frontend-engineer)
 * is the task that creates it and must leave these assertions unmodified.
 *
 * Follows `apps/kiosk-web/src/app/wallMode.test.ts`'s pattern for a
 * module that reads its environment once, at import: each case resets the
 * module registry and stubs the environment before a fresh dynamic import,
 * rather than stubbing after the fact and asserting against a configuration
 * the module never actually had.
 */
describe('The cameras remote registry (plan 316 §4.4, mirrors auth.ts)', () => {
  afterEach(() => {
    vi.unstubAllEnvs();
    vi.resetModules();
  });

  it('Throws at module load, naming VITE_CAMERAS_REMOTE_URL, when it is unset in a production build', async () => {
    vi.stubEnv('PROD', true);
    vi.stubEnv('VITE_CAMERAS_REMOTE_URL', '');

    await expect(import('./remotes.js')).rejects.toThrow(/VITE_CAMERAS_REMOTE_URL/);
  });

  it('Does not throw outside a production build, even when the URL is unset', async () => {
    vi.stubEnv('PROD', false);
    vi.stubEnv('VITE_CAMERAS_REMOTE_URL', '');

    await expect(import('./remotes.js')).resolves.toBeDefined();
  });

  it('Registers the cameras remote at the configured base URL', async () => {
    vi.stubEnv('PROD', true);
    vi.stubEnv('VITE_CAMERAS_REMOTE_URL', 'http://localhost:5176');

    const { remotes } = await import('./remotes.js');

    expect(remotes).toEqual([{ name: 'cameras', baseUrl: 'http://localhost:5176' }]);
  });
});
