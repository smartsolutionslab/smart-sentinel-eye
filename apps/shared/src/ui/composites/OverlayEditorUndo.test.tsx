// @vitest-environment jsdom
import { useState } from 'react';
import type { CSSProperties, KeyboardEvent as ReactKeyboardEvent, ReactNode } from 'react';
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { OverlayLabel } from '@smart-sentinel-eye/shared/api/overlays.api';
import type { ResolvedTextPreview } from '@smart-sentinel-eye/shared/api/systemVariables.api';
import { TEXT_IDLE_MS } from './useOverlayEditHistory.js';

/**
 * Spec 154 (issue #2347) T005-T009 — step-wise undo/redo over one overlay-
 * editor session, rendered bare with no `<Provider>` (Decision 4, spec 150
 * FR-018), exactly as the five existing `OverlayEditor*`/`OverlayLabel*`
 * suites do. New behaviour, RED (ADR-0139/ADR-0144): `OverlayEditor.tsx` is
 * entirely unmodified by this commit — `useOverlayEditHistory.ts` lands
 * beside this file as a phase-4a scaffold that does nothing (see its own
 * doc comment) — so every failure below is either "no Undo/Redo control
 * exists" or "Ctrl+Z did nothing", never an import or a type error.
 * T011-T016 (phase 4b) wire the hook into `OverlayEditor.tsx`; per
 * ADR-0144 the engineer may not edit this file to make it pass.
 *
 * <p>
 * <b>Why `react-rnd` is stubbed here, and why the stub differs from both
 * existing ones.</b> `OverlayEditorCharacterisation.test.tsx`'s stub
 * (`Rnd: (props) => props.children`) renders no DOM node of its own — every
 * attribute `OverlayEditor.tsx` passes to `<Rnd>` (`tabIndex`,
 * `data-testid="overlay-editor-label"`, `role`, the focus/blur/keyboard
 * handlers) is lost with it, which is exactly why
 * `OverlayEditorKeyboard.test.tsx` refuses to reuse it and renders the real
 * `react-rnd` instead. This file needs both halves at once: `onDragStop`/
 * `onResizeStop` invoked directly (sites 1-2, atomic), *and* real DOM
 * `keydown`/`keyup`/`focus`/`blur` landing on a real element (site 6's
 * keydown-to-keyup coalescing). The stub below renders a plain `<div>`
 * carrying every attribute/handler `OverlayEditor.tsx` gives `<Rnd>` and
 * separately exposes `onDragStop`/`onResizeStop` for a test to call
 * directly — the logic under test in both halves lives in
 * `OverlayEditor.tsx`'s own callbacks, never inside `react-rnd` itself,
 * which is the same reasoning the characterisation guard already gives for
 * stubbing `Rnd` at all.
 * </p>
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

// Phase 6 review finding — Undo/Redo are `aria-disabled`, not natively
// `disabled`. A native `disabled` attribute makes a browser blur the
// element the instant it is applied, so undoing to the floor *by mouse*
// would drop focus to `<body>` — outside the editor root — and `Ctrl+Z`
// would stop working entirely until the operator clicked back in
// (FR-007's root-element binding cannot fire on a target it no longer
// contains focus inside). `aria-disabled` keeps the control focusable and
// keyboard-reachable; the click handler itself is what must refuse to act.
// `apps/shared` does not carry `@testing-library/jest-dom`
// (`OverlayEditorBackdrop.test.tsx`'s own comment), so this is read
// directly off the element rather than via `toBeDisabled()`.
function isDisabled(el: HTMLElement): boolean {
  return el.getAttribute('aria-disabled') === 'true';
}

function getLabel(): HTMLElement {
  return screen.getByTestId('overlay-editor-label');
}

function getTextInput(): HTMLInputElement {
  return screen.getByTestId('overlay-editor-text') as HTMLInputElement;
}

function getFontSlider(): HTMLInputElement {
  return screen.getByTestId('overlay-editor-font-size') as HTMLInputElement;
}

function field(label: string): HTMLInputElement {
  return screen.getByLabelText(label, { exact: true }) as HTMLInputElement;
}

function getUndoButton(): HTMLButtonElement {
  return screen.getByRole('button', { name: /^undo$/i }) as HTMLButtonElement;
}

function getRedoButton(): HTMLButtonElement {
  return screen.getByRole('button', { name: /^redo$/i }) as HTMLButtonElement;
}

/**
 * Both bindings land on the editor's root element (FR-007), not the label,
 * so they work from anywhere focus sits inside it — but firing the DOM
 * event on the label and letting it bubble exercises exactly that: the
 * label's own `onKeyDown` (`handleLabelKeyDown`) ignores non-arrow keys and
 * lets them fall through, so `Ctrl+Z` fired here still has to reach the
 * root handler by bubbling, the same as a real operator whose focus is on
 * the label when they press it.
 */
