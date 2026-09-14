// @vitest-environment jsdom
import { useState } from 'react';
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { OverlayLabel } from '@smart-sentinel-eye/shared/api/overlays.api';
import { formatPercent, parsePercent, toPercentText } from './normalizedPercent.js';
import { OverlayGeometryFields } from './OverlayGeometryFields.js';

/**
 * Spec 151 (issue #2346) T001 — four percent-denominated fields for an
 * overlay label's position and size, live during drag, with validation. New
 * behaviour, RED (ADR-0139/ADR-0144): every assertion below is expected to
 * fail against the phase-4a scaffolds committed alongside this file —
 * `normalizedPercent.ts` returns a wrong value on purpose, `OverlayGeometryFields`
 * renders nothing at all — never on a missing export or a type error. T003
 * (`normalizedPercent.ts`), T004 (`OverlayGeometryFields.tsx`) and T005 (wiring
 * it into `OverlayEditor.tsx`) turn this file green in that order.
 *
 * <p>
 * <b>The float trap this file exists to catch</b> (spec.md §Precision):
 * `/100` and the correct `Math.round(percent * 100) / 10_000` disagree on
 * 2760 of the 10000 values on the 2dp percent grid — `0.07% -> 0.07 / 100 ===
 * 0.0007000000000000001`, not `0.0007` — the same class of defect spec 149's
 * phase-6 review found in `quantizeBoundFloor`. `24.87` does not happen to be
 * one of the 2760 (`24.87 / 100 === 0.2487` exactly), so it cannot carry this
 * assertion; `0.07` is used instead, below. The assertion that pins this uses
 * `toBe`, never `toBeCloseTo` — `toBeCloseTo` passes on every one of the 2760
 * (one of them, `0.35%`, even rounds *down* in the division form) and would
 * make this test useless for the one thing it is here to catch.
 * </p>
 *
 * <p>
 * <b>Why `react-rnd` is mocked for the "wires the live readout" section</b> —
 * exactly `OverlayEditorCharacterisation.test.tsx`'s reasoning: jsdom has no
 * layout engine, so a real drag reports 0×0 regardless of input. The mapping
 * under test (pixel drag delta → normalized preview → the Left field's text)
 * does not live in `react-rnd` at all; standing a controllable stub in for
 * `Rnd` isolates it, exactly as the characterisation guard already does for
 * `onDragStop`/`onResizeStop`.
 * </p>
 */

