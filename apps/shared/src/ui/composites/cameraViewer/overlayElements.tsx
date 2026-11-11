import { overlayLabelSurfaceStyle, overlayShapeStyle } from '../overlayLabelStyle';
import type { CameraViewerOverlayLabel, CameraViewerOverlayShape } from './overlayTypes';

export function OverlayLabel({ overlay }: { overlay: CameraViewerOverlayLabel }) {
  return (
    <span
      data-testid="camera-viewer-overlay-label"
      style={{
        ...overlayLabelSurfaceStyle(overlay),
        position: 'absolute',
        left: `${overlay.normalizedX * 100}%`,
        top: `${overlay.normalizedY * 100}%`,
        width: `${overlay.normalizedWidth * 100}%`,
        height: `${overlay.normalizedHeight * 100}%`,
        pointerEvents: 'none',
      }}
    >
      {overlay.text}
    </span>
  );
}

/** A Box or Ellipse stroke, `aria-hidden` (it carries no information a screen reader can read) and never intercepting pointer events (spec 300, #2349, ADR-0165). */
export function OverlayShape({ overlay }: { overlay: CameraViewerOverlayShape }) {
  return (
    <span
      data-testid="camera-viewer-overlay-shape"
      data-kind={overlay.kind}
      aria-hidden="true"
      style={{
        ...overlayShapeStyle(overlay),
        position: 'absolute',
        left: `${overlay.normalizedX * 100}%`,
        top: `${overlay.normalizedY * 100}%`,
        width: `${overlay.normalizedWidth * 100}%`,
        height: `${overlay.normalizedHeight * 100}%`,
        pointerEvents: 'none',
      }}
    />
  );
}
