// @vitest-environment jsdom
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';

const useGetStreamQueryMock = vi.fn();

vi.mock('@smart-sentinel-eye/shared/api/streams.api', () => ({
  useGetStreamQuery: (...args: unknown[]) => useGetStreamQueryMock(...args),
}));

const { CameraViewer } = await import('@smart-sentinel-eye/shared/ui/composites/CameraViewer');

/**
 * Spec 150 (issue #2345), T001. The gap the existing guards leave: none of
 * them counts how many `camera-viewer-overlay-label` nodes render, or where
 * in the tree they sit. FR-013's only guard. Captured green against today's
 * single-`overlay?` shape; must still pass, with only its prop-construction
 * lines edited, once T019 moves to `overlays?: readonly CameraViewerOverlay[]`
 * (an edited assertion here is a block, per tasks.md T019).
 */
describe('CameraViewer overlay label node count (characterisation — the gap the other guards leave)', () => {
  afterEach(() => {
    cleanup();
  });

  function renderViewer(overlay?: {
    text: string;
    normalizedX: number;
    normalizedY: number;
    normalizedWidth: number;
    normalizedHeight: number;
    fontSizePx: number;
  }) {
    useGetStreamQueryMock.mockReturnValue({ data: undefined, isLoading: false, error: undefined });

    const { container } = render(
      <CameraViewer cameraIdentifier="cam-42" getToken={async () => null} overlay={overlay} />,
    );

    return container;
  }

  it('Renders exactly one overlay-label node for a tile with an overlay', () => {
    renderViewer({
      text: 'Production Line 1',
      normalizedX: 0.25,
      normalizedY: 0.05,
      normalizedWidth: 0.5,
      normalizedHeight: 0.1,
      fontSizePx: 48,
    });

    expect(screen.getAllByTestId('camera-viewer-overlay-label')).toHaveLength(1);
  });

  it('Renders zero overlay-label nodes for a tile with no overlay', () => {
    renderViewer(undefined);

    expect(screen.queryAllByTestId('camera-viewer-overlay-label')).toHaveLength(0);
  });

  it("Renders the overlay-label node as a direct child of the 'relative aspect-video' container", () => {
    const container = renderViewer({
      text: 'Production Line 1',
      normalizedX: 0.25,
      normalizedY: 0.05,
      normalizedWidth: 0.5,
      normalizedHeight: 0.1,
      fontSizePx: 48,
    });

    const videoContainer = container.querySelector('.relative.aspect-video');
    expect(videoContainer).not.toBeNull();

    const label = screen.getByTestId('camera-viewer-overlay-label');
    expect(label.parentElement).toBe(videoContainer);
  });
});
