import { useState, type ReactNode } from 'react';
import { AuthProvider, useAuth } from 'react-oidc-context';
import { RouterProvider } from 'react-router-dom';
import { setAccessTokenProvider, setOnSessionExpired, setSessionRenewer } from '@smart-sentinel-eye/shared/api/gateway';
import { oidcConfig } from './app/auth.js';
import { createAppRouter } from './app/router.js';

export function App() {
  return (
    <AuthProvider {...oidcConfig}>
      <AuthGate />
    </AuthProvider>
  );
}

function AuthGate() {
  const auth = useAuth();
  const [sessionExpired, setSessionExpired] = useState(false);

  // Register the bearer getter synchronously, before any child query dispatches,
  // so the first authenticated REST call already carries the token (ADR-0007/
  // 0008). A useEffect here fires AFTER the child query's mount effect (effects
  // run child-first), which races the token and 401s the first request. Setting
  // a module-level getter during render is idempotent. The session renewer and
  // expiry escalation (spec 011 FR-012/014) register the same way.
  setAccessTokenProvider(() => auth.user?.access_token);
  setSessionRenewer(() =>
    auth
      .signinSilent()
      .then((user) => user !== null)
      .catch(() => false),
  );
  setOnSessionExpired(() => setSessionExpired(true));

  if (sessionExpired) {
    return (
      <Centered>
        <h1 className="text-2xl font-semibold">Session expired</h1>
        <p className="text-fg-muted">Your session could not be renewed. Sign in to continue.</p>
        <button
          type="button"
          className="rounded-md bg-accent-active px-6 py-3 text-bg-base"
          onClick={() => void auth.signinRedirect({ state: { returnTo: window.location.pathname } })}
        >
          Sign in
        </button>
      </Centered>
    );
  }

  if (auth.isLoading) {
    return <Centered>Signing in…</Centered>;
  }

  if (auth.error !== undefined) {
    return (
      <Centered>
        <h1 className="text-2xl font-semibold">Sign-in failed</h1>
        <p className="text-fg-muted">{auth.error.message}</p>
        <button
          type="button"
          className="rounded-md bg-accent-active/20 px-4 py-2 text-accent-active"
          onClick={() => void auth.signinRedirect()}
        >
          Try again
        </button>
      </Centered>
    );
  }

  if (!auth.isAuthenticated) {
    return (
      <Centered>
        <h1 className="text-3xl font-semibold">Smart Sentinel Eye — Management</h1>
        <button
          type="button"
          className="rounded-md bg-accent-active px-6 py-3 text-bg-base"
          onClick={() => void auth.signinRedirect()}
        >
          Sign in
        </button>
      </Centered>
    );
  }

  return <RoutedApp />;
}

/**
 * Builds the router, and does it here rather than in {@link AuthGate}.
 *
 * <p>
 * A data router reads the location once, when it is created. `oidcConfig`'s
 * `onSigninCallback` restores the stashed `returnTo` with a raw
 * `history.replaceState` while the sign-in is still resolving — so a router
 * created before that point would have already read `/?code=…` and would never
 * see where the operator was actually going.
 * </p>
 *
 * <p>
 * Mounting only once authenticated puts the router's creation after that
 * replace. It did not matter before this feature, because the `useState` shell
 * ignored the path entirely and `returnTo` only ever changed the address bar.
 * </p>
 */
function RoutedApp() {
  // Held rather than rebuilt: a router recreated on each render would reset
  // navigation to wherever the location currently points.
  const [router] = useState(createAppRouter);

  return <RouterProvider router={router} />;
}

function Centered({ children }: { children: ReactNode }) {
  return (
    <main className="flex min-h-screen flex-col items-center justify-center gap-4 bg-bg-base text-fg-primary">
      {children}
    </main>
  );
}
