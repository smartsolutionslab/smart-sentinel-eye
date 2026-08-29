import { useGetLayoutQuery } from '@smart-sentinel-eye/shared/api/layouts.api';
import type { LayoutTile } from '@smart-sentinel-eye/shared/api/layouts.api';
import { overlaysApi, useGetOverlayQuery } from '@smart-sentinel-eye/shared/api/overlays.api';
import { systemVariablesApi, useGetOverlaySnapshotQuery } from '@smart-sentinel-eye/shared/api/systemVariables.api';
import { CameraViewer } from '@smart-sentinel-eye/shared/ui/composites/CameraViewer';
import { measureOverlayDraw, reportKioskLatency } from '@smart-sentinel-eye/shared/observability/kioskLatency';
import clsx from 'clsx';
import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import { useDispatch } from 'react-redux';
import type { AppDispatch } from '../../app/store.js';
import { useAuth } from 'react-oidc-context';
import { useNavigate, useParams } from 'react-router-dom';
import { LiveUpdatesBadge } from '../revocation/LiveUpdatesBadge.js';
import { useLayoutLifecycle } from '../revocation/useLayoutLifecycle.js';
import { TileAlignmentBadge } from './TileAlignmentBadge.js';
import { useLabelDelay } from './useLabelDelay.js';
import { useWallAlignment } from './useWallAlignment.js';

