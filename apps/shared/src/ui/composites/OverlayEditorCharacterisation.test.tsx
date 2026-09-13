// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { OverlayLabel } from '@smart-sentinel-eye/shared/api/overlays.api';

/**
 * Spec 147 T001 — the characterisation of `OverlayEditor` as it stands **before**
 * the backdrop work lands. `OverlayEditor` has no test file of its own today
 * (#2354's two files are about the label's own surface; `OverlayEditorDialog.test.tsx`
 * is about the form around it), so this is the safety net spec 147 needs before
 * touching the canvas at all (constitution §Testing, ADR-0144).
 *
 * <p>
 * <b>Every test here must still pass, unmodified, once T005–T010 land.</b> An
 * assertion that has to change is evidence the behaviour moved — block the
 * change, do not adjust the test (CLAUDE.md "Phase 4a has two colours").
 * </p>
 *
 * <p>
 * <b>Why `react-rnd` is mocked here, rather than dragged for real.</b> jsdom
 * hard-codes `offsetWidth`/`offsetHeight` to 0 and `getBoundingClientRect()` to an
 * all-zero rect (`HTMLElement-impl.js`), and react-rnd's resize path reads the
 * dragged size from exactly those two instruments — so a real mouse-event
 * simulation would report 0×0 for every resize regardless of which handle was
 * dragged, and `Rnd`'s own position bookkeeping (`offsetFromParent`) cancels
 * itself out for the same reason, always reporting `{x: 0, y: 0}`. Testing that
 * would be testing jsdom's absence of a layout engine, not this file's logic. The
 * mapping this file owns — normalized ⇄ pixel, and the [0,1] clamp — does not
 * live in react-rnd at all; it lives in `emitGeometry` and the `pixelX/pixelY/
 * pixelWidth/pixelHeight` calculation right here in `OverlayEditor.tsx`. Standing
 * a controllable stub in for `Rnd` isolates exactly that logic, the same way
 * `CameraViewerMedia.test.tsx` stands `FakePeerConnection` in for the browser's
 * `RTCPeerConnection` rather than exercising a real network.
 * </p>
 */

interface RndStubProps {
  size: { width: number; height: number };
  position: { x: number; y: number };
  onDragStop: (e: unknown, data: { x: number; y: number }) => void;
  onResizeStop: (
    e: unknown,
    dir: string,
    ref: { offsetWidth: number; offsetHeight: number },
    delta: unknown,
    position: { x: number; y: number },
  ) => void;
  children?: unknown;
}

let lastRndProps: RndStubProps | null = null;

vi.mock('react-rnd', () => ({
  Rnd: (props: RndStubProps) => {
    lastRndProps = props;
    return props.children;
  },
}));

const { OverlayEditor } = await import('./OverlayEditor.js');

// The source literal is `repeating-linear-gradient(45deg, #1f2937, #1f2937
// 12px, #111827 12px, #111827 24px)` (`OverlayEditor.tsx:69`). This is that
// same string as a browser — and jsdom's `cssstyle` — normalizes it once it
// has gone through `element.style`: colours become `rgb(...)`. This is what
// `overlay-editor-canvas`'s `style.background` actually reads back as, and the
// exact-match `toBe` below is what pins it — not softened to a substring, so
// any change to the gradient (a colour, the angle, a stop) fails this.
const GRADIENT_RENDERED =
  'repeating-linear-gradient(45deg, rgb(31, 41, 55), rgb(31, 41, 55) 12px, rgb(17, 24, 39) 12px, rgb(17, 24, 39) 24px)';

const BASE_LABEL: OverlayLabel = {
  text: 'Line-1 Inlet',
  normalizedX: 0.25,
  normalizedY: 0.5,
  normalizedWidth: 0.25,
  normalizedHeight: 0.2,
  fontSizePx: 32,
};

function buildLabel(overrides: Partial<OverlayLabel> = {}): OverlayLabel {
  return { ...BASE_LABEL, ...overrides };
}

