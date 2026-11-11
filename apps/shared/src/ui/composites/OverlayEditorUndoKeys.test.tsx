// @vitest-environment jsdom
import { useState } from 'react';
import type { CSSProperties, KeyboardEvent as ReactKeyboardEvent, ReactNode } from 'react';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { OverlayLabel } from '@smart-sentinel-eye/shared/api/overlays.api';

/**
 * Characterisation gap-fill for how the undo history is segmented and which
 * key chords drive it (spec 478, T003): typed geometry and colour commits are
 * one step each, a modifier keyup does not end a held arrow run, the font-size
 * run ends on pointer release, and Undo/Redo shortcuts are claimed only for the
 * exact chords.
 *
 * `react-rnd` is stubbed with a plain `<div>` carrying the editor's handlers
 * (the shape `OverlayEditorUndo.test.tsx` uses). The editor is driven through a
 * controlled wrapper because a run only coalesces across renders the way a
 * real parent would produce them.
 */

interface RndStubProps {
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

vi.mock('react-rnd', () => ({
  Rnd: (props: RndStubProps) => (
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
  ),
}));

const { OverlayEditor } = await import('./OverlayEditor');

const BASE_LABEL: OverlayLabel = {
  text: 'Furnace',
  normalizedX: 0.25,
  normalizedY: 0.5,
  normalizedWidth: 0.25,
  normalizedHeight: 0.2,
  fontSizePx: 32,
};

function ControlledOverlayEditor({ onChangeSpy }: { onChangeSpy: (next: OverlayLabel) => void }) {
  const [value, setValue] = useState(BASE_LABEL);
  return (
    <OverlayEditor
      value={value}
      onChange={(next) => {
        setValue(next);
        onChangeSpy(next);
      }}
    />
  );
}

function getLabel(): HTMLElement {
  return screen.getByTestId('overlay-editor-label');
}

function field(label: string): HTMLInputElement {
  return screen.getByLabelText(label, { exact: true }) as HTMLInputElement;
}

function lastEmitted(spy: ReturnType<typeof vi.fn>): OverlayLabel {
  return spy.mock.calls[spy.mock.calls.length - 1]![0] as OverlayLabel;
}

describe('OverlayEditor undo steps and shortcut chords (spec 478 gap-fill)', () => {
  afterEach(() => {
    cleanup();
  });

  describe('step boundaries', () => {
    it('Makes each typed geometry commit its own undo step', () => {
      const onChange = vi.fn();
      render(<ControlledOverlayEditor onChangeSpy={onChange} />);

      fireEvent.change(field('Left'), { target: { value: '30' } });
      fireEvent.blur(field('Left'));
      fireEvent.change(field('Left'), { target: { value: '40' } });
      fireEvent.blur(field('Left'));
      expect(lastEmitted(onChange).normalizedX).toBe(0.4);

      fireEvent.keyDown(getLabel(), { key: 'z', ctrlKey: true });

      expect(lastEmitted(onChange).normalizedX).toBe(0.3);
    });

    it('Makes each accepted colour its own undo step', () => {
      const onChange = vi.fn();
      render(<ControlledOverlayEditor onChangeSpy={onChange} />);

      fireEvent.click(screen.getByRole('button', { name: 'gray-500' }));
      fireEvent.click(screen.getByRole('button', { name: 'cyan-600' }));
      expect(lastEmitted(onChange).color).toBe('#3CA2B2FF');

      fireEvent.keyDown(getLabel(), { key: 'z', ctrlKey: true });

      expect(lastEmitted(onChange).color).toBe('#818A98FF');
    });

    it('Keeps a held arrow run open across the keyup of a modifier key', () => {
      const onChange = vi.fn();
      render(<ControlledOverlayEditor onChangeSpy={onChange} />);

      fireEvent.keyDown(getLabel(), { key: 'ArrowRight' });
      fireEvent.keyDown(getLabel(), { key: 'ArrowRight' });
      fireEvent.keyUp(getLabel(), { key: 'Shift' });
      fireEvent.keyDown(getLabel(), { key: 'ArrowRight' });
      fireEvent.keyUp(getLabel(), { key: 'ArrowRight' });
      expect(lastEmitted(onChange).normalizedX).toBe(0.265);

      fireEvent.keyDown(getLabel(), { key: 'z', ctrlKey: true });

      expect(lastEmitted(onChange).normalizedX).toBe(0.25);
    });

    it('Ends the font-size run when the pointer is released', () => {
      const onChange = vi.fn();
      render(<ControlledOverlayEditor onChangeSpy={onChange} />);
      const slider = screen.getByTestId('overlay-editor-font-size');

      fireEvent.change(slider, { target: { value: '40' } });
      fireEvent.pointerUp(slider);
      fireEvent.change(slider, { target: { value: '50' } });
      expect(lastEmitted(onChange).fontSizePx).toBe(50);

      fireEvent.keyDown(slider, { key: 'z', ctrlKey: true });

      expect(lastEmitted(onChange).fontSizePx).toBe(40);
    });
  });

  describe('shortcut chords', () => {
    it('Claims Ctrl+Z and Cmd+Z so the browser does not also undo', () => {
      render(<ControlledOverlayEditor onChangeSpy={vi.fn()} />);

      // fireEvent returns false when the event was cancelled.
      expect(fireEvent.keyDown(getLabel(), { key: 'z', ctrlKey: true })).toBe(false);
      expect(fireEvent.keyDown(getLabel(), { key: 'z', metaKey: true })).toBe(false);
    });

    it('Does not treat Cmd+Y as redo', () => {
      const onChange = vi.fn();
      render(<ControlledOverlayEditor onChangeSpy={onChange} />);
      fireEvent.keyDown(getLabel(), { key: 'ArrowRight' });
      fireEvent.keyDown(getLabel(), { key: 'z', ctrlKey: true });
      const emissionsBefore = onChange.mock.calls.length;

      const notCancelled = fireEvent.keyDown(getLabel(), { key: 'y', metaKey: true });

      expect(notCancelled).toBe(true);
      expect(onChange).toHaveBeenCalledTimes(emissionsBefore);
      expect(screen.getByTestId('overlay-editor-redo').getAttribute('aria-disabled')).toBe('false');
    });

    it('Treats Ctrl+Y as redo', () => {
      const onChange = vi.fn();
      render(<ControlledOverlayEditor onChangeSpy={onChange} />);
      fireEvent.keyDown(getLabel(), { key: 'ArrowRight' });
      fireEvent.keyDown(getLabel(), { key: 'z', ctrlKey: true });

      const notCancelled = fireEvent.keyDown(getLabel(), { key: 'y', ctrlKey: true });

      expect(notCancelled).toBe(false);
      expect(lastEmitted(onChange).normalizedX).toBe(0.255);
    });
  });
});
