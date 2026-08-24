import type { ReactNode } from 'react';
import { NavLink, Outlet, useLocation, useNavigate, useRouteError } from 'react-router-dom';
import { logResilienceEvent } from '@smart-sentinel-eye/shared/observability/resilienceLog';

/**
 * The management shell: navigation that is always visible, and the current
 * surface rendered beneath it.
 *
 * Replaces the hand-rolled `useState` toggle this app used from spec 004, whose
 * own comment said a real router lands once more than three surfaces exist.
 * There are six.
 *
 * <p>
 * A layout route rather than an ErrorBoundary wrapped around the whole
 * RouterProvider, which is what kiosk-web does: the nav has to stay outside
 * crash containment so an operator can leave a crashed surface. See
 * {@link SurfaceCrash}.
 * </p>
 */
export function ShellLayout() {
  return (
    <main className="min-h-screen bg-bg-base text-fg-primary">
      <nav className="flex items-center gap-3 border-b border-fg-muted/30 px-6 py-3">
        <NavItem to="/cameras">Cameras</NavItem>
        <NavItem to="/layouts">Layouts</NavItem>
        <NavItem to="/overlays">Overlays</NavItem>
        <NavItem to="/rules">Rules</NavItem>
        <NavItem to="/system-variables">System variables</NavItem>
        <NavItem to="/audit">Audit</NavItem>
      </nav>
      <Outlet />
    </main>
  );
}

/**
 * Crash containment for a surface, as a route `errorElement` (spec 011 FR-016).
 *
 * <p>
 * <b>It has to be an errorElement, not an ErrorBoundary around the Outlet.</b>
 * A data router installs its own React error boundary around every route
 * element, so it catches a surface's render error before anything wrapping
 * <c>Outlet</c> can see it — a wrapping boundary is simply dead code for the
 * case it was written for. Discovered by the crash test failing after the
 * router conversion, which is the whole reason that test predates this feature.
 * </p>
 *
 * <p>
 * Attached to the <em>child</em> routes rather than the layout route, so it
 * renders inside {@link ShellLayout}'s outlet and the navigation above it
 * survives. On the layout route it would replace the nav too, and an operator
 * looking at a crashed page could not leave it.
 * </p>
 */
export function SurfaceCrash() {
  const error = useRouteError();
  const navigate = useNavigate();
  const location = useLocation();
  const message = describeError(error);

  logResilienceEvent('crash', 'render-error', { message });

  return (
    <CrashPanel
      message={message}
      // Re-navigating to the same path gives the router a fresh location, which
      // resets its error boundary and re-renders the surface. The old shell
      // called the boundary's own reset; a data router has no equivalent.
      onRetry={() => navigate(location.pathname, { replace: true })}
    />
  );
}

/**
 * A link, not a button calling `navigate()`.
 *
 * The button form would keep every existing `getByRole('button')` selector
 * green, and would lose middle-click, open-in-new-tab and copy-link — most of
 * what having real locations is for. Keeping selectors green by making the
 * markup wrong is paying for the router and not collecting.
 */
function NavItem({ to, children }: { to: string; children: ReactNode }) {
  return (
    <NavLink
      to={to}
      className={({ isActive }) =>
        isActive
          ? 'rounded-md bg-accent-active/10 px-3 py-1 text-sm font-medium text-accent-active'
          : 'rounded-md px-3 py-1 text-sm text-fg-muted hover:text-fg-primary'
      }
    >
      {children}
    </NavLink>
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
      <button type="button" className="rounded-md bg-accent-active px-6 py-3 text-bg-base" onClick={onRetry}>
        Try again
      </button>
    </section>
  );
}
