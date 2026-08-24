import { createBrowserRouter } from 'react-router-dom';
import { AuditPage } from '../features/audit/AuditPage.js';
import { CameraDetailPage } from '../features/cameras/CameraDetailPage.js';
import { CamerasPage } from '../features/cameras/CamerasPage.js';
import { LayoutsPage } from '../features/layouts/LayoutsPage.js';
import { OverlaysPage } from '../features/overlays/OverlaysPage.js';
import { RulesPage } from '../features/rules/RulesPage';
import { SystemVariablesPage } from '../features/systemVariables/SystemVariablesPage.js';
import { ShellLayout, SurfaceCrash } from './ShellLayout.js';

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
export const createAppRouter = () => createBrowserRouter([
  {
    path: '/',
    element: <ShellLayout />,
    children: [
      // Cameras was the shell's default view, so the bare origin keeps showing
      // it and an existing bookmark still arrives somewhere familiar.
      //
      // Rendered directly rather than redirected to `/cameras`. A `<Navigate>`
      // costs an extra render cycle before anything appears, which is a real
      // flash on a cold load and not only a test inconvenience.
      { index: true, element: <CamerasPage />, errorElement: <SurfaceCrash /> },
      { path: 'cameras', element: <CamerasPage />, errorElement: <SurfaceCrash /> },
      {
        path: 'cameras/:cameraIdentifier',
        element: <CameraDetailPage />,
        errorElement: <SurfaceCrash />,
      },
      { path: 'layouts', element: <LayoutsPage />, errorElement: <SurfaceCrash /> },
      { path: 'overlays', element: <OverlaysPage />, errorElement: <SurfaceCrash /> },
      { path: 'rules', element: <RulesPage />, errorElement: <SurfaceCrash /> },
      { path: 'system-variables', element: <SystemVariablesPage />, errorElement: <SurfaceCrash /> },
      { path: 'audit', element: <AuditPage />, errorElement: <SurfaceCrash /> },
    ],
  },
]);
