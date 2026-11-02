// @vitest-environment jsdom
import type { CSSProperties, KeyboardEvent as ReactKeyboardEvent, ReactNode } from 'react';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { OverlayLabel } from '@smart-sentinel-eye/shared/api/overlays.api';

/**
 * Characterisation gap-fill for the editor's own form controls and the
 * remaining keyboard-map corners (spec 478, T003): the text and font-size
 * input limits, the resolved-text preview and its failure notice, a nudge
 * landing exactly on the quantum grid, and a Meta chord being left alone.
 *
 * `react-rnd` is stubbed with a plain `<div>` carrying the editor's handlers
 * (the shape `OverlayEditorUndo.test.tsx` uses).
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

const { OverlayEditor } = await import('./OverlayEditor.js');

const BASE_LABEL: OverlayLabel = {
  text: 'Furnace {{temperature}}',
  normalizedX: 0.1,
  normalizedY: 0.5,
  normalizedWidth: 0.25,
  normalizedHeight: 0.2,
  fontSizePx: 32,
};

function getLabel(): HTMLElement {
  return screen.getByTestId('overlay-editor-label');
}

describe('OverlayEditor form controls and key corners (spec 478 gap-fill)', () => {
  afterEach(() => {
    cleanup();
  });

  describe('input limits', () => {
    it('Caps the label text at 256 characters', () => {
      render(<OverlayEditor value={BASE_LABEL} onChange={vi.fn()} />);

      expect((screen.getByTestId('overlay-editor-text') as HTMLInputElement).maxLength).toBe(256);
    });

    it('Bounds the font size between 8 and 256 px', () => {
      render(<OverlayEditor value={BASE_LABEL} onChange={vi.fn()} />);
      const slider = screen.getByTestId('overlay-editor-font-size') as HTMLInputElement;

      expect(slider.min).toBe('8');
      expect(slider.max).toBe('256');
    });
  });

  describe('the resolved-text preview', () => {
    it('Sizes and shows the canvas label from the resolved text when one has arrived', () => {
      render(
        <OverlayEditor
          value={BASE_LABEL}
          onChange={vi.fn()}
          resolvedPreview={{ resolvedText: 'Furnace 1250 C', placeholders: [] }}
        />,
      );

      expect(screen.getByTestId('overlay-editor-preview').textContent).toBe('Furnace 1250 C');
    });

    it('Falls back to the raw text while no resolved text exists', () => {
      render(<OverlayEditor value={BASE_LABEL} onChange={vi.fn()} />);

      expect(screen.getByTestId('overlay-editor-preview').textContent).toBe('Furnace {{temperature}}');
    });

    it('Reports a failed text check in the preview panel', () => {
      render(<OverlayEditor value={BASE_LABEL} onChange={vi.fn()} resolveFailed />);

      expect(screen.getByTestId('placeholder-preview-error').textContent).toBe('Could not check this text right now.');
    });
  });

  describe('keyboard corners', () => {
    it('Lands a nudge exactly on the 0.01% grid rather than on the float sum', () => {
      const onChange = vi.fn();
      render(<OverlayEditor value={BASE_LABEL} onChange={onChange} />);

      fireEvent.keyDown(getLabel(), { key: 'ArrowRight' });

      // 0.1 + 0.005 is 0.10500000000000001 in IEEE-754.
      expect((onChange.mock.calls[0]![0] as OverlayLabel).normalizedX).toBe(0.105);
    });

    it('Leaves Meta+Arrow to the browser: no move and no cancelled default', () => {
      const onChange = vi.fn();
      render(<OverlayEditor value={BASE_LABEL} onChange={onChange} />);

      const notCancelled = fireEvent.keyDown(getLabel(), { key: 'ArrowRight', metaKey: true });

      expect(notCancelled).toBe(true);
      expect(onChange).not.toHaveBeenCalled();
    });
  });
});
