import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, act } from '@testing-library/react';
import { Provider } from 'react-redux';
import { type ReactNode } from 'react';

const oidcMocks = vi.hoisted(() => ({
  signinRedirect: vi.fn(() => Promise.resolve()),
  signinSilent: vi.fn(() => Promise.resolve<unknown>({ access_token: 'renewed' })),
}));

// The AuthGate registers its session hooks against the shared gateway module
// singletons; capture them so tests can drive the 401-renewal/expiry flow.
const sessionCallbacks = vi.hoisted(() => ({
  renew: undefined as (() => Promise<boolean>) | undefined,
  expired: undefined as (() => void) | undefined,
}));

vi.mock('@smart-sentinel-eye/shared/api/gateway', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/gateway')>();
  return {
    ...actual,
    setSessionRenewer: (renew: () => Promise<boolean>) => {
      sessionCallbacks.renew = renew;
    },
    setOnSessionExpired: (handler: () => void) => {
      sessionCallbacks.expired = handler;
    },
  };
});

// App is gated behind OIDC; render as an authenticated operator so these tests
// exercise the shell rather than the sign-in screen.
vi.mock('react-oidc-context', () => ({
  AuthProvider: ({ children }: { children: ReactNode }) => <>{children}</>,
  useAuth: () => ({
    isLoading: false,
    isAuthenticated: true,
    error: undefined,
    user: { access_token: 'test-token' },
    signinRedirect: oidcMocks.signinRedirect,
    signinSilent: oidcMocks.signinSilent,
  }),
}));

vi.mock('@smart-sentinel-eye/shared/api/cameras.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/cameras.api')>();
  return {
    ...actual,
    useListCamerasQuery: () => ({
      data: { items: [], count: 0, offset: 0, limit: 50 },
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    }),
    useRegisterCameraMutation: () => [vi.fn(async () => ({ data: 'noop' })), { isLoading: false, error: undefined, reset: vi.fn() }],
  };
});

vi.mock('@smart-sentinel-eye/shared/api/streams.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/streams.api')>();
  return {
    ...actual,
    useGetStreamQuery: () => ({ data: undefined, isLoading: false, error: undefined }),
    useListStreamsQuery: () => ({ data: [], isLoading: false, error: undefined }),
  };
});

vi.mock('@smart-sentinel-eye/shared/api/layouts.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/layouts.api')>();
  return {
    ...actual,
    useListLayoutsQuery: () => ({
      data: { chains: [], published: [] },
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    }),
    useCreateLayoutDraftMutation: () => [vi.fn(async () => ({ data: 'noop' })), { isLoading: false, error: undefined, reset: vi.fn() }],
    usePublishRevisionMutation: () => [vi.fn(async () => ({ data: 1 })), { isLoading: false }],
    useArchiveRevisionMutation: () => [vi.fn(async () => ({ data: 1 })), { isLoading: false }],
    useBranchDraftRevisionMutation: () => [vi.fn(async () => ({ data: 2 })), { isLoading: false }],
    useRevertRevisionMutation: () => [vi.fn(async () => ({ data: 1 })), { isLoading: false }],
  };
});

vi.mock('@smart-sentinel-eye/shared/api/overlays.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/overlays.api')>();
  return {
    ...actual,
    useListOverlaysQuery: () => ({
      data: { chains: [], published: [] },
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    }),
    useCreateOverlayDraftMutation: () => [vi.fn(async () => ({ data: 'noop' })), { isLoading: false, error: undefined, reset: vi.fn() }],
    usePublishOverlayRevisionMutation: () => [vi.fn(async () => ({ data: 1 })), { isLoading: false }],
    useArchiveOverlayRevisionMutation: () => [vi.fn(async () => ({ data: 1 })), { isLoading: false }],
    useBranchDraftOverlayRevisionMutation: () => [vi.fn(async () => ({ data: 2 })), { isLoading: false }],
    useRevertOverlayRevisionMutation: () => [vi.fn(async () => ({ data: 1 })), { isLoading: false }],
  };
});

vi.mock('@smart-sentinel-eye/shared/api/systemVariables.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/systemVariables.api')>();
  return {
    ...actual,
    useListVariablesQuery: () => ({
      data: [],
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    }),
    useGetVariableQuery: () => ({ data: undefined, isLoading: false }),
    useGetOverlaySnapshotQuery: () => ({ data: undefined, isLoading: false }),
    useDefineVariableMutation: () => [vi.fn(async () => ({ data: 'noop' })), { isLoading: false, error: undefined, reset: vi.fn() }],
    useSetVariableValueMutation: () => [vi.fn(async () => ({ data: 'noop' })), { isLoading: false }],
    useArchiveVariableMutation: () => [vi.fn(async () => ({ data: 'noop' })), { isLoading: false }],
  };
});

const { App } = await import('./App.js');
const { store } = await import('./app/store.js');
const { oidcConfig } = await import('./app/auth.js');

describe('App shell', () => {
  it('Renders the Cameras page heading and the Register button', () => {
    render(
      <Provider store={store}>
        <App />
      </Provider>,
    );
    expect(screen.getByRole('heading', { name: /cameras/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /register camera/i })).toBeInTheDocument();
  });

  it('Has navigation to the Layouts page', () => {
    render(
      <Provider store={store}>
        <App />
      </Provider>,
    );
    expect(screen.getByRole('button', { name: /^layouts$/i })).toBeInTheDocument();
  });

  it('Escalates an expired session to an explicit re-sign-in prompt', () => {
    render(
      <Provider store={store}>
        <App />
      </Provider>,
    );

    act(() => sessionCallbacks.expired?.());

    expect(screen.getByRole('heading', { name: /session expired/i })).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /^sign in$/i }));
    expect(oidcMocks.signinRedirect).toHaveBeenCalledWith({ state: { returnTo: '/' } });
  });

  it('Registers a session renewer that reports silent-renewal success and failure', async () => {
    render(
      <Provider store={store}>
        <App />
      </Provider>,
    );

    oidcMocks.signinSilent.mockResolvedValueOnce({ access_token: 'fresh' });
    await expect(sessionCallbacks.renew?.()).resolves.toBe(true);

    oidcMocks.signinSilent.mockResolvedValueOnce(null);
    await expect(sessionCallbacks.renew?.()).resolves.toBe(false);
  });

  it('Restores the stashed path in the sign-in callback', () => {
    oidcConfig.onSigninCallback?.({ state: { returnTo: '/layouts' } } as never);
    expect(window.location.pathname).toBe('/layouts');

    oidcConfig.onSigninCallback?.({ state: { returnTo: 'https://evil.example/' } } as never);
    expect(window.location.pathname).toBe('/');
  });
});