interface RndStubProps {
  size: { width: number; height: number };
  position: { x: number; y: number };
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

/** The field's editable text input, reached the way FR-017 says an operator does — by its label. */
function field(label: string): HTMLInputElement {
  return screen.getByLabelText(label) as HTMLInputElement;
}

/**
 * Feeds every `onCommit` field/value pair back into `value`, the way
 * `OverlayEditor.tsx`'s `handleGeometryCommit` does against the real dialog's
 * state (plan.md §3c). Needed for FR-011 (a later commit clearing a standing
 * error) and FR-012 (the advisory reacting to the value it was just shown).
 */
function ControlledFields({
  initial,
  onCommitSpy,
}: {
  initial: OverlayLabel;
  onCommitSpy: (field: string, normalized: number) => void;
}) {
  const [value, setValue] = useState(initial);
  return (
    <OverlayGeometryFields
      value={value}
      preview={null}
      onCommit={(next, normalized) => {
        setValue((prev) => ({ ...prev, [next]: normalized }));
        onCommitSpy(next, normalized);
      }}
    />
  );
}

describe('normalizedPercent (FR-016, spec.md §Precision)', () => {
  it('Parses "0.07" to exactly 0.0007 — not the 17-decimal /100 result (2760 of 10000 grid values disagree)', () => {
    // The trap, named: 0.07/100 is off-grid at 17dp. `24.87` is not one of the
    // 2760 grid values that discriminate the two forms (24.87/100 === 0.2487
    // exactly) and cannot carry this assertion — see the header comment.
    // toBe, never toBeCloseTo: toBeCloseTo passes on every one of the 2760,
    // including the one (0.35%) that rounds down instead of up.
    expect(0.07 / 100).not.toBe(0.0007);
    expect(parsePercent('0.07')).toBe(0.0007);
  });

  it('Parses "25" to exactly 0.25', () => {
    expect(parsePercent('25')).toBe(0.25);
  });

  it('Parses "0.5" to exactly 0.005 — the fine step, in percent', () => {
    expect(parsePercent('0.5')).toBe(0.005);
  });

  it('Parses "0.01" to exactly 0.0001 — the bottom of the 1e-4 grid', () => {
    expect(parsePercent('0.01')).toBe(0.0001);
  });

  it('Formats an off-grid stored value at grid resolution: 0.2487142 -> "24.87"', () => {
    expect(toPercentText(0.2487142)).toBe('24.87');
  });

  it('Formats 0.25 as "25", never "25.00"', () => {
    expect(toPercentText(0.25)).toBe('25');
  });

  it.each([0, 0.0001, 0.005, 0.25, 0.3333, 1])('Round-trips %s through toPercentText and back', (v) => {
    expect(parsePercent(toPercentText(v))).toBe(v);
  });

  it.each(['', '   ', 'abc', '0x10', '1e2', '--5', 'NaN', 'Infinity'])('Rejects %j as unparseable', (text) => {
    expect(parsePercent(text)).toBeNull();
  });

  it('formatPercent still returns "30%" for 0.3 and "0.5%" for 0.005 — pinned from the new module side too', () => {
    expect(formatPercent(0.3)).toBe('30%');
    expect(formatPercent(0.005)).toBe('0.5%');
  });
});

describe('OverlayGeometryFields (FR-001–FR-012, FR-017)', () => {
  afterEach(() => {
    cleanup();
  });

  it('Shows the label’s current geometry in percent, trailing zeros trimmed (FR-001, FR-002)', () => {
    render(
      <OverlayGeometryFields
        value={buildLabel({ normalizedX: 0.25, normalizedY: 0.05, normalizedWidth: 0.5, normalizedHeight: 0.1 })}
        preview={null}
        onCommit={vi.fn()}
      />,
    );

    expect(field('Left').value).toBe('25');
    expect(field('Top').value).toBe('5');
    expect(field('Width').value).toBe('50');
    expect(field('Height').value).toBe('10');
  });

  it('Displays an off-grid stored value at grid resolution, not the raw float', () => {
    render(<OverlayGeometryFields value={buildLabel({ normalizedX: 0.2487142 })} preview={null} onCommit={vi.fn()} />);

    expect(field('Left').value).toBe('24.87');
  });

  it('Renders every field as type="text" with inputMode="decimal", never type="number" (FR-003)', () => {
    render(<OverlayGeometryFields value={buildLabel()} preview={null} onCommit={vi.fn()} />);

    for (const label of ['Left', 'Top', 'Width', 'Height']) {
      const input = field(label);
      expect(input.type).toBe('text');
      expect(input.getAttribute('inputmode')).toBe('decimal');
    }
  });

  it('Commits an exact value on Enter, quantized to the grid, and calls onCommit once (FR-004, FR-005, FR-006)', () => {
    const onCommit = vi.fn();
    render(<OverlayGeometryFields value={buildLabel()} preview={null} onCommit={onCommit} />);

    fireEvent.change(field('Left'), { target: { value: '33.33' } });
    fireEvent.keyDown(field('Left'), { key: 'Enter' });

    expect(onCommit).toHaveBeenCalledTimes(1);
    expect(onCommit).toHaveBeenCalledWith('normalizedX', 0.3333);
    expect(field('Left').value).toBe('33.33');
  });

  it('Commits on blur exactly as Enter does (FR-004)', () => {
    const onCommit = vi.fn();
    render(<OverlayGeometryFields value={buildLabel()} preview={null} onCommit={onCommit} />);

    fireEvent.change(field('Top'), { target: { value: '40' } });
    fireEvent.blur(field('Top'));

    expect(onCommit).toHaveBeenCalledTimes(1);
    expect(onCommit).toHaveBeenCalledWith('normalizedY', 0.4);
  });

  it('Refuses a non-numeric draft with an alert, keeps it in the field, and calls onCommit not at all (FR-007, FR-010)', () => {
    const onCommit = vi.fn();
    render(<OverlayGeometryFields value={buildLabel({ normalizedWidth: 0.5 })} preview={null} onCommit={onCommit} />);

    fireEvent.change(field('Width'), { target: { value: 'abc' } });
    fireEvent.keyDown(field('Width'), { key: 'Enter' });

    expect(screen.getByRole('alert').textContent).toBe('Enter a number.');
    expect(field('Width').value).toBe('abc');
    expect(onCommit).not.toHaveBeenCalled();
  });

  it('Refuses a typed zero size with a message rather than flooring it (FR-009, #2361 from the guarded side)', () => {
    const onCommit = vi.fn();
    render(<OverlayGeometryFields value={buildLabel({ normalizedHeight: 0.2 })} preview={null} onCommit={onCommit} />);

    fireEvent.change(field('Height'), { target: { value: '0' } });
    fireEvent.keyDown(field('Height'), { key: 'Enter' });

    expect(screen.getByRole('alert').textContent).toBe('Height must be greater than 0% and at most 100%.');
    expect(onCommit).not.toHaveBeenCalled();
  });

  it('Refuses a position above the canvas (FR-008)', () => {
    const onCommit = vi.fn();
    render(<OverlayGeometryFields value={buildLabel()} preview={null} onCommit={onCommit} />);

    fireEvent.change(field('Top'), { target: { value: '150' } });
    fireEvent.keyDown(field('Top'), { key: 'Enter' });

    expect(screen.getByRole('alert').textContent).toBe('Top must be between 0% and 100%.');
    expect(onCommit).not.toHaveBeenCalled();
  });

  it('Refuses a negative position (FR-008)', () => {
    const onCommit = vi.fn();
    render(<OverlayGeometryFields value={buildLabel()} preview={null} onCommit={onCommit} />);

    fireEvent.change(field('Left'), { target: { value: '-5' } });
    fireEvent.keyDown(field('Left'), { key: 'Enter' });

    expect(screen.getByRole('alert').textContent).toBe('Left must be between 0% and 100%.');
    expect(onCommit).not.toHaveBeenCalled();
  });

  it('Escape discards a refused draft, restores the last committed value, and clears the message (FR-004, FR-011)', () => {
    const onCommit = vi.fn();
    render(<OverlayGeometryFields value={buildLabel({ normalizedX: 0.25 })} preview={null} onCommit={onCommit} />);

    fireEvent.change(field('Left'), { target: { value: '-5' } });
    fireEvent.keyDown(field('Left'), { key: 'Enter' });
    expect(screen.getByRole('alert')).toBeTruthy();

    fireEvent.keyDown(field('Left'), { key: 'Escape' });

    expect(field('Left').value).toBe('25');
    expect(screen.queryByRole('alert')).toBeNull();
    expect(onCommit).not.toHaveBeenCalled();
  });

  it('A successful commit clears a standing error on the same field (FR-011)', () => {
    render(<ControlledFields initial={buildLabel({ normalizedWidth: 0.5 })} onCommitSpy={vi.fn()} />);

    fireEvent.change(field('Width'), { target: { value: 'abc' } });
    fireEvent.keyDown(field('Width'), { key: 'Enter' });
    expect(screen.getByRole('alert')).toBeTruthy();

    fireEvent.change(field('Width'), { target: { value: '60' } });
    fireEvent.keyDown(field('Width'), { key: 'Enter' });

    expect(screen.queryByRole('alert')).toBeNull();
    expect(field('Width').value).toBe('60');
  });

  it('An off-edge rectangle is accepted, not refused, and shows a non-blocking status advisory (FR-012)', () => {
    const onCommitSpy = vi.fn();
    render(<ControlledFields initial={buildLabel({ normalizedWidth: 0.25 })} onCommitSpy={onCommitSpy} />);

    fireEvent.change(field('Left'), { target: { value: '90' } });
    fireEvent.keyDown(field('Left'), { key: 'Enter' });

    expect(onCommitSpy).toHaveBeenCalledWith('normalizedX', 0.9);
    expect(screen.queryByRole('alert')).toBeNull();
    expect(screen.getByRole('status').textContent).toContain('clipped');
  });

  it('The advisory clears once the label is committed back inside the canvas (FR-012)', () => {
    render(
      <ControlledFields initial={buildLabel({ normalizedX: 0.9, normalizedWidth: 0.25 })} onCommitSpy={vi.fn()} />,
    );
    expect(screen.getByRole('status').textContent).toContain('clipped');

    fireEvent.change(field('Left'), { target: { value: '50' } });
    fireEvent.keyDown(field('Left'), { key: 'Enter' });

    expect(screen.queryByRole('status')).toBeNull();
  });

  it('Each field has a visible label bound by htmlFor, describing itself only while a message is present (FR-017)', () => {
    render(<OverlayGeometryFields value={buildLabel()} preview={null} onCommit={vi.fn()} />);
    const left = field('Left');
    expect(left.getAttribute('aria-describedby')).toBeNull();

    fireEvent.change(left, { target: { value: 'abc' } });
    fireEvent.keyDown(left, { key: 'Enter' });

    const describedBy = left.getAttribute('aria-describedby');
    expect(describedBy).toBeTruthy();
    expect(document.getElementById(describedBy!)?.textContent).toBe('Enter a number.');
  });

  it('An untouched field tracks the live drag/resize preview rather than the committed value (FR-013)', () => {
    render(
      <OverlayGeometryFields
        value={buildLabel({ normalizedX: 0.25, normalizedY: 0.5, normalizedWidth: 0.25, normalizedHeight: 0.2 })}
        preview={{ x: 0.5, y: 0.5, width: 0.25, height: 0.2 }}
        onCommit={vi.fn()}
      />,
    );

    expect(field('Left').value).toBe('50');
  });
});

describe('OverlayEditor wires the live readout and the commit path (FR-006, FR-013, FR-014)', () => {
  afterEach(() => {
    cleanup();
    lastRndProps = null;
  });

  it('The Left field tracks a drag in progress on the default 800x450 canvas, and onChange is not called (FR-013, FR-014)', () => {
    const onChange = vi.fn();
    render(<OverlayEditor value={buildLabel()} onChange={onChange} />);

    act(() => {
      lastRndProps!.onDrag?.({}, { x: 400, y: 0 });
    });

    expect(field('Left').value).toBe('50');
    expect(onChange).not.toHaveBeenCalled();
  });

  it('Releasing the drag emits once through the existing onDragStop path, unchanged', () => {
    const onChange = vi.fn();
    render(<OverlayEditor value={buildLabel()} onChange={onChange} />);

    act(() => {
      lastRndProps!.onDrag?.({}, { x: 400, y: 0 });
      lastRndProps!.onDragStop({}, { x: 400, y: 0 });
    });

    expect(onChange).toHaveBeenCalledTimes(1);
    const next = onChange.mock.calls[0]![0] as OverlayLabel;
    expect(next.normalizedX).toBe(0.5);
    expect(field('Left').value).toBe('50');
  });

  it('Committing "33.33" into Left calls onChange once, leaving the other three geometry values, text and fontSizePx untouched (FR-006)', () => {
    const onChange = vi.fn();
    render(<OverlayEditor value={buildLabel()} onChange={onChange} />);

    fireEvent.change(field('Left'), { target: { value: '33.33' } });
    fireEvent.keyDown(field('Left'), { key: 'Enter' });

    expect(onChange).toHaveBeenCalledTimes(1);
    const next = onChange.mock.calls[0]![0] as OverlayLabel;
    expect(next.normalizedX).toBe(0.3333);
    expect(next.normalizedY).toBe(BASE_LABEL.normalizedY);
    expect(next.normalizedWidth).toBe(BASE_LABEL.normalizedWidth);
    expect(next.normalizedHeight).toBe(BASE_LABEL.normalizedHeight);
    expect(next.text).toBe(BASE_LABEL.text);
    expect(next.fontSizePx).toBe(BASE_LABEL.fontSizePx);
  });
});
