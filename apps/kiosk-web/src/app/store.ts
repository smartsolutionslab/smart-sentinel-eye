import { configureStore } from '@reduxjs/toolkit';
import { camerasApi } from '@smart-sentinel-eye/shared/api/cameras.api';
import { layoutsApi } from '@smart-sentinel-eye/shared/api/layouts.api';
import { overlaysApi } from '@smart-sentinel-eye/shared/api/overlays.api';
import { streamsApi } from '@smart-sentinel-eye/shared/api/streams.api';
import { systemVariablesApi } from '@smart-sentinel-eye/shared/api/systemVariables.api';

// Single Redux store per app (ADR-0075). Kiosk-web consumes the read
// sides of every API it touches; no mutations originate here.
export const store = configureStore({
  reducer: {
    [camerasApi.reducerPath]: camerasApi.reducer,
    [layoutsApi.reducerPath]: layoutsApi.reducer,
    [overlaysApi.reducerPath]: overlaysApi.reducer,
    [streamsApi.reducerPath]: streamsApi.reducer,
    [systemVariablesApi.reducerPath]: systemVariablesApi.reducer,
  },
  middleware: (getDefault) =>
    getDefault().concat(
      camerasApi.middleware,
      layoutsApi.middleware,
      overlaysApi.middleware,
      streamsApi.middleware,
      systemVariablesApi.middleware,
    ),
});

export type RootState = ReturnType<typeof store.getState>;
export type AppDispatch = typeof store.dispatch;
