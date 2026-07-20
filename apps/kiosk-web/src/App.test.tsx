import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { Provider } from 'react-redux';
import type { ReactNode } from 'react';

const authState = vi.hoisted(() => ({ current: {} as Record<string, unknown> }));

vi.mock('react-oidc-context', () => ({
  AuthProvider: ({ children }: { children: ReactNode }) => <>{children}</>,
  useAuth: () => authState.current,
}));

const { App } = await import('./App.js');
const { store } = await import('./app/store.js');
const { oidcConfig } = await import('./app/auth.js');

const noopUnsubscribe = () => undefined;

function unauthenticatedAuth() {
  return {
    isAuthenticated: false,
    isLoading: false,
    error: undefined,
    user: undefined,
    activeNavigator: undefined,
    signinRedirect: vi.fn(() => Promise.resolve()),
    signinSilent: vi.fn(() => Promise.resolve(null)),
    events: {
      addSilentRenewError: vi.fn(() => noopUnsubscribe),
      addAccessTokenExpired: vi.fn(() => noopUnsubscribe),
    },
  };
}

const renderApp = () =>
  render(
    <Provider store={store}>
      <App />
    </Provider>,
  );

describe('Kiosk app shell', () => {
  beforeEach(() => {
    window.sessionStorage.clear();
    vi.spyOn(console, 'info').mockImplementation(() => undefined);
  });

  afterEach(() => {
    vi.restoreAllMocks();
    window.history.replaceState({}, '', '/');
  });

  it('Shows the sign-in screen on first boot when no user is authenticated', () => {
    const auth = unauthenticatedAuth();
    authState.current = auth;

    renderApp();

    expect(screen.getByRole('heading', { name: /smart sentinel eye/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /sign in/i })).toBeInTheDocument();
    expect(auth.signinRedirect).not.toHaveBeenCalled();
  });

  it('Redirects to sign-in once with the return path when a previously authenticated session expires', () => {
    window.sessionStorage.setItem('sse.auth.wasAuthenticated', 'true');
    const auth = unauthenticatedAuth();
    authState.current = auth;

    renderApp();

    expect(auth.signinRedirect).toHaveBeenCalledTimes(1);
    expect(auth.signinRedirect).toHaveBeenCalledWith({ state: { returnTo: '/' } });
    expect(window.sessionStorage.getItem('sse.auth.redirectGuard')).not.toBeNull();
    expect(screen.queryByRole('button', { name: /^sign in$/i })).not.toBeInTheDocument();
  });

  it('Shows the session-expired screen instead of redirecting again while the loop guard is fresh', () => {
    window.sessionStorage.setItem('sse.auth.wasAuthenticated', 'true');
    window.sessionStorage.setItem('sse.auth.redirectGuard', String(Date.now()));
    const auth = unauthenticatedAuth();
    authState.current = auth;

    renderApp();

    expect(screen.getByRole('heading', { name: /session expired/i })).toBeInTheDocument();
    expect(auth.signinRedirect).not.toHaveBeenCalled();
  });

  it('Retry on the session-expired screen clears the guard and starts a fresh sign-in', () => {
    window.sessionStorage.setItem('sse.auth.wasAuthenticated', 'true');
    window.sessionStorage.setItem('sse.auth.redirectGuard', String(Date.now()));
    const auth = unauthenticatedAuth();
    authState.current = auth;
    renderApp();

    fireEvent.click(screen.getByRole('button', { name: /sign in again/i }));

    expect(window.sessionStorage.getItem('sse.auth.redirectGuard')).toBeNull();
    expect(auth.signinRedirect).toHaveBeenCalledTimes(1);
    expect(auth.signinRedirect).toHaveBeenCalledWith({ state: { returnTo: '/' } });
  });

  it('Restores the stashed path and clears the guard in the sign-in callback', () => {
    window.sessionStorage.setItem('sse.auth.redirectGuard', String(Date.now()));

    oidcConfig.onSigninCallback?.({ state: { returnTo: '/layouts/abc' } } as never);

    expect(window.location.pathname).toBe('/layouts/abc');
    expect(window.sessionStorage.getItem('sse.auth.redirectGuard')).toBeNull();
  });

  it('Falls back to the root when the callback state has no usable return path', () => {
    window.history.replaceState({}, '', '/somewhere');

    oidcConfig.onSigninCallback?.({ state: { returnTo: 'https://evil.example/' } } as never);

    expect(window.location.pathname).toBe('/');
  });
});
