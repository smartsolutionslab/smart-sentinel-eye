import { useCallback, useEffect, useRef, useState } from 'react';
import { useDispatch } from 'react-redux';
import type { LayoutTile } from '@smart-sentinel-eye/shared/api/layouts.api';
import { overlaysApi } from '@smart-sentinel-eye/shared/api/overlays.api';
import { systemVariablesApi } from '@smart-sentinel-eye/shared/api/systemVariables.api';
import { logResilienceEvent } from '@smart-sentinel-eye/shared/observability/resilienceLog';
import type { AppDispatch } from '../../app/store.js';
import type { UseLayoutLifecycleOptions } from '../revocation/useLayoutLifecycle.js';
import { boundOverlayIn, namedFab } from './wallBindings.js';

/**
 * The kiosk wall's overlay-scoped SignalR hub handling (spec 229, extracted
 * from `CellPage`; originally specs 004/005/010/011/141/145).
 *
 * <p>
 * Owns the state the four overlay-scoped hub frames —
 * `OverlayRevisionPublished`, `OverlayRevisionArchived`,
 * `ResolvedOverlayTextChanged`, `OverlayHighlightChanged` — write into, and
 * hands back handler bodies for `CellPage` to spread into
 * `useLayoutLifecycle`. `CellPage` keeps the hub subscription itself (spec
 * 229 plan D3); this hook only reacts to the frames it is handed.
 * </p>
 *
 * <p>
 * <b>The handlers are not memoised.</b> `useLayoutLifecycle` keeps the
 * latest options behind a ref it refreshes every render (its own "Keep the
 * latest options in a ref"), so a fresh closure each render is how it sees
 * the current `boundOverlays` and `wallFab`. Wrapping these in `useCallback`
 * would pin them to the values captured at mount — a behaviour change, not a
 * cleanup.
 * </p>
 */
export interface OverlayHubHandlers {
  /** Spread into `useLayoutLifecycle`. Fresh each render — see above. */
  handlers: Required<
    Pick<
      UseLayoutLifecycleOptions,
      'onOverlayPublished' | 'onOverlayArchived' | 'onResolvedOverlayTextChanged' | 'onOverlayHighlightChanged'
    >
  >;
  /** Overlays a pushed OverlayRevisionArchived has flagged (spec 011 path). */
  unavailableOverlays: ReadonlySet<string>;
  /** Overlays with an active highlight (spec 010 US3, ADR-0112 §5). */
  highlightedOverlays: ReadonlySet<string>;
  /** Stable. Tile → page placeholder verdict (spec 141 site 3). */
  onLabelVerdict: (overlayIdentifier: string, hasPlaceholder: boolean) => void;
}

