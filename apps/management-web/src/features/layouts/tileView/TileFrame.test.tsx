import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import type { CameraViewerOverlay } from '@smart-sentinel-eye/shared/ui/composites/CameraViewer';
import { TileFrame } from './TileFrame';

const LABEL: CameraViewerOverlay = {
  kind: 'Text',
  color: '#FFFFFFFF',
  text: 'Line 3 – 42 pcs/min',
  normalizedX: 0.1,
  normalizedY: 0.2,
  normalizedWidth: 0.5,
  normalizedHeight: 0.1,
  fontSizePx: 32,
};

const BOX: CameraViewerOverlay = {
  kind: 'Box',
  color: '#FF0000FF',
  normalizedX: 0.6,
  normalizedY: 0.5,
  normalizedWidth: 0.3,
  normalizedHeight: 0.4,
};

describe('TileFrame', () => {
  it('is exactly the cell size it is given', () => {
    render(<TileFrame widthPx={635} heightPx={355} backdropUrl={null} elements={[]} />);
    const frame = screen.getByTestId('tile-frame');
    expect(frame).toHaveStyle({ width: '635px', height: '355px' });
  });

  it('keeps the 16:9 viewer box as wide as the cell, as the wall does', () => {
    render(<TileFrame widthPx={635} heightPx={195} backdropUrl={null} elements={[]} />);
    const viewer = screen.getByTestId('tile-frame-viewer');
    expect(viewer).toHaveClass('aspect-video', 'w-full');
    // Clipped, not shrunk: the cell hides what a flat cell cannot show.
    expect(screen.getByTestId('tile-frame')).toHaveClass('overflow-hidden');
  });

  it('puts every element at its normalised position and size', () => {
    render(<TileFrame widthPx={640} heightPx={360} backdropUrl={null} elements={[LABEL, BOX]} />);
    const label = screen.getByTestId('camera-viewer-overlay-label');
    expect(label).toHaveStyle({ left: '10%', top: '20%', width: '50%', height: '10%' });
    const box = screen.getByTestId('camera-viewer-overlay-shape');
    expect(box).toHaveStyle({ left: '60%', top: '50%', width: '30%', height: '40%' });
    expect(box).toHaveAttribute('data-kind', 'Box');
  });

  it('shows the text it is given, resolved or not, without interpreting it', () => {
    render(<TileFrame widthPx={640} heightPx={360} backdropUrl={null} elements={[LABEL]} />);
    expect(screen.getByTestId('camera-viewer-overlay-label')).toHaveTextContent('Line 3 – 42 pcs/min');
  });

  it('shows the captured picture when it has one', () => {
    render(<TileFrame widthPx={640} heightPx={360} backdropUrl="data:image/png;base64,AAAA" elements={[]} />);
    expect(screen.getByTestId('tile-frame-backdrop')).toHaveAttribute('src', 'data:image/png;base64,AAAA');
    expect(screen.queryByTestId('tile-frame-backdrop-neutral')).not.toBeInTheDocument();
  });

  it('shows a neutral backdrop, not an empty hole, when it has no picture', () => {
    render(<TileFrame widthPx={640} heightPx={360} backdropUrl={null} elements={[LABEL]} />);
    expect(screen.getByTestId('tile-frame-backdrop-neutral')).toBeInTheDocument();
    expect(screen.queryByTestId('tile-frame-backdrop')).not.toBeInTheDocument();
    // The overlay is still there.
    expect(screen.getByTestId('camera-viewer-overlay-label')).toBeInTheDocument();
  });

  it('draws nothing over the picture when there are no elements', () => {
    render(<TileFrame widthPx={640} heightPx={360} backdropUrl="data:image/png;base64,AAAA" elements={undefined} />);
    expect(screen.queryByTestId('camera-viewer-overlay-label')).not.toBeInTheDocument();
    expect(screen.queryByTestId('camera-viewer-overlay-shape')).not.toBeInTheDocument();
  });
});
