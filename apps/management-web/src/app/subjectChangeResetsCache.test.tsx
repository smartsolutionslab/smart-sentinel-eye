import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { configureStore } from '@reduxjs/toolkit';
import { render, screen, fireEvent, waitFor, act } from '@testing-library/react';
import { Provider } from 'react-redux';
import { AuthProvider, useAuth } from 'react-oidc-context';
import { UserManager, User, WebStorageStateStore } from 'oidc-client-ts';
import type { CameraDetail } from '@smart-sentinel-eye/shared/api/cameras.api';

// Spec 303 (#2524).
//
// This file drives the real `AuthProvider` + `UserManager` dispatch, the real
// gateway's 401-retry/renewal path, and a real RTK Query cache, exactly as
// `staleBearerRetry.test.tsx` (spec 205) does — because the bug this spec
// fixes is a property of the MECHANISM (oidc-client-ts's renewal ordering +
// RTK Query's own error-keeps-stale-data behaviour), not of any symbol the
// fix introduces. A mocked `useAuth`/`useGetCameraQuery` could not see it,
// for the same reason `App.test.tsx` cannot (see that file's own comment).
//
// The `Gate` probe below mirrors production `AuthGate` (`App.tsx`'s three
// setters, plus the hook under test) rather than importing `AuthGate`
// itself, so this file is not coupled to `App.tsx`'s routing/session-expiry
// chrome — only to the auth wiring spec.md §5's acceptance test describes.
//
// gateway.ts resolves its origin once at module load; stub it before
// importing anything that touches it, same as staleBearerRetry.test.tsx and
// CameraDetailPageNavigation.test.tsx.
vi.stubEnv('VITE_API_GATEWAY_URL', 'http://gateway.test');
const { setAccessTokenProvider, setSessionRenewer, setOnSessionExpired } = await import(
  '@smart-sentinel-eye/shared/api/gateway'
);
const { camerasApi } = await import('@smart-sentinel-eye/shared/api/cameras.api');
const { useResetApiCachesOnSubjectChange } = await import('./useResetApiCachesOnSubjectChange.js');

const ok = (body: CameraDetail) =>
  new Response(JSON.stringify(body), { status: 200, headers: { 'content-type': 'application/json' } });
const unauthorized = () => new Response(null, { status: 401 });
const notFound = () => new Response(null, { status: 404 });

const cameraRecord = (cameraIdentifier: string, name: string): CameraDetail => ({
  cameraIdentifier,
  version: 1,
  fab: 'fab-1',
  name,
  rtspUrl: `rtsp://${cameraIdentifier}`,
  registeredAt: '2026-01-01T00:00:00Z',
  status: 'Registered',
});

// RTK Query 2.12.0 calls `fetchFn(request)` with one argument, so the header
// lives on the `Request` at `calls[n][0]` (same reasoning as gateway.test.ts
// and staleBearerRetry.test.tsx).
const authorizationOf = (call: unknown[]): string | null => (call[0] as Request).headers.get('authorization');

const nowEpoch = () => Math.floor(Date.now() / 1000);

/** `staleBearerRetry.test.tsx`'s helper, extended with `sub` per plan.md §4. */
const userWith = (accessToken: string, sub: string): User =>
  new User({
    access_token: accessToken,
    token_type: 'Bearer',
    profile: {
      sub,
      iss: 'https://keycloak.test/realms/smart-sentinel-eye',
      aud: 'management-web',
      exp: nowEpoch() + 3600,
      iat: nowEpoch(),
    },
    expires_at: nowEpoch() + 3600,
  });

function createStore() {
  return configureStore({
    reducer: { [camerasApi.reducerPath]: camerasApi.reducer },
    middleware: (getDefault) => getDefault().concat(camerasApi.middleware),
  });
}