/**
 * Kiosk wall view (spec 010 US2 + US3). Renders the Published revision's
 * tile set as a CSS grid (`gridRows × gridCols`); each populated cell is a
 * `<CameraViewer>` (spec 002 composite, unchanged) owning its per-tile
 * overlay fetch + resolved-text snapshot. Empty cells render a placeholder.
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
export function CellPage() {
  const { layoutIdentifier = '' } = useParams<{ layoutIdentifier: string }>();
  const navigate = useNavigate();
  const auth = useAuth();
  const dispatch = useDispatch<AppDispatch>();

  // Stable identity, holding the newest token behind a ref. Every tile puts this
  // into effect dependency arrays, so a fresh function each render rebuilds those
  // effects: the overlay-draw measurement then times renders that changed nothing,
  // and the decode sampler's interval is cleared before it can take a second
  // sample (issues 1888, 1889). `useWhepSession` already guards its own use of
  // this prop the same way, and says why.
  const accessTokenRef = useRef(auth.user?.access_token);
  // Deliberate, for the reason above: keying the effects on the token value
  // restarts them on every silent renew, which is precisely what broke the
  // overlay-draw and decode-leg measurements (issues 1888, 1889).
  // eslint-disable-next-line react-hooks/refs -- see above
  accessTokenRef.current = auth.user?.access_token;
  const getToken = useCallback(() => Promise.resolve(accessTokenRef.current ?? null), []);
  const { data, isLoading, error, refetch } = useGetLayoutQuery(layoutIdentifier, {
    skip: layoutIdentifier === '',
  });

  const published = data?.revisions.find((revision) => revision.state === 'Published');
  const tiles = published?.tiles ?? [];

  // Spec 045: the wall's playout control loop. Only this page sees every tile,
  // which is why the decision lives here and the actuation lives in the tile.
  // Below two tiles it does nothing and sets nothing (FR-004).
  const alignment = useWallAlignment(tiles.length, getToken);

  // Cross-tile event state. The hub is a single subscription on this page;
  // overlay-scoped events are routed by `overlayIdentifier` to whichever
  // tile(s) bind that overlay (a tile derives its own flag from the set).
  const [unavailableOverlays, setUnavailableOverlays] = useState<ReadonlySet<string>>(() => new Set());
  const [highlightedOverlays, setHighlightedOverlays] = useState<ReadonlySet<string>>(() => new Set());
  // Monotonic per-overlay version guard for resolved-text pushes — drops
  // out-of-order frames (mirrors the pre-feature single-cell ref).
  const overlayTextVersionsRef = useRef<Map<string, number>>(new Map());
  // Per-overlay highlight expiry so overlapping highlights on the same
  // overlay survive until the LATER expiry (OR'd, FR-012 / US3 sc.3).
  // performance.now(), never Date.now(): fab clocks are PTP-stepped and an
  // epoch comparison can pin a highlight on forever or clear it early
  // (spec 011 edge case).
  const highlightExpiryRef = useRef<Map<string, number>>(new Map());
  const highlightTimersRef = useRef<Set<number>>(new Set());

  useEffect(() => {
    const timers = highlightTimersRef.current;
    return () => {
      for (const timer of timers) {
        window.clearTimeout(timer);
      }
      timers.clear();
    };
  }, []);

  // The set of overlays actually bound to a rendered tile — used so a
  // highlight (or any overlay event) for an unbound overlay is a no-op.
  const boundOverlays = tilesToBoundOverlays(tiles);

  const startHighlight = (overlay: string, durationMs: number) => {
    if (!boundOverlays.has(overlay)) {
      // No rendered tile binds this overlay — nothing to light (US3 sc.4).
      return;
    }
    // startHighlight is defined during render but never called during it: its
    // only caller is the onOverlayHighlightChanged hub callback below. The rule
    // cannot see that, so it reads performance.now() as render-phase work.
    // eslint-disable-next-line react-hooks/purity -- see above
    const expireAt = performance.now() + durationMs;
    const expiries = highlightExpiryRef.current;
    expiries.set(overlay, Math.max(expiries.get(overlay) ?? 0, expireAt));
    setHighlightedOverlays((current) => withAdded(current, overlay));

    const timer = window.setTimeout(() => {
      highlightTimersRef.current.delete(timer);
      // Only revert once the LATEST expiry has passed; a later overlapping
      // highlight pushes the expiry out and keeps the tile lit (US3 sc.3).
      if (performance.now() >= (highlightExpiryRef.current.get(overlay) ?? 0)) {
        highlightExpiryRef.current.delete(overlay);
        setHighlightedOverlays((current) => withRemoved(current, overlay));
      }
    }, durationMs);
    highlightTimersRef.current.add(timer);
  };

  const { degraded } = useLayoutLifecycle({
    accessTokenFactory: () => auth.user?.access_token ?? '',
    enabled: auth.isAuthenticated,
    onArchived: (message) => {
      if (message.layout === layoutIdentifier) {
        navigate('/', { replace: true });
      }
    },
    onOverlayPublished: (message) => {
      if (!boundOverlays.has(message.overlay)) return;
      dispatch(overlaysApi.util.invalidateTags([{ type: 'Overlay', id: message.overlay }]));
      dispatch(systemVariablesApi.util.invalidateTags([{ type: 'OverlaySnapshot', id: message.overlay }]));
      setUnavailableOverlays((current) => withRemoved(current, message.overlay));
    },
    onOverlayArchived: (message) => {
      if (!boundOverlays.has(message.overlay)) return;
      setUnavailableOverlays((current) => withAdded(current, message.overlay));
    },
    onResolvedOverlayTextChanged: (message) => {
      if (!boundOverlays.has(message.overlay)) return;
      const versions = overlayTextVersionsRef.current;
      if (message.version <= (versions.get(message.overlay) ?? 0)) return;
      versions.set(message.overlay, message.version);
      // Patch the snapshot cache in place so the bound tile re-renders
      // without a full re-fetch (spec 005 variable push).
      dispatch(
        systemVariablesApi.util.upsertQueryData('getOverlaySnapshot', message.overlay, {
          overlayIdentifier: message.overlay,
          resolvedText: message.resolvedText,
          version: message.version,
        }),
      );
    },
    onOverlayHighlightChanged: (message) => {
      startHighlight(message.overlay, message.durationMs);
    },
    onReconnected: () => {
      void refetch();
    },
  });

  useEffect(() => {
    if (!isLoading && error === undefined && data !== undefined && published === undefined) {
      navigate('/', { replace: true });
    }
  }, [data, error, isLoading, navigate, published]);

  if (isLoading) {
    return <FullScreen message="Loading camera…" />;
  }
  if (error !== undefined || data === undefined || published === undefined || tiles.length === 0) {
    return (
      <FullScreen
        message="Layout is no longer available."
        action={
          <button
            type="button"
            className="rounded-md bg-accent-active/20 px-4 py-2 text-accent-active"
            onClick={() => navigate('/')}
          >
            Back to picker
          </button>
        }
      />
    );
  }

  const cells = buildGridCells(published.gridRows, published.gridCols, tiles);

  return (
    <main className="relative min-h-screen bg-black">
      <header className="absolute left-0 right-0 top-0 z-10 flex items-center justify-between bg-black/50 px-6 py-3 text-fg-primary">
        <h1 className="text-lg font-medium">{data.name}</h1>
        <button type="button" className="rounded-md bg-bg-elevated/60 px-3 py-1 text-sm" onClick={() => navigate('/')}>
          Back
        </button>
      </header>
      <div
        data-testid="layout-grid"
        className="grid h-screen gap-1 p-1"
        style={{
          gridTemplateColumns: `repeat(${published.gridCols}, minmax(0, 1fr))`,
          gridTemplateRows: `repeat(${published.gridRows}, minmax(0, 1fr))`,
        }}
      >
        {cells.map((cell) =>
          cell.tile === null ? (
            <EmptyCell key={cell.key} />
          ) : (
            <Tile
              key={cell.key}
              tile={cell.tile}
              getToken={getToken}
              unavailable={cell.tile.overlayIdentifier !== null && unavailableOverlays.has(cell.tile.overlayIdentifier)}
              highlighted={cell.tile.overlayIdentifier !== null && highlightedOverlays.has(cell.tile.overlayIdentifier)}
              playoutTargetMilliseconds={alignment.targetFor(cell.key)}
              frameAgeMilliseconds={alignment.frameAgeFor(cell.key)}
              onLagMeasured={(camera, lag, buffer) => alignment.reportLag(cell.key, camera, lag, buffer)}
              outOfAlignment={alignment.released.has(cell.key)}
            />
          ),
        )}
      </div>
      <LiveUpdatesBadge degraded={degraded} />
    </main>
  );
}

interface TileProps {
  tile: LayoutTile;
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
   */
  frameAgeMilliseconds: number | null;
}

