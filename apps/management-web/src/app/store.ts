import { configureStore } from '@reduxjs/toolkit';
import { camerasApi } from '@smart-sentinel-eye/shared/api/cameras.api';

// Single Redux store per app (ADR-0075). RTK Query slices added per feature.
export const store = configureStore({
  reducer: {
    [camerasApi.reducerPath]: camerasApi.reducer,
  },
  middleware: (getDefault) => getDefault().concat(camerasApi.middleware),
});

export type RootState = ReturnType<typeof store.getState>;
export type AppDispatch = typeof store.dispatch;
