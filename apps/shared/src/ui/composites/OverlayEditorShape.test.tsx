// @vitest-environment jsdom
import { useState } from 'react';
import type { CSSProperties, KeyboardEvent as ReactKeyboardEvent, ReactNode } from 'react';
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { OverlayShape } from '@smart-sentinel-eye/shared/api/overlays.api';
import { overlayShapeStyle } from './overlayLabelStyle.js';

/**
 * Spec 457 (issue #3107) C4. New behaviour, RED (ADR-0139): `OverlayEditor` edits a Box or
 * an Ellipse. Today it reads `value.text`, so a shape gets a label inspector; every failure
 * below names a missing shape behaviour, not an import.
 *
 * `react-rnd` is stubbed the way `OverlayEditorUndo.test.tsx` stubs it: a plain div that
 * carries every attribute and handler the editor gives `<Rnd>`, and exposes `onDragStop`.
 */

interface RndStubProps {
  size: { width: number; height: number };
  position: { x: number; y: number };
  onDragStop: (e: unknown, data: { x: number; y: number }) => void;
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

let lastRndProps: RndStubProps | null = null;

vi.mock('react-rnd', () => ({
  Rnd: (props: RndStubProps) => {
    lastRndProps = props;
    return (
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
    );
  },
}));

const { OverlayEditor } = await import('./OverlayEditor.js');

const FINE = 0.005;

function buildShape(overrides: Partial<OverlayShape> = {}): OverlayShape {
  return {
    kind: 'Box',
    color: '#D32F2F80',
    normalizedX: 0.1,
    normalizedY: 0.1,
    normalizedWidth: 0.3,
    normalizedHeight: 0.2,
    ...overrides,
  };
}

function Controlled({ initial, spy }: { initial: OverlayShape; spy?: (next: OverlayShape) => void }) {
  const [value, setValue] = useState(initial);
  return (
    <OverlayEditor
      value={value}
      onChange={(next) => {
        const shape = next as OverlayShape;
        setValue(shape);
        spy?.(shape);
      }}
    />
  );
}

function node(): HTMLElement {
  return screen.getByTestId('overlay-editor-label');
}

function field(label: string): HTMLInputElement {
  return screen.getByLabelText(label, { exact: true }) as HTMLInputElement;
}

function pressUndo(): void {
  fireEvent.keyDown(node(), { key: 'z', ctrlKey: true });
}

afterEach(() => {
  cleanup();
  lastRndProps = null;
});

describe('OverlayEditor with a shape value', () => {
  it.each(['Box', 'Ellipse'] as const)('Paints a %s with the wall overlayShapeStyle', (kind) => {
    const value = buildShape({ kind, color: '#12AB34CC' });
    render(<OverlayEditor value={value} onChange={vi.fn()} />);
    expect(lastRndProps!.style).toMatchObject(overlayShapeStyle(value));
  });

  it('Does not give a shape the label surface (no filled label background)', () => {
    render(<OverlayEditor value={buildShape()} onChange={vi.fn()} />);
    expect(lastRndProps!.style?.background).toBe('transparent');
  });

  it.each(['Box', 'Ellipse'] as const)('Names a %s by its kind with the roledescription "Overlay shape"', (kind) => {
    render(<OverlayEditor value={buildShape({ kind })} onChange={vi.fn()} />);
    expect(node()).toHaveAttribute('aria-label', kind);
    expect(node()).toHaveAttribute('aria-roledescription', 'Overlay shape');
    expect(node()).toHaveAttribute('role', 'application');
  });

  it('Describes the keyboard instructions as moving a shape, not a label', () => {
    render(<OverlayEditor value={buildShape()} onChange={vi.fn()} />);
    const instructions = document.getElementById(node().getAttribute('aria-describedby')!)!;
    expect(instructions.textContent).toMatch(/shape/i);
    expect(instructions.textContent).not.toMatch(/label/i);
  });

  it('Renders no label text field, no font size and no on-canvas text for a shape', () => {
    render(<OverlayEditor value={buildShape()} onChange={vi.fn()} />);
    expect(screen.queryByTestId('overlay-editor-text')).not.toBeInTheDocument();
    expect(screen.queryByTestId('overlay-editor-font-size')).not.toBeInTheDocument();
    expect(screen.queryByTestId('overlay-editor-preview')).not.toBeInTheDocument();
    expect(screen.queryByText('Label text')).not.toBeInTheDocument();
  });

  it('Renders no placeholder preview panel for a shape', () => {
    render(<OverlayEditor value={buildShape()} onChange={vi.fn()} />);
    expect(screen.queryByTestId('placeholder-preview-panel')).not.toBeInTheDocument();
  });

  it('Keeps the four geometry fields, undo and redo, and the backdrop controls for a shape', () => {
    render(<OverlayEditor value={buildShape()} onChange={vi.fn()} />);
    expect(field('Left').value).toBe('10');
    expect(field('Top').value).toBe('10');
    expect(field('Width').value).toBe('30');
    expect(field('Height').value).toBe('20');
    expect(screen.getByTestId('overlay-editor-undo')).toBeInTheDocument();
    expect(screen.getByTestId('overlay-editor-redo')).toBeInTheDocument();
    expect(screen.getByText('Backdrop')).toBeInTheDocument();
  });

  it('Emits a dragged Box as the same kind and colour, with no text keys', () => {
    const spy = vi.fn();
    render(<Controlled initial={buildShape()} spy={spy} />);

    act(() => {
      lastRndProps!.onDragStop({}, { x: 320, y: 135 });
    });

    expect(field('Left').value).toBe('40');
    const emitted = spy.mock.calls[0]![0] as Record<string, unknown>;
    expect(emitted).toMatchObject({ kind: 'Box', color: '#D32F2F80', normalizedX: 0.4, normalizedY: 0.3 });
    expect(Object.keys(emitted)).not.toContain('text');
    expect(Object.keys(emitted)).not.toContain('fontSizePx');
  });

  it('Emits a dragged Ellipse as an Ellipse of the same colour', () => {
    const spy = vi.fn();
    render(<Controlled initial={buildShape({ kind: 'Ellipse', color: '#00FF0040' })} spy={spy} />);

    act(() => {
      lastRndProps!.onDragStop({}, { x: 320, y: 135 });
    });

    expect(spy.mock.calls[0]![0]).toMatchObject({ kind: 'Ellipse', color: '#00FF0040', normalizedX: 0.4 });
  });

  it('Nudges a shape with ArrowRight by one fine step, keeping kind and colour', () => {
    const spy = vi.fn();
    render(<Controlled initial={buildShape({ kind: 'Ellipse', normalizedX: 0.4 })} spy={spy} />);

    fireEvent.keyDown(node(), { key: 'ArrowRight' });

    expect(spy).toHaveBeenCalledTimes(1);
    const emitted = spy.mock.calls[0]![0] as OverlayShape;
    expect(emitted.kind).toBe('Ellipse');
    expect(emitted.color).toBe('#D32F2F80');
    expect(emitted.normalizedX).toBeCloseTo(0.4 + FINE, 10);
    expect(emitted.normalizedY).toBe(0.1);
  });

  it('Undoes a nudge with Ctrl+Z, emitting the earlier shape of the same kind and colour', () => {
    const spy = vi.fn();
    render(<Controlled initial={buildShape({ kind: 'Ellipse', normalizedX: 0.4 })} spy={spy} />);

    fireEvent.keyDown(node(), { key: 'ArrowRight' });
    fireEvent.keyUp(node(), { key: 'ArrowRight' });
    pressUndo();

    expect(field('Left').value).toBe('40');
    const last = spy.mock.calls[spy.mock.calls.length - 1]![0] as OverlayShape;
    expect(last).toEqual(buildShape({ kind: 'Ellipse', normalizedX: 0.4 }));
    expect(screen.getByTestId('overlay-editor-undo-live-region')).toHaveTextContent('Undone');
  });

  it('Undoes a dragged Box back to its opened geometry', () => {
    render(<Controlled initial={buildShape()} />);

    act(() => {
      lastRndProps!.onDragStop({}, { x: 320, y: 135 });
    });
    expect(field('Left').value).toBe('40');
    fireEvent.click(screen.getByRole('button', { name: /^undo$/i }));

    expect(field('Left').value).toBe('10');
    expect(field('Top').value).toBe('10');
  });

  it('Commits a typed geometry field on a shape, keeping kind and colour', () => {
    const spy = vi.fn();
    render(<Controlled initial={buildShape()} spy={spy} />);

    fireEvent.change(field('Left'), { target: { value: '40' } });
    fireEvent.blur(field('Left'));

    expect(spy).toHaveBeenCalledTimes(1);
    expect(spy.mock.calls[0]![0]).toEqual(buildShape({ normalizedX: 0.4 }));
  });
});
