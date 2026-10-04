import { useGetLayoutQuery } from '@smart-sentinel-eye/shared/api/layouts.api';
import type { LayoutTile } from '@smart-sentinel-eye/shared/api/layouts.api';
import { useGetOverlayQuery } from '@smart-sentinel-eye/shared/api/overlays.api';
import { useGetOverlaySnapshotQuery } from '@smart-sentinel-eye/shared/api/systemVariables.api';
import { Badge } from '@smart-sentinel-eye/shared/ui/composites/Badge';
import { CameraViewer } from '@smart-sentinel-eye/shared/ui/composites/CameraViewer';
import { measureOverlayDraw, reportKioskLatency } from '@smart-sentinel-eye/shared/observability/kioskLatency';
import { logResilienceEvent } from '@smart-sentinel-eye/shared/observability/resilienceLog';
import clsx from 'clsx';
import { useCallback, useEffect, useRef, useState, type CSSProperties, type ReactNode } from 'react';
import { useAuth } from 'react-oidc-context';
import { useNavigate } from 'react-router-dom';
import { LiveUpdatesBadge } from '../revocation/LiveUpdatesBadge.js';
import { useLayoutLifecycle } from '../revocation/useLayoutLifecycle.js';
import { TileAlignmentBadge } from './TileAlignmentBadge.js';
import { useLabelDelay } from './useLabelDelay.js';
import { useOverlayHubHandlers } from './useOverlayHubHandlers.js';
import { useWallAlignment } from './useWallAlignment.js';
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
            <button
              type="button"
              className="rounded-md bg-accent-active/20 px-4 py-2 text-accent-active"
              onClick={() => navigate('/')}
            >
              Back to picker
            </button>
          ) : undefined
        }
      />
    );
  }

  const items = buildGridItems(published.gridRows, published.gridCols, tiles);

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
              key={item.key}
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
  style,
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
  // Spec 150 (#2345): the published revision's ordered, non-empty label set —
  // undefined only while the overlay query has not yet resolved a Published
  // revision.
  const labels = publishedOverlay?.labels;
  // The SystemVariables snapshot only matters for overlays whose label set
  // embeds a `{{name}}` placeholder in ANY label — a wholly static set has
  // none, so the service holds no resolved snapshot for it and the fetch
  // would 404. Skip it for static sets (avoids the console noise + a
  // pointless round-trip); the resolved-text SignalR push still upserts the
  // cache for overlays that do use variables.
  const hasPlaceholder = labels?.some((label) => label.text.includes('{{')) ?? false;
  // Spec 141 site 3 (FR-005, plan R5) / spec 150: whether the tile actually
  // *knows* every label's text — while the overlay query is still loading, or
  // when a label's `text` is absent (the out-of-scope omitted/renamed case),
  // `hasPlaceholder` above is `false` for the same reason a genuinely static
  // set is, and the two must not be conflated. A verdict is registered only
  // when this is true, and it stays one verdict per overlay (FR-011) — not
  // per label.
  const labelTextKnown = labels !== undefined && labels.every((label) => typeof label.text === 'string');
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

  // Prefer the SystemVariables-resolved text over the raw label, positionally
  // — never a whole-list fallback, so a snapshot shorter than the set (a race
  // across a republish) cannot drop labels (spec 150).
  const liveTexts = labels?.map((label, index) => snapshot?.resolvedTexts[index] ?? label.text);
  // A stable scalar derived from the resolved texts. `\u0000` cannot appear in
  // an operator-authored label, so this is a lossless join for the purpose of
  // detecting "did anything in the set actually change" — the same role
  // `overlayText` played for one label.
  const liveTextsKey = liveTexts?.join('\u0000');
  // The set is resolved and versioned atomically (FR-011), so a shared
  // reference — stable across renders that rebuild an equal array — is the
  // consistent reading: `liveTexts` above is a fresh array every render
  // (`labels.map(...)`), and handing that straight to `useLabelDelay` would
  // make every render look like a change. `useStableByKey` is this file's
  // version of the fix #1888/#1889 already made for `measureOverlayDraw`'s
  // effect below, applied to a value instead of an effect dependency.
  const stableLiveTexts = useStableByKey(liveTexts, liveTextsKey);

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

  // One `useLabelDelay` call per tile for the WHOLE set (FR-014) — hooks
  // cannot be called in a loop, and the set shares one age.
  const resolvedTexts = useLabelDelay(stableLiveTexts, frameAgeMilliseconds, reportHeld);

  const renderLabels =
    !overlayUnavailable && labels !== undefined && resolvedTexts !== undefined
      ? labels.map((label, index) => ({
          text: resolvedTexts[index] ?? label.text,
          normalizedX: label.normalizedX,
          normalizedY: label.normalizedY,
          normalizedWidth: label.normalizedWidth,
          normalizedHeight: label.normalizedHeight,
          fontSizePx: label.fontSizePx,
        }))
      : undefined;

  // Spec 040: the overlay-draw leg (ADR-0015, ≤ 50 ms — a whole leg). Timed
  // from the overlay's rendered state changing to the browser having painted
  // it. Observation only: nothing here alters what is drawn or when.
  //
  // Keyed on a stable scalar derived from the label set and the highlight,
  // never on the array itself (spec 150) — an array dependency is a new
  // reference on every render and floods the instrument with no-op samples,
  // the exact defect #1888/#1889 fixed and ADR-0123 forbids reintroducing.
  const renderLabelsKey = renderLabels?.map((label) => label.text).join('\u0000');
  useEffect(() => {
    if (renderLabelsKey === undefined) {
      return;
    }
    measureOverlayDraw(tile.cameraIdentifier, getToken);
  }, [renderLabelsKey, highlighted, tile.cameraIdentifier, getToken]);

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
      <CameraViewer
        cameraIdentifier={tile.cameraIdentifier}
        getToken={getToken}
        overlays={renderLabels}
        playoutTargetMilliseconds={playoutTargetMilliseconds}
        onLagMeasured={onLagMeasured}
      />
    </div>
  );
}

