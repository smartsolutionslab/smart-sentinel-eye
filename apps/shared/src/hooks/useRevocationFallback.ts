import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { addListener } from '@reduxjs/toolkit';
import { useDispatch } from 'react-redux';
import { isForbidden, isNotFound } from '../api/problemDetail.js';

/** Consecutive refusals (403, or a qualifying 404) to a subject's query before its refusal surface shows (spec 310 #2725, spec 313 #2750). */
export const REVOCATION_STRIKE_THRESHOLD = 3;

/** The slice of an RTK Query hook's result `useRevocationFallback` needs. */
export interface RevocationQueryState {
  error: unknown;
  isFetching: boolean;
  requestId: string | undefined;
  /**
   * Opt-in (spec 313): true when this subject's cache entry already holds a
   * successfully loaded record AND the resource is never deleted, so a 404 can
   * only mean access was lost. Omit on list pages — a 404 there resets.
   */
  notFoundRevokes?: boolean;
}

/** The post-reducer shape of an RTK Query cache entry, as `endpoint.select(args)(state)` returns it. */
interface RevocationCacheEntry {
  requestId?: string;
  error?: unknown;
}

/**
 * A settled (fulfilled or rejected) RTK Query action, narrowed to the fields
 * the listener reads. Shaped like redux's own `UnknownAction` — `type` plus
 * an index signature, not just the one field this hook reads — because
 * `addListener`'s type-guard overload only infers its effect's action type
 * when the guarded type satisfies that shape structurally; without the index
 * signature TypeScript silently falls back to the untyped overload and
 * `action` in the effect comes back as `UnknownAction` (not narrowed).
 */
interface RevocationSettledAction {
  type: string;
  meta: { requestId: string };
  [extraProps: string]: unknown;
}

/**
 * What the hook needs from the page's RTK Query endpoint to observe its own
 * settlements from the action stream (spec 314). `select`'s `state` parameter
 * is deliberately typed `never`: any concrete `RootState`-typed `select` is
 * assignable to that (parameter contravariance), so a page's real endpoint —
 * whatever its store's root state type — satisfies this structurally without
 * the hook naming that type.
 */
export interface RevocationSource<Args> {
  /** The RTK Query endpoint the page's query hook reads. */
  endpoint: {
    select: (args: Args) => (state: never) => RevocationCacheEntry;
    matchFulfilled: (action: unknown) => action is RevocationSettledAction;
    matchRejected: (action: unknown) => action is RevocationSettledAction;
  };
  /** The same argument value passed to the page's `useXQuery` call — otherwise `select` reads a different cache entry. */
  args: Args;
}

interface RevocationState {
  subject: string;
  counted: string | undefined;
  strikes: number;
}

/**
 * The one transition both observation paths apply: count a settlement at
 * most once (keyed by `requestId`), incrementing on a refusal (403, or a
 * qualifying 404 — spec 313 #2750) and resetting on anything else (spec 314
 * FR-002/FR-003).
 */
function applySettlement(
  state: RevocationState,
  requestId: string,
  error: unknown,
  notFoundRevokes: boolean,
): RevocationState {
  if (requestId === state.counted) {
    return state;
  }
  const isRefusal = isForbidden(error) || (notFoundRevokes && isNotFound(error));
  return { subject: state.subject, counted: requestId, strikes: isRefusal ? state.strikes + 1 : 0 };
}

