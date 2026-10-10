import type { ReactNode } from 'react';
import { createBrowserRouter } from 'react-router-dom';
import { AuditPage } from '../features/audit/AuditPage.js';
import { LayoutsPage } from '../features/layouts/LayoutsPage.js';
import { OverlayCreatePage } from '../features/overlays/OverlayCreatePage.js';
import { OverlayEditPage } from '../features/overlays/OverlayEditPage.js';
import { OverlaysPage } from '../features/overlays/OverlaysPage.js';
import { RulesPage } from '../features/rules/RulesPage';
import { SystemVariablesPage } from '../features/systemVariables/SystemVariablesPage.js';
import { WallsPage } from '../features/walls/WallsPage.js';
import { WallDetailPage } from '../features/walls/WallDetailPage.js';
import { NotAvailable } from './navigation/NotAvailable.js';
import { useNavigation } from './navigation/NavigationProvider.js';
import { RemoteSurface } from './navigation/RemoteSurface.js';
import { useVisibleEntries } from './navigation/useVisibleEntries.js';
import { ShellLayout, SurfaceCrash } from './ShellLayout.js';

/**
 * Spec 316 FR-013 — wraps one child route's element, rendering
 * {@link NotAvailable} instead when `path` is not in the session's visible
 * set. For the remote route this also keeps `<RemoteSurface>` from ever
 * mounting for an unentitled session, so it never registers or fetches the
 * remote's code (FR-003/FR-013): gating decides what is fetched as code, not
 * only what is shown.
 */
function Gated({ path, children }: { path: string; children: ReactNode }) {
  const visible = useVisibleEntries();
  if (!visible.some((entry) => entry.path === path)) {
    return <NotAvailable />;
  }
  return <>{children}</>;
}

/**
 * Spec 316 FR-012 — the index route renders the first visible entry's
 * element directly, in manifest order, with no `<Navigate>` (preserving this
 * file's existing "no redirect flash" reasoning). While a manifest that
 * could still own an earlier-ordered entry has not settled
 * (`useNavigation().status === 'loading'`), it shows the existing
 * `Centered`-style "Loading…" state rather than guessing from a partial set;
 * with no visible entry at all it shows the empty-navigation message.
 */
function FirstVisibleSurface() {
  const { status } = useNavigation();
  const visible = useVisibleEntries();

  if (status === 'loading') {
    return <Centered>Loading…</Centered>;
  }

  const first = visible[0];
  const element = first !== undefined ? SURFACE_ELEMENTS[first.path] : undefined;
  if (element !== undefined) {
    return <>{element}</>;
  }

  return <Centered>No surfaces are available to your account.</Centered>;
}

function Centered({ children }: { children: ReactNode }) {
  return (
    <main className="flex min-h-screen flex-col items-center justify-center gap-4 bg-bg-base text-fg-primary">
      {children}
    </main>
  );
}

/** Keyed by each in-shell/remote entry's `path` (plan.md §4.3's "Entry" table). */
const SURFACE_ELEMENTS: Readonly<Record<string, ReactNode>> = {
  '/cameras': <RemoteSurface remote="cameras" />,
  '/layouts': <LayoutsPage />,
  '/walls': <WallsPage />,
  '/overlays': <OverlaysPage />,
  '/rules': <RulesPage />,
  '/system-variables': <SystemVariablesPage />,
  '/audit': <AuditPage />,
};

/**
 * Mirrors `apps/kiosk-web/src/app/router.tsx` so the two apps stay recognisable
 * to each other. This app's OIDC redirect_uri is the origin root rather than
 * a dedicated callback path, so unlike kiosk-web it needs no route for one —
 * react-oidc-context strips the code and state from `/` before the router
 * matches it.
 *
 * <p>
 * A factory rather than the module-level constant kiosk-web exports. A data
 * router owns its own history from the moment it is created, so a shared
 * instance keeps whatever location the last render left it at and ignores the
 * real one — which makes it untestable across more than one render in a file.
 * `App` builds one and holds it, so production still has exactly one router.
 * </p>
 *
 * <p>
 * A layout route rather than six standalone ones: the nav has to be visible on
 * every surface and outside the error boundary, which is what
 * {@link ShellLayout} arranges.
 * </p>
 */
export const createAppRouter = () =>
  createBrowserRouter([
    {
      path: '/',
      element: <ShellLayout />,
      children: [
        // Spec 316 US2 (FR-012): the first *visible* entry in manifest order,
        // rendered directly rather than redirected to its own path. A
        // `<Navigate>` costs an extra render cycle before anything appears,
        // which is a real flash on a cold load and not only a test
        // inconvenience. For a full-scope session that is still Cameras, as
        // before this feature.
        { index: true, element: <FirstVisibleSurface />, errorElement: <SurfaceCrash /> },
        {
          path: 'cameras/*',
          element: (
            <Gated path="/cameras">
              <RemoteSurface remote="cameras" />
            </Gated>
          ),
          errorElement: <SurfaceCrash />,
        },
        {
          path: 'layouts',
          element: (
            <Gated path="/layouts">
              <LayoutsPage />
            </Gated>
          ),
          errorElement: <SurfaceCrash />,
        },
        {
          path: 'walls',
          element: (
            <Gated path="/walls">
              <WallsPage />
            </Gated>
          ),
          errorElement: <SurfaceCrash />,
        },
        {
          path: 'walls/:wallIdentifier',
          element: (
            <Gated path="/walls">
              <WallDetailPage />
            </Gated>
          ),
          errorElement: <SurfaceCrash />,
        },
        {
          path: 'overlays',
          element: (
            <Gated path="/overlays">
              <OverlaysPage />
            </Gated>
          ),
          errorElement: <SurfaceCrash />,
        },
        {
          path: 'overlays/new',
          element: (
            <Gated path="/overlays">
              <OverlayCreatePage />
            </Gated>
          ),
          errorElement: <SurfaceCrash />,
        },
        {
          path: 'overlays/:overlayIdentifier/revisions/:revisionNumber/edit',
          element: (
            <Gated path="/overlays">
              <OverlayEditPage />
            </Gated>
          ),
          errorElement: <SurfaceCrash />,
        },
        {
          path: 'rules',
          element: (
            <Gated path="/rules">
              <RulesPage />
            </Gated>
          ),
          errorElement: <SurfaceCrash />,
        },
        {
          path: 'system-variables',
          element: (
            <Gated path="/system-variables">
              <SystemVariablesPage />
            </Gated>
          ),
          errorElement: <SurfaceCrash />,
        },
        {
          path: 'audit',
          element: (
            <Gated path="/audit">
              <AuditPage />
            </Gated>
          ),
          errorElement: <SurfaceCrash />,
        },
      ],
    },
  ]);
