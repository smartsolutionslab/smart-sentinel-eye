/**
 * A tile's overlay identifier, or `null` when it names none (spec 141
 * FR-001). All four readers of the sentinel go through this one function:
 * `tilesToBoundOverlays` (`useOverlayHubHandlers`), the `unavailable`/`highlighted`
 * props and the `Tile` query's `skip` (`CellPage`).
 *
 * Parameter deliberately wider than `LayoutTile.overlayIdentifier`'s declared
 * `string | null` — admitting `undefined` too — exactly as
 * `countReportableSkew` takes `frameFab: string | undefined` where
 * `message.fab` is `string`. `LayoutTile` itself is not widened (FR-001):
 * doing so would push a `| undefined` through every consumer in both apps to
 * describe a server that does not exist.
 */
export function boundOverlayIn(overlayIdentifier: string | null | undefined): string | null {
  return typeof overlayIdentifier === 'string' && overlayIdentifier !== '' ? overlayIdentifier : null;
}

/**
 * The same test as `boundOverlayIn`, for the wall's fab (spec 141 site 2,
 * FR-004). `Layout.fab` stays declared `string` (not widened, same reasoning
 * as above), so the wider parameter here is what admits the drift this
 * guards against.
 */
export function namedFab(fab: string | undefined): string | null {
  return typeof fab === 'string' && fab.trim() !== '' ? fab : null;
}
