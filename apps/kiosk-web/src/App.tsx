import { AuthProvider, useAuth } from 'react-oidc-context';
import { RouterProvider } from 'react-router-dom';
import { setAccessTokenProvider } from '@smart-sentinel-eye/shared/api/gateway';
import { oidcConfig } from './app/auth.js';
import { router } from './app/router.js';
import { hasBeenAuthenticated, useSessionExpiry } from './app/useSessionExpiry.js';

export function App() {
  return (
    <AuthProvider {...oidcConfig}>
      <AuthGate />
    </AuthProvider>
  );
}

function AuthGate() {
  const auth = useAuth();

  // Register the bearer getter synchronously, before any child query dispatches,
  // so the first authenticated REST call already carries the token. A useEffect
  // here fires after the child's mount effect and races the token (ADR-0007/0008).
  // useSessionExpiry registers the session renewer/expiry handlers the same way.
  setAccessTokenProvider(() => auth.user?.access_token);
  const { sessionExpired, retrySignIn } = useSessionExpiry(auth);

  if (sessionExpired) {
    return (
      <main className="flex min-h-screen flex-col items-center justify-center gap-4 bg-bg-base text-fg-primary">
        <h1 className="text-3xl font-semibold">Session expired</h1>
        <p className="text-fg-muted">
          Automatic sign-in did not complete. Sign in again to resume the wall.
        </p>
        <button
          type="button"
          className="rounded-md bg-accent-active px-6 py-3 text-bg-base"
          onClick={retrySignIn}
        >
          Sign in again
        </button>
      </main>
    );
  }

  if (auth.isLoading) {
    return (
      <main className="flex min-h-screen items-center justify-center bg-bg-base text-fg-primary">
        <p>Signing in…</p>
      </main>
    );
  }

  if (auth.error !== undefined) {
    return (
      <main className="flex min-h-screen flex-col items-center justify-center gap-4 bg-bg-base text-fg-primary">
        <h1 className="text-2xl font-semibold">Sign-in failed</h1>
        <p className="text-fg-muted">{auth.error.message}</p>
        <button
          type="button"
          className="rounded-md bg-accent-active/20 px-4 py-2 text-accent-active"
          onClick={() => void auth.signinRedirect()}
        >
          Try again
        </button>
      </main>
    );
  }

  if (!auth.isAuthenticated) {
    // A previously authenticated kiosk re-authenticates automatically
    // (useSessionExpiry); only first boot shows the manual sign-in button.
    if (hasBeenAuthenticated()) {
      return (
        <main className="flex min-h-screen items-center justify-center bg-bg-base text-fg-primary">
          <p>Session expired — signing in again…</p>
        </main>
      );
    }
    return (
      <main className="flex min-h-screen flex-col items-center justify-center gap-4 bg-bg-base text-fg-primary">
        <h1 className="text-3xl font-semibold">Smart Sentinel Eye — Kiosk</h1>
        <button
          type="button"
          className="rounded-md bg-accent-active px-6 py-3 text-bg-base"
          onClick={() => void auth.signinRedirect()}
        >
          Sign in
        </button>
      </main>
    );
  }

  return <RouterProvider router={router} />;
}
