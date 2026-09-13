// @vitest-environment jsdom
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';

const useGetStreamQueryMock = vi.fn();

vi.mock('@smart-sentinel-eye/shared/api/streams.api', () => ({
  useGetStreamQuery: (...args: unknown[]) => useGetStreamQueryMock(...args),
}));

const { CameraViewer } = await import('@smart-sentinel-eye/shared/ui/composites/CameraViewer');
const { OverlayEditor } = await import('@smart-sentinel-eye/shared/ui/composites/OverlayEditor');

/**
 * Spec 146 (issue #2339). `OverlayEditor`'s preview and `CameraViewer`'s
 * painted label hand-write the same eight surface properties separately and
 * disagree on four of them. This guard compares the two *rendered
 * components* to each other rather than to the not-yet-existing
 * `overlayLabelSurfaceStyle` module — importing that module here would make
 * this test fail on module resolution today, which is evidence of nothing
 * (plan.md "the trap in the red"). Comparing the components directly fails
 * on `origin/develop` with a real value mismatch, and keeps failing if
 * someone re-inlines a diverging value after the fold.
 *
 * Each known divergence gets its own test: `expect` throws on first failure,
 * so one test asserting all eight properties would report only the first
 * mismatch and hide the rest. The engineer needs all four visible from the
 * first run — fontSize (the clamp formula) is the hard one and should not
 * ambush them last.
 */
describe('OverlayEditor preview vs CameraViewer wall label (parity guard)', () => {
  afterEach(() => {
    cleanup();
  });

  const label = {
    text: 'Production Line 1',
    normalizedX: 0.25,
    normalizedY: 0.05,
    normalizedWidth: 0.5,
    normalizedHeight: 0.1,
    fontSizePx: 48,
  };

  function renderWallLabel() {
    useGetStreamQueryMock.mockReturnValue({ data: undefined, isLoading: false, error: undefined });

    render(<CameraViewer cameraIdentifier="cam-42" getToken={async () => null} overlay={label} />);

    return screen.getByTestId('camera-viewer-overlay-label');
  }

  function renderEditorLabel() {
    render(<OverlayEditor value={label} onChange={() => {}} />);

    return screen.getByTestId('overlay-editor-preview').parentElement as HTMLElement;
  }

  it('Agrees with the wall on the flex layout (display, alignItems, justifyContent)', () => {
    const wallLabel = renderWallLabel();
    const editorLabel = renderEditorLabel();

    expect(editorLabel.style.display).toBe(wallLabel.style.display);
    expect(editorLabel.style.alignItems).toBe(wallLabel.style.alignItems);
    expect(editorLabel.style.justifyContent).toBe(wallLabel.style.justifyContent);
  });

  it('Agrees with the wall on the ink colour and weight', () => {
    const wallLabel = renderWallLabel();
    const editorLabel = renderEditorLabel();

    expect(editorLabel.style.color).toBe(wallLabel.style.color);
    expect(editorLabel.style.fontWeight).toBe(wallLabel.style.fontWeight);
  });

  it('Agrees with the wall on the background', () => {
    const wallLabel = renderWallLabel();
    const editorLabel = renderEditorLabel();

    expect(wallLabel.style.background).toBe('rgba(255, 255, 255, 0.85)');
    expect(editorLabel.style.background).toBe(wallLabel.style.background);
  });

  it('Agrees with the wall on the padding', () => {
    const wallLabel = renderWallLabel();
    const editorLabel = renderEditorLabel();

    expect(editorLabel.style.padding).toBe(wallLabel.style.padding);
  });

  it('Agrees with the wall on the type size', () => {
    const wallLabel = renderWallLabel();
    const editorLabel = renderEditorLabel();

    expect(editorLabel.style.fontSize).toBe(wallLabel.style.fontSize);
  });

  it('Does not paint a border the wall does not have', () => {
    const wallLabel = renderWallLabel();
    const editorLabel = renderEditorLabel();

    expect(editorLabel.style.border).toBe(wallLabel.style.border);
  });
});
