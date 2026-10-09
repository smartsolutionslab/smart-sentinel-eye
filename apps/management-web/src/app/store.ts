import type { Dispatch } from '@reduxjs/toolkit';
import { setupListeners } from '@reduxjs/toolkit/query';
import { createApiStore } from '@smart-sentinel-eye/shared/store';
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
// The listener-middleware wiring (spec 314, #2762: useRevocationFallback's
// own `addListener` must be seen by the serializability check before that
// check inspects its function-valued payload) lives in `createApiStore`
// (plan.md §4.1), shared with every app that mounts RTK Query slices.
export const store = createApiStore(apiSlices);

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

/**
 * Spec 317 (#2751): installs RTK Query's `focus`/`visibilitychange`
 * listeners against this app's store, so a subscription's `refetchOnFocus`
 * is not inert. Deliberately NOT called at module load — ~30 test files
 * import this module for the real `store`, and `setupListeners` would
 * attach `window` listeners in every one of them. `main.tsx` calls this
 * once, before rendering. Returns the unsubscribe function.
 */
export function listenForWindowFocus(): () => void {
  return setupListeners(store.dispatch);
}
