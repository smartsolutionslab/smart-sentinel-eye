// @vitest-environment jsdom
import type { CSSProperties, KeyboardEvent as ReactKeyboardEvent, ReactNode } from 'react';
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { OverlayLabel } from '@smart-sentinel-eye/shared/api/overlays.api';

/**
 * Characterisation gap-fill for the pointer-gesture and typed-entry paths of
 * `OverlayEditor` (spec 478, T003): the live readout while a drag or resize is
 * in flight, its discard on release, the clamps on pixel-derived geometry, the
 * one-field spread of a typed commit, and the props handed to `react-rnd`.
 *
 * `react-rnd` is stubbed and its last props kept, the shape
 * `OverlayEditorUndo.test.tsx` uses, so a test can invoke `onDrag`,
 * `onDragStop`, `onResize` and `onResizeStop` directly and read `bounds`/`size`
 * as the contract with the library. jsdom has no layout, so the real component
 * could not show either.
 */

interface RndStubProps {
  size: { width: number; height: number };
  position: { x: number; y: number };
  bounds?: string;
  onDrag?: (e: unknown, data: { x: number; y: number }) => void;
  onDragStop: (e: unknown, data: { x: number; y: number }) => void;
  onResize?: (
    e: unknown,
    dir: string,
    ref: { offsetWidth: number; offsetHeight: number },
    delta: unknown,
    position: { x: number; y: number },
  ) => void;
  onResizeStop: (
    e: unknown,
    dir: string,
    ref: { offsetWidth: number; offsetHeight: number },
    delta: unknown,
    position: { x: number; y: number },
  ) => void;
  tabIndex?: number;
  'data-testid'?: string;
  role?: string;
  onKeyDown?: (event: ReactKeyboardEvent<HTMLDivElement>) => void;
  onKeyUp?: (event: ReactKeyboardEvent<HTMLDivElement>) => void;
  onFocus?: () => void;
  onBlur?: () => void;
  style?: CSSProperties;
  children?: ReactNode;
}

let lastRndProps: RndStubProps | null = null;

vi.mock('react-rnd', () => ({
  Rnd: (props: RndStubProps) => {
    lastRndProps = props;
    return (
      <div
        tabIndex={props.tabIndex}
        data-testid={props['data-testid']}
        role={props.role}
        onKeyDown={props.onKeyDown}
        onKeyUp={props.onKeyUp}
        onFocus={props.onFocus}
        onBlur={props.onBlur}
        style={props.style}
      >
        {props.children}
      </div>
    );
  },
}));

const { OverlayEditor } = await import('./OverlayEditor');

// 800x450 default canvas: x = 80 px, y = 45 px, width = 240 px, height = 36 px.
const BASE_LABEL: OverlayLabel = {
  text: 'Furnace',
  normalizedX: 0.1,
  normalizedY: 0.1,
  normalizedWidth: 0.3,
  normalizedHeight: 0.08,
  fontSizePx: 32,
};

function buildLabel(overrides: Partial<OverlayLabel> = {}): OverlayLabel {
  return { ...BASE_LABEL, ...overrides };
}

function field(label: string): HTMLInputElement {
  return screen.getByLabelText(label, { exact: true }) as HTMLInputElement;
}

function rnd(): RndStubProps {
  if (lastRndProps === null) throw new Error('Rnd was never rendered');
  return lastRndProps;
}

function drag(x: number, y: number): void {
  act(() => {
    rnd().onDrag?.({}, { x, y });
  });
}

function dragStop(x: number, y: number): void {
  act(() => {
    rnd().onDragStop({}, { x, y });
  });
}

function resize(width: number, height: number, x: number, y: number): void {
  act(() => {
    rnd().onResize?.({}, 'bottomRight', { offsetWidth: width, offsetHeight: height }, {}, { x, y });
  });
}

function resizeStop(width: number, height: number, x: number, y: number): void {
  act(() => {
    rnd().onResizeStop({}, 'bottomRight', { offsetWidth: width, offsetHeight: height }, {}, { x, y });
  });
}

function commitTyped(label: string, text: string): void {
  fireEvent.change(field(label), { target: { value: text } });
  fireEvent.blur(field(label));
}

