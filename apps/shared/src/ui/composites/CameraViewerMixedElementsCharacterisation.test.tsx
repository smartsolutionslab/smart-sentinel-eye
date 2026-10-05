// @vitest-environment jsdom
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';

const useGetStreamQueryMock = vi.fn();

vi.mock('@smart-sentinel-eye/shared/api/streams.api', () => ({
  useGetStreamQuery: (...args: unknown[]) => useGetStreamQueryMock(...args),
}));

const { CameraViewer } = await import('@smart-sentinel-eye/shared/ui/composites/CameraViewer');
import type { CameraViewerOverlay } from '@smart-sentinel-eye/shared/ui/composites/CameraViewer';

/**
 * Spec 300 (#2349) phase-6 S1. Tasks.md's T019 listed "Box/Ellipse render
 * tests" as required red tests; they were never written — only e2e covered
 * this path, and that e2e spec was itself broken until a separate fix. This
 * file is the missing vitest coverage for `CameraViewer.tsx`'s kind switch
 * (~lines 424-435) and its `OverlayShape` renderer (~lines 577-594).
 *
 * The implementation is already correct (phase-6 review verified it), so
 * this is expected GREEN — proving existing-but-untested behaviour, not a
 * red-then-green cycle for new code.
 */
describe('CameraViewer renders a mixed Box/Text/Ellipse element set (characterisation)', () => {
  afterEach(() => {
    cleanup();
  });

  const elements: readonly CameraViewerOverlay[] = [
    {
      kind: 'Box',
      color: '#D32F2FFF',
      normalizedX: 0.05,
      normalizedY: 0.05,
      normalizedWidth: 0.2,
      normalizedHeight: 0.2,
    },
    {
      kind: 'Text',
      color: '#FFFFFFD9',
      text: 'Production Line 1',
      normalizedX: 0.3,
      normalizedY: 0.3,
      normalizedWidth: 0.3,
      normalizedHeight: 0.08,
      fontSizePx: 48,
    },
    {
      kind: 'Ellipse',
      color: '#FFEB3B80',
      normalizedX: 0.6,
      normalizedY: 0.6,
      normalizedWidth: 0.2,
      normalizedHeight: 0.2,
    },
  ];

  function renderViewer() {
    useGetStreamQueryMock.mockReturnValue({ data: undefined, isLoading: false, error: undefined });

    const { container } = render(
      <CameraViewer cameraIdentifier="cam-42" getToken={async () => null} overlays={elements} />,
    );

    return container;
  }

  it('Renders exactly two overlay-shape nodes and one overlay-label node for a [Box, Text, Ellipse] set', () => {
    renderViewer();

    expect(screen.getAllByTestId('camera-viewer-overlay-shape')).toHaveLength(2);
    expect(screen.getAllByTestId('camera-viewer-overlay-label')).toHaveLength(1);
  });

  it("Renders each shape node as a direct child of the 'relative aspect-video' container, no wrapper (spec 150 FR-012)", () => {
    const container = renderViewer();

    const videoContainer = container.querySelector('.relative.aspect-video');
    expect(videoContainer).not.toBeNull();

    for (const shape of screen.getAllByTestId('camera-viewer-overlay-shape')) {
      expect(shape.parentElement).toBe(videoContainer);
    }
  });

  it('Marks each shape node aria-hidden and carries its own data-kind', () => {
    renderViewer();

    const shapes = screen.getAllByTestId('camera-viewer-overlay-shape');
    expect(shapes.map((shape) => shape.getAttribute('data-kind'))).toEqual(['Box', 'Ellipse']);
    for (const shape of shapes) {
      expect(shape.getAttribute('aria-hidden')).toBe('true');
    }
  });

  it('Leaves the status live region unaffected by the shapes in the set', () => {
    renderViewer();

    const status = screen.getByTestId('camera-viewer-status');
    expect(status).toHaveAttribute('role', 'status');
    // Live while the stream status is 'idle' with no failed read (see
    // CameraViewer.tsx's `announcementFor`) — unchanged by what overlays,
    // text or shapes, are painted on top.
    expect(status).toHaveTextContent('');
  });
});
