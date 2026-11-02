import { Button } from '@smart-sentinel-eye/shared/ui/primitives/Button';
import { useCallback, useEffect } from 'react';
import { useAuth } from 'react-oidc-context';
import { useNavigate } from 'react-router-dom';
import { LiveUpdatesBadge } from '../revocation/LiveUpdatesBadge.js';
import { useLayoutLifecycle } from '../revocation/useLayoutLifecycle.js';
import { useOverlayHubHandlers } from './useOverlayHubHandlers.js';
import { useWallAlignment } from './useWallAlignment.js';
import { WallCredentialNotice } from './WallCredentialNotice.js';
import { EmptyCell, FullScreen, gridPlacementStyle, WallHeader } from './layoutGrid/gridPieces.js';
import { Tile } from './layoutGrid/Tile.js';
import { useLatestAccessToken } from './layoutGrid/useLatestAccessToken.js';
import { useLayoutData } from './layoutGrid/useLayoutData.js';
import { useLayoutFaultReporting } from './layoutGrid/useLayoutFaultReporting.js';
import { boundOverlayIn } from './wallBindings.js';

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
  const { data, isLoading, error, refetch, published, tiles, items, wallCredential, wallFab } = useLayoutData(
    layoutIdentifier,
    auth.user?.access_token,
  );

  // Spec 045: the wall's playout control loop. Only this page sees every tile,
  // which is why the decision lives here and the actuation lives in the tile.
  // Below two tiles it does nothing and sets nothing (FR-004).
  const alignment = useWallAlignment(tiles.length, getToken);

  // Spec 229: the overlay-scoped hub handlers (OverlayRevisionPublished,
  // OverlayRevisionArchived, ResolvedOverlayTextChanged,
  // OverlayHighlightChanged) and the state they own now live in their own
  // hook, spread into useLayoutLifecycle below.
  const overlayHub = useOverlayHubHandlers(tiles, wallFab);

  useLayoutFaultReporting({ layoutIdentifier, published, wallFab });

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
