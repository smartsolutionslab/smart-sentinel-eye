import { useState } from 'react';
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

interface RevocationState {
  subject: string;
  counted: string | undefined;
  strikes: number;
}

/**
 * True once {@link REVOCATION_STRIKE_THRESHOLD} consecutive settled responses
 * for `subject` were a refusal (403, or 404 where `notFoundRevokes`) (spec
 * 310 #2725, spec 313 #2750) — the signal a page masks its data with to fall
 * back to its existing no-data-plus-error render (FR-004). A page never
 * reads `error.status` itself; this hook is the only caller of
 * {@link isForbidden} and {@link isNotFound} (FR-005).
 *
 * Counts one strike per settled (`!isFetching`), not-yet-counted `requestId`.
 * Any settled non-refusal resets the count to zero, as does a change of
 * `subject`. A `requestId` of `undefined` — every existing page test's mock
 * query result — never counts, so those tests render exactly as before.
 *
 * State is adjusted **during render** (the same pattern `CamerasPage` uses
 * for `lastFragment`), not in an effect, so React StrictMode cannot double-
 * count one response and the boolean is correct in the same commit as the
 * response that produced it.
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
export function useRevocationFallback(subject: string, query: RevocationQueryState): boolean {
  const [state, setState] = useState<RevocationState>({ subject, counted: undefined, strikes: 0 });

  let next = state;
  if (subject !== state.subject) {
    next = { subject, counted: undefined, strikes: 0 };
  }

  const settledAndNew = !query.isFetching && query.requestId !== undefined && query.requestId !== next.counted;
  if (settledAndNew) {
    next = {
      subject: next.subject,
      counted: query.requestId,
      strikes:
        isForbidden(query.error) || (query.notFoundRevokes === true && isNotFound(query.error)) ? next.strikes + 1 : 0,
    };
  }

  if (next !== state) {
    setState(next);
  }

  return next.strikes >= REVOCATION_STRIKE_THRESHOLD;
}
