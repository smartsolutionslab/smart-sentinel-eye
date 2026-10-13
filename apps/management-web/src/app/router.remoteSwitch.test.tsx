import { act, cleanup, render, screen, waitFor } from '@testing-library/react';
import { AuthProvider } from 'react-oidc-context';
import { UserManager, User, WebStorageStateStore } from 'oidc-client-ts';
import { Provider } from 'react-redux';
import { RouterProvider } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { NavigationProvider } from './navigation/NavigationProvider.js';
import { store } from './store.js';
import { createAppRouter } from './router.js';

/**
 * e2e/operator-shell-federation.spec.ts:85 ("cameras -> event-ingestion ->
 * cameras in one session") failed in CI with the Event types surface still on
 * screen after clicking Cameras. The two remote routes are separate route
 * objects whose elements are the same `<Gated><RemoteSurface/></Gated>` shape
 * at the same tree position, and a route element carries no key, so React
 * reused the mounted `RemoteSurface` and only changed its `remote` prop —
 * which `RemoteSurface` cannot react to once the target is already cached.
 * Same mechanism e8cc5fa8 fixed for `SURFACE_ELEMENTS`, missed here.
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

const operator = new User({
  access_token: 'ACCESS-TOKEN',
  token_type: 'Bearer',
  scope: 'sse.cameras.read sse.events.read',
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

const manifests: Record<string, unknown> = {
  'http://cameras.test/nav-manifest.json': {
    schemaVersion: 1,
    remote: 'cameras',
    basePath: '/cameras',
    entries: [{ path: '/cameras', label: 'Cameras', order: 10, requiredScopes: ['sse.cameras.read'] }],
  },
  'http://events.test/nav-manifest.json': {
    schemaVersion: 1,
    remote: 'eventIngestion',
    basePath: '/event-ingestion',
    entries: [{ path: '/event-ingestion', label: 'Event types', order: 80, requiredScopes: ['sse.events.read'] }],
  },
};

function CamerasSurfaceStub() {
  return <h1>Cameras stub</h1>;
}
function EventIngestionSurfaceStub() {
  return <h1>Event types stub</h1>;
}

describe('createAppRouter — switching between the two remote routes in one session', () => {
  beforeEach(() => {
    window.history.pushState({}, '', '/');
    registerRemotesMock.mockReset();
    loadRemoteMock.mockReset();
    loadRemoteMock.mockImplementation((moduleKey: string) =>
      Promise.resolve({
        default: moduleKey.startsWith('cameras/') ? CamerasSurfaceStub : EventIngestionSurfaceStub,
      }),
    );
    vi.stubGlobal(
      'fetch',
      vi.fn((url: string) =>
        Promise.resolve(
          new Response(JSON.stringify(manifests[url]), {
            status: 200,
            headers: { 'content-type': 'application/json' },
          }),
        ),
      ),
    );
  });

  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
    window.sessionStorage.clear();
    window.history.pushState({}, '', '/');
  });

  it('cameras -> event-ingestion -> cameras renders the matching surface at every step, not the previous remote’s', async () => {
    window.history.pushState({}, '', '/cameras');
    const router = createAppRouter();
    render(
      <AuthProvider userManager={managerWith(operator)}>
        <NavigationProvider
          remotes={[
            { name: 'cameras', baseUrl: 'http://cameras.test' },
            { name: 'eventIngestion', baseUrl: 'http://events.test' },
          ]}
        >
          <Provider store={store}>
            <RouterProvider router={router} />
          </Provider>
        </NavigationProvider>
      </AuthProvider>,
    );

    expect(await screen.findByRole('heading', { name: 'Cameras stub' })).toBeInTheDocument();

    await act(async () => {
      await router.navigate('/event-ingestion');
    });
    expect(await screen.findByRole('heading', { name: 'Event types stub' })).toBeInTheDocument();

    await act(async () => {
      await router.navigate('/cameras');
    });
    await waitFor(() => expect(screen.getByRole('heading', { name: 'Cameras stub' })).toBeInTheDocument());
    expect(screen.queryByRole('heading', { name: 'Event types stub' })).not.toBeInTheDocument();
  });
});