export function useOverlayHubHandlers(tiles: readonly LayoutTile[], wallFab: string | undefined): OverlayHubHandlers {
  const dispatch = useDispatch<AppDispatch>();

  // Cross-tile event state. The hub is a single subscription on the page;
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
  // How many fab-less frames each route has dropped this session (#2084).
  // Counted per route, never shared: a server that carries `fab` on one frame
  // type and not the other is exactly the skew this reports, and one counter
  // would hide it.
  const textWithoutFabCountRef = useRef(0);
  const highlightWithoutFabCountRef = useRef(0);
  // Spec 141 site 3 (FR-005): what each tile decided about its own label —
  // `true`/`false`, written only once the tile actually knows the text (never
  // on the not-yet path, R5) — and the overlays already reported for
  // disagreeing with a resolved-text push. A `Map`, not a `Set` of static
  // overlays: the push handler must tell "the tile said static" apart from
  // "the tile has not decided", and a `Set` would conflate the two (plan
  // §"Where the state lives").
  const staticLabelOverlaysRef = useRef<Map<string, boolean>>(new Map());
  const reportedStaticPushRef = useRef<Set<string>>(new Set());
  const onLabelVerdict = useCallback((overlay: string, hasPlaceholder: boolean) => {
    staticLabelOverlaysRef.current.set(overlay, hasPlaceholder);
  }, []);

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
    // only caller is the onOverlayHighlightChanged hub callback below.
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

  const handlers: OverlayHubHandlers['handlers'] = {
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
      // Before the version mark, never after (ADR-0145 §1, FR-005). Cheap
      // insurance against a prospective hazard, not a fix for an observed one:
      // `version` is today a single per-overlay counter *shared* across fabs
      // (one overlay on the wire ran 11 dresden, 12-14 munich, 15 dresden, 16
      // munich), so the wall's own frame is never lower-versioned than a
      // foreign frame preceding it, and the failure below cannot currently
      // occur. It becomes live the moment that counter is re-keyed on
      // (fab, overlay) — already recorded as a follow-up in spec 067's Out of
      // scope — because then a foreign frame allowed to move the mark first
      // would suppress the wall's own next, legitimately lower-versioned
      // update for the rest of the session, and a frozen wall looks like a
      // working one.
      //
      // Load-bearing in the suite regardless: `Does not let another fab's
      // frame advance the version mark` constructs that version sequence
      // directly and fails if these two lines are transposed. Unreachable in
      // production and asserted in the tests are both true — do not simplify
      // one away on the strength of the other.
      //
      // Fails closed: a frame carrying no fab is `undefined` and matches
      // nothing.
      //
      // Which is silent, and a silently frozen wall looks like a working one
      // (#2084). So report the one drop that is a fault before taking it:
      // `fab` is declared `string`, and a frame carrying none is a server
      // older than the field, not another plant.
      const textSkewCount = countReportableSkew(textWithoutFabCountRef, message.fab, wallFab);
      if (textSkewCount !== null) {
        logResilienceEvent('hub', 'resolved-text-without-fab', { overlay: message.overlay, count: textSkewCount });
      }
      if (namedFab(wallFab) === null || message.fab !== wallFab) return;
      // Spec 141 site 3 (FR-005): a ResolvedOverlayTextChangedV1 is only ever
      // published for an overlay the server's own reverse index found a
      // placeholder in — so arriving here for an overlay this tile parsed as
      // static (no `{{`) is, by construction, the server and the kiosk
      // disagreeing about the delimiter. Reported once per overlay per
      // session (FR-007); does not change what happens below (FR-006) and
      // sits before the version guard, so a push that loses the version race
      // is still evidence of the disagreement.
      if (
        staticLabelOverlaysRef.current.get(message.overlay) === false &&
        !reportedStaticPushRef.current.has(message.overlay)
      ) {
        reportedStaticPushRef.current.add(message.overlay);
        logResilienceEvent('hub', 'resolved-text-for-static-label', { overlay: message.overlay });
      }
      const versions = overlayTextVersionsRef.current;
      if (message.version <= (versions.get(message.overlay) ?? 0)) return;
      versions.set(message.overlay, message.version);
      // Patch the snapshot cache in place so the bound tile re-renders
      // without a full re-fetch (spec 005 variable push).
      //
      // The first argument is the endpoint's cache key and must be identical
      // to the one `Tile` queries with (`CellPage.tsx`), the fab included:
      // disagree and this writes an entry nothing reads, leaving the tile
      // silently stale instead of wrong (spec 067 plan, Risk 1). `message.fab`
      // is that fab — the guard above has already established it equals
      // `wallFab`, and it is the `string` the key needs where `wallFab` is
      // `string | undefined`.
      dispatch(
        systemVariablesApi.util.upsertQueryData(
          'getOverlaySnapshot',
          { overlayIdentifier: message.overlay, fabId: message.fab },
          {
            overlayIdentifier: message.overlay,
            resolvedText: message.resolvedText,
            version: message.version,
          },
        ),
      );
    },
    onOverlayHighlightChanged: (message) => {
      // A rule that fired in another plant must not light this wall's tiles
      // (ADR-0145 §1).
      //
      // A highlight that never fires leaves nothing stale behind to notice —
      // just a tile that never lights — and this route has no version guard
      // behind it either, so the fab-less frame is reported here too (#2084).
      const highlightSkewCount = countReportableSkew(highlightWithoutFabCountRef, message.fab, wallFab);
      if (highlightSkewCount !== null) {
        logResilienceEvent('hub', 'highlight-without-fab', { overlay: message.overlay, count: highlightSkewCount });
      }
      if (namedFab(wallFab) === null || message.fab !== wallFab) return;
      startHighlight(message.overlay, message.durationMs);
    },
  };

  return { handlers, unavailableOverlays, highlightedOverlays, onLabelVerdict };
}

/**
 * Counts a fab-less frame against `counter` and answers the running count when
 * this one is worth a line, or null (#2084).
 *
 * Keyed on the frame carrying no fab — never on the mismatch. A frame naming
 * another plant is the filter working as designed, and reporting that would
 * announce a permanent skew on every healthy two-fab principal, every day. A
 * frame naming no plant at all violates the `fab: string` the message declares,
 * and means a server older than the field: every update dropped for the whole
 * session while the hub, and so `LiveUpdatesBadge`, stays perfectly healthy.
 *
 * Silent until the layout lands, because `wallFab` is `undefined` in that
 * window and nothing dropped there is yet evidence of anything.
 *
 * Bounded at the first occurrence and every power of ten thereafter: a wall
 * runs for weeks, so a line per frame evicts the first — the diagnostically
 * valuable — occurrence, and a line per session is emitted before anyone is
 * looking.
 */
function countReportableSkew(
  counter: { current: number },
  frameFab: string | undefined,
  wallFab: string | undefined,
): number | null {
  if (wallFab === undefined) return null;
  if (typeof frameFab === 'string' && frameFab !== '') return null;
  counter.current += 1;
  const count = counter.current;
  let decade = 1;
  while (decade < count) decade *= 10;
  return decade === count ? count : null;
}

function tilesToBoundOverlays(tiles: readonly LayoutTile[]): ReadonlySet<string> {
  const bound = new Set<string>();
  for (const tile of tiles) {
    const overlay = boundOverlayIn(tile.overlayIdentifier);
    if (overlay !== null) {
      bound.add(overlay);
    }
  }
  return bound;
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
