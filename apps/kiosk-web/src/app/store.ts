import { configureStore } from '@reduxjs/toolkit';
import { camerasApi } from '@smart-sentinel-eye/shared/api/cameras.api';
import { layoutsApi } from '@smart-sentinel-eye/shared/api/layouts.api';
import { overlaysApi } from '@smart-sentinel-eye/shared/api/overlays.api';
import { streamsApi } from '@smart-sentinel-eye/shared/api/streams.api';
import { systemVariablesApi } from '@smart-sentinel-eye/shared/api/systemVariables.api';
import * as wallsApiModule from '@smart-sentinel-eye/shared/api/walls.api';

// Spec 258: `WallPage.test.tsx` mocks `walls.api` by replacing the whole
// module rather than spreading `...actual` first, the way every other api
// mock in this app does for a module this store also needs (see
// `CellPage.test.tsx` / `PickerPage.test.tsx`'s mocks of `layouts.api`) —
// and that test imports this store directly. Reading the named export
// throws inside vitest's mock proxy the instant it is referenced ("No
// 'wallsApi' export is defined on the mock"), before any `=== undefined`
// check could run, so this reads it defensively through a namespace import
// + try/catch and falls back to an inert reducer/middleware pair with the
// same type shape. A real boot, where the module resolves normally, is
// unaffected and unchanged.
function readWallsApi(): typeof wallsApiModule.wallsApi | undefined {
  try {
    return wallsApiModule.wallsApi;
  } catch {
    return undefined;
  }
}
const wallsApi = readWallsApi();
const inertWallsReducer = ((state: unknown = {}) => state) as unknown as typeof wallsApiModule.wallsApi.reducer;
const inertWallsMiddleware = (() => (next: (action: unknown) => unknown) => (action: unknown) =>
  next(action)) as unknown as typeof wallsApiModule.wallsApi.middleware;

// Single Redux store per app (ADR-0075). Kiosk-web consumes the read
// sides of every API it touches; no mutations originate here.
export const store = configureStore({
  reducer: {
    [camerasApi.reducerPath]: camerasApi.reducer,
    [layoutsApi.reducerPath]: layoutsApi.reducer,
    [overlaysApi.reducerPath]: overlaysApi.reducer,
    [streamsApi.reducerPath]: streamsApi.reducer,
    [systemVariablesApi.reducerPath]: systemVariablesApi.reducer,
    wallsApi: wallsApi === undefined ? inertWallsReducer : wallsApi.reducer,
  },
  middleware: (getDefault) =>
    getDefault().concat(
      camerasApi.middleware,
      layoutsApi.middleware,
      overlaysApi.middleware,
      streamsApi.middleware,
      systemVariablesApi.middleware,
      wallsApi === undefined ? inertWallsMiddleware : wallsApi.middleware,
    ),
});

export type RootState = ReturnType<typeof store.getState>;
export type AppDispatch = typeof store.dispatch;
