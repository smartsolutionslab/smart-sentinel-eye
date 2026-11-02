import { useGetLayoutQuery } from '@smart-sentinel-eye/shared/api/layouts.api';
import { Button } from '@smart-sentinel-eye/shared/ui/primitives/Button';
import { logResilienceEvent } from '@smart-sentinel-eye/shared/observability/resilienceLog';
import { useCallback, useEffect, useRef } from 'react';
import { useAuth } from 'react-oidc-context';
import { useNavigate } from 'react-router-dom';
import { LiveUpdatesBadge } from '../revocation/LiveUpdatesBadge.js';
import { useLayoutLifecycle } from '../revocation/useLayoutLifecycle.js';
import { useOverlayHubHandlers } from './useOverlayHubHandlers.js';
import { useWallAlignment } from './useWallAlignment.js';
import { useWallCredential } from './useWallCredential.js';
import { WallCredentialNotice } from './WallCredentialNotice.js';
import { EmptyCell, FullScreen, gridPlacementStyle, WallHeader } from './layoutGrid/gridPieces.js';
import { Tile } from './layoutGrid/Tile.js';
import { useLatestAccessToken } from './layoutGrid/useLatestAccessToken.js';
import { boundOverlayIn, namedFab } from './wallBindings.js';
import { buildGridItems } from './wallGrid.js';

export interface LayoutGridProps {
  /** The Layout chain to render — a route param for `CellPage`, or a wall's current `Showing` for `WallPage` (spec 258 plan.md §6.3). */
  layoutIdentifier: string;
  /**
   * Called instead of navigating to the picker when this layout becomes
   * unavailable (archived, or loaded with no Published revision). Defaults
   * to `navigate('/')`, so `CellPage` — which doesn't pass this — is
   * completely unchanged. `WallPage` (spec 258 PD-6) passes a no-op: a wall
   * does not auto-advance off itself, it stays put showing this component's
   * own archived-layout placeholder (24/7 operation).
   */
  onUnavailable?: () => void;
  /**
   * Overrides the header's title (normally the current scene's own layout
   * name) and hides the "Back" button. `WallPage` passes the wall's name
   * here: a wall's header should read as the wall, not whichever scene it
   * happens to be showing, and a wall page offers no way to leave the wall
   * (spec 258 PD-6). `CellPage` doesn't pass this, so its header is
   * unchanged.
   */
  headerTitle?: string;
}

/**
 * Renders one Layout chain's currently Published revision as a tile grid
 * (spec 010 US2 + US3). Extracted from `CellPage` (spec 258 T063,
 * behaviour-preserving — `CellPage.test.tsx` covers this component
 * unmodified via `CellPage`, which is now a thin route wrapper) so
 * `WallPage` can reuse the same renderer for whichever scene a wall is
 * currently showing, remounting it on every scene change via `key`.
 *
 * Renders the Published revision's tile set as a CSS grid
 * (`gridRows × gridCols`); each populated cell is a `<CameraTile>` (spec
 * 002 composite, wrapped per #2846/ADR-0169) owning its per-tile overlay fetch + resolved-text
 * snapshot. Empty cells render a placeholder.
 *
 * N=1 (including layouts migrated from before this feature) is a 1×1 grid
 * with one tile, so it renders identically to the pre-feature single-cell
 * view (FR-011, SC-004).
 *
 * Per-tile highlight (US3): on an `OverlayHighlightChanged` frame, every
 * tile whose `overlayIdentifier` matches lights for `durationMs`, then
 * auto-reverts; overlapping durations on the same overlay are OR'd
 * (highlight-all-matching, ADR-0112 §5). A highlight for an overlay bound
 * to no rendered tile is a no-op.
 */
