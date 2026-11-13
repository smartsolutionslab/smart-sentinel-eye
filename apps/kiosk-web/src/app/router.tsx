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
      // A wall is not a Layout, so it gets its own route.
      path: '/walls/:wallIdentifier',
      element: <WallPage />,
    },
    // react-oidc-context intercepts the callback before the router sees it;
    // the route only keeps the router from rejecting the path.
    {
      path: '/oidc/callback',
      element: <PickerPage />,
    },
  ],
  { basename: routerBasename(import.meta.env.BASE_URL) },
);
