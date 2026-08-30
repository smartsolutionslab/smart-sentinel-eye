import { describe, it, expect, beforeEach, vi } from 'vitest';
import { WebStorageStateStore } from 'oidc-client-ts';

vi.stubEnv('VITE_KEYCLOAK_URL', 'http://keycloak.test');
const { oidcConfig } = await import('./auth.js');

/**
 * Spec 049 US1/US2 — how a kiosk comes back (ADR-0131).
 *
 * <p>
 * <b>Every case here starts from empty storage.</b> A check that begins signed
 * in proves nothing about coming back, which is the entire subject. This is the
 * third feature running where the convenient fixture is the one that hides the
 * defect — label text seeded at mount two features ago, a camera list resolved
 * at first render in the last one, and both shipped a defect because of it.
 * </p>
 */

describe('A kiosk keeps its grant across a restart (spec 049 US1)', () => {
  beforeEach(() => {
    window.localStorage.clear();
    window.sessionStorage.clear();
  });

  /**
   * The defect, stated as a property: tokens must not live somewhere the
   * browser process takes with it. That was the whole reason a reboot lost
   * everything regardless of any server-side session setting.
   */
  it('Keeps its tokens somewhere a restart does not destroy', async () => {
    expect(oidcConfig.userStore).toBeInstanceOf(WebStorageStateStore);

    // **Asserted through the store, not on its type.** The first version checked
    // only that a store was configured, and passed happily when the backing
    // storage was swapped back to the process-bound one — the exact defect this
    // feature removes. Found by mutation, which is the only reason the assertion
    // is written this way.
    //
    // Read back through fresh stores rather than raw storage keys: the store
    // prefixes what it writes, so a direct getItem would look for the wrong key
    // and report a false failure.
    await oidcConfig.userStore?.set('user:probe', JSON.stringify({ access_token: 'kept' }));

    const onDisk = new WebStorageStateStore({ store: window.localStorage });
    const withTheProcess = new WebStorageStateStore({ store: window.sessionStorage });

    expect(await onDisk.get('user:probe'), 'the grant must land on storage a restart does not take with it').toContain(
      'kept',
    );
    expect(await withTheProcess.get('user:probe'), 'and not in the storage the browser process destroys').toBeNull();
  });

  /**
   * **A restart, simulated the only way jsdom can: a fresh store over the same
   * storage.** That is what a rebooted kiosk does — new process, same disk.
   *
   * <p>
   * Induced from *empty*, then written, then read back through an instance that
   * never saw the write. Reading back through the same instance would pass
   * against an in-memory cache and prove nothing.
   * </p>
   */
  it('Reads back a grant written before the process ended', async () => {
    const beforeRestart = new WebStorageStateStore({ store: window.localStorage });
    expect(await beforeRestart.get('oidc.user:test'), 'nothing is stored yet').toBeNull();

    await beforeRestart.set('oidc.user:test', '{"access_token":"a-grant"}');

    const afterRestart = new WebStorageStateStore({ store: window.localStorage });
    expect(await afterRestart.get('oidc.user:test')).toContain('a-grant');
  });

  /**
   * The counterpart, and the one that fails if the store is ever moved back.
   * Process-bound storage loses the grant, which is the old behaviour.
   */
  it('Would lose the grant if it were kept with the process', async () => {
    const processBound = new WebStorageStateStore({ store: window.sessionStorage });
    await processBound.set('oidc.user:test', '{"access_token":"a-grant"}');

    // What survives a restart is what is on disk, and this wrote nowhere near it.
    const afterRestart = new WebStorageStateStore({ store: window.localStorage });
    expect(afterRestart.get('oidc.user:test')).resolves.toBeNull();
  });
});

describe('A kiosk asks for a grant that outlives the session ceiling (spec 049 US2)', () => {
  /**
   * The sign-in session ends on a ten-hour clock **regardless of activity**, so
   * a wall that never reboots still dropped to a prompt roughly twice a day per
   * screen. Only a grant outside that session escapes it.
   */
  it('Requests the long-lived grant', () => {
    expect(oidcConfig.scope?.split(' ')).toContain('offline_access');
  });

  /**
   * **Authority is unchanged, and this is the cheap place to notice if it ever
   * is not.** Unattended recovery must not be bought with a broader grant. The
   * six sse.* scopes are default client scopes, applied whether or not they are
   * asked for, so naming any of them here would be new authority arriving under
   * cover of a recovery feature.
   */
  it('Asks for nothing beyond signing in and coming back', () => {
    expect(oidcConfig.scope?.split(' ').sort()).toEqual(['offline_access', 'openid']);
  });

  /**
   * A screen nobody watches must never pass through the expired state on its
   * way back — renewing before expiry is what keeps the wall up rather than
   * recovering it after a gap.
   */
  it('Renews before expiry rather than recovering afterwards', () => {
    expect(oidcConfig.automaticSilentRenew).toBe(true);
  });
});
