import { configureStore } from '@reduxjs/toolkit';
import { camerasApi } from '@smart-sentinel-eye/shared/api/cameras.api';
import { streamsApi } from '@smart-sentinel-eye/shared/api/streams.api';
import { layoutsApi } from '@smart-sentinel-eye/shared/api/layouts.api';

// Single Redux store per app (ADR-0075). RTK Query slices added per feature.
export const store = configureStore({
  reducer: {
    [camerasApi.reducerPath]: camerasApi.reducer,
    [streamsApi.reducerPath]: streamsApi.reducer,
    [layoutsApi.reducerPath]: layoutsApi.reducer,
  },
  middleware: (getDefault) =>
    getDefault().concat(camerasApi.middleware, streamsApi.middleware, layoutsApi.middleware),
});

export type RootState = ReturnType<typeof store.getState>;
export type AppDispatch = typeof store.dispatch;
