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
 * `useRevocationFallbackCoalesced.test.tsx` is the only consumer today,
 * exercising the hook's three-argument signature (plan.md §1.2) against the
 * listener path phase 4b added.
 */

/** The scripted query's args — one cache key per "subject" under test. */
export interface ScriptedArgs {
  key: string;
}

/** What the next dispatched request for this harness should answer. */
export type ScriptedOutcome = 'ok' | 403 | 404 | 503;

function buildApi() {
  const responses: ScriptedOutcome[] = [];
  const api = createApi({
    reducerPath: 'revocationHarness',
    baseQuery: fakeBaseQuery<{ status: number }>(),
    endpoints: (builder) => ({
      getThing: builder.query<{ outcome: 'ok' }, ScriptedArgs>({
        queryFn: () => {
          const next = responses.shift();
          if (next === undefined) {
            throw new Error('revocationHarness: scripted response queue is empty — push one before dispatching');
          }
          return next === 'ok' ? { data: { outcome: 'ok' } } : { error: { status: next } };
        },
      }),
    }),
  });
  return { api, responses };
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
  const { api, responses } = buildApi();
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

  return { store, responses, renderLog, refusedByRenderRef, flush, request, Probe };
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
