import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { Controller, useForm } from 'react-hook-form';
import type { OverlayLabel } from '@smart-sentinel-eye/shared/api/overlays.api';
import { OverlayEditor } from '@smart-sentinel-eye/shared/ui/composites/OverlayEditor';

/**
 * Spec 154 (issue #2347), phase 6 review finding — the regression this file
 * exists to pin. `OverlayEditorUndo.test.tsx`'s `ControlledOverlayEditor` is
 * a hand-rolled `useState` passthrough: its `onChange` calls `setValue(next)`
 * with the exact object the hook itself just built, so `value` on the next
 * render is reference-identical to what `useOverlayEditHistory` last
 * emitted. It is not `OverlayEditorDialog.tsx`'s real parent, and no test in
 * either new suite renders `OverlayEditor` under that real parent — so the
 * production wiring has never actually been exercised.
 *
 * <p>
 * `OverlayEditorDialog.tsx:252-264` wires `OverlayEditor` through RHF's
 * `Controller`, exactly reproduced by the harness below. `useController`
 * (which `Controller` calls internally) sources `field.value` through
 * `useWatch`, and `useWatch` re-derives its return value via
 * `generateWatchOutput` on every form-state notification — deep-equal to
 * what the hook emitted, but a **new object**, never the same reference.
 * Confirmed directly against the installed `react-hook-form@7.86.0`:
 * </p>
 * <pre>
 * renders: 4
 * 2 text= ax   value===lastEmitted? false
 * 3 text= axx  value===lastEmitted? false
 * IDENTITY-PRESERVED: false
 * </pre>
 * <p>
 * `useOverlayEditHistory`'s re-seed detector
 * (`useOverlayEditHistory.ts:113-121`) compares `value` against
 * `lastEmitted` by reference only, so it reads every one of the
 * `Controller`'s own echoes as an external re-seed — `OverlayEditorDialog.tsx`
 * calling `reset(defaultValues)` — and clears both stacks the render after
 * every single edit. Against the real component tree:
 * </p>
 * <pre>
 * PROBE after edit : text = Furnace A | undo disabled = true
 * PROBE after undo : text = Furnace A
 * PROBE live region: ""
 * </pre>
 * <p>
 * This file renders exactly that real tree — the genuine `Controller`/
 * `useWatch` machinery, no mock of `react-hook-form` — so a failure here
 * names the actual defect, not a stand-in for it.
 * </p>
 */

const BASE_LABEL: OverlayLabel = {
  text: 'Furnace',
  normalizedX: 0.1,
  normalizedY: 0.1,
  normalizedWidth: 0.3,
  normalizedHeight: 0.08,
  fontSizePx: 32,
};

function RealControllerHarness({ initial }: { initial: OverlayLabel }) {
  const { control } = useForm<{ label: OverlayLabel }>({ defaultValues: { label: initial } });
  return (
    <Controller
      control={control}
      name="label"
      render={({ field }) => <OverlayEditor value={field.value} onChange={field.onChange} />}
    />
  );
}

// `apps/management-web`'s eslint config carries no DOM-element globals (no
// suite here has needed one before), unlike `apps/shared`'s — so these read
// through a local structural type instead of naming `HTMLElement` /
// `HTMLInputElement`, rather than widening that config for one file. Return
// types are inferred (RTL's own `getByRole`/`getByTestId` are already typed
// as `HTMLElement`), which is why `getUndoButton` carries none.
type InputLike = { value: string };

function getUndoButton() {
  return screen.getByRole('button', { name: /^undo$/i });
}

function textInputValue(): string {
  return (screen.getByTestId('overlay-editor-text') as unknown as InputLike).value;
}

describe('OverlayEditor undo/redo under a real RHF Controller (spec 154, issue #2347)', () => {
  afterEach(() => {
    cleanup();
  });

  it('Typing one character under the real Controller leaves Undo enabled, and Undo restores the prior text', () => {
    render(<RealControllerHarness initial={BASE_LABEL} />);

    const textInput = screen.getByTestId('overlay-editor-text');
    act(() => {
      fireEvent.change(textInput, { target: { value: 'Furnace A' } });
    });

    // The commit already happened; this is the render *after* it, where the
    // Controller's own `useWatch`-sourced echo lands as a new object. If the
    // re-seed detector is reference-only, this is exactly where it (wrongly)
    // declares a re-seed and wipes the step just recorded.
    expect(getUndoButton()).not.toBeDisabled();

    fireEvent.click(getUndoButton());

    expect(textInputValue()).toBe('Furnace');
  });

  it('A second character continues to coalesce into the same run under the real Controller', () => {
    render(<RealControllerHarness initial={BASE_LABEL} />);

    const textInput = () => screen.getByTestId('overlay-editor-text');
    act(() => {
      fireEvent.change(textInput(), { target: { value: 'Furnace A' } });
    });
    act(() => {
      fireEvent.change(textInput(), { target: { value: 'Furnace AB' } });
    });

    fireEvent.click(getUndoButton());

    // One run, one undo step: back to the pre-typing text in a single undo,
    // not merely one character shorter.
    expect(textInputValue()).toBe('Furnace');
  });
});
