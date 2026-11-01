import { skipToken } from '@reduxjs/toolkit/query/react';
import { useGetLayoutQuery } from '@smart-sentinel-eye/shared/api/layouts.api';

export function useLayoutChain(layoutIdentifier: string | undefined) {
  // The If-Match version has to be the chain's *current* one (ADR-0113).
  // The page branches a new draft before opening this dialog, and that branch
  // is itself a write, so the version the page held is already one behind.
  // Reading it back rather than inferring "+1" keeps the client from doing
  // arithmetic on server state -- and still fails correctly if another
  // operator moves the chain while the dialog is open.
  //
  // `currentData`, never `data` (spec 153): `data` is RTK Query's last
  // successful result for ANY argument this hook has ever been called with,
  // so it survives a `skipToken` step and an argument change. This dialog
  // stays permanently mounted in `LayoutsPage` and is driven
  // `editTarget: A -> undefined -> B` on the same component -- not an
  // unmount and a fresh mount -- so `data` would hand layout B's dialog
  // layout A's chain, including A's version, before B's own GET has ever
  // answered. `currentData` resets on both, which is what "re-read", not
  // "reused", requires.
  //
  // `refetchOnMountOrArgChange`: kept even though the only path into this
  // dialog already re-fetches without it -- `branchDraftRevision`'s
  // invalidation lands with zero subscribers (`LayoutsPage.onEdit` awaits it
  // before `setEditTarget`), so the entry is evicted outright before the
  // dialog ever reopens on the same layout (spec.md "Window 3, corrected in
  // phase 4a"). The option is what makes FR-003 true on its own terms -- a
  // reopen within the 60s cache window always asks the server again --
  // rather than resting on an eviction behaviour that is RTK Query's
  // implementation detail, not a contract, and could change under a library
  // upgrade.
  const {
    currentData: currentChain,
    isError: chainFailed,
    isFetching: chainFetching,
    refetch: refetchChain,
  } = useGetLayoutQuery(layoutIdentifier ?? skipToken, { refetchOnMountOrArgChange: true });
  return { currentChain, chainFailed, chainFetching, refetchChain };
}
