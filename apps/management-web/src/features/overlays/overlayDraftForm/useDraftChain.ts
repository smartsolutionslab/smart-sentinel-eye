import { useGetOverlayQuery } from '@smart-sentinel-eye/shared/api/overlays.api';
import { skipToken } from '@reduxjs/toolkit/query/react';

/**
 * The If-Match version has to be the chain's *current* one (ADR-0113), read
 * back rather than inferred: in US1 the page's held version merely might be
 * stale by the time Save is clicked, and in US2 the branch that got the
 * operator here was itself a write, so the page's version is already one
 * behind. `version + 1` is the obvious wrong implementation and it works on
 * a single-operator machine (LayoutEditorDialog.tsx:59-65).
 *
 * `currentData`, never `data` (phase-6 review, re-pinned by
 * `OverlayEditPageNavigation.test.tsx` on the route): `data` is RTK
 * Query's last successful result for ANY argument this hook has ever been
 * called with, so it survives a `skipToken` step and an argument change.
 * `currentData` resets on both, which is what "re-read", not "reused",
 * requires.
 *
 * `refetchOnMountOrArgChange`: without it a reopen within the 60s cache
 * window answers from cache with no request, which makes FR-011's "re-read
 * from the server" only sometimes true. Does not stand in for the
 * `currentData` fix above — `data` would still shadow a fresh fetch either
 * way.
 */
export function useDraftChain(overlayIdentifier: string | undefined) {
  const {
    currentData: currentChain,
    isError: chainFailed,
    isFetching: chainFetching,
    refetch: refetchChain,
  } = useGetOverlayQuery(overlayIdentifier ?? skipToken, { refetchOnMountOrArgChange: true });
  return { currentChain, chainFailed, chainFetching, refetchChain };
}
