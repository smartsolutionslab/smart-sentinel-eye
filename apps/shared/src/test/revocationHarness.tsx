// @vitest-environment jsdom
import { act } from '@testing-library/react';
import { configureStore, createListenerMiddleware } from '@reduxjs/toolkit';
import { createApi, fakeBaseQuery } from '@reduxjs/toolkit/query/react';
import { useEffect, type ReactElement, type ReactNode } from 'react';
import { Provider } from 'react-redux';
import { useRevocationFallback, type RevocationQueryState } from '../hooks/useRevocationFallback.js';

/**
 * Spec 314 (#2762) T002. A scripted RTK Query store whose autobatch
 * notifications are held until the test releases them by hand — the
 * deterministic stand-in for "settled in the store, not yet rendered" that
 * replaces the real `requestAnimationFrame` timing that made the original
 * defect hard to reproduce (spec §0, FR-008).
 *
 * `useRevocationFallbackCoalesced.test.tsx` is the only consumer of `Probe`.
 *
 * Spec 317 (#2751) T003 adds `Subscribe` + `queryFnCallCount` — additive,
 * existing exports unchanged. `Probe` reads the cache via `useQueryState`,
 * which does not itself subscribe, so it is invisible to RTK Query's focus
 * refetch (`windowEventHandling.ts` only refetches cache entries that have a
 * subscription). `Subscribe` creates a real one, with the same `refetchOnFocus`
 * option a page's own query hook would pass.
 */

/** The scripted query's args — one cache key per "subject" under test. */
export interface ScriptedArgs {
  key: string;
}

/** What the next dispatched request for this harness should answer. */
export type ScriptedOutcome = 'ok' | 403 | 404 | 503;

function buildApi() {
  const responses: ScriptedOutcome[] = [];
  // Spec 317 (#2751) T003: counts every `queryFn` invocation — the dedup
  // case (a focus+visibility pair) asserts on this directly, at the
  // boundary RTK Query itself calls, rather than inferring it from a render.
  const queryFnCalls = { current: 0 };
  const api = createApi({
    reducerPath: 'revocationHarness',
    baseQuery: fakeBaseQuery<{ status: number }>(),
    endpoints: (builder) => ({
      getThing: builder.query<{ outcome: 'ok' }, ScriptedArgs>({
        queryFn: () => {
          queryFnCalls.current += 1;
          const next = responses.shift();
          if (next === undefined) {
            throw new Error('revocationHarness: scripted response queue is empty — push one before dispatching');
          }
          return next === 'ok' ? { data: { outcome: 'ok' } } : { error: { status: next } };
        },
      }),
    }),
  });
  return { api, responses, queryFnCalls };
}

/** One rendered observation of the scripted query's cache entry. */
export interface RenderLogEntry {
  requestId: string | undefined;
  isFetching: boolean;
}

export interface RevocationHarness {
  store: ReturnType<typeof buildHarnessStore>['store'];
  responses: ScriptedOutcome[];
  renderLog: RenderLogEntry[];
  /** The hook's own return value as of the most recent render. */
  refusedByRenderRef: { current: boolean };
  /** Releases the held autobatch notification, flushing one render. */
  flush: () => void;
  /** Dispatches a scripted request directly, bypassing any rendered hook — not subscribed, so it never keeps the cache entry alive on its own. */
  request: (
    outcome: ScriptedOutcome,
    args: ScriptedArgs,
    options?: { forceRefetch?: boolean },
  ) => {
    requestId: string;
    settled: Promise<unknown>;
  };
  Probe: (props: { subject: string; args: ScriptedArgs; notFoundRevokes?: boolean }) => null;
  /**
   * Spec 317 (#2751) T003. Holds a REAL subscription to `args` — unlike
   * `Probe`'s `useQueryState` — so RTK Query's `windowEventHandling` sees it
   * and, with `refetchOnFocus: true`, refetches it on a focus/visibility
   * signal. Mirrors what a page's own `useXQuery(args, { refetchOnFocus })`
   * call does; `Probe` stays the thing that observes the hook's result.
   */
  Subscribe: (props: { args: ScriptedArgs; refetchOnFocus?: boolean }) => null;
  /** How many times the scripted endpoint's `queryFn` has run so far. */
  queryFnCallCount: () => number;
  /**
   * Awaits every query thunk RTK Query currently has running (ADR-0150 — a
   * store condition, not a timer), for a settlement this harness did not
   * hand back a promise for itself — a focus/visibility-triggered refetch,
   * dispatched by RTK Query's own middleware rather than by `request()`.
   * Wrapped in Testing Library's `act()`, which also renders any state this
   * settlement caused — use `settleBare` instead to await a settlement
   * while keeping the autobatch notification held (see its own comment).
   */
  settle: () => Promise<void>;
  /**
   * The same wait as `settle`, WITHOUT the `act()` wrapper. `act()` itself —
   * independent of this harness's held `queueNotification` — forces React to
   * re-check `useSyncExternalStore`'s snapshot on every call, so an `act()`
   * anywhere between two held settlements renders the first one early
   * (proved by running both ways: identical `queryFnCallCount` progression,
   * but `renderLog` grows immediately with `settle`, only on `flush()` with
   * `settleBare`). Use this to await a focus-triggered settlement while a
   * "coalesced" scenario still needs the notification held across more than
   * one of them.
   */
  settleBare: () => Promise<void>;
}

