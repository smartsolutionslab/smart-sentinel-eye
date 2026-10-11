import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';

const { CameraViewer } = await import('@smart-sentinel-eye/shared/ui/composites/CameraViewer');

function renderViewer(overlay?: {
  kind: 'Text';
  color: string;
  text: string;
  normalizedX: number;
  normalizedY: number;
  normalizedWidth: number;
  normalizedHeight: number;
  fontSizePx: number;
}) {
  // CameraViewer no longer calls useGetStreamQuery itself (ADR-0169, #2846) —
  // it takes the stream as a prop. No stream → no WhepClient is constructed;
  // the overlay branch is independent of the streaming path and is what this
  // test is about.
  return render(
    <CameraViewer
      cameraIdentifier="cam-42"
      getToken={async () => null}
      stream={undefined}
      overlays={overlay === undefined ? undefined : [overlay]}
    />,
  );
}

describe('CameraViewer overlay rendering', () => {
  it('Renders the overlay label when the overlay prop is set', () => {
    renderViewer({
      kind: 'Text',
      color: '#FFFFFFD9',
      text: 'Production Line 1',
      normalizedX: 0.5,
      normalizedY: 0.05,
      normalizedWidth: 0.3,
      normalizedHeight: 0.08,
      fontSizePx: 48,
    });

    const label = screen.getByTestId('camera-viewer-overlay-label');
    expect(label).toBeInTheDocument();
    expect(label).toHaveTextContent('Production Line 1');
    expect(label.style.left).toBe('50%');
    expect(label.style.top).toBe('5%');
  });

  it('Renders no overlay label when the overlay prop is absent', () => {
    renderViewer();
    expect(screen.queryByTestId('camera-viewer-overlay-label')).toBeNull();
  });
});
