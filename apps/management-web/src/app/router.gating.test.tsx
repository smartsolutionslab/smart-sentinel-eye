import { cleanup, render, screen, waitFor } from '@testing-library/react';
import { AuthProvider } from 'react-oidc-context';
import { UserManager, User, WebStorageStateStore } from 'oidc-client-ts';
import { Provider } from 'react-redux';
import { RouterProvider } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { NavigationProvider } from './navigation/NavigationProvider.js';
import { store } from './store.js';
import { createAppRouter } from './router.js';

/**
 * Spec 316 (issue superseding #1008) T010 — RED (ADR-0139/0144), plan.md §4.3
 * / §6 test 5. Today `router.tsx` renders every route unconditionally — no
 * `<Gated>` wrapper exists yet (T011, US2) and `./navigation/NavigationProvider.ts(x)`
 * does not exist at all. Every case here fails on the missing module today;
 * once T011 lands and `NavigationProvider` exists but before `<Gated>` is
 * wired in, the fallback failure mode is the one spec.md's "unconditional"
 * language names directly — the remote route's `RemoteSurface` still
 * registers and loads regardless of scope.
 *
 * `registerRemotes`/`loadRemote` mocked exactly as `RemoteSurface.test.tsx`
 * already does, so a call to either is observable without a real
 * `:5176` dev server.
 *
 * <p>
 * <b>Judgment call (flagged):</b> same as `ShellLayout.navigation.test.tsx` —
 * `NavigationProvider` is assumed to wrap the routed tree, mirroring where
 * plan.md §4.3 says it mounts ("in RoutedApp, i.e. after authentication").
 * </p>
 */

const registerRemotesMock = vi.fn();
const loadRemoteMock = vi.fn();

vi.mock('@module-federation/runtime', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@module-federation/runtime')>();
  return {
    ...actual,
    registerRemotes: (...args: unknown[]) => registerRemotesMock(...args),
    loadRemote: (...args: unknown[]) => loadRemoteMock(...args),
  };
});

const nowEpoch = () => Math.floor(Date.now() / 1000);

const userWithScope = (scope: string): User =>
  new User({
    access_token: 'ACCESS-TOKEN',
    token_type: 'Bearer',
    scope,
    profile: {
      sub: 'operator',
      iss: 'https://keycloak.test/realms/smart-sentinel-eye',
      aud: 'management-web',
      exp: nowEpoch() + 3600,
      iat: nowEpoch(),
    },
    expires_at: nowEpoch() + 3600,
  });

function managerWith(user: User): UserManager {
  const manager = new UserManager({
    authority: 'https://keycloak.test/realms/smart-sentinel-eye',
    client_id: 'management-web',
    redirect_uri: 'http://localhost/',
    userStore: new WebStorageStateStore({ store: window.sessionStorage }),
    automaticSilentRenew: false,
  });
  void manager.storeUser(user);
  return manager;
}

const CAMERAS_BASE_URL = 'http://localhost:5176';

const VALID_CAMERAS_MANIFEST = {
  schemaVersion: 1,
  remote: 'cameras',
  basePath: '/cameras',
  entries: [{ path: '/cameras', label: 'Cameras', order: 10, requiredScopes: ['sse.cameras.read'] }],
};

function renderAppAt(path: string, manager: UserManager) {
  window.history.pushState({}, '', path);
  const router = createAppRouter();
  return render(
    <AuthProvider userManager={manager}>
      <NavigationProvider remotes={[{ name: 'cameras', baseUrl: CAMERAS_BASE_URL }]}>
        <Provider store={store}>
          <RouterProvider router={router} />
        </Provider>
      </NavigationProvider>
    </AuthProvider>,
  );
}

describe('createAppRouter — route visibility follows the session’s granted scopes (spec 316 FR-013, plan.md §4.3)', () => {
  const fetchMock = vi.fn();

  beforeEach(() => {
    window.history.pushState({}, '', '/');
    registerRemotesMock.mockReset();
    loadRemoteMock.mockReset();
    loadRemoteMock.mockImplementation(() => new Promise<never>(() => {}));
    fetchMock.mockReset();
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify(VALID_CAMERAS_MANIFEST), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      }),
    );
    vi.stubGlobal('fetch', fetchMock);
  });

  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
    window.sessionStorage.clear();
    window.history.pushState({}, '', '/');
  });

  it('An unentitled session visiting /cameras/x sees a "not available" panel, and the remote is never registered or loaded', async () => {
    const withoutCamerasRead = userWithScope('sse.audit.read sse.layouts.read');

    renderAppAt('/cameras/11111111-1111-1111-1111-111111111111', managerWith(withoutCamerasRead));

    await waitFor(() => expect(screen.getByText(/not available/i)).toBeInTheDocument());

    expect(registerRemotesMock).not.toHaveBeenCalled();
    expect(loadRemoteMock).not.toHaveBeenCalled();
  });

  it('An audit-only session lands on the Audit surface at "/" (first visible entry by order)', async () => {
    const auditOnly = userWithScope('sse.audit.read');

    renderAppAt('/', managerWith(auditOnly));

    await waitFor(() => expect(screen.getByRole('heading', { name: 'Audit' })).toBeInTheDocument());

    expect(registerRemotesMock).not.toHaveBeenCalled();
    expect(loadRemoteMock).not.toHaveBeenCalled();
  });
});
