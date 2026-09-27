import { useEffect, useRef, useState, type ComponentRef, type ReactNode, type Ref } from 'react';
import { NavLink, Outlet, useLocation, useNavigate, useRouteError } from 'react-router-dom';
import { logResilienceEvent } from '@smart-sentinel-eye/shared/observability/resilienceLog';
import { Button } from '@smart-sentinel-eye/shared/ui/primitives/Button';
import { CommandPalette } from '@smart-sentinel-eye/shared/ui/primitives/CommandPalette';

/**
 * The nav's destinations — the single source both the nav bar and the
 * command palette render, so the palette can never offer a surface the nav
 * does not (spec 266 SC-6). There are seven.
 */
const DESTINATIONS: ReadonlyArray<{ to: string; label: string }> = [
  { to: '/cameras', label: 'Cameras' },
  { to: '/layouts', label: 'Layouts' },
  { to: '/walls', label: 'Walls' },
  { to: '/overlays', label: 'Overlays' },
  { to: '/rules', label: 'Rules' },
  { to: '/system-variables', label: 'System variables' },
  { to: '/audit', label: 'Audit' },
];

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
  const navigate = useNavigate();
  const [paletteOpen, setPaletteOpen] = useState(false);
  const linkRefs = useRef(new Map<string, ComponentRef<'a'>>());

  // Mounted once, in the shell — the one component that lives for the whole
  // signed-in session (spec 266 US6, plan.md §4.4). The listener is inline
  // (an `AbortController` handles cleanup, not a named reference) so its
  // `event` parameter is inferred from `addEventListener`'s own overload
  // rather than spelled `KeyboardEvent`. This file still avoids the literal
  // type: `KeyboardEvent` was added to eslint.config.js's globals only for
  // `ShellLayout.test.tsx`'s `new KeyboardEvent(...)` dispatch, not to let
  // production code name it (see `ComponentRef<'a'>` above, and spec 154's
  // `LayoutEditorDialog.tsx` for the established reasoning: naming DOM event
  // types in this app's own code is the gate-weakening ADR-0144 rules out).
  useEffect(() => {
    const controller = new AbortController();
    document.addEventListener(
      'keydown',
      (event) => {
        if (event.altKey || event.shiftKey) return;
        if (!(event.ctrlKey || event.metaKey)) return;
        if (event.key.toLowerCase() !== 'k') return;

        // A stray chord must not navigate an operator out of an unsaved
        // form — this also covers the palette itself already being open.
        const anotherDialogOpen = document.querySelector(
          '[role="dialog"][data-state="open"], [role="alertdialog"][data-state="open"]',
        );
        if (anotherDialogOpen !== null) return;

        event.preventDefault();
        setPaletteOpen(true);
      },
      { signal: controller.signal },
    );
    return () => controller.abort();
  }, []);

  return (
    <main className="min-h-screen bg-bg-base text-fg-primary">
      <nav className="flex items-center gap-3 overflow-x-auto border-b border-fg-muted/30 px-6 py-3">
        {DESTINATIONS.map((destination) => (
          <NavItem
            key={destination.to}
            to={destination.to}
            ref={(element) => {
              if (element === null) {
                linkRefs.current.delete(destination.to);
              } else {
                linkRefs.current.set(destination.to, element);
              }
            }}
          >
            {destination.label}
          </NavItem>
        ))}
        <Button
          variant="ghost"
          className="ml-auto"
          aria-keyshortcuts="Control+K Meta+K"
          onClick={() => setPaletteOpen(true)}
        >
          Go to…
        </Button>
      </nav>
      <Outlet />
      <CommandPalette
        open={paletteOpen}
        onOpenChange={setPaletteOpen}
        items={DESTINATIONS.map(({ to, label }) => ({ value: to, label }))}
        onSelect={(to) => {
          navigate(to);
          linkRefs.current.get(to)?.focus();
        }}
        label="Go to"
        placeholder="Go to a surface…"
        emptyText="No matching surfaces"
      />
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
function NavItem({ to, children, ref }: { to: string; children: ReactNode; ref?: Ref<ComponentRef<'a'>> }) {
  return (
    <NavLink
      ref={ref}
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
