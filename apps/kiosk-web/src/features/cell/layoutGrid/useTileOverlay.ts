import type { LayoutTile } from '@smart-sentinel-eye/shared/api/layouts.api';
import { useGetOverlayQuery } from '@smart-sentinel-eye/shared/api/overlays.api';
import type { OverlayElement, OverlayTextElement } from '@smart-sentinel-eye/shared/api/overlays.api';
import { useEffect } from 'react';
import { boundOverlayIn } from '../wallBindings.js';

/** Spec 300 (#2349), ADR-0165: a type guard, not a bare `=== 'Text'` comparison, so `.filter`/narrowing sees a `OverlayTextElement[]` rather than staying `OverlayElement[]`. */
function isTextElement(element: OverlayElement): element is OverlayTextElement {
  return element.kind === 'Text';
}

interface TileOverlayInput {
  tile: LayoutTile;
  /** The bound overlay went Archived (spec 004 path, applied per tile). */
  unavailable: boolean;
  onLabelVerdict: (overlayIdentifier: string, hasPlaceholder: boolean) => void;
}

export function useTileOverlay({ tile, unavailable, onLabelVerdict }: TileOverlayInput) {
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
  // Spec 150 (#2345): the published revision's ordered, non-empty element set —
  // undefined only while the overlay query has not yet resolved a Published
  // revision. Spec 300 (#2349, ADR-0165): a mix of Text/Box/Ellipse kinds.
  const elements = publishedOverlay?.elements;
  // Spec 300 (#2349, ADR-0165): a shape has no text to know or resolve —
  // both of the below consider Text elements only.
  const textElements = elements?.filter(isTextElement);
  // The SystemVariables snapshot only matters for overlays whose Text
  // elements embed a `{{name}}` placeholder in ANY of them — a wholly
  // static set has none, so the service holds no resolved snapshot for it
  // and the fetch would 404. Skip it for static sets (avoids the console
  // noise + a pointless round-trip); the resolved-text SignalR push still
  // upserts the cache for overlays that do use variables.
  const hasPlaceholder = textElements?.some((label) => label.text?.includes('{{')) ?? false;
  // Spec 141 site 3 (FR-005, plan R5) / spec 150: whether the tile actually
  // *knows* every Text element's text — while the overlay query is still
  // loading, or when a Text element's `text` is absent (the out-of-scope
  // omitted/renamed case), `hasPlaceholder` above is `false` for the same
  // reason a genuinely static set is, and the two must not be conflated. A
  // verdict is registered only when this is true, and it stays one verdict
  // per overlay (FR-011) — not per element. A set with no Text element at
  // all (shapes only) is vacuously "known" (`every` on an empty array).
  const labelTextKnown = textElements !== undefined && textElements.every((label) => typeof label.text === 'string');
  useEffect(() => {
    if (overlayIdentifier === null || !labelTextKnown) return;
    onLabelVerdict(overlayIdentifier, hasPlaceholder);
  }, [overlayIdentifier, labelTextKnown, hasPlaceholder, onLabelVerdict]);

  return { overlayIdentifier, publishedOverlay, overlayUnavailable, elements, textElements, hasPlaceholder };
}
