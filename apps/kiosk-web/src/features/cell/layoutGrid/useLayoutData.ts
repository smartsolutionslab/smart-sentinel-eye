import { useGetLayoutQuery } from '@smart-sentinel-eye/shared/api/layouts.api';
import { useWallCredential } from '../useWallCredential';
import { buildGridItems } from '../wallGrid';

export function useLayoutData(layoutIdentifier: string, accessToken: string | undefined) {
  const { data, isLoading, error, refetch } = useGetLayoutQuery(layoutIdentifier, {
    skip: layoutIdentifier === '',
  });

  const published = data?.revisions.find((revision) => revision.state === 'Published');
  const tiles = published?.tiles ?? [];
  const items = published === undefined ? [] : buildGridItems(published.gridRows, published.gridCols, tiles);
  const wallCredential = useWallCredential(
    items.filter((item) => item.tile !== null).map((item) => item.key),
    accessToken,
  );

  // ADR-0145: the wall's fab is *derived* from the layout it displays — never
  // chosen, never inferred from the token, never held as session state. It is
  // both the fab the opening label resolves in and the fab a pushed frame has
  // to carry to be applied. `undefined` while the layout is still loading —
  // and both hub handlers in useOverlayHubHandlers read it through `namedFab`,
  // which classifies that `undefined` (and any blank or whitespace-only fab)
  // as "no fab", so every frame is dropped in that window by the guard, not
  // by coincidence of equality.
  //
  // `undefined` rather than a `''` sentinel, because unmatchability has to
  // hold on both halves of this. `namedFab` is what actually enforces that:
  // it classifies `undefined`, `''`, and any whitespace-only fab alike as
  // "no fab", so none of them can equal another absent fab — no bare `!==`
  // is trusted to get this right on its own. On the snapshot query's *query
  // string*, though, an empty or whitespace fab is maximally permissive: the
  // server reads it as "resolve across every fab I hold" and answers with
  // whichever sorts first, which is the defect this filter exists to close.
  // `undefined` cannot reach that query at all, since
  // `OverlaySnapshotInput.fabId` is a required `string`. Any future default
  // has to keep that unmatchability on both halves.
  const wallFab = data?.fab;

  return { data, isLoading, error, refetch, published, tiles, items, wallCredential, wallFab };
}
