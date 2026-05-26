import { AuthProvider, useAuth } from 'react-oidc-context';
import { RouterProvider } from 'react-router-dom';
import { oidcConfig } from './app/auth.js';
import { router } from './app/router.js';

export function App() {
  return (
    <AuthProvider {...oidcConfig}>
      <AuthGate />
    </AuthProvider>
  );
}

function AuthGate() {
  const auth = useAuth();

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
