// @vitest-environment jsdom
import { useState } from 'react';
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { OverlayLabel } from '@smart-sentinel-eye/shared/api/overlays.api';
import { OverlayEditor } from './OverlayEditor.js';

/**
 * Spec 149 (issue #2344) — an overlay label reachable and operable by
 * keyboard alone (WCAG 2.2 SC 2.1.1, Level A). New behaviour, RED
 * (ADR-0139/ADR-0144): every test here is expected to fail on a missing
 * control (`overlay-editor-label` does not exist, `role="application"` is
 * absent, no `onKeyDown`…), not on an import or a compile error.
 *
 * <p>
 * <b>The real `react-rnd` is used, not the characterisation guard's stub.</b>
 * `OverlayEditorCharacterisation.test.tsx` mocks `react-rnd` as
 * `Rnd: (props) => props.children` — a stub that renders no element at all.
 * A keyboard test copying that mock would be testing the stub. Keyboard
 * handling reads no geometry from the DOM — it reads `value` from props and
 * arithmetic from constants — so jsdom's all-zero `getBoundingClientRect`
 * (the reason spec 147 stubbed `Rnd` in the first place) does not matter
 * here, exactly as `OverlayLabelParity.test.tsx` already proves by mounting
 * the real component cleanly.
 * </p>
 *
 * <p>
 * <b>What jsdom cannot prove</b> — the ring's contrast over a real backdrop,
 * that `bounds="parent"` and the reachable-region bound in code actually
 * agree, that `role="application"` makes NVDA/JAWS pass arrows through, and
 * that `Ctrl`+arrow is free of real browser/Radix conflicts — is phase 5
 * only (spec.md §Independent end-to-end test procedure, tasks.md T011). No
 * test here stands in for those.
 * </p>
 */

const FINE = 0.005;
const COARSE = 0.05;

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

function getLabel(): HTMLElement {
  return screen.getByTestId('overlay-editor-label');
}

/**
 * The geometry live region an operator (or NVDA) would actually hear for a
 * keyboard move/resize. `PlaceholderPreviewPanel` renders its own
 * `aria-live="polite"` region (the text-resolution status), so a bare
 * `[aria-live="polite"]` query is ambiguous between the two and would
 * silently retarget onto whichever happens to come first in the DOM (phase 6
 * should-fix 6) — `data-testid` picks this one specifically, and the
 * accessibility contract (`aria-live="polite"`) is still asserted on it.
 */
function getLiveRegion(): HTMLElement {
  const region = screen.getByTestId('overlay-editor-geometry-live-region');
  expect(region.getAttribute('aria-live')).toBe('polite');
  return region;
}

/**
 * Feeds every `onChange` payload back in as the next `value`, the way a real
 * operator's repeated keypresses do against the real dialog's state. Needed
 * for FR-009 (drift over many presses) and FR-015 (the announcement names
 * where the label actually ended up, not a single delta from a frozen prop).
 */
