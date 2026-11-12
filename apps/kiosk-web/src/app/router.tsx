import { createBrowserRouter } from 'react-router-dom';
import { CellPage } from '../features/cell/CellPage';
import { routerBasename } from './signinPaths';
import { PickerPage } from '../features/picker/PickerPage';
import { WallPage } from '../features/wall/WallPage';

export const router = createBrowserRouter(
  [
    {
      path: '/',
      element: <PickerPage />,
    },
    {
      path: '/layouts/:layoutIdentifier',
      element: <CellPage />,
    },
    {
      // Spec 258 US1 PD-2: a wall is not a Layout, so it gets its own route.
      path: '/walls/:wallIdentifier',
      element: <WallPage />,
    },
    // The OIDC callback is handled by react-oidc-context; the redirect
    // URI lands on /oidc/callback and the AuthProvider intercepts before
    // the router sees it. A fallback path keeps the router happy.
    {
      path: '/oidc/callback',
      element: <PickerPage />,
    },
  ],
  { basename: routerBasename(import.meta.env.BASE_URL) },
);
