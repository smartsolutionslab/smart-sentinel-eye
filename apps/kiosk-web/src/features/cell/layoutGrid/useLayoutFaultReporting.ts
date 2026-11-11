import type { LayoutRevision } from '@smart-sentinel-eye/shared/api/layouts.api';
import { logResilienceEvent } from '@smart-sentinel-eye/shared/observability/resilienceLog';
import { useEffect, useRef } from 'react';
import { boundOverlayIn, namedFab } from '../wallBindings';

interface LayoutFaultReportingInput {
  layoutIdentifier: string;
  published: LayoutRevision | undefined;
  wallFab: string | undefined;
}

export function useLayoutFaultReporting({ layoutIdentifier, published, wallFab }: LayoutFaultReportingInput) {
  // Spec 141: one latch set keyed by transition, so FR-002 and FR-004 each
  // fire at most once per mounted page per session (FR-007) without needing
  // two separate booleans.
  const reportedLayoutFaultsRef = useRef<Set<string>>(new Set());

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
}