function ControlledOverlayEditor({
  initial,
  onChangeSpy,
}: {
  initial: OverlayLabel;
  onChangeSpy: (next: OverlayLabel) => void;
}) {
  const [value, setValue] = useState(initial);
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

describe('OverlayEditor keyboard operability (spec 149)', () => {
  afterEach(() => {
    cleanup();
  });

  describe('focusability (FR-001)', () => {
    it('Is a focus stop reachable by Tab, ahead of the text input in DOM order', () => {
      render(<OverlayEditor value={buildLabel()} onChange={vi.fn()} />);

      const label = getLabel();
      const textInput = screen.getByTestId('overlay-editor-text');

      expect(label.tabIndex).toBe(0);
      // The label must precede the text input in DOM order — compareDocumentPosition
      // reports bit Node.DOCUMENT_POSITION_FOLLOWING when the argument comes after
      // the node it is called on.
      expect(label.compareDocumentPosition(textInput) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();

      label.focus();
      expect(document.activeElement).toBe(label);
    });
  });

  describe('arrow, Shift and Ctrl move and resize (FR-004, FR-005, FR-006, FR-010)', () => {
    interface Combo {
      description: string;
      key: string;
      shiftKey: boolean;
      ctrlKey: boolean;
      axis: 'normalizedX' | 'normalizedY' | 'normalizedWidth' | 'normalizedHeight';
      delta: number;
    }

    // The sixteen arrow × modifier payloads the keyboard map (spec.md §The
    // keyboard map) defines. A base position clear of every clamp (x=y=0.4,
    // width=height=0.2) so none of these sixteen presses is also exercising
    // FR-007/FR-008 — those get their own tests below.
    const COMBOS: Combo[] = [
      {
        description: 'ArrowLeft moves x by -fine',
        key: 'ArrowLeft',
        shiftKey: false,
        ctrlKey: false,
        axis: 'normalizedX',
        delta: -FINE,
      },
      {
        description: 'ArrowRight moves x by +fine',
        key: 'ArrowRight',
        shiftKey: false,
        ctrlKey: false,
        axis: 'normalizedX',
        delta: FINE,
      },
      {
        description: 'ArrowUp moves y by -fine',
        key: 'ArrowUp',
        shiftKey: false,
        ctrlKey: false,
        axis: 'normalizedY',
        delta: -FINE,
      },
      {
        description: 'ArrowDown moves y by +fine',
        key: 'ArrowDown',
        shiftKey: false,
        ctrlKey: false,
        axis: 'normalizedY',
        delta: FINE,
      },
      {
        description: 'Shift+ArrowLeft moves x by -coarse',
        key: 'ArrowLeft',
        shiftKey: true,
        ctrlKey: false,
        axis: 'normalizedX',
        delta: -COARSE,
      },
      {
        description: 'Shift+ArrowRight moves x by +coarse',
        key: 'ArrowRight',
        shiftKey: true,
        ctrlKey: false,
        axis: 'normalizedX',
        delta: COARSE,
      },
      {
        description: 'Shift+ArrowUp moves y by -coarse',
        key: 'ArrowUp',
        shiftKey: true,
        ctrlKey: false,
        axis: 'normalizedY',
        delta: -COARSE,
      },
      {
        description: 'Shift+ArrowDown moves y by +coarse',
        key: 'ArrowDown',
        shiftKey: true,
        ctrlKey: false,
        axis: 'normalizedY',
        delta: COARSE,
      },
      {
        description: 'Ctrl+ArrowLeft shrinks width by fine, top-left anchored',
        key: 'ArrowLeft',
        shiftKey: false,
        ctrlKey: true,
        axis: 'normalizedWidth',
        delta: -FINE,
      },
      {
        description: 'Ctrl+ArrowRight grows width by fine, top-left anchored',
        key: 'ArrowRight',
        shiftKey: false,
        ctrlKey: true,
        axis: 'normalizedWidth',
        delta: FINE,
      },
      {
        description: 'Ctrl+ArrowUp shrinks height by fine, top-left anchored',
        key: 'ArrowUp',
        shiftKey: false,
        ctrlKey: true,
        axis: 'normalizedHeight',
        delta: -FINE,
      },
      {
        description: 'Ctrl+ArrowDown grows height by fine, top-left anchored',
        key: 'ArrowDown',
        shiftKey: false,
        ctrlKey: true,
        axis: 'normalizedHeight',
        delta: FINE,
      },
      {
        description: 'Ctrl+Shift+ArrowLeft shrinks width by coarse',
        key: 'ArrowLeft',
        shiftKey: true,
        ctrlKey: true,
        axis: 'normalizedWidth',
        delta: -COARSE,
      },
      {
        description: 'Ctrl+Shift+ArrowRight grows width by coarse',
        key: 'ArrowRight',
        shiftKey: true,
        ctrlKey: true,
        axis: 'normalizedWidth',
        delta: COARSE,
      },
      {
        description: 'Ctrl+Shift+ArrowUp shrinks height by coarse',
        key: 'ArrowUp',
        shiftKey: true,
        ctrlKey: true,
        axis: 'normalizedHeight',
        delta: -COARSE,
      },
      {
        description: 'Ctrl+Shift+ArrowDown grows height by coarse',
        key: 'ArrowDown',
        shiftKey: true,
        ctrlKey: true,
        axis: 'normalizedHeight',
        delta: COARSE,
      },
    ];

    it.each(COMBOS)('$description', ({ key, shiftKey, ctrlKey, axis, delta }) => {
      const onChange = vi.fn();
      const base = buildLabel({ normalizedX: 0.4, normalizedY: 0.4, normalizedWidth: 0.2, normalizedHeight: 0.2 });
      render(<OverlayEditor value={base} onChange={onChange} />);

      fireEvent.keyDown(getLabel(), { key, shiftKey, ctrlKey });

      expect(onChange).toHaveBeenCalledTimes(1);
      const next = onChange.mock.calls[0]![0] as OverlayLabel;

      expect(next.normalizedX).toBeCloseTo(axis === 'normalizedX' ? base.normalizedX + delta : base.normalizedX, 10);
      expect(next.normalizedY).toBeCloseTo(axis === 'normalizedY' ? base.normalizedY + delta : base.normalizedY, 10);
      expect(next.normalizedWidth).toBeCloseTo(
        axis === 'normalizedWidth' ? base.normalizedWidth + delta : base.normalizedWidth,
        10,
      );
      expect(next.normalizedHeight).toBeCloseTo(
        axis === 'normalizedHeight' ? base.normalizedHeight + delta : base.normalizedHeight,
        10,
      );
      // FR-010 — nothing but geometry changes.
      expect(next.text).toBe(base.text);
      expect(next.fontSizePx).toBe(base.fontSizePx);
    });

    it('Shift coarsens the step to exactly ten times the fine step, not merely a bigger number (FR-005)', () => {
      const fine = vi.fn();
      render(<OverlayEditor value={buildLabel({ normalizedY: 0.4 })} onChange={fine} />);
      fireEvent.keyDown(getLabel(), { key: 'ArrowDown' });
      cleanup();

      const coarse = vi.fn();
      render(<OverlayEditor value={buildLabel({ normalizedY: 0.4 })} onChange={coarse} />);
      fireEvent.keyDown(getLabel(), { key: 'ArrowDown', shiftKey: true });

      const fineDelta = (fine.mock.calls[0]![0] as OverlayLabel).normalizedY - 0.4;
      const coarseDelta = (coarse.mock.calls[0]![0] as OverlayLabel).normalizedY - 0.4;

      expect(coarseDelta).toBeCloseTo(fineDelta * 10, 10);
    });
  });

  describe('the reachable region matches bounds="parent" (FR-007)', () => {
    it('Refuses to move left of the left edge', () => {
      const onChange = vi.fn();
      render(
        <OverlayEditor
          value={buildLabel({ normalizedX: 0, normalizedY: 0.3, normalizedWidth: 0.2, normalizedHeight: 0.2 })}
          onChange={onChange}
        />,
      );

      fireEvent.keyDown(getLabel(), { key: 'ArrowLeft' });

      expect(onChange).toHaveBeenCalledTimes(1);
      expect((onChange.mock.calls[0]![0] as OverlayLabel).normalizedX).toBe(0);
    });

    it('Refuses to move above the top edge', () => {
      const onChange = vi.fn();
      render(
        <OverlayEditor
          value={buildLabel({ normalizedX: 0.3, normalizedY: 0, normalizedWidth: 0.2, normalizedHeight: 0.2 })}
          onChange={onChange}
        />,
      );

      fireEvent.keyDown(getLabel(), { key: 'ArrowUp' });

      expect(onChange).toHaveBeenCalledTimes(1);
      expect((onChange.mock.calls[0]![0] as OverlayLabel).normalizedY).toBe(0);
    });

    it('Refuses to move right of the reachable region (x = 1 - width), not clamp01’s 1', () => {
      const onChange = vi.fn();
      render(
        <OverlayEditor
          value={buildLabel({ normalizedX: 0.8, normalizedY: 0.3, normalizedWidth: 0.2, normalizedHeight: 0.2 })}
          onChange={onChange}
        />,
      );

      fireEvent.keyDown(getLabel(), { key: 'ArrowRight' });

      expect(onChange).toHaveBeenCalledTimes(1);
      expect((onChange.mock.calls[0]![0] as OverlayLabel).normalizedX).toBe(0.8);
    });

    it('Refuses to move below the reachable region (y = 1 - height), not clamp01’s 1', () => {
      const onChange = vi.fn();
      render(
        <OverlayEditor
          value={buildLabel({ normalizedX: 0.3, normalizedY: 0.8, normalizedWidth: 0.2, normalizedHeight: 0.2 })}
          onChange={onChange}
        />,
      );

      fireEvent.keyDown(getLabel(), { key: 'ArrowDown' });

      expect(onChange).toHaveBeenCalledTimes(1);
      expect((onChange.mock.calls[0]![0] as OverlayLabel).normalizedY).toBe(0.8);
    });

    it('Stops growth at the canvas edge, not at clamp01’s 1 — 0.19 + 0.05 clamps to 0.2', () => {
      const onChange = vi.fn();
      render(
        <OverlayEditor
          value={buildLabel({ normalizedX: 0.8, normalizedY: 0.3, normalizedWidth: 0.19, normalizedHeight: 0.2 })}
          onChange={onChange}
        />,
      );

      fireEvent.keyDown(getLabel(), { key: 'ArrowRight', ctrlKey: true, shiftKey: true });

      expect(onChange).toHaveBeenCalledTimes(1);
      const next = onChange.mock.calls[0]![0] as OverlayLabel;
      expect(next.normalizedWidth).toBeCloseTo(0.2, 10);
      expect(next.normalizedX).toBe(0.8);
    });
  });

  describe('the keyboard resize floor (FR-008)', () => {
    it('Floors width at 0.005, not clamp01’s 0 — the server refuses a zero-width label', () => {
      const onChange = vi.fn();
      render(<OverlayEditor value={buildLabel({ normalizedWidth: 0.005 })} onChange={onChange} />);

      fireEvent.keyDown(getLabel(), { key: 'ArrowLeft', ctrlKey: true });

      expect(onChange).toHaveBeenCalledTimes(1);
      expect((onChange.mock.calls[0]![0] as OverlayLabel).normalizedWidth).toBe(0.005);
    });

    it('Floors height at 0.005, not clamp01’s 0', () => {
      const onChange = vi.fn();
      render(<OverlayEditor value={buildLabel({ normalizedHeight: 0.005 })} onChange={onChange} />);

      fireEvent.keyDown(getLabel(), { key: 'ArrowUp', ctrlKey: true });

      expect(onChange).toHaveBeenCalledTimes(1);
      expect((onChange.mock.calls[0]![0] as OverlayLabel).normalizedHeight).toBe(0.005);
    });
  });

  describe('a small but domain-valid size is untouched by an unrelated nudge (#2361 guard)', () => {
    // The critical "a monotone floor is wrong" guard (spec.md FR-002):
    // `emitNormalized` re-clamps all four values on every keypress, including
    // axes the operator did not touch. A width of 0.003 is below
    // `MIN_NORMALIZED_SIZE` (0.005) but domain-valid (> 0) — spec 151 FR-009
    // lets an operator type it. Nudging x must not silently rewrite it. Must
    // stay green on both current code (clamp01 never floors a positive
    // value) and the fixed code (a non-monotone clampSize); T005's
    // counterfactual (a monotone Math.max(v, 0.005)) must turn it red.
    it('ArrowRight on x leaves an untouched width of 0.003 unchanged, not floored to 0.005', () => {
      const onChange = vi.fn();
      render(<OverlayEditor value={buildLabel({ normalizedX: 0.4, normalizedWidth: 0.003 })} onChange={onChange} />);

      fireEvent.keyDown(getLabel(), { key: 'ArrowRight' });

      expect(onChange).toHaveBeenCalledTimes(1);
      const next = onChange.mock.calls[0]![0] as OverlayLabel;
      expect(next.normalizedWidth).toBe(0.003);
    });
  });

  describe('the 1e-4 quantum holds under repeated presses (FR-009)', () => {
    it('Reaches the clamp boundary exactly after a hundred and sixty fine presses', () => {
      const onChange = vi.fn();
      render(
        <ControlledOverlayEditor
          initial={buildLabel({ normalizedX: 0, normalizedWidth: 0.2 })}
          onChangeSpy={onChange}
        />,
      );
      const label = getLabel();

      for (let i = 0; i < 160; i += 1) {
        fireEvent.keyDown(label, { key: 'ArrowRight' });
      }

      expect(onChange).toHaveBeenCalledTimes(160);
      const finalX = (onChange.mock.calls[159]![0] as OverlayLabel).normalizedX;
      expect(finalX).toBe(0.8);

      for (const call of onChange.mock.calls) {
        const x = (call[0] as OverlayLabel).normalizedX;
        const scaled = x * 10_000;
        expect(Math.abs(scaled - Math.round(scaled))).toBeLessThan(1e-6);
      }
    });

    // Phase 6 finding 1: `width: 0.2` gives a bound (`1 - 0.2 === 0.8`) that
    // happens to be exactly representable, so it passed even when the bound
    // itself was never quantized. `width: 0.7` gives a bound
    // (`1 - 0.7 === 0.30000000000000004`) that is not, and the quantum was
    // silently defeated by it at exactly the boundary the quantum exists for
    // — a held ArrowRight from x = 0.25 used to latch on a 17-decimal value
    // forever instead of stopping at 0.3.
    it('Reaches the clamp boundary exactly at an off-grid bound (width 0.7, bound 0.3)', () => {
      const onChange = vi.fn();
      render(
        <ControlledOverlayEditor
          initial={buildLabel({ normalizedX: 0.25, normalizedWidth: 0.7 })}
          onChangeSpy={onChange}
        />,
      );
      const label = getLabel();

      for (let i = 0; i < 160; i += 1) {
        fireEvent.keyDown(label, { key: 'ArrowRight' });
      }

      expect(onChange).toHaveBeenCalledTimes(160);
      const finalX = (onChange.mock.calls[159]![0] as OverlayLabel).normalizedX;
      expect(finalX).toBe(0.3);

      for (const call of onChange.mock.calls) {
        const x = (call[0] as OverlayLabel).normalizedX;
        const scaled = x * 10_000;
        expect(Math.abs(scaled - Math.round(scaled))).toBeLessThan(1e-6);
      }
    });
  });

  describe('an already off-canvas label moves only toward the canvas (FR-007, finding 2b)', () => {
    // NormalizedPosition.cs bounds x/y to [0, 1] independently and permits
    // x + width > 1 — the domain's own off-edge rectangle, not reachable by
    // drag but exactly what #2346's typed entry will produce routinely. A
    // press pointing further out must be refused, not snapped onto the
    // bound: snapping onto the bound is how ArrowRight used to move the
    // label 40% of the canvas to the *left*.

    it('ArrowRight refuses to grow the excursion — no leftward snap to the bound', () => {
      const onChange = vi.fn();
      render(<OverlayEditor value={buildLabel({ normalizedX: 0.9, normalizedWidth: 0.5 })} onChange={onChange} />);

      fireEvent.keyDown(getLabel(), { key: 'ArrowRight' });

      expect(onChange).toHaveBeenCalledTimes(1);
      expect((onChange.mock.calls[0]![0] as OverlayLabel).normalizedX).toBe(0.9);
    });

    it('ArrowLeft moves the label toward the canvas by exactly one fine step', () => {
      const onChange = vi.fn();
      render(<OverlayEditor value={buildLabel({ normalizedX: 0.9, normalizedWidth: 0.5 })} onChange={onChange} />);

      fireEvent.keyDown(getLabel(), { key: 'ArrowLeft' });

      expect(onChange).toHaveBeenCalledTimes(1);
      expect((onChange.mock.calls[0]![0] as OverlayLabel).normalizedX).toBeCloseTo(0.9 - FINE, 10);
    });

    it('Ctrl+ArrowRight refuses to grow the excursion — no 80% shrink-then-jump', () => {
      const onChange = vi.fn();
      render(<OverlayEditor value={buildLabel({ normalizedX: 0.9, normalizedWidth: 0.5 })} onChange={onChange} />);

      fireEvent.keyDown(getLabel(), { key: 'ArrowRight', ctrlKey: true });

      expect(onChange).toHaveBeenCalledTimes(1);
      expect((onChange.mock.calls[0]![0] as OverlayLabel).normalizedWidth).toBe(0.5);
    });

    it('Ctrl+ArrowLeft shrinks the label toward the canvas by exactly one fine step', () => {
      const onChange = vi.fn();
      render(<OverlayEditor value={buildLabel({ normalizedX: 0.9, normalizedWidth: 0.5 })} onChange={onChange} />);

      fireEvent.keyDown(getLabel(), { key: 'ArrowLeft', ctrlKey: true });

      expect(onChange).toHaveBeenCalledTimes(1);
      expect((onChange.mock.calls[0]![0] as OverlayLabel).normalizedWidth).toBeCloseTo(0.5 - FINE, 10);
    });
  });

  describe('unhandled keys are left to the browser (FR-011 / bad request)', () => {
    it.each(['Tab', 'Escape', 'Enter', 'a'])('%s fires no onChange and is not preventDefault-ed', (key) => {
      const onChange = vi.fn();
      render(<OverlayEditor value={buildLabel()} onChange={onChange} />);

      const notPrevented = fireEvent.keyDown(getLabel(), { key });

      expect(onChange).not.toHaveBeenCalled();
      expect(notPrevented).toBe(true);
    });

    it('Alt+ArrowLeft fires no onChange and is not preventDefault-ed (it stays browser Back)', () => {
      const onChange = vi.fn();
      render(<OverlayEditor value={buildLabel()} onChange={onChange} />);

      const notPrevented = fireEvent.keyDown(getLabel(), { key: 'ArrowLeft', altKey: true });

      expect(onChange).not.toHaveBeenCalled();
      expect(notPrevented).toBe(true);
    });

    it('ArrowRight is handled and is preventDefault-ed', () => {
      render(<OverlayEditor value={buildLabel()} onChange={vi.fn()} />);

      const notPrevented = fireEvent.keyDown(getLabel(), { key: 'ArrowRight' });

      expect(notPrevented).toBe(false);
    });

    it('Ctrl+ArrowRight is handled and is preventDefault-ed', () => {
      render(<OverlayEditor value={buildLabel()} onChange={vi.fn()} />);

      const notPrevented = fireEvent.keyDown(getLabel(), { key: 'ArrowRight', ctrlKey: true });

      expect(notPrevented).toBe(false);
    });
  });

  describe('accessible name and role (FR-012, FR-013)', () => {
    it('Carries aria-roledescription="Overlay label" and names itself by its text', () => {
      render(<OverlayEditor value={buildLabel({ text: 'Line-1 Inlet' })} onChange={vi.fn()} />);

      const label = getLabel();
      expect(label.getAttribute('aria-roledescription')).toBe('Overlay label');
      expect(label.getAttribute('aria-label')).toContain('Line-1 Inlet');
    });

    it('Falls back to a fixed, non-empty accessible name when the text is empty', () => {
      render(<OverlayEditor value={buildLabel({ text: '' })} onChange={vi.fn()} />);

      const name = getLabel().getAttribute('aria-label');
      expect(name).toBeTruthy();
      expect(name).not.toBe('');
    });

    it('Carries role="application", scoped to the label, so arrow keys reach the handler', () => {
      render(<OverlayEditor value={buildLabel()} onChange={vi.fn()} />);

      expect(getLabel().getAttribute('role')).toBe('application');
    });
  });

  describe('key-map instructions (FR-014)', () => {
    it('Describes the key map via aria-describedby, resolving to an element that actually exists', () => {
      render(<OverlayEditor value={buildLabel()} onChange={vi.fn()} />);

      const describedBy = getLabel().getAttribute('aria-describedby');
      expect(describedBy).toBeTruthy();

      const description = document.getElementById(describedBy!);
      expect(description).not.toBeNull();
      expect(description!.textContent).not.toBe('');
    });
  });

  describe('the debounced live-region announcement (FR-015, FR-016, FR-017)', () => {
    beforeEach(() => {
      vi.useFakeTimers();
    });

    afterEach(() => {
      vi.useRealTimers();
    });

    it('Is empty at rest, and stays empty after a focus with no keypress (FR-017)', () => {
      render(<OverlayEditor value={buildLabel()} onChange={vi.fn()} />);

      expect(getLiveRegion().textContent).toBe('');

      fireEvent.focus(getLabel());
      expect(getLiveRegion().textContent).toBe('');
    });

    it('Announces exactly once, 500 ms after the last of ten presses within the burst', async () => {
      const onChange = vi.fn();
      render(<ControlledOverlayEditor initial={buildLabel({ normalizedX: 0.25 })} onChangeSpy={onChange} />);
      const label = getLabel();

      for (let i = 0; i < 10; i += 1) {
        fireEvent.keyDown(label, { key: 'ArrowRight' });
      }

      expect(onChange).toHaveBeenCalledTimes(10);
      expect(getLiveRegion().textContent).toBe('');

      await act(async () => {
        await vi.advanceTimersByTimeAsync(500);
      });

      // 0.25 + 10 * 0.005 = 0.3 = "30%".
      expect(getLiveRegion().textContent).toBe('Left 30%');
    });

    it('Announces the refused edge when a press is clamped by FR-007 (left edge)', async () => {
      render(
        <OverlayEditor
          value={buildLabel({ normalizedX: 0, normalizedY: 0.3, normalizedWidth: 0.2, normalizedHeight: 0.2 })}
          onChange={vi.fn()}
        />,
      );

      fireEvent.keyDown(getLabel(), { key: 'ArrowLeft' });

      await act(async () => {
        await vi.advanceTimersByTimeAsync(500);
      });

      expect(getLiveRegion().textContent).toBe('Left 0%, at the left edge');
    });

    it('Announces the refused minimum when a resize is floored by FR-008', async () => {
      render(<OverlayEditor value={buildLabel({ normalizedWidth: 0.005 })} onChange={vi.fn()} />);

      fireEvent.keyDown(getLabel(), { key: 'ArrowLeft', ctrlKey: true });

      await act(async () => {
        await vi.advanceTimersByTimeAsync(500);
      });

      expect(getLiveRegion().textContent).toBe('Width 0.5%, at the minimum');
    });
  });

  describe('the focus ring never becomes a border (FR-002, FR-003)', () => {
    it('Has no outline, boxShadow or border at rest', () => {
      render(<OverlayEditor value={buildLabel()} onChange={vi.fn()} />);

      const label = getLabel();
      expect(label.style.outline).toBe('');
      expect(label.style.boxShadow).toBe('');
      expect(label.style.border).toBe('');
    });

    it('Gains an outline and a boxShadow on focus, and never sets border', () => {
      render(<OverlayEditor value={buildLabel()} onChange={vi.fn()} />);

      const label = getLabel();
      fireEvent.focus(label);

      expect(label.style.outline).not.toBe('');
      expect(label.style.boxShadow).not.toBe('');
      expect(label.style.border).toBe('');
    });

    it('Loses the outline and boxShadow again on blur', () => {
      render(<OverlayEditor value={buildLabel()} onChange={vi.fn()} />);

      const label = getLabel();
      fireEvent.focus(label);
      fireEvent.blur(label);

      expect(label.style.outline).toBe('');
      expect(label.style.boxShadow).toBe('');
    });
  });

  describe('no Redux is required (FR-018)', () => {
    it('Renders with no Provider in the tree, exactly as the three existing guards do', () => {
      expect(() => render(<OverlayEditor value={buildLabel()} onChange={vi.fn()} />)).not.toThrow();
    });
  });
});
