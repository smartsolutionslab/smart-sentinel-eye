import { useState, type ReactNode } from 'react';
import { AuthProvider, useAuth } from 'react-oidc-context';
import {
  setAccessTokenProvider,
  setOnSessionExpired,
  setSessionRenewer,
} from '@smart-sentinel-eye/shared/api/gateway';
import { logResilienceEvent } from '@smart-sentinel-eye/shared/observability/resilienceLog';
import { ErrorBoundary } from '@smart-sentinel-eye/shared/ui/composites/ErrorBoundary';
import { oidcConfig } from './app/auth.js';
import { CamerasPage } from './features/cameras/CamerasPage.js';
import { LayoutsPage } from './features/layouts/LayoutsPage.js';
import { OverlaysPage } from './features/overlays/OverlaysPage.js';
import { SystemVariablesPage } from './features/systemVariables/SystemVariablesPage.js';
import { AuditPage } from './features/audit/AuditPage.js';

type View = 'cameras' | 'layouts' | 'overlays' | 'system-variables' | 'audit';

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
          onClick={() =>
            void auth.signinRedirect({ state: { returnTo: window.location.pathname } })
          }
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

  return <Shell />;
}

// Placeholder shell for the management app. A real router lands when more
// than three surfaces exist; for spec 004 we toggle between cameras,
// layouts, and overlays so the nav remains visible everywhere.
function Shell() {
  const [view, setView] = useState<View>('cameras');

  return (
    <main className="min-h-screen bg-bg-base text-fg-primary">
      <nav className="flex items-center gap-3 border-b border-fg-muted/30 px-6 py-3">
        <NavButton active={view === 'cameras'} onClick={() => setView('cameras')}>
          Cameras
        </NavButton>
        <NavButton active={view === 'layouts'} onClick={() => setView('layouts')}>
          Layouts
        </NavButton>
        <NavButton active={view === 'overlays'} onClick={() => setView('overlays')}>
          Overlays
        </NavButton>
        <NavButton active={view === 'system-variables'} onClick={() => setView('system-variables')}>
          System variables
        </NavButton>
        <NavButton active={view === 'audit'} onClick={() => setView('audit')}>
          Audit
        </NavButton>
      </nav>
      <ErrorBoundary
        // Keyed on the view so navigating away from a crashed page renders
        // the next page fresh instead of a stale error panel. The nav above
        // stays outside the boundary and survives any page crash (FR-016).
        key={view}
        onError={(error) =>
          logResilienceEvent('crash', 'render-error', { message: describeError(error) })
        }
        fallback={(error, reset) => <CrashPanel message={describeError(error)} onRetry={reset} />}
      >
        {view === 'cameras' && <CamerasPage />}
        {view === 'layouts' && <LayoutsPage />}
        {view === 'overlays' && <OverlaysPage />}
        {view === 'system-variables' && <SystemVariablesPage />}
        {view === 'audit' && <AuditPage />}
      </ErrorBoundary>
    </main>
  );
}

function describeError(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

function CrashPanel({ message, onRetry }: { message: string; onRetry: () => void }) {
  return (
    <section
      role="alert"
      className="mx-auto mt-16 flex max-w-lg flex-col items-center gap-4 rounded-lg border border-fg-muted/30 p-8 text-center"
    >
      <h1 className="text-2xl font-semibold">Something went wrong</h1>
      <p className="text-fg-muted">{message}</p>
      <button
        type="button"
        className="rounded-md bg-accent-active px-6 py-3 text-bg-base"
        onClick={onRetry}
      >
        Try again
      </button>
    </section>
  );
}

function Centered({ children }: { children: ReactNode }) {
  return (
    <main className="flex min-h-screen flex-col items-center justify-center gap-4 bg-bg-base text-fg-primary">
      {children}
    </main>
  );
}

function NavButton({
  active,
  onClick,
  children,
}: {
  active: boolean;
  onClick: () => void;
  children: ReactNode;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={
        active
          ? 'rounded-md bg-accent-active/10 px-3 py-1 text-sm font-medium text-accent-active'
          : 'rounded-md px-3 py-1 text-sm text-fg-muted hover:text-fg-primary'
      }
    >
      {children}
    </button>
  );
}