export function LayoutGrid({ layoutIdentifier, onUnavailable, headerTitle }: LayoutGridProps) {
  const navigate = useNavigate();
  const auth = useAuth();
  const handleUnavailable = useCallback(() => {
    if (onUnavailable !== undefined) {
      onUnavailable();
      return;
    }
    navigate('/', { replace: true });
  }, [navigate, onUnavailable]);

  const getToken = useLatestAccessToken(auth.user?.access_token);
  const { data, isLoading, error, refetch } = useGetLayoutQuery(layoutIdentifier, {
    skip: layoutIdentifier === '',
  });

  const published = data?.revisions.find((revision) => revision.state === 'Published');
  const tiles = published?.tiles ?? [];
  const items = published === undefined ? [] : buildGridItems(published.gridRows, published.gridCols, tiles);
  const wallCredential = useWallCredential(
    items.filter((item) => item.tile !== null).map((item) => item.key),
    auth.user?.access_token,
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

  // Spec 045: the wall's playout control loop. Only this page sees every tile,
  // which is why the decision lives here and the actuation lives in the tile.
  // Below two tiles it does nothing and sets nothing (FR-004).
  const alignment = useWallAlignment(tiles.length, getToken);

  // Spec 141: one latch set keyed by transition, so FR-002 and FR-004 each
  // fire at most once per mounted page per session (FR-007) without needing
  // two separate booleans.
  const reportedLayoutFaultsRef = useRef<Set<string>>(new Set());

  // Spec 229: the overlay-scoped hub handlers (OverlayRevisionPublished,
  // OverlayRevisionArchived, ResolvedOverlayTextChanged,
  // OverlayHighlightChanged) and the state they own now live in their own
  // hook, spread into useLayoutLifecycle below.
  const overlayHub = useOverlayHubHandlers(tiles, wallFab);

  // Spec 141 FR-002/FR-004: a layout row that omits (or blanks) a tile's
  // overlay identifier, or a layout that carries no fab at all, no longer
  // takes the safe-looking branch silently — each says so once per mounted
  // page. An effect, not the render body (plan R4): a `console.info` during
  // render doubles under StrictMode, and the latch above is belt-and-braces
  // rather than the only defence. `countReportableSkew` is not touched (plan
  // invariant 3): it still compares `wallFab === undefined` directly rather
  // than through `namedFab`, so a whitespace or empty `wallFab` still lets it
  // report noise. That gap is deliberate and accepted (SC-6), not a defect
  // tracked elsewhere.
  //
  // The `published === undefined` guard below is also what keeps FR-004
  // silent while the layout is still loading: `wallFab` is legitimately
  // `undefined` in that window for a reason that has nothing to do with a
  // fab-less layout, and this early return is what tells the two apart. A
  // future split of this effect has to keep that coupling or risk a false
  // `layout-without-fab` line on every page load.
  useEffect(() => {
    if (published === undefined) return;
    const affectedTiles = published.tiles.filter(
      (candidate) => candidate.overlayIdentifier !== null && boundOverlayIn(candidate.overlayIdentifier) === null,
    ).length;
    if (affectedTiles > 0 && !reportedLayoutFaultsRef.current.has('tile-without-overlay-identifier')) {
      reportedLayoutFaultsRef.current.add('tile-without-overlay-identifier');
      logResilienceEvent('hub', 'tile-without-overlay-identifier', {
        layout: layoutIdentifier,
        tiles: affectedTiles,
      });
    }
    if (namedFab(wallFab) === null && !reportedLayoutFaultsRef.current.has('layout-without-fab')) {
      reportedLayoutFaultsRef.current.add('layout-without-fab');
      logResilienceEvent('hub', 'layout-without-fab', { layout: layoutIdentifier });
    }
  }, [layoutIdentifier, published, wallFab]);

  const { degraded } = useLayoutLifecycle({
    accessTokenFactory: () => auth.user?.access_token ?? '',
    enabled: auth.isAuthenticated,
    onArchived: (message) => {
      if (message.layout === layoutIdentifier) {
        handleUnavailable();
      }
    },
    ...overlayHub.handlers,
    onReconnected: () => {
      void refetch();
    },
  });

  useEffect(() => {
    if (!isLoading && error === undefined && data !== undefined && published === undefined) {
      handleUnavailable();
    }
  }, [data, error, isLoading, handleUnavailable, published]);

  // `headerTitle` (WallPage) has to stay visible through every branch below —
  // loading, the archived/unavailable fallback, and the rendered grid — so a
  // wall's own name doesn't blink out while a scene it just switched to is
  // still loading. `CellPage` doesn't pass it, so its loading/fallback
  // screens stay exactly as before (no header at all).
  const wallHeader = headerTitle !== undefined ? <WallHeader title={headerTitle} /> : undefined;

  if (isLoading) {
    return <FullScreen message="Loading camera…" header={wallHeader} />;
  }
  if (error !== undefined || data === undefined || published === undefined || tiles.length === 0) {
    return (
      <FullScreen
        message="Layout is no longer available."
        header={wallHeader}
        action={
          headerTitle === undefined ? (
            <Button variant="secondary" size="touch" onClick={() => navigate('/')}>
              Back to picker
            </Button>
          ) : undefined
        }
      />
    );
  }

  return (
    <main className="relative min-h-screen bg-black">
      <WallHeader
        title={headerTitle ?? data.name}
        onBack={headerTitle === undefined ? () => navigate('/') : undefined}
      />
      <div
        data-testid="layout-grid"
        className="grid h-screen gap-1 p-1"
        style={{
          gridTemplateColumns: `repeat(${published.gridCols}, minmax(0, 1fr))`,
          gridTemplateRows: `repeat(${published.gridRows}, minmax(0, 1fr))`,
        }}
      >
        {items.map((item) => {
          const placement = gridPlacementStyle(item.row, item.col, item.rowSpan, item.colSpan);
          if (item.tile === null) {
            return <EmptyCell key={item.key} style={placement} />;
          }
          const boundOverlay = boundOverlayIn(item.tile.overlayIdentifier);
          return (
            <Tile
              key={`${item.key}:${wallCredential.credentialGeneration}`}
              onRefused={wallCredential.reportRefusal}
              onLive={wallCredential.reportLive}
              onRefusalLifted={wallCredential.clearRefusal}
              tile={item.tile}
              fab={data.fab}
              getToken={getToken}
              unavailable={boundOverlay !== null && overlayHub.unavailableOverlays.has(boundOverlay)}
              highlighted={boundOverlay !== null && overlayHub.highlightedOverlays.has(boundOverlay)}
              playoutTargetMilliseconds={alignment.targetFor(item.key)}
              frameAgeFor={alignment.frameAgeFor}
              tileKey={item.key}
              onLagMeasured={(camera, lag, buffer) => alignment.reportLag(item.key, camera, lag, buffer)}
              outOfAlignment={alignment.released.has(item.key)}
              onLabelVerdict={overlayHub.onLabelVerdict}
              style={placement}
            />
          );
        })}
      </div>
      {wallCredential.state !== 'idle' && <WallCredentialNotice state={wallCredential.state} />}
      <LiveUpdatesBadge degraded={degraded} />
    </main>
  );
}