describe('OverlayEditor characterisation (spec 147 T001)', () => {
  afterEach(() => {
    cleanup();
    lastRndProps = null;
  });

  /**
   * FR-002, byte-for-byte. An exact `toBe` against the full string, not a
   * substring or a regex — a single mangled colour, angle or stop in a rebase
   * (PR #2354 touches this same file) fails this loudly.
   */
  it('Paints the checkerboard gradient on the canvas', () => {
    render(<OverlayEditor value={buildLabel()} onChange={vi.fn()} />);
    const canvas = screen.getByTestId('overlay-editor-canvas');
    expect(canvas.style.background).toBe(GRADIENT_RENDERED);
  });

  it('Fixes the canvas at 800 by 450 pixels by default', () => {
    render(<OverlayEditor value={buildLabel()} onChange={vi.fn()} />);
    const canvas = screen.getByTestId('overlay-editor-canvas');
    expect(canvas.style.width).toBe('800px');
    expect(canvas.style.height).toBe('450px');
  });

  it("Maps the label's authored normalized geometry onto the draggable in pixel space", () => {
    render(
      <OverlayEditor
        value={buildLabel({ normalizedX: 0.25, normalizedY: 0.4, normalizedWidth: 0.3, normalizedHeight: 0.2 })}
        onChange={vi.fn()}
      />,
    );
    expect(lastRndProps).not.toBeNull();
    expect(lastRndProps!.position).toEqual({ x: 200, y: 180 });
    expect(lastRndProps!.size).toEqual({ width: 240, height: 90 });
  });

  it('Emits clamped [0,1] geometry through onChange when a drag stops', () => {
    const onChange = vi.fn();
    render(<OverlayEditor value={buildLabel()} onChange={onChange} />);

    act(() => {
      lastRndProps!.onDragStop({}, { x: -50, y: 5_000 });
    });

    expect(onChange).toHaveBeenCalledTimes(1);
    const next = onChange.mock.calls[0]![0] as OverlayLabel;
    // Below 0 and above the canvas both clamp to the [0,1] edge (clamp01).
    expect(next.normalizedX).toBe(0);
    expect(next.normalizedY).toBe(1);
    // A drag carries the label's current size and every other field forward
    // untouched — only position moves.
    expect(next.normalizedWidth).toBeCloseTo(BASE_LABEL.normalizedWidth, 10);
    expect(next.normalizedHeight).toBeCloseTo(BASE_LABEL.normalizedHeight, 10);
    expect(next.text).toBe(BASE_LABEL.text);
    expect(next.fontSizePx).toBe(BASE_LABEL.fontSizePx);
  });

  it('Emits clamped [0,1] geometry through onChange when a resize stops', () => {
    const onChange = vi.fn();
    render(<OverlayEditor value={buildLabel()} onChange={onChange} />);

    act(() => {
      lastRndProps!.onResizeStop({}, 'bottomRight', { offsetWidth: 1_600, offsetHeight: -100 }, {}, { x: 100, y: 50 });
    });

    expect(onChange).toHaveBeenCalledTimes(1);
    const next = onChange.mock.calls[0]![0] as OverlayLabel;
    expect(next.normalizedX).toBeCloseTo(100 / 800, 10);
    expect(next.normalizedY).toBeCloseTo(50 / 450, 10);
    // 1600px on an 800px-wide canvas is above 1 and clamps; -100px is below 0.
    expect(next.normalizedWidth).toBe(1);
    expect(next.normalizedHeight).toBe(0);
  });

  it('Emits the typed label text via onChange, leaving geometry untouched', () => {
    const onChange = vi.fn();
    render(<OverlayEditor value={buildLabel()} onChange={onChange} />);

    fireEvent.change(screen.getByTestId('overlay-editor-text'), { target: { value: 'New text' } });

    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ ...BASE_LABEL, text: 'New text' }));
  });

  it('Emits the numeric font size via onChange, leaving geometry untouched', () => {
    const onChange = vi.fn();
    render(<OverlayEditor value={buildLabel()} onChange={onChange} />);

    fireEvent.change(screen.getByTestId('overlay-editor-font-size'), { target: { value: '48' } });

    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ ...BASE_LABEL, fontSizePx: 48 }));
  });
});
