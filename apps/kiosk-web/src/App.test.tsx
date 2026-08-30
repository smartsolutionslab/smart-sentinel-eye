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

  /**
   * **Spends the stored grant before asking a person** (spec 049, ADR-0131).
   *
   * <p>
   * A screen that signed in before but holds no session in this page's life has
   * restarted. It now tries the grant on disk first; only when that comes back
   * with nothing does it fall through to an interactive redirect. Previously it
   * redirected immediately, which after a restart lands on a login form because
   * the sign-in cookie is gone with the process.
   * </p>
   */
  /**
   * **Exercises the write, not a seeded value.** Every other case here seeds the
   * marker directly, so writing it to storage that dies with the process went
   * unnoticed — a mutation swapping the destination kept the whole suite green.
   *
   * <p>
   * It matters because the marker is what tells a restarted screen it has signed
   * in before. Written to the wrong place, a kiosk holding a perfectly usable
   * grant comes back showing a first-boot sign-in button for someone to press.
   * </p>
   */
  it('Records that it signed in where a restart can still read it', async () => {
    window.localStorage.clear();
    authState.current = {
      isAuthenticated: true,
      isLoading: false,
      error: undefined,
      user: { access_token: 'a-grant' },
      activeNavigator: undefined,
      signinRedirect: vi.fn(() => Promise.resolve()),
      signinSilent: vi.fn(() => Promise.resolve(null)),
      events: {
        addSilentRenewError: vi.fn(() => noopUnsubscribe),
        addAccessTokenExpired: vi.fn(() => noopUnsubscribe),
      },
    } as never;

    renderApp();

    await vi.waitFor(() =>
      expect(
        window.localStorage.getItem('sse.auth.wasAuthenticated'),
        'a restart must be able to read this back',
      ).not.toBeNull(),
    );
    expect(
      window.sessionStorage.getItem('sse.auth.wasAuthenticated'),
      'and it must not be written only where the process keeps it',
    ).toBeNull();
  });

  it('Tries the stored grant first, then redirects when it yields nothing', async () => {
    window.localStorage.setItem('sse.auth.wasAuthenticated', 'true');
    const auth = unauthenticatedAuth();
    authState.current = auth;

    renderApp();

    expect(auth.signinSilent, 'the grant on disk is tried before a person is asked').toHaveBeenCalledTimes(1);

    // The double resolves with no user, which is a spent grant. The redirect is
    // therefore a tick later, not synchronous — asserting it immediately is what
    // made this test fail when the silent attempt was added.
    await vi.waitFor(() => expect(auth.signinRedirect).toHaveBeenCalledTimes(1));
    expect(auth.signinRedirect).toHaveBeenCalledWith({ state: { returnTo: '/' } });
    expect(window.sessionStorage.getItem('sse.auth.redirectGuard')).not.toBeNull();
    expect(screen.queryByRole('button', { name: /^sign in$/i })).not.toBeInTheDocument();
  });

  /**
   * **The other side of the restart distinction, and it was untested.**
   *
   * <p>
   * A session expiring *mid-run* keeps the behaviour spec 011 FR-013 describes:
   * straight to an interactive redirect, no silent attempt. Only a screen that
   * has restarted — signed in before, never in this page's life — spends the
   * stored grant first.
   * </p>
   *
   * <p>
   * Found by mutation: removing the guard that tells those two apart changed
   * nothing that any test could see, so the distinction this feature deliberately
   * preserved was resting on nothing.
   * </p>
   */
  it('Redirects without trying the grant when a live session expires mid-run', async () => {
    window.localStorage.clear();
    const events = {
      addSilentRenewError: vi.fn(() => noopUnsubscribe),
      addAccessTokenExpired: vi.fn(() => noopUnsubscribe),
    };
    const signinRedirect = vi.fn(() => Promise.resolve());
    const signinSilent = vi.fn(() => Promise.resolve(null));

    // Held a session in this page's life…
    authState.current = {
      isAuthenticated: true,
      isLoading: false,
      error: undefined,
      user: { access_token: 'a-grant' },
      activeNavigator: undefined,
      signinRedirect,
      signinSilent,
      events,
    } as never;

    const view = renderApp();
    await vi.waitFor(() => expect(window.localStorage.getItem('sse.auth.wasAuthenticated')).not.toBeNull());

    // …and then lost it, without the page ever restarting.
    authState.current = {
      isAuthenticated: false,
      isLoading: false,
      error: undefined,
      user: undefined,
      activeNavigator: undefined,
      signinRedirect,
      signinSilent,
      events,
    } as never;
    // Rerendered inside the same Provider. Dropping it changes the root
    // element type, which remounts the tree and resets the very ref that
    // distinguishes a restart from this case — the test then reported a defect
    // that was its own.
    view.rerender(
      <Provider store={store}>
        <App />
      </Provider>,
    );

    await vi.waitFor(() => expect(signinRedirect).toHaveBeenCalledTimes(1));
    expect(signinSilent, 'a mid-run expiry goes straight to a person, as it always did').not.toHaveBeenCalled();
  });

  it('Shows the session-expired screen instead of redirecting again while the loop guard is fresh', async () => {
    window.localStorage.setItem('sse.auth.wasAuthenticated', 'true');
    window.sessionStorage.setItem('sse.auth.redirectGuard', String(Date.now()));
    const auth = unauthenticatedAuth();
    authState.current = auth;

    renderApp();

    // The stored grant is still tried — a fresh redirect guard means the *last*
    // interactive attempt looped, not that the grant is unusable.
    await vi.waitFor(() => expect(screen.getByRole('heading', { name: /session expired/i })).toBeInTheDocument());
    expect(auth.signinRedirect).not.toHaveBeenCalled();
  });

  it('Retry on the session-expired screen clears the guard and starts a fresh sign-in', () => {
    window.localStorage.setItem('sse.auth.wasAuthenticated', 'true');
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