function EmptyCell({ style }: { style: CSSProperties }) {
  return (
    <div
      data-testid="layout-empty-cell"
      className="flex h-full w-full items-center justify-center rounded-md border border-dashed border-fg-muted/30 bg-bg-elevated/20 text-sm text-fg-muted"
      style={style}
    >
      Empty
    </div>
  );
}

/** Explicit CSS grid placement for a `GridItem`'s span (spec 262 FR-008). */
function gridPlacementStyle(row: number, col: number, rowSpan: number, colSpan: number): CSSProperties {
  return {
    gridRow: `${row + 1} / span ${rowSpan}`,
    gridColumn: `${col + 1} / span ${colSpan}`,
  };
}

function FullScreen({
  message,
  action,
  header,
}: {
  message: string;
  action?: ReactNode;
  /** A `WallHeader` to keep visible through this placeholder (see `wallHeader` above). */
  header?: ReactNode;
}) {
  return (
    <main className="relative flex min-h-screen flex-col items-center justify-center gap-4 bg-bg-base p-8 text-center">
      {header}
      <p className="text-lg">{message}</p>
      {action}
    </main>
  );
}

/** The persistent title bar; `onBack` renders the "Back" button (CellPage only — a wall page offers no way to leave, PD-6). */
function WallHeader({ title, onBack }: { title: string; onBack?: () => void }) {
  return (
    <header className="absolute left-0 right-0 top-0 z-10 flex items-center justify-between bg-black/50 px-6 py-3 text-fg-primary">
      <h1 className="text-lg font-medium">{title}</h1>
      {onBack !== undefined && (
        <button type="button" className="rounded-md bg-bg-elevated/60 px-3 py-1 text-sm" onClick={onBack}>
          Back
        </button>
      )}
    </header>
  );
}
