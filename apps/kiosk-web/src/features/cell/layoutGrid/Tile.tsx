import type { LayoutTile } from '@smart-sentinel-eye/shared/api/layouts.api';
import { Badge } from '@smart-sentinel-eye/shared/ui/composites/Badge';
import type { CameraViewerOverlay } from '@smart-sentinel-eye/shared/ui/composites/CameraViewer';
import { measureOverlayDraw, reportKioskLatency } from '@smart-sentinel-eye/shared/observability/kioskLatency';
import clsx from 'clsx';
import { useCallback, useEffect, useState, type CSSProperties } from 'react';
import { CameraTile } from '../CameraTile.js';
import { TileAlignmentBadge } from '../TileAlignmentBadge.js';
import { useLabelDelay } from '../useLabelDelay.js';
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
 * Keeps handing back the same `value` reference across renders whose `key` —
 * a cheap scalar derived from it — is unchanged, even though `value` itself
 * is a fresh object/array every render (spec 150, #2345). The opposite of
 * `useMemo`'s dependency-list form, which would need `value` itself listed as
 * a dependency and would therefore recompute on every reference change
 * regardless of content — exactly the trap `measureOverlayDraw`'s own effect
 * below avoids by keying on a derived scalar instead of the object
 * (#1888/#1889, ADR-0123). `undefined` in, `undefined` out.
 *
 * <p>
 * State, not a ref: `useLabelDelay.ts`'s own `adjustedFor` already uses this
 * "adjust state during render" idiom (React's documented pattern), which a
 * ref cannot — reading or writing `ref.current` during render is refused
 * outright (`react-hooks/refs`).
 * </p>
 */
function useStableByKey<T>(value: T, key: string | undefined): T {
  const [held, setHeld] = useState<{ key: string | undefined; value: T }>({ key, value });
  if (held.key !== key) {
    setHeld({ key, value });
    return value;
  }
  return held.value;
}

/**
 * A stable scalar over every PAINTED field of every element — kind, colour,
 * all four geometry fields, and (Text only) fontSizePx and text. A field
 * missing here makes a change to that field invisible to whichever key
 * consumes it. Shared by both keys so there is exactly one definition of
 * "every painted field" rather than two copies that can drift apart again.
 * `\u0000` cannot appear in an operator-authored label or a hex colour, so
 * this stays a lossless join for the purpose of detecting "did anything in
 * the set actually change".
 */
function paintedElementsKey(elements: readonly CameraViewerOverlay[] | undefined): string | undefined {
  return elements
    ?.map(
      (element) =>
        `${element.kind}|${element.color}|${element.normalizedX}|${element.normalizedY}|${element.normalizedWidth}|${element.normalizedHeight}|${element.kind === 'Text' ? element.fontSizePx : ''}|${element.kind === 'Text' ? element.text : ''}`,
    )
    .join('\u0000');
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
  const liveElementsKey = paintedElementsKey(liveElements);
  // The set is resolved and versioned atomically (FR-011), so a shared
  // reference — stable across renders that rebuild an equal array — is the
  // consistent reading: `liveElements` above is a fresh array every render,
  // and handing that straight to `useLabelDelay` would make every render
  // look like a change. `useStableByKey` is this file's version of the fix
  // #1888/#1889 already made for `measureOverlayDraw`'s effect below,
  // applied to the paired set instead of a text-only list.
  const stableLiveElements = useStableByKey(liveElements, liveElementsKey);

  // Held back so the label set describes the same moment as the picture
  // beneath it (ADR-0129). **Not frame accuracy** — it makes the set as old
  // as the picture and pairs nothing with a frame. A tile with no readable
  // age, or one past the cap, gets its labels immediately.
  // Reported per tile so one badly-buffered camera is visible, and reported
  // as what was achieved rather than what was asked for (FR-015).
  const reportHeld = useCallback(
    (achievedMilliseconds: number) =>
      reportKioskLatency('label_delay', tile.cameraIdentifier, achievedMilliseconds, getToken),
    [tile.cameraIdentifier, getToken],
  );

  // One `useLabelDelay` call per tile for the WHOLE PAIRED set (FR-014,
  // spec 301 US1) — hooks cannot be called in a loop, and the set shares one
  // age. Geometry now travels with its text, so a reorder or a Box/Text swap
  // during the hold cannot re-join a held text with a different element's
  // current geometry.
  const heldElements = useLabelDelay(stableLiveElements, frameAgeMilliseconds, reportHeld);

  // `overlayUnavailable` bypasses the hold, as it did before (ADR-0129,
  // "Removal is not held either") — it is a final selection, not a change to
  // what `useLabelDelay` itself holds.
  const renderElements: CameraViewerOverlay[] | undefined = overlayUnavailable ? undefined : heldElements;

  // Spec 040: the overlay-draw leg (ADR-0015, ≤ 50 ms — a whole leg). Timed
  // from the overlay's rendered state changing to the browser having painted
  // it. Observation only: nothing here alters what is drawn or when.
  //
  // Keyed on a stable scalar derived from the element set and the highlight,
  // never on the array itself (spec 150) — an array dependency is a new
  // reference on every render and floods the instrument with no-op samples,
  // the exact defect #1888/#1889 fixed and ADR-0123 forbids reintroducing.
  // Must re-measure on a change to any painted field, not text alone, and
  // not re-measure on an equal re-render (#1888/#1889, #2714) — FR-018
  // (spec 300) covers kind/colour/text; geometry and fontSizePx are this
  // key's own extension of that requirement, not something FR-018 states.
  // Shares `paintedElementsKey` with `liveElementsKey` above so the two keys
  // cannot drift apart again, which is exactly how #2714 happened. When
  // `renderElements` is the same reference as `stableLiveElements` (the
  // common case: not mid-hold, overlay available), its key is already
  // `liveElementsKey` by construction — reuse it instead of re-walking the
  // same elements a second time.
  const renderElementsKey =
    renderElements === stableLiveElements ? liveElementsKey : paintedElementsKey(renderElements);
  useEffect(() => {
    if (renderElementsKey === undefined) {
      return;
    }
    measureOverlayDraw(tile.cameraIdentifier, getToken);
  }, [renderElementsKey, highlighted, tile.cameraIdentifier, getToken]);

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
