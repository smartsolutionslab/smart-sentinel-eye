import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { Provider } from 'react-redux';
import type { ReactNode } from 'react';

vi.mock('react-oidc-context', () => ({
  AuthProvider: ({ children }: { children: ReactNode }) => <>{children}</>,
  useAuth: () => ({
    isAuthenticated: false,
    isLoading: false,
    error: undefined,
    signinRedirect: () => Promise.resolve(),
    user: undefined,
  }),
}));

const { App } = await import('./App.js');
const { store } = await import('./app/store.js');

describe('Kiosk app shell', () => {
  it('Shows the sign-in screen when no user is authenticated', () => {
    render(
      <Provider store={store}>
        <App />
      </Provider>,
    );
    expect(screen.getByRole('heading', { name: /smart sentinel eye/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /sign in/i })).toBeInTheDocument();
  });
});
