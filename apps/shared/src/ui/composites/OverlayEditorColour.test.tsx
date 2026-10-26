// @vitest-environment jsdom
import { useState } from 'react';
import type { CSSProperties, KeyboardEvent as ReactKeyboardEvent, ReactNode } from 'react';
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { OverlayLabel, OverlayShape } from '@smart-sentinel-eye/shared/api/overlays.api';
import { overlayLabelSurfaceStyle, overlayShapeStyle } from './overlayLabelStyle.js';
import type { OverlayEditorValue } from './useOverlayEditHistory.js';

/**
 * Spec 463 (issue #3108) T042, plan C12. New behaviour, RED (ADR-0139): `OverlayEditor` renders
 * the colour control for the selected element; choosing a swatch changes the preview exactly as
 * the wall paints it, and is one undo step, for a Text and for a Box. Today the editor has no
 * colour control, so every case fails on the missing swatch. `react-rnd` is stubbed as in
 * `OverlayEditorShape.test.tsx`.
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

function label(): OverlayLabel {
  return {
    kind: 'Text',
    color: '#9F9F9FD9',
    text: 'T1',
    normalizedX: 0.1,
    normalizedY: 0.1,
    normalizedWidth: 0.3,
    normalizedHeight: 0.1,
    fontSizePx: 32,
  };
}

function box(): OverlayShape {
  return {
    kind: 'Box',
    color: '#D32F2F80',
    normalizedX: 0.1,
    normalizedY: 0.1,
    normalizedWidth: 0.3,
    normalizedHeight: 0.2,
  };
}

function Controlled({ initial, spy }: { initial: OverlayEditorValue; spy?: (next: OverlayEditorValue) => void }) {
  const [value, setValue] = useState(initial);
  return (
    <OverlayEditor
      value={value}
      onChange={(next) => {
        setValue(next);
        spy?.(next);
      }}
    />
  );
}

function pressUndo(): void {
  fireEvent.keyDown(screen.getByTestId('overlay-editor-label'), { key: 'z', ctrlKey: true });
}

function hexField(): HTMLInputElement {
  return screen.getByLabelText('Colour', { exact: true }) as HTMLInputElement;
}

afterEach(() => {
  cleanup();
  lastRndProps = null;
});

describe('OverlayEditor colour control', () => {
  it('Previews a label at the chosen swatch exactly as overlayLabelSurfaceStyle paints it', () => {
    render(<Controlled initial={label()} />);

    fireEvent.click(screen.getByRole('button', { name: 'cyan-700' }));

    expect(lastRndProps!.style).toMatchObject(overlayLabelSurfaceStyle({ fontSizePx: 32, color: '#0F707DFF' }));
  });

  it('Shows the chosen swatch hex in the hex field', () => {
    render(<Controlled initial={label()} />);

    fireEvent.click(screen.getByRole('button', { name: 'cyan-700' }));

    expect(hexField().value).toBe('#0F707DFF');
  });

  it('Emits the new colour through onChange once', () => {
    const emitted: OverlayEditorValue[] = [];
    render(<Controlled initial={label()} spy={(next) => emitted.push(next)} />);

    fireEvent.click(screen.getByRole('button', { name: 'cyan-700' }));

    expect(emitted).toHaveLength(1);
    expect(emitted[0]!.color).toBe('#0F707DFF');
  });

  it('Undoes a label colour change in one step', () => {
    render(<Controlled initial={label()} />);

    fireEvent.click(screen.getByRole('button', { name: 'cyan-700' }));
    pressUndo();

    expect(hexField().value).toBe('#9F9F9FD9');
    expect(lastRndProps!.style).toMatchObject(overlayLabelSurfaceStyle({ fontSizePx: 32, color: '#9F9F9FD9' }));
  });

  it('Previews a Box stroke at the chosen swatch exactly as overlayShapeStyle paints it', () => {
    render(<Controlled initial={box()} />);

    fireEvent.click(screen.getByRole('button', { name: 'gray-500' }));

    expect(lastRndProps!.style).toMatchObject(overlayShapeStyle({ kind: 'Box', color: '#818A98FF' }));
  });

  it('Undoes a Box colour change in one step', () => {
    render(<Controlled initial={box()} />);

    fireEvent.click(screen.getByRole('button', { name: 'gray-500' }));
    pressUndo();

    expect(hexField().value).toBe('#D32F2F80');
    expect(lastRndProps!.style).toMatchObject(overlayShapeStyle({ kind: 'Box', color: '#D32F2F80' }));
  });

  it('Refuses a too-bright hex in the editor without emitting', () => {
    const emitted: OverlayEditorValue[] = [];
    render(<Controlled initial={label()} spy={(next) => emitted.push(next)} />);

    fireEvent.change(hexField(), { target: { value: '#FFFFFF' } });
    fireEvent.keyDown(hexField(), { key: 'Enter' });

    expect(emitted).toHaveLength(0);
    expect(within(screen.getByRole('group', { name: 'Colour' })).getByRole('alert').textContent).toMatch(
      /brighter than the wall allows/i,
    );
  });

  it('Disables the colour controls when the editor is read-only', () => {
    render(<OverlayEditor value={label()} onChange={() => {}} readOnly />);

    expect(hexField().readOnly).toBe(true);
    expect((screen.getByRole('button', { name: 'cyan-700' }) as HTMLButtonElement).disabled).toBe(true);
  });
});
