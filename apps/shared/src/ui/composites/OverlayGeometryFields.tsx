import type { OverlayLabel } from '@smart-sentinel-eye/shared/api/overlays.api';

/**
 * In-flight drag/resize geometry, normalized [0,1] (spec 151 FR-013). `null`
 * while no gesture is in progress, in which case a field falls back to
 * `value`.
 */
export interface OverlayGeometry {
  x: number;
  y: number;
  width: number;
  height: number;
}

/** The one normalized field a single commit changes (spec 151 FR-006). */
export type OverlayGeometryField = 'normalizedX' | 'normalizedY' | 'normalizedWidth' | 'normalizedHeight';

export interface OverlayGeometryFieldsProps {
  /** The committed truth. */
  value: OverlayLabel;
  /** In-flight drag/resize geometry (FR-013), or `null` when nothing is dragging. */
  preview: OverlayGeometry | null;
  /** Fires once per successful commit, naming exactly the one field that changed (FR-006). */
  onCommit: (field: OverlayGeometryField, normalized: number) => void;
}

/**
 * Four percent-denominated fields — Left, Top, Width, Height — for an overlay
 * label's position and size (spec 151, issue #2346). Per plan.md §2:
 * `type="text"` + `inputMode="decimal"` (FR-003 — a `type="number"` input
 * blocks `OverlayEditorDialog`'s submit before `onSubmit` runs, which no
 * jsdom test can see); a draft-string commit/revert protocol on blur / Enter
 * / Escape (FR-004); validation against the normalized bound with a
 * percent-phrased message (FR-007–FR-011); a non-blocking `role="status"`
 * advisory when the committed rectangle runs off the canvas (FR-012); a
 * `<label htmlFor>` per field (FR-017).
 *
 * <p><b>Phase 4a scaffold (spec 151, T001).</b> Renders nothing at all, so
 * every `OverlayGeometryFields.test.tsx` query for a labelled field is
 * observed red on a genuine missing control — never on a missing export or a
 * type error, per ADR-0139/ADR-0144. T004 fills in FR-001 through FR-012 and
 * FR-017.</p>
 */
export function OverlayGeometryFields(props: OverlayGeometryFieldsProps): null {
  void props;
  return null;
}
