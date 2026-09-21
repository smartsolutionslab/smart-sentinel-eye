import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { AuthProvider, useAuth } from 'react-oidc-context';
import { UserManager, User, WebStorageStateStore } from 'oidc-client-ts';
import type { BaseQueryApi } from '@reduxjs/toolkit/query';

// Spec 205 (#2301) US2: this file proves the *mechanism*, not merely the
// contract — it drives the real react-oidc-context dispatch-vs-microtask
// ordering that makes the gateway's 401 retry re-send a bearer that just
// failed. `App.test.tsx:32-42` mocks `react-oidc-context` wholesale, which is
// precisely why that file cannot see this race: a mocked `useAuth` never
// dispatches anything, so there is no ordering to get wrong. This file
// **must not** mock `react-oidc-context`.
//
// gateway.ts resolves its origin once at module load; stub it before
// importing, same as gateway.test.ts.
vi.stubEnv('VITE_API_GATEWAY_URL', 'http://gateway.test');
const { gatewayBaseQuery, setAccessTokenProvider, setOnSessionExpired, setSessionRenewer } =
  await import('@smart-sentinel-eye/shared/api/gateway');

const queryApi = {
  // `globalThis.` rather than the bare global: management-web's eslint config
  // does not whitelist `AbortController` (gateway.test.ts's own workspace
  // does), and this avoids adding one for a single test-only reference.
  signal: new globalThis.AbortController().signal,
  abort: () => undefined,
  dispatch: () => undefined,
  getState: () => ({}),
  extra: undefined,
  endpoint: 'test',
  type: 'query',
  forced: false,
} as unknown as BaseQueryApi;

const ok = () =>
  new Response(JSON.stringify({ ok: true }), {
    status: 200,
    headers: { 'content-type': 'application/json' },
  });
const unauthorized = () => new Response(null, { status: 401 });

// Same reasoning as gateway.test.ts's helper: RTK Query 2.12.0 calls
// `fetchFn(request)` with one argument, so the header lives on the `Request`
// at `calls[n][0]`.
const authorizationOf = (call: unknown[]): string | null => (call[0] as Request).headers.get('authorization');

const nowEpoch = () => Math.floor(Date.now() / 1000);

const userWith = (accessToken: string): User =>
  new User({
    access_token: accessToken,
    token_type: 'Bearer',
    profile: {
      sub: 'operator',
      iss: 'https://keycloak.test/realms/smart-sentinel-eye',
      aud: 'management-web',
      exp: nowEpoch() + 3600,
      iat: nowEpoch(),
    },
    expires_at: nowEpoch() + 3600,
  });

/**
 * Mirrors `App.tsx:26-33` — the same three setters, called with the same
 * expressions the real `AuthGate` uses today, rendered inside the real
 * `AuthProvider`. `App.test.tsx:205-217` is the guard that keeps this mirror
 * honest: US1 changes what that test asserts, so a future drift between the
 * mirror here and the original gets caught there, not silently.
 */
function Gate({ onExpired }: { onExpired: () => void }) {
  const auth = useAuth();

  setAccessTokenProvider(() => auth.user?.access_token);
  setSessionRenewer(() =>
    auth
      .signinSilent()
      .then((user) => user?.access_token)
      .catch(() => undefined),
  );
  setOnSessionExpired(onExpired);

  return <div data-testid="token">{auth.user?.access_token ?? 'none'}</div>;
}

const fetchMock = vi.fn();

describe('The 401 retry against the real react-oidc-context provider (#2301 US2)', () => {
  beforeEach(() => {
    fetchMock.mockReset();
    vi.stubGlobal('fetch', fetchMock);
    vi.spyOn(console, 'info').mockImplementation(() => undefined);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
    window.sessionStorage.clear();
  });

  it('A renewal racing a retry does not end the session', async () => {
    const manager = new UserManager({
      authority: 'https://keycloak.test/realms/smart-sentinel-eye',
      client_id: 'management-web',
      redirect_uri: 'http://localhost/',
      userStore: new WebStorageStateStore({ store: window.sessionStorage }),
    });
    await manager.storeUser(userWith('OLD-TOKEN'));

    // The only stub in this test, and the only reason it exists is to avoid a
    // network round trip: these are the exact two statements oidc-client-ts@
    // 3.5.0 performs before resolving a renewal on both of its real paths —
    // `_useRefreshToken` (dist/umd/oidc-client-ts.js:3209-3210) and
    // `_signinEnd`, reached from `_signin` (dist/umd/oidc-client-ts.js:3376,
    // 3378). Nothing about the ordering below is faked: `events.load` raises
    // the library's own `userLoaded` event, synchronously, before this
    // promise resolves — exactly as the real library does.
    const newUser = userWith('NEW-TOKEN');
    manager.signinSilent = vi.fn(async () => {
      await manager.storeUser(newUser);
      await manager.events.load(newUser);
      return newUser;
    });

    let expiredCalls = 0;

    render(
      <AuthProvider userManager={manager}>
        <Gate onExpired={() => (expiredCalls += 1)} />
      </AuthProvider>,
    );

    // Settle the initial render on a condition — never a fixed yield count
    // (ADR-0150). AuthProvider's mount effect resolves `userManager.getUser()`
    // asynchronously, so the first paint is not authenticated yet.
    expect(await screen.findByTestId('token')).toHaveTextContent('OLD-TOKEN');

    fetchMock.mockResolvedValueOnce(unauthorized()).mockResolvedValueOnce(ok());

    const result = await gatewayBaseQuery('cameras')('items', queryApi, {});

    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(authorizationOf(fetchMock.mock.calls[1]!)).toBe('Bearer NEW-TOKEN');
    expect(expiredCalls).toBe(0);
    expect(result.data).toEqual({ ok: true });
  });
});