function buildHarnessStore(api: ReturnType<typeof buildApi>['api'], withListenerMiddleware: boolean) {
  let heldNotify: (() => void) | undefined;
  const listenerMiddleware = createListenerMiddleware();

  const store = configureStore({
    reducer: { [api.reducerPath]: api.reducer },
    middleware: (getDefaultMiddleware) => {
      const base = getDefaultMiddleware().concat(api.middleware);
      return withListenerMiddleware ? base.prepend(listenerMiddleware.middleware) : base;
    },
    enhancers: (getDefaultEnhancers) =>
      getDefaultEnhancers({
        autoBatch: {
          type: 'callback',
          queueNotification: (notify) => {
            heldNotify = notify;
          },
        },
      }),
  });

  function flush(): void {
    act(() => {
      const notify = heldNotify;
      heldNotify = undefined;
      notify?.();
    });
  }

  return { store, flush };
}

/**
 * Builds one isolated scripted store + Provider-wrapped probe. Each call is
 * fully independent — no module-level state survives between tests.
 *
 * `withListenerMiddleware: false` (FR-006, T002's second store variant)
 * reproduces the "store reachable through react-redux but lacking the
 * listener middleware" case from spec.md's bad-request scenario.
 */
export function createRevocationHarness(options: { withListenerMiddleware?: boolean } = {}): RevocationHarness {
  const { api, responses, queryFnCalls } = buildApi();
  const { store, flush } = buildHarnessStore(api, options.withListenerMiddleware !== false);
  const renderLog: RenderLogEntry[] = [];
  const refusedByRenderRef = { current: false };

  function request(
    outcome: ScriptedOutcome,
    args: ScriptedArgs,
    requestOptions: { forceRefetch?: boolean } = {},
  ): { requestId: string; settled: Promise<unknown> } {
    responses.push(outcome);
    const thunkResult = store.dispatch(
      api.endpoints.getThing.initiate(args, { subscribe: false, forceRefetch: requestOptions.forceRefetch ?? false }),
    );
    return { requestId: thunkResult.requestId, settled: thunkResult };
  }

  function Probe({
    subject,
    args,
    notFoundRevokes,
  }: {
    subject: string;
    args: ScriptedArgs;
    /** Spec 313 (#2750): opts a 404 settlement into counting, same as the real pages' field. */
    notFoundRevokes?: boolean;
  }): null {
    const query = api.endpoints.getThing.useQueryState(args);
    const queryState: RevocationQueryState = {
      error: query.error,
      isFetching: query.isFetching,
      requestId: query.requestId,
      notFoundRevokes,
    };
    // The third argument is the hook's eventual `source` parameter
    // (plan.md §1.2, not yet added — T006). Today's two-argument hook
    // ignores it at runtime, which is why this executes instead of throwing;
    // `tsc --noEmit` reports the extra argument, and that diagnostic is
    // deliberately left unsuppressed — it is part of the red (plan.md §4).
    const refused = useRevocationFallback(subject, queryState, { endpoint: api.endpoints.getThing, args });
    // Recorded from an effect, not during render: the render-log/result are
    // plain objects owned by the test, not `useRef`s, and
    // react-hooks/immutability forbids mutating anything else mid-render.
    // Effects flush synchronously inside `act()` (both `render` and
    // `flush` use it), so the test still observes every committed render.
    useEffect(() => {
      renderLog.push({ requestId: query.requestId, isFetching: query.isFetching });
      refusedByRenderRef.current = refused;
    }, [query.requestId, query.isFetching, refused]);
    return null;
  }

  function Subscribe({ args, refetchOnFocus }: { args: ScriptedArgs; refetchOnFocus?: boolean }): null {
    api.endpoints.getThing.useQuery(args, refetchOnFocus === undefined ? undefined : { refetchOnFocus });
    return null;
  }

  function queryFnCallCount(): number {
    return queryFnCalls.current;
  }

  async function settle(): Promise<void> {
    await act(async () => {
      const running = store.dispatch(api.util.getRunningQueriesThunk());
      await Promise.all(running);
    });
  }

  async function settleBare(): Promise<void> {
    const running = store.dispatch(api.util.getRunningQueriesThunk());
    await Promise.all(running);
  }

  return {
    store,
    responses,
    renderLog,
    refusedByRenderRef,
    flush,
    request,
    Probe,
    Subscribe,
    queryFnCallCount,
    settle,
    settleBare,
  };
}

export function HarnessProvider({
  harness,
  children,
}: {
  harness: RevocationHarness;
  children: ReactNode;
}): ReactElement {
  return <Provider store={harness.store}>{children}</Provider>;
}