/**
 * One populated grid cell. Owns its overlay fetch + resolved-text snapshot
 * so each tile resolves its own label independently (per-tile binding,
 * FR-011). The bound overlay's geometry comes from OverlayDesigner; the live
 * label text comes from the SystemVariables snapshot, falling back to the
 * raw label when the snapshot is unavailable.
 */
function Tile({
  tile,
  getToken,
  unavailable,
  highlighted,
  playoutTargetMilliseconds,
  onLagMeasured,
  outOfAlignment,
  frameAgeMilliseconds,
}: TileProps) {
  const overlayIdentifier = tile.overlayIdentifier;
  const { data: overlay } = useGetOverlayQuery(overlayIdentifier ?? '', {
    skip: overlayIdentifier === null,
  });

  const publishedOverlay = overlay?.revisions.find((r) => r.state === 'Published');
  // FR-009 (spec 011): unavailability is derived state — a pushed
  // OverlayArchived frame OR a fetched overlay with no Published revision
  // (archived before this kiosk ever loaded the layout).
  const overlayUnavailable = unavailable || (overlay !== undefined && publishedOverlay === undefined);
  // The SystemVariables snapshot only matters for overlays whose label embeds
  // `{{name}}` placeholders; a static label has none, so the service holds no
  // resolved snapshot for it and the fetch would 404. Skip it for static labels
  // (avoids the console noise + a pointless round-trip); the resolved-text
  // SignalR push still upserts the cache for overlays that do use variables.
  const hasPlaceholder = publishedOverlay?.text?.includes('{{') ?? false;
  const { data: snapshot } = useGetOverlaySnapshotQuery(overlayIdentifier ?? '', {
    skip: overlayIdentifier === null || !hasPlaceholder,
  });

  // Prefer the SystemVariables-resolved text over the raw label so any
  // `{{name}}` placeholders show their live values; fall back to the raw
  // label if SystemVariables is unreachable.
  const liveText = snapshot?.resolvedText ?? publishedOverlay?.text;

  // Held back so the label describes the same moment as the picture beneath it
  // (ADR-0129). **Not frame accuracy** — it makes the label as old as the
  // picture and pairs nothing with a frame. A tile with no readable age, or one
  // past the cap, gets its label immediately.
  // Reported per tile so one badly-buffered camera is visible, and reported
  // as what was achieved rather than what was asked for (FR-015).
  const reportHeld = useCallback(
    (achievedMilliseconds: number) =>
      reportKioskLatency('label_delay', tile.cameraIdentifier, achievedMilliseconds, getToken),
    [tile.cameraIdentifier, getToken],
  );

  const resolvedText = useLabelDelay(liveText, frameAgeMilliseconds, reportHeld);

  const renderOverlay =
    !overlayUnavailable && publishedOverlay !== undefined && resolvedText !== undefined
      ? {
          text: resolvedText,
          normalizedX: publishedOverlay.normalizedX,
          normalizedY: publishedOverlay.normalizedY,
          normalizedWidth: publishedOverlay.normalizedWidth,
          normalizedHeight: publishedOverlay.normalizedHeight,
          fontSizePx: publishedOverlay.fontSizePx,
        }
      : undefined;

  // Spec 040: the overlay-draw leg (ADR-0015, ≤ 50 ms — a whole leg). Timed
  // from the overlay's rendered state changing to the browser having painted
  // it. Observation only: nothing here alters what is drawn or when.
  //
  // Keyed on the text and the highlight because those are what change on a
  // hub push; re-running on every render would time renders that changed
  // nothing and flatten the distribution with zeros.
  const overlayText = renderOverlay?.text;
  useEffect(() => {
    if (overlayText === undefined) {
      return;
    }
    measureOverlayDraw(tile.cameraIdentifier, getToken);
  }, [overlayText, highlighted, tile.cameraIdentifier, getToken]);

  return (
    <div
      data-testid="layout-tile"
      data-highlighted={highlighted ? 'true' : 'false'}
      className={clsx(
        'relative flex h-full w-full items-center justify-center overflow-hidden rounded-md',
        highlighted && 'ssE-overlay-highlight',
      )}
    >
      {overlayUnavailable && (
        <div
          role="status"
          className="absolute left-1/2 top-2 z-10 -translate-x-1/2 rounded-md bg-accent-warning/30 px-4 py-1 text-sm text-accent-warning"
        >
          Overlay unavailable
        </div>
      )}
      {outOfAlignment && <TileAlignmentBadge camera={tile.cameraIdentifier} />}
      <CameraViewer
        cameraIdentifier={tile.cameraIdentifier}
        getToken={getToken}
        overlay={renderOverlay}
        playoutTargetMilliseconds={playoutTargetMilliseconds}
        onLagMeasured={onLagMeasured}
      />
    </div>
  );
}

