import { useGetLayoutQuery } from '@smart-sentinel-eye/shared/api/layouts.api';
import type { LayoutTile } from '@smart-sentinel-eye/shared/api/layouts.api';
import { useGetOverlayQuery } from '@smart-sentinel-eye/shared/api/overlays.api';
import { useGetOverlaySnapshotQuery } from '@smart-sentinel-eye/shared/api/systemVariables.api';
import { CameraViewer } from '@smart-sentinel-eye/shared/ui/composites/CameraViewer';
import { measureOverlayDraw, reportKioskLatency } from '@smart-sentinel-eye/shared/observability/kioskLatency';
import { logResilienceEvent } from '@smart-sentinel-eye/shared/observability/resilienceLog';
import clsx from 'clsx';
import { useCallback, useEffect, useRef, type ReactNode } from 'react';
import { useAuth } from 'react-oidc-context';
import { useNavigate } from 'react-router-dom';
import { LiveUpdatesBadge } from '../revocation/LiveUpdatesBadge.js';
import { useLayoutLifecycle } from '../revocation/useLayoutLifecycle.js';
import { TileAlignmentBadge } from './TileAlignmentBadge.js';
import { useLabelDelay } from './useLabelDelay.js';
import { useOverlayHubHandlers } from './useOverlayHubHandlers.js';
import { useWallAlignment } from './useWallAlignment.js';
import { boundOverlayIn, namedFab } from './wallBindings.js';

export interface LayoutGridProps {
  /** The Layout chain to render — a route param for `CellPage`, or a wall's current `Showing` for `WallPage` (spec 258 plan.md §6.3). */
  layoutIdentifier: string;
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
 * (`gridRows × gridCols`); each populated cell is a `<CameraViewer>` (spec
 * 002 composite, unchanged) owning its per-tile overlay fetch + resolved-text
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
export function LayoutGrid({ layoutIdentifier }: LayoutGridProps) {
  const navigate = useNavigate();
  const auth = useAuth();

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
        navigate('/', { replace: true });
      }
    },
    ...overlayHub.handlers,
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
        {cells.map((cell) => {
          if (cell.tile === null) {
            return <EmptyCell key={cell.key} />;
          }
          const boundOverlay = boundOverlayIn(cell.tile.overlayIdentifier);
          return (
            <Tile
              key={cell.key}
              tile={cell.tile}
              fab={data.fab}
              getToken={getToken}
              unavailable={boundOverlay !== null && overlayHub.unavailableOverlays.has(boundOverlay)}
              highlighted={boundOverlay !== null && overlayHub.highlightedOverlays.has(boundOverlay)}
              playoutTargetMilliseconds={alignment.targetFor(cell.key)}
              frameAgeFor={alignment.frameAgeFor}
              tileKey={cell.key}
              onLagMeasured={(camera, lag, buffer) => alignment.reportLag(cell.key, camera, lag, buffer)}
              outOfAlignment={alignment.released.has(cell.key)}
              onLabelVerdict={overlayHub.onLabelVerdict}
            />
          );
        })}
      </div>
      <LiveUpdatesBadge degraded={degraded} />
    </main>
  );
}

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
}: TileProps) {
  // Read at the moment this tile renders, not handed down as a value computed
  // during the parent's last render (spec 204) — the parent has no reason to
  // re-render on its own cadence, so a stale prop would silently freeze the
  // label's held age. `Tile` is kept current by its own RTK Query
  // subscriptions below.
  const frameAgeMilliseconds = frameAgeFor(tileKey);
  // Spec 141 site 1 (FR-001): an overlay identifier this tile actually binds —
  // `null` for an omitted, blank, or genuinely absent field alike, matching
  // #2084's own sentinel (`:519`) exactly. Widened past `LayoutTile`'s
  // declared `string | null` (to admit `undefined`) because an omitted field
  // is `undefined` on the wire, never `null` — `LayoutTile` itself stays
  // untouched (FR-001).
  const overlayIdentifier = boundOverlayIn(tile.overlayIdentifier);
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
  // Spec 141 site 3 (FR-005, plan R5): whether the tile actually *knows* the
  // text — while the overlay query is still loading, or when `text` is
  // absent (the out-of-scope omitted/renamed case), `hasPlaceholder` above is
  // `false` for the same reason a genuinely static label is, and the two must
  // not be conflated. A verdict is registered only when this is true.
  const labelTextKnown = publishedOverlay !== undefined && typeof publishedOverlay.text === 'string';
  useEffect(() => {
    if (overlayIdentifier === null || !labelTextKnown) return;
    onLabelVerdict(overlayIdentifier, hasPlaceholder);
  }, [overlayIdentifier, labelTextKnown, hasPlaceholder, onLabelVerdict]);
  // Naming the fab is what makes the opening label resolve in the plant this
  // wall shows rather than in whichever of the caller's fabs sorts first
  // (ADR-0145 §2). Same object shape as the push's `upsertQueryData` above —
  // it is the cache key they share.
  //
  // Skipped on an unnamed fab rather than sent as one: an empty or absent
  // `fabId` on the query string is not a narrower request, it is the
  // cross-fab request this exists to avoid (spec 141 site 2, FR-004). No
  // fab, no query.
  const { data: snapshot } = useGetOverlaySnapshotQuery(
    { overlayIdentifier: overlayIdentifier ?? '', fabId: fab },
    { skip: overlayIdentifier === null || !hasPlaceholder || namedFab(fab) === null },
  );

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

function positionKey(row: number, col: number): string {
  return `${row}:${col}`;
}

function FullScreen({ message, action }: { message: string; action?: ReactNode }) {
  return (
    <main className="flex min-h-screen flex-col items-center justify-center gap-4 bg-bg-base p-8 text-center">
      <p className="text-lg">{message}</p>
      {action}
    </main>
  );
}