function pressUndo(): void {
  fireEvent.keyDown(getLabel(), { key: 'z', ctrlKey: true });
}

function pressRedo(): void {
  fireEvent.keyDown(getLabel(), { key: 'z', ctrlKey: true, shiftKey: true });
}

function getUndoLiveRegion(): HTMLElement {
  const region = screen.getByTestId('overlay-editor-undo-live-region');
  expect(region.getAttribute('aria-live')).toBe('polite');
  return region;
}

function getGeometryLiveRegion(): HTMLElement {
  return screen.getByTestId('overlay-editor-geometry-live-region');
}

function ControlledOverlayEditor({
  initial,
  onChangeSpy,
  resolvedPreview,
}: {
  initial: OverlayLabel;
  onChangeSpy?: (next: OverlayLabel) => void;
  resolvedPreview?: ResolvedTextPreview;
}) {
  const [value, setValue] = useState(initial);
  return (
    <OverlayEditor
      value={value}
      onChange={(next) => {
        setValue(next);
        onChangeSpy?.(next);
      }}
      resolvedPreview={resolvedPreview}
    />
  );
}

describe('OverlayEditor undo/redo (spec 154, issue #2347)', () => {
  afterEach(() => {
    cleanup();
    lastRndProps = null;
  });

  describe('sites 1-3 — atomic: drag, resize, geometry-field commit', () => {
    it('A completed drag is one undo step, and onChange fires exactly twice — drag, then undo', () => {
      const onChange = vi.fn();
      render(<ControlledOverlayEditor initial={buildLabel()} onChangeSpy={onChange} />);

      act(() => {
        lastRndProps!.onDragStop({}, { x: 320, y: 135 });
      });
      expect(field('Left').value).toBe('40');
      expect(field('Top').value).toBe('30');

      fireEvent.click(getUndoButton());

      expect(field('Left').value).toBe('10');
      expect(field('Top').value).toBe('10');
      expect(onChange).toHaveBeenCalledTimes(2);
      expect(isDisabled(getUndoButton())).toBe(true);
    });

    it('A completed resize is one undo step', () => {
      render(<ControlledOverlayEditor initial={buildLabel({ normalizedWidth: 0.3, normalizedHeight: 0.08 })} />);

      act(() => {
        lastRndProps!.onResizeStop({}, 'bottomRight', { offsetWidth: 400, offsetHeight: 54 }, {}, { x: 80, y: 45 });
      });
      expect(field('Width').value).toBe('50');
      expect(field('Height').value).toBe('12');

      fireEvent.click(getUndoButton());

      expect(field('Width').value).toBe('30');
      expect(field('Height').value).toBe('8');
    });

    it('A committed geometry value is one undo step, and the label moves back with it', () => {
      render(<ControlledOverlayEditor initial={buildLabel()} />);

      fireEvent.change(field('Left'), { target: { value: '33.33' } });
      fireEvent.keyDown(field('Left'), { key: 'Enter' });
      expect(field('Left').value).toBe('33.33');

      fireEvent.click(getUndoButton());

      expect(field('Left').value).toBe('10');
    });

    it('A refused geometry draft (parse failure) is not an undo step', () => {
      render(<ControlledOverlayEditor initial={buildLabel()} />);

      fireEvent.change(field('Left'), { target: { value: 'abc' } });
      fireEvent.keyDown(field('Left'), { key: 'Enter' });
      expect(screen.getByRole('alert').textContent).toBe('Enter a number.');

      expect(isDisabled(getUndoButton())).toBe(true);
      pressUndo();
      expect(field('Left').value).toBe('abc');
    });

    it('An escaped geometry draft is not an undo step', () => {
      render(<ControlledOverlayEditor initial={buildLabel()} />);

      fireEvent.change(field('Left'), { target: { value: '77' } });
      fireEvent.keyDown(field('Left'), { key: 'Escape' });

      expect(field('Left').value).toBe('10');
      expect(isDisabled(getUndoButton())).toBe(true);
    });

    it('Redo puts back exactly what undo took', () => {
      render(<ControlledOverlayEditor initial={buildLabel()} />);

      act(() => {
        lastRndProps!.onDragStop({}, { x: 320, y: 135 });
      });
      fireEvent.click(getUndoButton());
      expect(field('Left').value).toBe('10');

      fireEvent.click(getRedoButton());

      expect(field('Left').value).toBe('40');
      expect(field('Top').value).toBe('30');
    });

    it('A new edit after an undo discards the redo stack, and a refused Ctrl+Shift+Z is announced, not silent', () => {
      render(<ControlledOverlayEditor initial={buildLabel()} />);

      act(() => {
        lastRndProps!.onDragStop({}, { x: 320, y: 135 });
      });
      fireEvent.click(getUndoButton());
      expect(isDisabled(getRedoButton())).toBe(false);

      act(() => {
        lastRndProps!.onResizeStop({}, 'bottomRight', { offsetWidth: 320, offsetHeight: 54 }, {}, { x: 80, y: 45 });
      });

      expect(isDisabled(getRedoButton())).toBe(true);
      const widthBefore = field('Width').value;
      pressRedo();
      expect(field('Width').value).toBe(widthBefore);
      // Phase 6 review finding — this used to only pin that the value did
      // not change, which reads as "Ctrl+Shift+Z does nothing" and endorses
      // silence as correct. It is not: `handleUndoClick` already announces
      // a refusal ('Nothing to undo'); `handleRedoClick` must say the
      // symmetric 'Nothing to redo', not leave the live region holding
      // whatever it last said.
      expect(getUndoLiveRegion().textContent).toBe('Nothing to redo');
    });

    it('Bad request — undo with nothing to undo changes nothing, calls onChange not at all, and the control is disabled', () => {
      const onChange = vi.fn();
      render(<ControlledOverlayEditor initial={buildLabel()} onChangeSpy={onChange} />);

      expect(isDisabled(getUndoButton())).toBe(true);
      pressUndo();

      expect(onChange).not.toHaveBeenCalled();
      expect(field('Left').value).toBe('10');
      expect(isDisabled(getUndoButton())).toBe(true);
    });

    it('A refused Undo stays focusable (aria-disabled, not disabled), and a mouse click on it still no-ops', () => {
      // The native `disabled` attribute would make this moot: a browser
      // refuses focus outright on a disabled element and blurs one that
      // becomes disabled while focused. `aria-disabled` is a purely
      // advisory ARIA state — the element stays a normal, focusable,
      // clickable control, which is why the click handler itself, not the
      // browser, has to be the thing that refuses to act.
      const onChange = vi.fn();
      render(<ControlledOverlayEditor initial={buildLabel()} onChangeSpy={onChange} />);

      const undoButton = getUndoButton();
      expect(isDisabled(undoButton)).toBe(true);
      expect(undoButton.disabled).toBe(false);

      undoButton.focus();
      expect(document.activeElement).toBe(undoButton);

      fireEvent.click(undoButton);

      expect(onChange).not.toHaveBeenCalled();
      expect(field('Left').value).toBe('10');
    });
  });

  describe('sites 4-5 — idle-bounded runs: typing and the font-size slider', () => {
    beforeEach(() => {
      vi.useFakeTimers();
    });

    afterEach(() => {
      vi.useRealTimers();
    });

    it('A run of typing is one undo step, not one per keystroke', () => {
      render(<ControlledOverlayEditor initial={buildLabel({ text: 'Furnace' })} />);

      fireEvent.change(getTextInput(), { target: { value: 'Furnace ' } });
      fireEvent.change(getTextInput(), { target: { value: 'Furnace B' } });
      fireEvent.change(getTextInput(), { target: { value: 'Furnace Bay 3' } });

      act(() => {
        vi.advanceTimersByTime(TEXT_IDLE_MS + 1);
      });

      fireEvent.click(getUndoButton());

      expect(getTextInput().value).toBe('Furnace');
      expect(isDisabled(getUndoButton())).toBe(true);
    });

    it('A run on the font-size slider — the fifth emission site — is one undo step', () => {
      render(<ControlledOverlayEditor initial={buildLabel({ fontSizePx: 32 })} />);

      for (let size = 33; size <= 40; size += 1) {
        fireEvent.change(getFontSlider(), { target: { value: String(size) } });
      }

      act(() => {
        vi.advanceTimersByTime(TEXT_IDLE_MS + 1);
      });

      fireEvent.click(getUndoButton());

      expect(getFontSlider().value).toBe('32');
      expect(isDisabled(getUndoButton())).toBe(true);
    });

    it('Blur closes a run mid-idle — typing again afterward is a second, separate step', () => {
      render(<ControlledOverlayEditor initial={buildLabel({ text: 'Furnace' })} />);

      fireEvent.change(getTextInput(), { target: { value: 'Furnace A' } });
      fireEvent.blur(getTextInput());
      fireEvent.focus(getTextInput());
      fireEvent.change(getTextInput(), { target: { value: 'Furnace A B' } });

      fireEvent.click(getUndoButton());
      expect(getTextInput().value).toBe('Furnace A');

      fireEvent.click(getUndoButton());
      expect(getTextInput().value).toBe('Furnace');
    });

    it('An emission from another site closes a run before the idle elapses (drag then type then undo restores the typing, not the pre-drag state)', () => {
      render(<ControlledOverlayEditor initial={buildLabel({ text: 'Furnace' })} />);

      act(() => {
        lastRndProps!.onDragStop({}, { x: 320, y: 135 });
      });
      fireEvent.change(getTextInput(), { target: { value: 'Furnace Bay 3' } });

      pressUndo();

      expect(getTextInput().value).toBe('Furnace');
      expect(field('Left').value).toBe('40');

      pressUndo();

      expect(field('Left').value).toBe('10');
    });

    it('Two runs separated by more than TEXT_IDLE_MS are two undo steps', () => {
      render(<ControlledOverlayEditor initial={buildLabel({ text: 'Furnace' })} />);

      fireEvent.change(getTextInput(), { target: { value: 'Furnace A' } });
      act(() => {
        vi.advanceTimersByTime(TEXT_IDLE_MS + 1);
      });
      fireEvent.change(getTextInput(), { target: { value: 'Furnace A B' } });
      act(() => {
        vi.advanceTimersByTime(TEXT_IDLE_MS + 1);
      });

      fireEvent.click(getUndoButton());
      expect(getTextInput().value).toBe('Furnace A');

      fireEvent.click(getUndoButton());
      expect(getTextInput().value).toBe('Furnace');
      expect(isDisabled(getUndoButton())).toBe(true);
    });
  });

  describe('site 6 — keydown-to-keyup: held arrows', () => {
    it('A held run of one hundred and sixty presses is one undo step, however long it was held', () => {
      render(<ControlledOverlayEditor initial={buildLabel({ normalizedX: 0.1, normalizedWidth: 0.2 })} />);
      const label = getLabel();

      for (let i = 0; i < 160; i += 1) {
        fireEvent.keyDown(label, { key: 'ArrowRight' });
      }
      fireEvent.keyUp(label, { key: 'ArrowRight' });

      fireEvent.click(getUndoButton());

      expect(field('Left').value).toBe('10');
      expect(isDisabled(getUndoButton())).toBe(true);
    });

    it('Releasing and pressing again is two steps, not one', () => {
      render(<ControlledOverlayEditor initial={buildLabel({ normalizedX: 0.1, normalizedWidth: 0.2 })} />);
      const label = getLabel();

      for (let i = 0; i < 10; i += 1) {
        fireEvent.keyDown(label, { key: 'ArrowRight' });
      }
      fireEvent.keyUp(label, { key: 'ArrowRight' });
      const afterFirstRun = field('Left').value;

      for (let i = 0; i < 10; i += 1) {
        fireEvent.keyDown(label, { key: 'ArrowRight' });
      }
      fireEvent.keyUp(label, { key: 'ArrowRight' });

      fireEvent.click(getUndoButton());

      expect(field('Left').value).toBe(afterFirstRun);
      expect(field('Left').value).not.toBe('10');
    });

    it('Switching axis mid-hold starts a new step', () => {
      render(
        <ControlledOverlayEditor
          initial={buildLabel({ normalizedX: 0.1, normalizedY: 0.1, normalizedWidth: 0.2, normalizedHeight: 0.2 })}
        />,
      );
      const label = getLabel();

      for (let i = 0; i < 5; i += 1) {
        fireEvent.keyDown(label, { key: 'ArrowRight' });
      }
      const leftAfterRightRun = field('Left').value;
      for (let i = 0; i < 5; i += 1) {
        fireEvent.keyDown(label, { key: 'ArrowDown' });
      }
      fireEvent.keyUp(label, { key: 'ArrowDown' });
      fireEvent.keyUp(label, { key: 'ArrowRight' });

      fireEvent.click(getUndoButton());

      expect(field('Top').value).toBe('10');
      expect(field('Left').value).toBe(leftAfterRightRun);
      expect(field('Left').value).not.toBe('10');
    });

    it('Ctrl mid-hold starts a new step; Shift does not', () => {
      render(<ControlledOverlayEditor initial={buildLabel({ normalizedX: 0.1, normalizedWidth: 0.2 })} />);
      const label = getLabel();

      fireEvent.keyDown(label, { key: 'ArrowRight' });
      fireEvent.keyDown(label, { key: 'ArrowRight' });
      fireEvent.keyDown(label, { key: 'ArrowRight', shiftKey: true });
      fireEvent.keyUp(label, { key: 'ArrowRight' });

      // All six presses (3 fine + Shift-still-same-run) collapse into one step.
      fireEvent.click(getUndoButton());
      expect(field('Left').value).toBe('10');
      expect(isDisabled(getUndoButton())).toBe(true);

      // Ctrl+ArrowRight is a *resize*, a different run key — its own step.
      fireEvent.keyDown(label, { key: 'ArrowRight', ctrlKey: true });
      fireEvent.keyDown(label, { key: 'ArrowRight', ctrlKey: true });
      fireEvent.keyUp(label, { key: 'ArrowRight' });

      expect(isDisabled(getUndoButton())).toBe(false);
      fireEvent.click(getUndoButton());
      expect(field('Width').value).toBe('20');
    });

    it('Blur of the label closes an open run', () => {
      render(<ControlledOverlayEditor initial={buildLabel({ normalizedX: 0.1, normalizedWidth: 0.2 })} />);
      const label = getLabel();

      fireEvent.keyDown(label, { key: 'ArrowRight' });
      fireEvent.keyDown(label, { key: 'ArrowRight' });
      fireEvent.blur(label);
      fireEvent.focus(label);
      fireEvent.keyDown(label, { key: 'ArrowRight' });
      fireEvent.keyDown(label, { key: 'ArrowRight' });
      fireEvent.keyUp(label, { key: 'ArrowRight' });

      const afterSecondRun = field('Left').value;
      fireEvent.click(getUndoButton());
      expect(field('Left').value).not.toBe(afterSecondRun);
      expect(field('Left').value).not.toBe('10');

      fireEvent.click(getUndoButton());
      expect(field('Left').value).toBe('10');
    });
  });

  describe('the floor, the re-seed detector, and the untouched authoring-session state', () => {
    it("Undo cannot pass the state the editor opened with — a saved-draft floor ('edit mode')", () => {
      const editTargetLabel = buildLabel({ text: 'Furnace', normalizedX: 0.1 });
      render(<ControlledOverlayEditor initial={editTargetLabel} />);

      act(() => {
        lastRndProps!.onDragStop({}, { x: 320, y: 45 });
      });
      fireEvent.change(getTextInput(), { target: { value: 'Kiln' } });

      pressUndo();
      pressUndo();
      pressUndo();
      pressUndo();

      expect(getTextInput().value).toBe('Furnace');
      expect(field('Left').value).toBe('10');
      expect(isDisabled(getUndoButton())).toBe(true);

      pressUndo();
      expect(getTextInput().value).toBe('Furnace');
    });

    it("Undo cannot pass the state the editor opened with — DEFAULT_INPUT's floor ('create mode')", () => {
      const defaultInputLabel: OverlayLabel = {
        text: 'Overlay text',
        normalizedX: 0.1,
        normalizedY: 0.1,
        normalizedWidth: 0.3,
        normalizedHeight: 0.08,
        fontSizePx: 32,
      };
      render(<ControlledOverlayEditor initial={defaultInputLabel} />);

      fireEvent.change(getTextInput(), { target: { value: 'My overlay' } });
      act(() => {
        lastRndProps!.onDragStop({}, { x: 80, y: 45 });
      });

      pressUndo();
      pressUndo();
      pressUndo();

      expect(getTextInput().value).toBe('Overlay text');
      expect(field('Left').value).toBe('10');
      expect(field('Top').value).toBe('10');
      expect(field('Width').value).toBe('30');
      expect(field('Height').value).toBe('8');
      expect(getFontSlider().value).toBe('32');
    });

    it('A value replaced from outside (not through onChange) starts a fresh history and a new floor', () => {
      const onChange = vi.fn();
      const { rerender } = render(<OverlayEditor value={buildLabel({ text: 'Draft A' })} onChange={onChange} />);

      act(() => {
        lastRndProps!.onDragStop({}, { x: 320, y: 45 });
      });
      expect(isDisabled(getUndoButton())).toBe(false);

      // The parent replaces `value` from outside — `OverlayEditorDialog.tsx`'s
      // `reset(defaultValues)` on close-and-reopen against a different draft
      // — never through this component's own `onChange`.
      rerender(<OverlayEditor value={buildLabel({ text: 'Draft B' })} onChange={onChange} />);

      expect(isDisabled(getUndoButton())).toBe(true);
      pressUndo();
      expect(getTextInput().value).toBe('Draft B');
    });

    it('A query settling does not erase the history — a new resolvedPreview object identity, the same value', () => {
      const preview = (): ResolvedTextPreview => ({ resolvedText: 'Furnace', placeholders: [] });
      const { rerender } = render(
        <ControlledOverlayEditor initial={buildLabel({ text: 'Furnace' })} resolvedPreview={preview()} />,
      );

      act(() => {
        lastRndProps!.onDragStop({}, { x: 320, y: 135 });
      });
      expect(isDisabled(getUndoButton())).toBe(false);

      // `value` is untouched — it is owned by `ControlledOverlayEditor`'s own
      // state and this rerender does not go through it — but `resolvedPreview`
      // is a brand-new object every call, exactly as
      // `useResolveOverlayTextQuery`'s `currentData` is on every settle
      // (`OverlayEditorDialog.tsx:166/178`). A `useEffect` keyed on
      // `resolvedPreview`, a deep-equality check on `value`, or "did the
      // component re-render" would all clear the stack here; only reference
      // identity against what the hook itself last emitted survives it
      // (plan.md §5).
      rerender(<ControlledOverlayEditor initial={buildLabel({ text: 'Furnace' })} resolvedPreview={preview()} />);

      expect(isDisabled(getUndoButton())).toBe(false);
      fireEvent.click(getUndoButton());
      expect(field('Left').value).toBe('10');
    });

    // Phase 6 review finding — the case the suite above does not present.
    // `resolvedPreview`'s identity changes there while `value` itself is left
    // completely alone (still the same reference `ControlledOverlayEditor`'s
    // own state holds), which catches a detector keyed on the wrong prop or
    // on "did the component re-render" — but not a detector that compares
    // `value` by reference *only*, with no structural fallback: that
    // implementation also survives the test above, because `value` never
    // moves in it at all. This test is the one that does move `value` — to a
    // brand-new object carrying the exact same six fields — which is what a
    // real controlled parent whose own render pipeline clones (RHF's
    // `Controller`, sourced through `useWatch`) hands back on every one of
    // its own echoes, not only on a query settling.
    it('A new object with content identical to the last emission preserves the history (echo-tolerant identity, not reference-only)', () => {
      const onChange = vi.fn();
      const { rerender } = render(<OverlayEditor value={buildLabel({ text: 'Furnace' })} onChange={onChange} />);

      act(() => {
        lastRndProps!.onDragStop({}, { x: 320, y: 135 });
      });
      expect(isDisabled(getUndoButton())).toBe(false);

      const emitted = onChange.mock.calls[onChange.mock.calls.length - 1]![0] as OverlayLabel;
      // A fresh object, never the one the hook itself handed to `onChange`,
      // but byte-for-byte the same six fields — exactly the shape
      // `useWatch`'s `generateWatchOutput` produces on every form-state
      // notification, echoing this hook's own emission back as a new clone.
      // A reference-only detector (the current implementation) cannot tell
      // this apart from `OverlayEditorDialog.tsx`'s `reset(defaultValues)`
      // and clears both stacks here.
      rerender(<OverlayEditor value={{ ...emitted }} onChange={onChange} />);

      expect(isDisabled(getUndoButton())).toBe(false);
      fireEvent.click(getUndoButton());
      expect(field('Left').value).toBe('10');
    });

    it('An uncommitted geometry draft is left alone by undo', () => {
      render(<ControlledOverlayEditor initial={buildLabel()} />);

      act(() => {
        lastRndProps!.onDragStop({}, { x: 320, y: 135 });
      });
      fireEvent.change(field('Width'), { target: { value: '77' } });

      fireEvent.click(getUndoButton());

      expect(field('Left').value).toBe('10');
      expect(field('Width').value).toBe('77');
    });

    it('The backdrop is not part of the history — selecting it emits no onChange, and undo leaves it alone', () => {
      const onChange = vi.fn();
      render(<ControlledOverlayEditor initial={buildLabel()} onChangeSpy={onChange} />);

      fireEvent.click(screen.getByRole('radio', { name: 'White field' }));
      expect(onChange).not.toHaveBeenCalled();

      act(() => {
        lastRndProps!.onDragStop({}, { x: 320, y: 135 });
      });
      expect(onChange).toHaveBeenCalledTimes(1);

      fireEvent.click(getUndoButton());

      expect(field('Left').value).toBe('10');
      expect((screen.getByRole('radio', { name: 'White field' }) as HTMLInputElement).checked).toBe(true);
      // The drag and the undo — never the backdrop selection.
      expect(onChange).toHaveBeenCalledTimes(2);
    });
  });

  describe('US2 — the announcement', () => {
    it('An undo is announced in its own polite region', () => {
      render(<ControlledOverlayEditor initial={buildLabel()} />);

      act(() => {
        lastRndProps!.onDragStop({}, { x: 320, y: 135 });
      });
      expect(getUndoLiveRegion().textContent).toBe('');

      fireEvent.click(getUndoButton());

      expect(getUndoLiveRegion().textContent).toBe('Undone');
    });

    it('A refused undo is announced as refused, not silently', () => {
      render(<ControlledOverlayEditor initial={buildLabel()} />);

      pressUndo();

      expect(getUndoLiveRegion().textContent).toBe('Nothing to undo');
    });

    it("The undo announcement does not collide with spec 149's geometry announcer", () => {
      render(<ControlledOverlayEditor initial={buildLabel()} />);

      act(() => {
        lastRndProps!.onDragStop({}, { x: 320, y: 135 });
      });
      fireEvent.click(getUndoButton());

      expect(getUndoLiveRegion()).not.toBe(getGeometryLiveRegion());
      expect(getGeometryLiveRegion().textContent).toBe('');
    });

    it('A redo is announced too', () => {
      render(<ControlledOverlayEditor initial={buildLabel()} />);

      act(() => {
        lastRndProps!.onDragStop({}, { x: 320, y: 135 });
      });
      fireEvent.click(getUndoButton());
      fireEvent.click(getRedoButton());

      expect(getUndoLiveRegion().textContent).toBe('Redone');
    });

    // Phase 6 review finding — #2344's defect recurring in a file that
    // already carries its own fix twenty lines above (`OverlayEditor.tsx`'s
    // `queueAnnouncement`, `lastAnnouncedRef` plus a trailing zero-width-space
    // toggle). `setUndoMessage('Undone')` with the region already reading
    // 'Undone' is a same-string write — React bails out and never touches the
    // DOM — so a screen reader, which only re-announces a polite region when
    // its text actually *changes*, says nothing on the second of two
    // consecutive identical outcomes. `MutationObserver.takeRecords()` reads
    // the pending mutation queue synchronously, so this needs no fake timers
    // and no `waitFor`.
    it('A second consecutive identical announcement still reaches the DOM (repeated-announcement bail-out)', () => {
      render(<ControlledOverlayEditor initial={buildLabel()} />);

      // Two undo steps, so two consecutive undos both succeed and both say
      // the identical 'Undone' — the exact repeat the bail-out needs.
      act(() => {
        lastRndProps!.onDragStop({}, { x: 320, y: 135 });
      });
      act(() => {
        lastRndProps!.onResizeStop({}, 'bottomRight', { offsetWidth: 320, offsetHeight: 54 }, {}, { x: 80, y: 45 });
      });

      const region = getUndoLiveRegion();
      // `globalThis.MutationObserver`, not the bare global — this file's
      // eslint config does not (yet) list it among the DOM globals it
      // recognizes, and `globalThis` is already one of them.
      const observer = new globalThis.MutationObserver(() => {});
      observer.observe(region, { childList: true, characterData: true, subtree: true });

      fireEvent.click(getUndoButton());
      expect(region.textContent).toBe('Undone');
      const firstMutations = observer.takeRecords().length;
      expect(firstMutations).toBeGreaterThan(0);

      fireEvent.click(getUndoButton());
      expect(region.textContent).toBe('Undone');
      const secondMutations = observer.takeRecords().length;
      expect(secondMutations).toBeGreaterThan(0);

      observer.disconnect();
    });
  });
});