/**
 * True once {@link REVOCATION_STRIKE_THRESHOLD} consecutive settled responses
 * for `subject` were a refusal (403, or 404 where `notFoundRevokes`) (spec
 * 310 #2725, spec 313 #2750) — the signal a page masks its data with to fall
 * back to its existing no-data-plus-error render (FR-004). A page never
 * reads `error.status` itself; this hook is the only caller of
 * {@link isForbidden} and {@link isNotFound} (FR-005).
 *
 * Counts one strike per settled, not-yet-counted `requestId`; any settled
 * non-refusal resets the count to zero, as does a change of `subject`.
 *
 * **Two observation paths, one transition (spec 314 #2762).** `configureStore`'s
 * default `autoBatchEnhancer` can coalesce a settlement's subscriber
 * notification with the next dispatched action — state updates synchronously,
 * but `store.subscribe` (and therefore everything React renders) can skip
 * straight past the in-between state. Counting only from render dropped a
 * strike, and symmetrically a reset, whenever that happened. So the hook
 * also registers an RTK listener middleware subscription (`dispatch(addListener(...))`
 * from a `useEffect`) that observes every fulfilled/rejected action for
 * `source.endpoint` at dispatch time — after the reducer, before any
 * notification — and applies it through the same {@link applySettlement}.
 * Ownership is checked against the post-reducer cache entry's `requestId`, so
 * a condition-aborted or superseded request's action is never counted. Each
 * path is deduplicated by `requestId` (`applySettlement`'s `counted` check),
 * so the two never double-count one settlement, including under
 * React.StrictMode's double effect invocation.
 *
 * The render path still runs (counts a settled, not-yet-counted `requestId`
 * seen in render) for two cases the listener cannot cover: a cache entry
 * already settled **before** this mount's listener existed (see "Returning
 * to a previously-visited subject" below), and page tests that mock the
 * query result directly and dispatch nothing.
 *
 * Requires a Redux Toolkit listener middleware in the store (any instance;
 * `addListener` is a global action creator) — throws naming the fix if one
 * is not present, rather than silently degrading to render-only counting
 * (FR-006).
 *
 * A `requestId` of `undefined` — every existing page test's mock query
 * result — never counts, so those tests render exactly as before.
 *
 * **Returning to a previously-visited subject.** RTK Query can still hold a
 * cached, rejected entry for a subject's `requestId` from an earlier visit.
 * On the first render back — before the natural refetch this hook's own
 * caller triggers has a chance to start — that cached `requestId` is new to
 * THIS mount's `counted`, so it is read as a fresh strike. This is harmless:
 * it is a real 403 that subject actually received, the threshold still
 * behaves correctly within the visit, and the refetch that follows settles
 * with its own `requestId` as usual. Flagged so a future reader does not
 * "fix" this into skipping it and double-counting a later response instead.
 */
export function useRevocationFallback<Args>(
  subject: string,
  query: RevocationQueryState,
  source: RevocationSource<Args>,
): boolean {
  const dispatch = useDispatch();
  const [state, setState] = useState<RevocationState>({ subject, counted: undefined, strikes: 0 });

  // Read by the listener effect below, whose own dependency array is
  // deliberately stable across renders (see the comment on it) — a ref is
  // how it sees this render's `notFoundRevokes` without resubscribing.
  // Layout, not passive, matching `useWhepSession`'s `getTokenRef` (#2740,
  // ADR-0167): a settlement dispatched between this commit and a later
  // passive-effect pass must still see this render's value, not the
  // previous one — mutating a ref directly during render (as opposed to
  // committing it in an effect) is also not safe to do unconditionally.
  const notFoundRevokesRef = useRef(query.notFoundRevokes === true);
  useLayoutEffect(() => {
    notFoundRevokesRef.current = query.notFoundRevokes === true;
  });

  let next = state;
  if (subject !== state.subject) {
    next = { subject, counted: undefined, strikes: 0 };
  }

  const settledAndNew = !query.isFetching && query.requestId !== undefined && query.requestId !== next.counted;
  if (settledAndNew) {
    next = applySettlement(next, query.requestId as string, query.error, query.notFoundRevokes === true);
  }

  if (next !== state) {
    setState(next);
  }

  useEffect(() => {
    const { endpoint, args } = source;
    const select = endpoint.select(args);
    const isSettlementAction = (action: unknown): action is RevocationSettledAction =>
      endpoint.matchFulfilled(action) || endpoint.matchRejected(action);

    const unsubscribe: unknown = dispatch(
      addListener({
        predicate: (action: unknown, currentState: unknown): action is RevocationSettledAction =>
          isSettlementAction(action) && select(currentState as never).requestId === action.meta.requestId,
        effect: (action, listenerApi) => {
          const entry = select(listenerApi.getState() as never);
          // Read at dispatch time, not inside the updater below — `setState`
          // may defer running it to a later render, by which point the ref
          // could hold a different value than it did when this settled.
          const notFoundRevokes = notFoundRevokesRef.current;
          setState((s) =>
            s.subject === subject ? applySettlement(s, action.meta.requestId, entry.error, notFoundRevokes) : s,
          );
        },
      }),
    );

    if (typeof unsubscribe !== 'function') {
      throw new Error(
        'useRevocationFallback needs a Redux Toolkit listener middleware in the store ' +
          '(createListenerMiddleware(), prepended onto the middleware chain) to observe ' +
          'settlements at dispatch time — see spec 314 (#2762).',
      );
    }

    return unsubscribe as () => void;
    // `source.args` (and the rest of `source`) is deliberately left out: it is captured per
    // subscription, keyed by `subject` — at every call site `subject` changes iff `args` does
    // (spec 314 plan.md §1.2) — so depending on it would resubscribe on every render instead.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [dispatch, source.endpoint, subject]);

  return next.strikes >= REVOCATION_STRIKE_THRESHOLD;
}
