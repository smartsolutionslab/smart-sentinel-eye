import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { configureStore } from '@reduxjs/toolkit';
import { render, screen, fireEvent, waitFor, act } from '@testing-library/react';
import { Provider } from 'react-redux';
import { AuthProvider, useAuth } from 'react-oidc-context';
import { UserManager, User, WebStorageStateStore } from 'oidc-client-ts';
import type { CameraDetail } from '@smart-sentinel-eye/shared/api/cameras.api';

// Spec 303 (#2524) T001 — RED.
//
// This file drives the real `AuthProvider` + `UserManager` dispatch, the real
// gateway's 401-retry/renewal path, and a real RTK Query cache, exactly as
// `staleBearerRetry.test.tsx` (spec 205) does — because the bug this spec
// fixes is a property of the MECHANISM (oidc-client-ts's renewal ordering +
// RTK Query's own error-keeps-stale-data behaviour), not of any symbol the
// fix introduces. A mocked `useAuth`/`useGetCameraQuery` could not see it,
// for the same reason `App.test.tsx` cannot (see that file's own comment).
//
// Deliberately, this file does NOT import `createSubjectWatcher` or
// `useResetApiCachesOnSubjectChange` — both are new files plan.md §1
// describes but T001 must not create (ADR-0139/0144: tests only). Vite's
// import-analysis statically resolves every import, dynamic or static,
// before a single test in the file runs; importing either nonexistent
// module collapses the WHOLE file to "0 tests" (verified directly: see this
// task's report). That would make every scenario here report as a
// collection failure, including plan.md §4 test 4's "same-subject renewal"
// guard, which the plan requires to be independently observed GREEN before
// the fix. So the `Gate` probe below mirrors today's REAL `AuthGate`
// (`App.tsx`'s three setters) with nothing added — which is also exactly
// what spec.md §5's own acceptance test describes: it never names the hook
// either, only the OIDC/gateway/RTK Query mechanics. See this task's report
// for the full reasoning and the two Vitest runs that prove the import
// behaviour above; it is flagged there as a discrepancy against plan.md's
// literal wording ("mirrors AuthGate's three setters and the new hook") for
// the architect/orchestrator to settle. Once wired, the implementer's hook
// still needs a call site IN THIS Gate for scenarios 5/6 below to turn
// green — that one-line addition is forward-wiring parity with production
// `AuthGate`, not a test assertion change, but it is a real gap this report
// calls out rather than silently papering over.
//
// gateway.ts resolves its origin once at module load; stub it before
// importing anything that touches it, same as staleBearerRetry.test.tsx and
// CameraDetailPageNavigation.test.tsx.
vi.stubEnv('VITE_API_GATEWAY_URL', 'http://gateway.test');
const { setAccessTokenProvider, setSessionRenewer, setOnSessionExpired } = await import(
  '@smart-sentinel-eye/shared/api/gateway'
);
const { camerasApi } = await import('@smart-sentinel-eye/shared/api/cameras.api');

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

  // Plan.md §4 test 5 — expected RED. Today's real defect: a renewal that
  // resolves to a different subject does not clear anything, so RTK Query
  // keeps A's stale `data` sitting next to the new error.
  it('A renewal that resolves to a different subject clears the stale camera on the 401 retry', async () => {
    const manager = createManager();
    await manager.storeUser(userWith('A-TOKEN', 'operator-a'));

    fetchMock.mockResolvedValueOnce(ok(cameraRecord('camera-switch', 'Camera A View')));

    render(
      <Provider store={createStore()}>
        <AuthProvider userManager={manager}>
          <Gate cameraId="camera-switch" />
        </AuthProvider>
      </Provider>,
    );

    expect(await screen.findByTestId('camera-name')).toHaveTextContent('Camera A View');

    // The only stub in this test, and the only reason it exists is to avoid a
    // network round trip to a real Keycloak: these are the exact two
    // statements oidc-client-ts performs before resolving a renewal
    // (`staleBearerRetry.test.tsx:120-131`). `events.load` raises the
    // library's own `userLoaded` event, synchronously, exactly as the real
    // library does — nothing about the ordering is faked.
    manager.signinSilent = vi.fn(async () => {
      const userB = userWith('B-TOKEN', 'operator-b');
      await manager.storeUser(userB);
      await manager.events.load(userB);
      return userB;
    });

    // The retry's refetch: 401 first (the token A still holds), then 404
    // under B's bearer (B may not see camera "camera-switch").
    fetchMock.mockResolvedValueOnce(unauthorized()).mockResolvedValueOnce(notFound());

    fireEvent.click(screen.getByTestId('refetch'));

    await waitFor(() => {
      expect(screen.getByTestId('camera-error')).toBeInTheDocument();
    });

    // RED today: the cache was never reset ahead of the retry, so A's name is
    // still rendered right next to the refusal, where an operator should see
    // only the refusal (spec.md US1 scenario 2).
    expect(screen.getByTestId('camera-name')).toHaveTextContent('none');

    // Confirms the renewal actually happened and the retry carried its token
    // (would only be reached once the assertion above holds).
    expect(fetchMock).toHaveBeenCalledTimes(3);
    expect(authorizationOf(fetchMock.mock.calls[2]!)).toBe('Bearer B-TOKEN');
  });

  // Plan.md §4 test 6 — expected RED on the second half only. An unload (no
  // subject) must not reset anything (FR-004); a later load with a genuinely
  // different subject must.
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

    // RED today: a later load with a DIFFERENT subject should count as a
    // change from A and clear the cache, but nothing resets anything yet.
    await waitFor(() => {
      expect(screen.getByTestId('camera-name')).toHaveTextContent('none');
    });
  });
});