function createManager(): UserManager {
  return new UserManager({
    authority: 'https://keycloak.test/realms/smart-sentinel-eye',
    client_id: 'management-web',
    redirect_uri: 'http://localhost/',
    userStore: new WebStorageStateStore({ store: window.sessionStorage }),
    // Otherwise defaults to true: the real UserManager would arm its own
    // background renewal timer, which these tests don't need and which
    // would outlive them unstopped (same reasoning as staleBearerRetry.test.tsx).
    automaticSilentRenew: false,
  });
}

/**
 * Mirrors `App.tsx`'s `AuthGate` — the same three setters, called with the
 * same expressions the real `AuthGate` uses today — around a probe that
 * renders a cached camera, exactly as spec.md §5's acceptance test describes.
 */
function Gate({ cameraId }: { cameraId: string }) {
  const auth = useAuth();
  useResetApiCachesOnSubjectChange();

  setAccessTokenProvider(() => auth.user?.access_token);
  setSessionRenewer(() =>
    auth
      .signinSilent()
      .then((user) => user?.access_token)
      .catch(() => undefined),
  );
  setOnSessionExpired(() => undefined);

  return <CameraProbe cameraId={cameraId} />;
}

function CameraProbe({ cameraId }: { cameraId: string }) {
  const { data, error, refetch } = camerasApi.useGetCameraQuery({ cameraIdentifier: cameraId });

  return (
    <div>
      <span data-testid="camera-name">{data?.name ?? 'none'}</span>
      {error !== undefined ? <span data-testid="camera-error">refused</span> : null}
      <button type="button" data-testid="refetch" onClick={() => void refetch()}>
        refetch
      </button>
    </div>
  );
}

const fetchMock = vi.fn();

