import { createBrowserRouter } from 'react-router-dom';
import { CellPage } from '../features/cell/CellPage.js';
import { PickerPage } from '../features/picker/PickerPage.js';

export const router = createBrowserRouter([
  {
    path: '/',
    element: <PickerPage />,
  },
  {
    path: '/layouts/:layoutIdentifier',
    element: <CellPage />,
  },
  // The OIDC callback is handled by react-oidc-context; the redirect
  // URI lands on /oidc/callback and the AuthProvider intercepts before
  // the router sees it. A fallback path keeps the router happy.
  {
    path: '/oidc/callback',
    element: <PickerPage />,
  },
]);
