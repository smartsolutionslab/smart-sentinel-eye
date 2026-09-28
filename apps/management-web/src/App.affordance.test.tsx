import { beforeEach, describe, it, expect, vi } from 'vitest';
import { render, screen, act } from '@testing-library/react';
import { Provider } from 'react-redux';
import { type ReactNode } from 'react';

/**
 * Spec 287 (issue #2623) T003 — RED. Covers the three `App.tsx` sign-in /
 * sign-in-failed sites (plan.md §5b). A separate file from `App.test.tsx`
 * (5a's characterisation, which must stay unmodified) because this needs a
 * *controllable* `react-oidc-context` mock: `App.test.tsx`'s mock is fixed to
 * an authenticated operator, and reaching the sign-in-failed screen needs
 * `auth.error` set, which its sessionCallbacks-only hook cannot drive.
 *
 * Expected red on develop, for the right reason: today these are raw
 * `<button>` elements styled with the triad (`bg-accent-active`,
 * `bg-accent-active/20`), not the shared `Button` primitive, so none of them
 * carries `hover:bg-accent-hover` / `border-border-strong` and all three
 * carry a class matching `/accent-active/`.
 */
const oidcMocks = vi.hoisted(() => ({
  signinRedirect: vi.fn(() => Promise.resolve()),
  signinSilent: vi.fn(() => Promise.resolve<unknown>({ access_token: 'renewed' })),
}));

// Mutable, unlike App.test.tsx's fixed authenticated mock — each test drives
// AuthGate into a different one of its four branches by setting these before
// rendering (or, for sessionExpired, via the captured setOnSessionExpired
// handler, same as App.test.tsx).
const authState = vi.hoisted(() => ({
  isLoading: false,
  isAuthenticated: false,
  error: undefined as Error | undefined,
  user: undefined as { access_token: string } | undefined,
}));

const sessionCallbacks = vi.hoisted(() => ({
  expired: undefined as (() => void) | undefined,
}));

vi.mock('@smart-sentinel-eye/shared/api/gateway', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/gateway')>();
  return {
    ...actual,
    setOnSessionExpired: (handler: () => void) => {
      sessionCallbacks.expired = handler;
    },
  };
});

vi.mock('react-oidc-context', () => ({
  AuthProvider: ({ children }: { children: ReactNode }) => <>{children}</>,
  useAuth: () => ({
    isLoading: authState.isLoading,
    isAuthenticated: authState.isAuthenticated,
    error: authState.error,
    user: authState.user,
    signinRedirect: oidcMocks.signinRedirect,
    signinSilent: oidcMocks.signinSilent,
  }),
}));

const { App } = await import('./App.js');
const { store } = await import('./app/store.js');

describe('App affordance — sign-in and crash actions use the shared Button (spec 287)', () => {
  beforeEach(() => {
    window.history.pushState({}, '', '/');
    authState.isLoading = false;
    authState.isAuthenticated = false;
    authState.error = undefined;
    authState.user = undefined;
    sessionCallbacks.expired = undefined;
  });

  it('The first-visit "Sign in" button is Button\'s primary variant, not the raw triad fill', async () => {
    render(
      <Provider store={store}>
        <App />
      </Provider>,
    );

    const signIn = await screen.findByRole('button', { name: /^sign in$/i });
    expect(signIn.className).toMatch(/hover:bg-accent-hover/);
    expect(signIn.className).not.toMatch(/accent-active/);
  });

  it('The session-expired "Sign in" button is Button\'s primary variant, not the raw triad fill', async () => {
    render(
      <Provider store={store}>
        <App />
      </Provider>,
    );

    expect(sessionCallbacks.expired).toBeDefined();
    act(() => sessionCallbacks.expired!());

    await screen.findByRole('heading', { name: /^session expired$/i });
    const signIn = await screen.findByRole('button', { name: /^sign in$/i });
    expect(signIn.className).toMatch(/hover:bg-accent-hover/);
    expect(signIn.className).not.toMatch(/accent-active/);
  });

  it('The sign-in-failed "Try again" button is Button\'s secondary variant, not the raw tinted triad fill', async () => {
    authState.error = new Error('sign-in exploded');

    render(
      <Provider store={store}>
        <App />
      </Provider>,
    );

    const tryAgain = await screen.findByRole('button', { name: /^try again$/i });
    expect(tryAgain.className).toMatch(/border-border-strong/);
    expect(tryAgain.className).not.toMatch(/accent-active/);
  });
});
