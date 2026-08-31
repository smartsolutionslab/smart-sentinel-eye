import { AuthProvider, useAuth } from 'react-oidc-context';
import { RouterProvider } from 'react-router-dom';
import { setAccessTokenProvider } from '@smart-sentinel-eye/shared/api/gateway';
import { logResilienceEvent } from '@smart-sentinel-eye/shared/observability/resilienceLog';
import { ErrorBoundary } from '@smart-sentinel-eye/shared/ui/composites/ErrorBoundary';
import { oidcConfig } from './app/auth.js';
import { router } from './app/router.js';
import { hasBeenAuthenticated, useSessionExpiry } from './app/useSessionExpiry.js';
import { NotAuthorizedScreen } from './features/auth/NotAuthorizedScreen.js';
import { ReconnectingScreen } from './features/auth/ReconnectingScreen.js';
import { DevCrashTrigger } from './features/recovery/DevCrashTrigger.js';
import { KioskCrashRecovery } from './features/recovery/KioskCrashRecovery.js';

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
  const { sessionExpired, identityFailure, attempt, retrySignIn, retryNow } = useSessionExpiry(auth);

  // **Refused comes first, and is not conditioned on being unauthenticated.**
  // A screen the provider has shut out must stop showing a wall it is no longer
  // entitled to, whatever token is still in hand (spec 051 US2).
  if (identityFailure === 'refused') {
    return <NotAuthorizedScreen />;
  }

  if (sessionExpired) {
    return (
      <main className="flex min-h-screen flex-col items-center justify-center gap-4 bg-bg-base text-fg-primary">
        <h1 className="text-3xl font-semibold">Session expired</h1>
        <p className="text-fg-muted">Automatic sign-in did not complete. Sign in again to resume the wall.</p>
        <button type="button" className="rounded-md bg-accent-active px-6 py-3 text-bg-base" onClick={retrySignIn}>
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

  // **A screen that still holds a working token keeps showing its wall.** A
  // renewal failing in the background is not a reason to blank a display that
  // has something valid to show; only losing the session is.
  if (identityFailure === 'recoverable' && !auth.isAuthenticated) {
    return <ReconnectingScreen attempt={attempt} onRetryNow={retryNow} />;
  }

  // Anything that reached the provider layer without being classified is
  // treated as recoverable, which is the asymmetric default (FR-005). It also
  // retires the screen that printed the library's own words — "Failed to
  // fetch" — above a button nobody was there to press.
  if (auth.error !== undefined && !auth.isAuthenticated) {
    return <ReconnectingScreen attempt={attempt} onRetryNow={retryNow} />;
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

  return (
    <ErrorBoundary
      onError={(error) =>
        logResilienceEvent('crash', 'render-error', {
          message: error instanceof Error ? error.message : String(error),
        })
      }
      fallback={() => <KioskCrashRecovery />}
    >
      <DevCrashTrigger />
      <RouterProvider router={router} />
    </ErrorBoundary>
  );
}