describe('OverlayEditor pointer gestures (spec 478 gap-fill)', () => {
  afterEach(() => {
    cleanup();
    lastRndProps = null;
  });

  describe('the live readout while a drag is in flight', () => {
    it('Shows the dragged position in the Left and Top fields before release', () => {
      const onChange = vi.fn();
      render(<OverlayEditor value={buildLabel()} onChange={onChange} />);

      drag(400, 225);

      expect(field('Left').value).toBe('50');
      expect(field('Top').value).toBe('50');
      expect(onChange).not.toHaveBeenCalled();
    });

    it('Clamps the readout to the canvas the way the release will', () => {
      render(<OverlayEditor value={buildLabel()} onChange={vi.fn()} />);

      drag(-80, 2000);

      expect(field('Left').value).toBe('0');
      expect(field('Top').value).toBe('100');
    });

    it('Falls back to the committed value once the drag is released', () => {
      const onChange = vi.fn();
      render(<OverlayEditor value={buildLabel()} onChange={onChange} />);

      drag(400, 225);
      dragStop(400, 225);

      expect(onChange).toHaveBeenCalledTimes(1);
      expect(onChange.mock.calls[0]![0]).toMatchObject({ normalizedX: 0.5, normalizedY: 0.5 });
      // onChange is an inert spy here, so `value` is unchanged and the readout must be reading it again.
      expect(field('Left').value).toBe('10');
      expect(field('Top').value).toBe('10');
    });
  });

  describe('the live readout while a resize is in flight', () => {
    it('Shows the resized rectangle in the four fields before release', () => {
      const onChange = vi.fn();
      render(<OverlayEditor value={buildLabel()} onChange={onChange} />);

      resize(400, 90, 80, 45);

      expect(field('Left').value).toBe('10');
      expect(field('Top').value).toBe('10');
      expect(field('Width').value).toBe('50');
      expect(field('Height').value).toBe('20');
      expect(onChange).not.toHaveBeenCalled();
    });

    it('Falls back to the committed value once the resize is released', () => {
      const onChange = vi.fn();
      render(<OverlayEditor value={buildLabel()} onChange={onChange} />);

      resize(400, 90, 80, 45);
      resizeStop(400, 90, 80, 45);

      expect(onChange).toHaveBeenCalledTimes(1);
      expect(onChange.mock.calls[0]![0]).toMatchObject({ normalizedWidth: 0.5, normalizedHeight: 0.2 });
      expect(field('Width').value).toBe('30');
      expect(field('Height').value).toBe('8');
    });
  });

  describe('clamps on pixel-derived geometry', () => {
    it('Emits 0 for a position that is not a number', () => {
      const onChange = vi.fn();
      render(<OverlayEditor value={buildLabel()} onChange={onChange} />);

      dragStop(Number.NaN, 45);

      expect(onChange.mock.calls[0]![0]).toMatchObject({ normalizedX: 0, normalizedY: 0.1 });
    });
  });

  describe('a typed geometry commit', () => {
    it('Changes only the committed field and leaves an out-of-range stored size untouched', () => {
      const onChange = vi.fn();
      render(<OverlayEditor value={buildLabel({ normalizedWidth: 1.2 })} onChange={onChange} />);

      commitTyped('Left', '30');

      expect(onChange).toHaveBeenCalledTimes(1);
      expect(onChange.mock.calls[0]![0]).toMatchObject({ normalizedX: 0.3, normalizedWidth: 1.2 });
    });

    it('Emits the typed size rather than the stored one', () => {
      const onChange = vi.fn();
      render(<OverlayEditor value={buildLabel()} onChange={onChange} />);

      commitTyped('Width', '50');

      expect(onChange.mock.calls[0]![0]).toMatchObject({ normalizedWidth: 0.5, normalizedX: 0.1 });
    });
  });

  describe('the props handed to react-rnd', () => {
    it('Asks for the label to stay inside its parent canvas', () => {
      render(<OverlayEditor value={buildLabel()} onChange={vi.fn()} />);

      expect(rnd().bounds).toBe('parent');
    });

    it('Gives a very narrow and a very short element a grabbable minimum pixel size', () => {
      render(
        <OverlayEditor value={buildLabel({ normalizedWidth: 0.01, normalizedHeight: 0.005 })} onChange={vi.fn()} />,
      );

      // 0.01 * 800 = 8 px and 0.005 * 450 = 2.25 px, both below the floor.
      expect(rnd().size).toEqual({ width: 24, height: 16 });
    });
  });
});
