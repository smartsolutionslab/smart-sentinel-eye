import type { CSSProperties } from 'react';
import type { OverlayElement } from '@smart-sentinel-eye/shared/api/overlays.api';
import { overlayLabelSurfaceStyle, overlayShapeStyle } from '../overlayLabelStyle.js';

export function InertElement({
  element,
  canvasWidthPx,
  canvasHeightPx,
}: {
  element: OverlayElement;
  canvasWidthPx: number;
  canvasHeightPx: number;
}) {
  const placement: CSSProperties = {
    position: 'absolute',
    left: element.normalizedX * canvasWidthPx,
    top: element.normalizedY * canvasHeightPx,
    width: element.normalizedWidth * canvasWidthPx,
    height: element.normalizedHeight * canvasHeightPx,
    pointerEvents: 'none',
  };
  const appearance = element.kind === 'Text' ? overlayLabelSurfaceStyle(element) : overlayShapeStyle(element);
  return (
    <div
      aria-hidden="true"
      data-testid="overlay-editor-inert-element"
      data-kind={element.kind}
      style={{ ...appearance, ...placement }}
    >
      {element.kind === 'Text' ? element.text : null}
    </div>
  );
}
