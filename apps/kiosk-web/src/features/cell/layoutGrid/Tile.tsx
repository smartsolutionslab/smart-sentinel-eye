import type { LayoutTile } from '@smart-sentinel-eye/shared/api/layouts.api';
import { Badge } from '@smart-sentinel-eye/shared/ui/composites/Badge';
import clsx from 'clsx';
import type { CSSProperties } from 'react';
import { CameraTile } from '../CameraTile.js';
import { TileAlignmentBadge } from '../TileAlignmentBadge.js';
import { useHeldOverlay } from './useHeldOverlay.js';
import { useTileLiveElements } from './useTileLiveElements.js';
import { useTileOverlay } from './useTileOverlay.js';

interface TileProps {
  tile: LayoutTile;
  /**
   * The wall's fab, so this tile's label resolves in that plant (ADR-0145).
   * Taken from the loaded layout, so it is present by the time a tile renders;
   * an empty one would mean "any fab" on the query string and is skipped below.
   */
  fab: string;
  getToken: () => Promise<string | null>;
  /** The bound overlay went Archived (spec 004 path, applied per tile). */
  unavailable: boolean;
  /** A matching `OverlayHighlightChanged` frame is currently active. */
  highlighted: boolean;
  /** How far behind live to hold this tile, or null to leave it alone (spec 045). */
  playoutTargetMilliseconds: number | null;
  /** Reports this tile's measured lag to the wall's controller (spec 045). */
  onLagMeasured: (cameraIdentifier: string, lagMilliseconds: number, bufferMilliseconds: number) => void;
  /** This tile could not be held inside the leg's budget (spec 045 FR-012). */
  outOfAlignment: boolean;
  /**
   * How old this tile's picture is, so its label can be held back to match
   * (spec 046, ADR-0129). Null when unreadable — the label then shows at once.
   *
   * <p>
   * A getter, not a value: the tile calls this itself, at the moment it
   * renders, rather than receiving an age the parent computed on its own last
   * render. `CellPage` re-renders on no fixed cadence, so a value handed down
   * as a prop would go stale between the parent's renders; the tile is kept
   * current by its own RTK Query subscriptions instead (spec 204).
   * </p>
   */
  frameAgeFor: (tileKey: string) => number | null;
  /** This tile's key in the layout grid, passed to `frameAgeFor` above. */
  tileKey: string;
  /**
   * Reports this tile's own placeholder verdict for its bound overlay up to
   * the page (spec 141 site 3, FR-005) — the same tile→page shape as
   * `onLagMeasured` above (spec 045): a stable callback writing a page-level
   * ref, so a later resolved-text push for this overlay can be checked for
   * disagreement. Called only once the tile actually knows the text.
   */
  onLabelVerdict: (overlayIdentifier: string, hasPlaceholder: boolean) => void;
  /** Reports this tile's terminal stream refusal to the wall's credential state (spec 439). */
  onRefused: (tileKey: string, kind: 'unauthorized' | 'forbidden') => void;
  /** Reports that this tile's stream is live, which ends a credential episode. */
  onLive: (tileKey: string) => void;
  /** Reports that this tile's reported refusal ended without going live. */
  onRefusalLifted: (tileKey: string) => void;
  /** Explicit CSS grid placement for this tile's span (spec 262 FR-008). */
  style: CSSProperties;
}

/**
 * One populated grid cell. Owns its overlay fetch + resolved-text snapshot
 * so each tile resolves its own label independently (per-tile binding,
 * FR-011). The bound overlay's geometry comes from OverlayDesigner; the live
 * label text comes from the SystemVariables snapshot, falling back to the
 * raw label when the snapshot is unavailable.
 */
export function Tile({
  tile,
  fab,
  getToken,
  unavailable,
  highlighted,
  playoutTargetMilliseconds,
  onLagMeasured,
  outOfAlignment,
  frameAgeFor,
  tileKey,
  onLabelVerdict,
  onRefused,
  onLive,
  onRefusalLifted,
  style,
}: TileProps) {
  // Read at the moment this tile renders, not handed down as a value computed
  // during the parent's last render (spec 204) — the parent has no reason to
  // re-render on its own cadence, so a stale prop would silently freeze the
  // label's held age. `Tile` is kept current by its own RTK Query
  // subscriptions below.
  const frameAgeMilliseconds = frameAgeFor(tileKey);
  const { overlayIdentifier, publishedOverlay, overlayUnavailable, elements, textElements, hasPlaceholder } =
    useTileOverlay({
      tile,
      unavailable,
      onLabelVerdict,
    });
  const { liveElements } = useTileLiveElements({
    overlayIdentifier,
    publishedOverlay,
    elements,
    textElements,
    hasPlaceholder,
    fab,
  });
  const { renderElements } = useHeldOverlay({
    liveElements,
    frameAgeMilliseconds,
    overlayUnavailable,
    cameraIdentifier: tile.cameraIdentifier,
    getToken,
    highlighted,
  });

  return (
    <div
      data-testid="layout-tile"
      data-highlighted={highlighted ? 'true' : 'false'}
      data-camera-identifier={tile.cameraIdentifier}
      className={clsx(
        'relative flex h-full w-full items-center justify-center overflow-hidden rounded-md',
        highlighted && 'ssE-overlay-highlight',
      )}
      style={style}
    >
      {overlayUnavailable && (
        <Badge tone="warning" size="md" asChild className="absolute left-[50%] top-2 z-10 -translate-x-[50%]">
          <div role="status">Overlay unavailable</div>
        </Badge>
      )}
      {outOfAlignment && <TileAlignmentBadge camera={tile.cameraIdentifier} />}
      <CameraTile
        cameraIdentifier={tile.cameraIdentifier}
        getToken={getToken}
        overlays={renderElements}
        playoutTargetMilliseconds={playoutTargetMilliseconds}
        onLagMeasured={onLagMeasured}
        onRefused={(kind) => onRefused(tileKey, kind)}
        onLive={() => onLive(tileKey)}
        onRefusalLifted={() => onRefusalLifted(tileKey)}
      />
    </div>
  );
}
