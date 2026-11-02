// @vitest-environment jsdom
import type { CSSProperties, KeyboardEvent as ReactKeyboardEvent, ReactNode } from 'react';
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { OverlayLabel } from '@smart-sentinel-eye/shared/api/overlays.api';

/**
 * Characterisation gap-fill for the geometry live-region announcer
 * (spec 478, T003): the axis words, the edge phrases for y and height, the
 * repeat-announcement toggle and the timer lifecycle. These are behaviours of
 * the keyboard path that no earlier suite pinned.
 *
 * `react-rnd` is stubbed with a plain `<div>` that carries every attribute and
 * handler `OverlayEditor` gives it, the same shape `OverlayEditorUndo.test.tsx`
 * uses: real DOM key events land on a real element and the logic under test
 * lives in `OverlayEditor` itself, never in `react-rnd`.
 */

interface RndStubProps {
  tabIndex?: number;
  'data-testid'?: string;
  role?: string;
  'aria-roledescription'?: string;
  'aria-label'?: string;
  'aria-describedby'?: string;
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
      aria-roledescription={props['aria-roledescription']}
      aria-label={props['aria-label']}
      aria-describedby={props['aria-describedby']}
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

const ZERO_WIDTH_SPACE = '​';

const BASE_LABEL: OverlayLabel = {
  text: 'Furnace',
  normalizedX: 0.25,
  normalizedY: 0.5,
  normalizedWidth: 0.25,
  normalizedHeight: 0.2,
  fontSizePx: 32,
};

function buildLabel(overrides: Partial<OverlayLabel> = {}): OverlayLabel {
  return { ...BASE_LABEL, ...overrides };
}

function getLabel(): HTMLElement {
  return screen.getByTestId('overlay-editor-label');
}

function getLiveRegion(): HTMLElement {
  return screen.getByTestId('overlay-editor-geometry-live-region');
}

function press(init: NonNullable<Parameters<typeof fireEvent.keyDown>[1]>): void {
  fireEvent.keyDown(getLabel(), init);
}

function settle(ms = 500): void {
  act(() => {
    vi.advanceTimersByTime(ms);
  });
}

describe('OverlayEditor geometry announcer (spec 478 gap-fill)', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    cleanup();
    vi.useRealTimers();
  });

  describe('axis words', () => {
    it('Announces a vertical move under the word Top', () => {
      render(<OverlayEditor value={buildLabel()} onChange={vi.fn()} />);

      press({ key: 'ArrowDown' });
      settle();

      expect(getLiveRegion().textContent).toBe('Top 50.5%');
    });

    it('Announces a height resize under the word Height', () => {
      render(<OverlayEditor value={buildLabel()} onChange={vi.fn()} />);

      press({ key: 'ArrowDown', ctrlKey: true });
      settle();

      expect(getLiveRegion().textContent).toBe('Height 20.5%');
    });
  });

  describe('edge phrases', () => {
    it('Names the top edge when an upward move is refused at y = 0', () => {
      render(<OverlayEditor value={buildLabel({ normalizedY: 0 })} onChange={vi.fn()} />);

      press({ key: 'ArrowUp' });
      settle();

      expect(getLiveRegion().textContent).toBe('Top 0%, at the top edge');
    });

    it('Names the minimum when a height shrink is refused at the size floor', () => {
      render(<OverlayEditor value={buildLabel({ normalizedHeight: 0.005 })} onChange={vi.fn()} />);

      press({ key: 'ArrowUp', ctrlKey: true });
      settle();

      expect(getLiveRegion().textContent).toBe('Height 0.5%, at the minimum');
    });
  });

  describe('repeated announcements', () => {
    it('Alternates a trailing zero-width space so an unchanged refusal is written again', () => {
      render(<OverlayEditor value={buildLabel({ normalizedX: 0 })} onChange={vi.fn()} />);
      const refusal = 'Left 0%, at the left edge';

      press({ key: 'ArrowLeft' });
      settle();
      expect(getLiveRegion().textContent).toBe(refusal);

      press({ key: 'ArrowLeft' });
      settle();
      expect(getLiveRegion().textContent).toBe(`${refusal}${ZERO_WIDTH_SPACE}`);

      press({ key: 'ArrowLeft' });
      settle();
      expect(getLiveRegion().textContent).toBe(refusal);
    });

    it('Does not decorate a message that differs from the previous one', () => {
      render(<OverlayEditor value={buildLabel({ normalizedX: 0 })} onChange={vi.fn()} />);

      press({ key: 'ArrowLeft' });
      settle();
      press({ key: 'ArrowRight' });
      settle();

      expect(getLiveRegion().textContent).toBe('Left 0.5%');
    });
  });

  describe('timer lifecycle', () => {
    it('Restarts the delay on each press so a burst produces one announcement of the last press', () => {
      render(<OverlayEditor value={buildLabel()} onChange={vi.fn()} />);

      press({ key: 'ArrowRight' });
      settle(300);
      press({ key: 'ArrowRight', shiftKey: true });
      // 600 ms after the first press, 300 ms after the second: neither delay has elapsed on a restarted timer.
      settle(300);
      expect(getLiveRegion().textContent).toBe('');

      settle(200);
      expect(getLiveRegion().textContent).toBe('Left 30%');
    });

    it('Leaves no announcement timer pending once the editor unmounts', () => {
      const { unmount } = render(<OverlayEditor value={buildLabel()} onChange={vi.fn()} />);

      press({ key: 'ArrowRight' });
      expect(vi.getTimerCount()).toBe(1);

      unmount();

      expect(vi.getTimerCount()).toBe(0);
    });
  });
});
