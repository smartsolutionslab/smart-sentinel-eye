import type { OverlayElement, OverlayRevision, OverlayTextElement } from '@smart-sentinel-eye/shared/api/overlays.api';
import { DEFAULT_OVERLAY_COLOR } from '@smart-sentinel-eye/shared/api/overlays.schema';
import { useGetOverlaySnapshotQuery } from '@smart-sentinel-eye/shared/api/systemVariables.api';
import type { CameraViewerOverlay } from '@smart-sentinel-eye/shared/ui/composites/CameraViewer';
import { useTemplateMissRefetch } from '../useTemplateMissRefetch.js';
import { namedFab } from '../wallBindings.js';

interface TileLiveElementsInput {
  overlayIdentifier: string | null;
  publishedOverlay: OverlayRevision | undefined;
  elements: OverlayElement[] | undefined;
  textElements: OverlayTextElement[] | undefined;
  hasPlaceholder: boolean;
  /** The wall's fab, so this tile's label resolves in that plant (ADR-0145). */
  fab: string;
}

export function useTileLiveElements({
  overlayIdentifier,
  publishedOverlay,
  elements,
  textElements,
  hasPlaceholder,
  fab,
}: TileLiveElementsInput) {
  // Naming the fab is what makes the opening label resolve in the plant this
  // wall shows rather than in whichever of the caller's fabs sorts first
  // (ADR-0145 §2). Same object shape as the push's `upsertQueryData` above —
  // it is the cache key they share.
  //
  // Skipped on an unnamed fab rather than sent as one: an empty or absent
  // `fabId` on the query string is not a narrower request, it is the
  // cross-fab request this exists to avoid (spec 141 site 2, FR-004). No
  // fab, no query.
  const snapshotSkipped = overlayIdentifier === null || !hasPlaceholder || namedFab(fab) === null;
  const { data: snapshot, refetch: refetchSnapshot } = useGetOverlaySnapshotQuery(
    { overlayIdentifier: overlayIdentifier ?? '', fabId: fab },
    { skip: snapshotSkipped },
  );

  // Spec 301 (#2720, US2): one raw template → resolved lookup, built once
  // per snapshot rather than per element. `Map.get` on a key that is not a
  // string (the wire-drift case, `element.text` absent/renamed) simply
  // misses rather than throwing, so this stays safe even when a Text
  // element's own `text` isn't the `string` its type claims.
  // Review S2 (#2720): `snapshot?.texts` rather than `snapshot?.texts!` —
  // a not-yet-upgraded backend on the old wire shape can send a snapshot
  // with no `texts` field at all during a rolling deploy, and `.map` on
  // `undefined` would crash the wall into `KioskCrashRecovery`'s reload,
  // which just re-fetches the same skewed snapshot (a crash loop). Treating
  // a missing `texts` as empty degrades to a template miss instead.
  const textsByTemplate = new Map(snapshot?.texts?.map((entry) => [entry.template, entry.resolved]));

  // Spec 301 (#2348, US1): geometry and text are paired into ONE unit before
  // the hold, rather than held separately (text only) and re-joined with
  // CURRENT geometry by array index afterwards — a reorder, or a Box/Text
  // swap, during the hold previously re-joined a held text with a DIFFERENT
  // element's current geometry (or painted "" for a Text element that used
  // to be a Box), because `elements` was read fresh every render while the
  // held text array stayed behind. Pairing happens here, before either half
  // ever reaches `useLabelDelay`.
  //
  // Spec 301 (#2720, US2): the text half of that pairing is no longer
  // positional either. Each Text element is looked up by its OWN raw
  // template rather than by its index into `snapshot.texts` — a stale or
  // not-yet-indexed snapshot can no longer hand one element another
  // element's resolved value, because there is no shared position for them
  // to collide on. A template with no entry falls back to itself (FR-007),
  // never to a neighbour's value; a shape contributes no entry and needs
  // none (FR-005).
  const liveElements: CameraViewerOverlay[] | undefined = elements?.map((element) => {
    return element.kind === 'Text'
      ? {
          kind: 'Text',
          color: element.color ?? DEFAULT_OVERLAY_COLOR,
          text: textsByTemplate.get(element.text) ?? element.text,
          normalizedX: element.normalizedX,
          normalizedY: element.normalizedY,
          normalizedWidth: element.normalizedWidth,
          normalizedHeight: element.normalizedHeight,
          fontSizePx: element.fontSizePx,
        }
      : {
          kind: element.kind,
          color: element.color,
          normalizedX: element.normalizedX,
          normalizedY: element.normalizedY,
          normalizedWidth: element.normalizedWidth,
          normalizedHeight: element.normalizedHeight,
        };
  });

  // Spec 301 (#2720, US2, FR-007): a placeholder Text element whose own
  // template has no entry in the current snapshot — a genuinely new
  // template SystemVariables has not indexed yet, never a stale index
  // collision (that case cannot arise any more; see `textsByTemplate`
  // above). Only live once a snapshot has actually arrived — while it is
  // still loading, "no entry" is simply "not yet", not a miss to retry.
  const hasTemplateMiss =
    snapshot !== undefined &&
    (textElements?.some(
      (label) => typeof label.text === 'string' && label.text.includes('{{') && !textsByTemplate.has(label.text),
    ) ??
      false);
  useTemplateMissRefetch({
    overlayIdentifier,
    publicationKey: publishedOverlay?.revisionNumber.toString(),
    hasMiss: hasTemplateMiss,
    skip: snapshotSkipped,
    refetch: refetchSnapshot,
  });

  return { liveElements };
}