function EmptyCell() {
  return (
    <div
      data-testid="layout-empty-cell"
      className="flex h-full w-full items-center justify-center rounded-md border border-dashed border-fg-muted/30 bg-bg-elevated/20 text-sm text-fg-muted"
    >
      Empty
    </div>
  );
}

interface GridCell {
  key: string;
  tile: LayoutTile | null;
}

/**
 * Lay out every grid coordinate in row-major order, slotting each tile at
 * its `(row, col)`. Cells without a tile are `null` (rendered as a
 * placeholder). Out-of-bounds tiles are ignored defensively (the aggregate
 * already enforces in-bounds; this keeps the renderer total).
 */
function buildGridCells(rows: number, cols: number, tiles: LayoutTile[]): GridCell[] {
  const byPosition = new Map<string, LayoutTile>();
  for (const tile of tiles) {
    byPosition.set(positionKey(tile.row, tile.col), tile);
  }
  const cells: GridCell[] = [];
  for (let row = 0; row < rows; row += 1) {
    for (let col = 0; col < cols; col += 1) {
      const key = positionKey(row, col);
      cells.push({ key, tile: byPosition.get(key) ?? null });
    }
  }
  return cells;
}

function tilesToBoundOverlays(tiles: LayoutTile[]): ReadonlySet<string> {
  const bound = new Set<string>();
  for (const tile of tiles) {
    if (tile.overlayIdentifier !== null) {
      bound.add(tile.overlayIdentifier);
    }
  }
  return bound;
}

function positionKey(row: number, col: number): string {
  return `${row}:${col}`;
}

function withAdded(current: ReadonlySet<string>, value: string): ReadonlySet<string> {
  if (current.has(value)) return current;
  const next = new Set(current);
  next.add(value);
  return next;
}

function withRemoved(current: ReadonlySet<string>, value: string): ReadonlySet<string> {
  if (!current.has(value)) return current;
  const next = new Set(current);
  next.delete(value);
  return next;
}

function FullScreen({ message, action }: { message: string; action?: ReactNode }) {
  return (
    <main className="flex min-h-screen flex-col items-center justify-center gap-4 bg-bg-base p-8 text-center">
      <p className="text-lg">{message}</p>
      {action}
    </main>
  );
}
