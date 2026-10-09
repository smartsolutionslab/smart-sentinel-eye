import { configureStore, createListenerMiddleware } from '@reduxjs/toolkit';
import type { Middleware, Reducer, UnknownAction } from '@reduxjs/toolkit';

/**
 * Any RTK Query slice created by `createApi` (ADR-0075) — only the three
 * members `createApiStore` actually mounts. The full `Api<...>` generic type
 * carries method signatures (`injectEndpoints`, `enhanceEndpoints`, ...)
 * whose own generics do not collapse to `any` cleanly, so this is a narrower,
 * structural shape every app's concrete api slices satisfy regardless of
 * their own `BaseQuery`/`Definitions`/`TagTypes` type parameters.
 */
export interface AnyApiSlice {
  readonly reducerPath: string;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  readonly reducer: Reducer<any, UnknownAction>;
  readonly middleware: Middleware;
}

/**
 * Builds the single Redux store an app mounts (ADR-0075), wiring every given
 * RTK Query slice's reducer and middleware. Spec 314 (#2762) prepends a
 * listener middleware ahead of the api slices' own middleware so a consumer's
 * `addListener` dispatch (e.g. `useRevocationFallback`) is seen by RTK
 * Query's serializability check before that check inspects the action's
 * function-valued payload.
 */
/**
 * The precise `{ reducerPath: reducer }` map for a given `Slices` tuple —
 * keyed by each slice's own literal `reducerPath`, valued with that same
 * slice's own `reducer` type. `Object.fromEntries` at runtime only ever
 * builds this same shape; the cast below exists so `configureStore` infers
 * the specific `RootState` each api slice's own thunks (e.g.
 * `upsertQueryData`, `resetApiState`) already expect, rather than the
 * `{ [reducerPath: string]: unknown }` index signature `fromEntries`'s own
 * return type carries.
 */
type ReducerMapFromSlices<Slices extends readonly AnyApiSlice[]> = {
  [ReducerPath in Slices[number]['reducerPath']]: Extract<Slices[number], { reducerPath: ReducerPath }>['reducer'];
};

export function createApiStore<Slices extends readonly AnyApiSlice[]>(slices: Slices) {
  const listenerMiddleware = createListenerMiddleware();

  const reducer = Object.fromEntries(
    slices.map((slice) => [slice.reducerPath, slice.reducer]),
  ) as ReducerMapFromSlices<Slices>;

  return configureStore({
    reducer,
    middleware: (getDefault) =>
      getDefault()
        .prepend(listenerMiddleware.middleware)
        .concat(slices.map((slice) => slice.middleware)),
  });
}
