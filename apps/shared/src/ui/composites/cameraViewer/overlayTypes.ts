/** Shared geometry every overlay element carries, normalized to [0,1] so it scales with the viewer regardless of viewport size (spec 004 FR-005 / FR-013). */
interface CameraViewerOverlayGeometry {
  normalizedX: number;
  normalizedY: number;
  normalizedWidth: number;
  normalizedHeight: number;
}

/** A text label drawn over the live video. */
export interface CameraViewerOverlayLabel extends CameraViewerOverlayGeometry {
  kind: 'Text';
  /** `#RRGGBBAA` (spec 300, #2349, ADR-0165). */
  color: string;
  text: string;
  fontSizePx: number;
}

/** A Box or Ellipse stroke drawn over the live video (spec 300, #2349, ADR-0165). No text layout, no shadow, no animation — costs no more than the label it can replace. */
export interface CameraViewerOverlayShape extends CameraViewerOverlayGeometry {
  kind: 'Box' | 'Ellipse';
  /** `#RRGGBBAA`, any alpha — strokes carry no readability floor (ADR-0165 §2). */
  color: string;
}

/**
 * One overlay element drawn over the live video — a text label, a box, or
 * an ellipse (spec 300, #2349, ADR-0165 — a union mirroring the API, one
 * renderer switch per spec 150 FR-012).
 */
export type CameraViewerOverlay = CameraViewerOverlayLabel | CameraViewerOverlayShape;
