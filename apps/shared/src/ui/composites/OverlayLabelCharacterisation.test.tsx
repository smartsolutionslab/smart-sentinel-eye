// @vitest-environment jsdom
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';

const useGetStreamQueryMock = vi.fn();

vi.mock('@smart-sentinel-eye/shared/api/streams.api', () => ({
  useGetStreamQuery: (...args: unknown[]) => useGetStreamQueryMock(...args),
}));

const { CameraViewer } = await import('@smart-sentinel-eye/shared/ui/composites/CameraViewer');

/**
 * Spec 146 (issue #2339). `CameraViewer.OverlayLabel` is the wall's paint —
 * this refactor's entire proof is that folding its style into a shared
 * function does not move a single one of these values. Captured green
 * against today's (unfolded) shape and left unmodified after the fold
 * (plan.md "Phase 4a", T002/T008).
 *
 * Per-property assertions only: React serialises inline styles in object
 * key-insertion order, and the fold changes that order (shared properties
 * arrive via a spread). Asserting the whole `style` attribute as one string
 * would go red on the reorder alone — a false red on a characterisation
 * test that must not move.
 */
describe('CameraViewer.OverlayLabel style (characterisation — must not move)', () => {
  afterEach(() => {
    cleanup();
  });

  function renderLabel() {
    useGetStreamQueryMock.mockReturnValue({ data: undefined, isLoading: false, error: undefined });

    render(
      <CameraViewer
        cameraIdentifier="cam-42"
        getToken={async () => null}
        overlay={{
          text: 'Production Line 1',
          normalizedX: 0.25,
          normalizedY: 0.05,
          normalizedWidth: 0.5,
          normalizedHeight: 0.1,
          fontSizePx: 48,
        }}
      />,
    );

    return screen.getByTestId('camera-viewer-overlay-label');
  }

  it('Positions the label at the overlay normalized coordinates', () => {
    const label = renderLabel();

    expect(label.style.position).toBe('absolute');
    expect(label.style.left).toBe('25%');
    expect(label.style.top).toBe('5%');
    expect(label.style.width).toBe('50%');
    expect(label.style.height).toBe('10%');
  });

  it('Centres its content with a flex box', () => {
    const label = renderLabel();

    expect(label.style.display).toBe('flex');
    expect(label.style.alignItems).toBe('center');
    expect(label.style.justifyContent).toBe('center');
  });

  it('Paints the wall background at 0.85 alpha with no border', () => {
    const label = renderLabel();

    expect(label.style.background).toBe('rgba(255, 255, 255, 0.85)');
    expect(label.style.border).toBe('');
  });

  it('Sets the ink colour and weight', () => {
    const label = renderLabel();

    // jsdom normalises the authored hex literal to its rgb() equivalent.
    expect(label.style.color).toBe('rgb(17, 24, 39)');
    expect(label.style.fontWeight).toBe('600');
  });

  it('Sizes the type with the vw-derived clamp formula', () => {
    const label = renderLabel();

    // jsdom preserves clamp() verbatim (jsdom 30.0.1, verified on this tree).
    expect(label.style.fontSize).toBe('clamp(12px, 3vw, 48px)');
  });

  it('Pads the label at 4px and ignores pointer events', () => {
    const label = renderLabel();

    // jsdom normalises the authored '0 4px' shorthand to '0px 4px'.
    expect(label.style.padding).toBe('0px 4px');
    expect(label.style.pointerEvents).toBe('none');
  });
});
