import { configureStore, type Dispatch } from '@reduxjs/toolkit';
import { camerasApi } from '@smart-sentinel-eye/shared/api/cameras.api';
import { streamsApi } from '@smart-sentinel-eye/shared/api/streams.api';
import { layoutsApi } from '@smart-sentinel-eye/shared/api/layouts.api';
import { overlaysApi } from '@smart-sentinel-eye/shared/api/overlays.api';
import { rulesApi } from '@smart-sentinel-eye/shared/api/rules.api';
import { systemVariablesApi } from '@smart-sentinel-eye/shared/api/systemVariables.api';
import { auditApi } from '@smart-sentinel-eye/shared/api/audit.api';
import { wallsApi } from '@smart-sentinel-eye/shared/api/walls.api';

// Every RTK Query slice mounted in the store, declared once. `store.test.ts`
// (spec 303, FR-005) fails if a 9th slice is added to `reducer`/`middleware`
// without being added here too — this array is also what `resetApiCaches`
// resets, so the two cannot drift independently.
export const apiSlices = [
  camerasApi,
  streamsApi,
  layoutsApi,
  overlaysApi,
  rulesApi,
  systemVariablesApi,
  auditApi,
  wallsApi,
];

// Single Redux store per app (ADR-0075). RTK Query slices added per feature.
export const store = configureStore({
  reducer: {
    [camerasApi.reducerPath]: camerasApi.reducer,
    [streamsApi.reducerPath]: streamsApi.reducer,
    [layoutsApi.reducerPath]: layoutsApi.reducer,
    [overlaysApi.reducerPath]: overlaysApi.reducer,
    [rulesApi.reducerPath]: rulesApi.reducer,
    [systemVariablesApi.reducerPath]: systemVariablesApi.reducer,
    [auditApi.reducerPath]: auditApi.reducer,
    [wallsApi.reducerPath]: wallsApi.reducer,
  },
  middleware: (getDefault) => getDefault().concat(apiSlices.map((slice) => slice.middleware)),
});

export type RootState = ReturnType<typeof store.getState>;
export type AppDispatch = typeof store.dispatch;

/**
 * Spec 303 (#2524): clears every RTK Query slice's cache, e.g. when the
 * authenticated OIDC subject changes underneath the store so a new operator
 * never sees a previous operator's cached records (FR-001). Does not branch
 * on any failure's HTTP status (FR-006).
 */
export function resetApiCaches(dispatch: Dispatch): void {
  for (const slice of apiSlices) {
    dispatch(slice.util.resetApiState());
  }
}