describe('A changed OIDC subject and the RTK Query cache (spec 303, #2524 observation 2)', () => {
  beforeEach(() => {
    fetchMock.mockReset();
    vi.stubGlobal('fetch', fetchMock);
    vi.spyOn(console, 'info').mockImplementation(() => undefined);
    // Reset gateway.ts's module singletons — the real module is not mocked in
    // this file, so a renewer/handler a previous test's Gate registered would
    // otherwise outlive it (same reasoning as staleBearerRetry.test.tsx).
    setAccessTokenProvider(() => undefined);
    setSessionRenewer(() => Promise.resolve(undefined));
    setOnSessionExpired(() => undefined);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
    window.sessionStorage.clear();
  });

  // Plan.md §4 test 4 — a green-before guard against over-resetting. Recorded
  // honestly: this scenario never depends on whether a reset mechanism
  // exists at all (routine renewal for the SAME subject has nothing to
  // reset), so it is expected to be green today, unmodified.
  it('A same-subject renewal keeps the cached camera rendered and triggers no extra fetch', async () => {
    const manager = createManager();
    await manager.storeUser(userWith('A-TOKEN', 'operator-a'));

    fetchMock.mockResolvedValueOnce(ok(cameraRecord('camera-guard', 'Camera Guard')));

    render(
      <Provider store={createStore()}>
        <AuthProvider userManager={manager}>
          <Gate cameraId="camera-guard" />
        </AuthProvider>
      </Provider>,
    );

    expect(await screen.findByTestId('camera-name')).toHaveTextContent('Camera Guard');
    expect(fetchMock).toHaveBeenCalledTimes(1);

    const renewedSameSubject = userWith('A-TOKEN-2', 'operator-a');
    manager.signinSilent = vi.fn(async () => {
      await manager.storeUser(renewedSameSubject);
      await manager.events.load(renewedSameSubject);
      return renewedSameSubject;
    });

    await manager.signinSilent();

    // The renewal alone must not touch the cache or issue a request: no new
    // fetch call, and the cached name is unchanged (FR-003).
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId('camera-name')).toHaveTextContent('Camera Guard');
    expect(screen.queryByTestId('camera-error')).not.toBeInTheDocument();
  });

  // Plan.md §4 test 5. Covers the reachable-today path: a renewal that
  // resolves to a different subject must clear the stale camera before the
  // gateway's 401 retry lands.
  it('A renewal that resolves to a different subject clears the stale camera on the 401 retry', async () => {
    const manager = createManager();
    await manager.storeUser(userWith('A-TOKEN', 'operator-a'));

    fetchMock.mockResolvedValueOnce(ok(cameraRecord('camera-switch', 'Camera A View')));

    const store = createStore();

    render(
      <Provider store={store}>
        <AuthProvider userManager={manager}>
          <Gate cameraId="camera-switch" />
        </AuthProvider>
      </Provider>,
    );

    expect(await screen.findByTestId('camera-name')).toHaveTextContent('Camera A View');

    // The only stub in this test, and the only reason it exists is to avoid a
    // network round trip to a real Keycloak: these are the two statements
    // oidc-client-ts's own silent-renewal path performs once a renewal is
    // accepted (`staleBearerRetry.test.tsx:120-131`). This is a SIMPLIFIED
    // simulation of a subject change, not a faithful replica of
    // `signinSilent`'s full validation: the real library's refresh-token path
    // defaults `validateSubOnSilentRenew` to `true` and rejects a genuinely
    // different `sub` before ever raising `userLoaded` (security review,
    // #2524 — see `auth.test.ts`'s guard and spec.md §2). This stub bypasses
    // that check on purpose, to exercise this hook's defense-in-depth for the
    // OIDC flows where a real subject change over `userLoaded` IS reachable
    // (`signinPopup`, `signinResourceOwnerCredentials`, or a future/accidental
    // `validateSubOnSilentRenew: false`) without standing up one of those
    // flows end to end. `events.load` itself does raise the library's own
    // `userLoaded` event synchronously, same as the real library.
    manager.signinSilent = vi.fn(async () => {
      const userB = userWith('B-TOKEN', 'operator-b');
      await manager.storeUser(userB);
      await manager.events.load(userB);
      return userB;
    });

    // The retry's refetch: 401 first (the token A still holds), then 404
    // under B's bearer (B may not see camera "camera-switch"). Any further
    // call (e.g. RTK Query's own resubscribe) gets a default 404 too, rather
    // than an unstubbed `undefined` surfacing as an accidental fetch error.
    fetchMock
      .mockResolvedValueOnce(unauthorized())
      .mockImplementationOnce(() => {
        // Proves the reset landed BEFORE the retry went out, not merely that
        // the end state eventually converges (frontend review, #2524): a
        // render-effect design watching `auth.user` instead of the
        // `userLoaded` event would still pass the end-state assertions below,
        // because the retry/resubscribe cycle still converges — but it would
        // let this retry go out while the cache still held A's record.
        const state = store.getState() as Record<
          string,
          { queries: Record<string, { data?: { name?: string } } | undefined> } | undefined
        >;
        const staleUnderA = Object.values(state[camerasApi.reducerPath]?.queries ?? {}).some(
          (entry) => entry?.data?.name === 'Camera A View',
        );
        expect(staleUnderA).toBe(false);
        return Promise.resolve(notFound());
      });
    fetchMock.mockResolvedValue(notFound());

    fireEvent.click(screen.getByTestId('refetch'));

    await waitFor(() => {
      expect(screen.getByTestId('camera-error')).toBeInTheDocument();
    });

    // The cache was reset ahead of the retry, so A's name is no longer
    // rendered next to the refusal — an operator sees only the refusal
    // (spec.md US1 scenario 2).
    expect(screen.getByTestId('camera-name')).toHaveTextContent('none');

    // Confirms the renewal actually happened and the retry carried its new
    // token. Plan.md §4 test 5 asserts "the last request's Authorization is
    // Bearer B-TOKEN" — not an exact call count. RTK Query 2.12's own hooks
    // middleware resubscribes the still-mounted query when `resetApiCaches`
    // clears its subscription, firing one extra real fetch beside gateway's
    // explicit 401 retry (the "(and any resubscribe fetch 404)" case the
    // plan already names) — so the total can be 3 or 4 depending on timing.
    expect(authorizationOf(fetchMock.mock.calls.at(-1)!)).toBe('Bearer B-TOKEN');
  });

  // Phase-6 review (security, #2524): the scenario above goes through a 401,
  // where the OLD token is already known-bad. This scenario proves the fix
  // also holds when the old token is still VALID — reachable without any
  // 401 at all (`signinPopup`, `signinResourceOwnerCredentials`, or a
  // misconfigured `validateSubOnSilentRenew: false`) — so a resubscribe fetch
  // firing in the gap between the `userLoaded` event and `AuthGate`'s own
  // re-render cannot ride out under the previous subject's bearer and land
  // back in the cache this hook just cleared.
  it('A subject change with no 401 never lets a resubscribe fetch carry the old bearer', async () => {
    const manager = createManager();
    await manager.storeUser(userWith('A-TOKEN', 'operator-a'));

    fetchMock.mockResolvedValueOnce(ok(cameraRecord('camera-leak', 'Camera A View')));

    render(
      <Provider store={createStore()}>
        <AuthProvider userManager={manager}>
          <Gate cameraId="camera-leak" />
        </AuthProvider>
      </Provider>,
    );

    expect(await screen.findByTestId('camera-name')).toHaveTextContent('Camera A View');

    const callsBeforeChange = fetchMock.mock.calls.length;
    // No 401 is ever queued for this test: A's token is still valid. Any
    // call that reaches the mock unstubbed gets this default instead of an
    // accidental fetch error.
    fetchMock.mockResolvedValue(notFound());

    const userB = userWith('B-TOKEN', 'operator-b');
    await act(async () => {
      await manager.storeUser(userB);
      // Raised directly, with no 401 and no `signinSilent` stub involved —
      // the path a `signinPopup` or `signinResourceOwnerCredentials` renewal
      // would take, and the one a misconfigured
      // `validateSubOnSilentRenew: false` would also take.
      await manager.events.load(userB);
    });

    await waitFor(() => {
      expect(fetchMock.mock.calls.length).toBeGreaterThan(callsBeforeChange);
    });

    // Every request issued after the subject change — including any
    // RTK-Query-driven resubscribe fetch — must carry B's bearer. None may
    // carry A's: that would be the previous subject's token reaching the
    // network after the cache was supposedly cleared for them.
    for (const call of fetchMock.mock.calls.slice(callsBeforeChange)) {
      expect(authorizationOf(call)).not.toBe('Bearer A-TOKEN');
      expect(authorizationOf(call)).toBe('Bearer B-TOKEN');
    }
  });

  // Plan.md §4 test 6. An unload (no subject) must not reset anything
  // (FR-004); a later load with a genuinely different subject must.
  it('Unloading the user resets nothing, but a later load with a different subject does', async () => {
    const manager = createManager();
    await manager.storeUser(userWith('A-TOKEN', 'operator-a'));

    fetchMock.mockResolvedValueOnce(ok(cameraRecord('camera-unload', 'Camera A View')));

    render(
      <Provider store={createStore()}>
        <AuthProvider userManager={manager}>
          <Gate cameraId="camera-unload" />
        </AuthProvider>
      </Provider>,
    );

    expect(await screen.findByTestId('camera-name')).toHaveTextContent('Camera A View');

    await act(async () => {
      await manager.removeUser();
    });

    // Signing out is not a subject change (FR-004, bad-input scenario):
    // nothing resets on an absent subject. Green either way — this half
    // holds with or without the fix.
    expect(screen.getByTestId('camera-name')).toHaveTextContent('Camera A View');

    const userB = userWith('B-TOKEN', 'operator-b');
    await act(async () => {
      await manager.storeUser(userB);
      await manager.events.load(userB);
    });

    // A later load with a DIFFERENT subject counts as a change from A and
    // clears the cache.
    await waitFor(() => {
      expect(screen.getByTestId('camera-name')).toHaveTextContent('none');
    });
  });
});
