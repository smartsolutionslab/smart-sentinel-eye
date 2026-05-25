# ADR-0075: Frontend State Management — Redux Toolkit + RTK Query

**Status:** Accepted
**Date:** 2026-05-25

## Context

Each app holds three categories of state:

- **Server state** — cameras, layouts, overlays from REST APIs.
- **Real-time state** — variable changes, event arrivals, overlay
  bindings pushed via the realtime channel (ADR-0076).
- **UI state** — selected cell, dialogs open, drag-drop in progress.

## Decision

**Redux Toolkit + RTK Query** for both apps.

- **RTK Query** handles server state: auto-generated hooks per
  endpoint, caching, optimistic updates, polling, devtools.
- **Redux slices** hold UI state.
- **Real-time updates dispatch actions** into the store via the
  realtime client (ADR-0076); reducers update relevant slices and
  RTK Query caches.
- **Redux DevTools** enabled in dev mode for both apps.

```typescript
// apps/shared/api/cameras.api.ts
export const camerasApi = createApi({
  reducerPath: 'camerasApi',
  baseQuery: fetchBaseQuery({ baseUrl: '/api/cameras' }),
  endpoints: (b) => ({
    listCameras: b.query<Camera[], void>({ query: () => '' }),
    registerCamera: b.mutation<CameraId, RegisterCameraInput>({
      query: (body) => ({ url: '', method: 'POST', body }),
    }),
  }),
});
```

## Consequences

- **Positive:** single store per app; predictable data flow.
- **Positive:** RTK Query devtools and Redux devtools accelerate
  debugging.
- **Negative:** more ceremony per slice than Zustand/Jotai. Acceptable.
- **Negative:** Redux mental model required of every frontend
  contributor.

## Alternatives Considered

- **TanStack Query + Zustand** — lighter, more ergonomic; loses the
  unified-store advantage and devtools richness.
- **Jotai (atomic state)** — fine-grained re-renders; smaller
  ecosystem.
- **Plain React Context + useState** — scales poorly.
