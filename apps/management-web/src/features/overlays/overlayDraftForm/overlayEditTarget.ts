import type { OverlayElement } from '@smart-sentinel-eye/shared/api/overlays.api';

/**
 * Spec 152. Carries what the caller already knows about the draft being
 * edited, so the form needs no lookup to render its first frame — the
 * `elements` array lifted off the target `OverlayRevision`, not the whole
 * revision (a spread would carry `state`/`createdAt`/etc. into the form
 * value and then into the PATCH body).
 *
 * <p>
 * Spec 150 (#2345) FR-018: `elements` is the WHOLE set, not just the one this
 * form's editor shows. Spec 300 (#2349) FR-019: the shared `OverlayEditor`
 * still edits exactly one Text element (FR-016) — found by kind, never by a
 * fixed index — but this must hold and submit every element the draft
 * carries, or the rest are silently deleted on the first console edit of a
 * mixed-kind overlay.
 * </p>
 */
export interface OverlayEditTarget {
  overlayIdentifier: string;
  revisionNumber: number;
  name: string;
  elements: OverlayElement[];
}
