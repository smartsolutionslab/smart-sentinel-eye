// @vitest-environment jsdom
import { useEffect, useState } from 'react';
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
 *
 * <p>
 * <b>Phase-6 fix round.</b> Two tests in the "wires the live readout" and
 * "commits" sections used to render with a fixed `value` prop and a bare
 * `vi.fn()` in place of a real caller's feedback loop — a shape no real
 * parent has. `OverlayEditorDialog.tsx:134-147` renders this tree inside a
 * React Hook Form `<Controller>`, whose `onChange` flows straight back down
 * as a new `value` prop on every render; a fixed-`value` harness cannot tell
 * a correct implementation from an incorrect one for anything that depends
 * on that feedback, and one currently does not — the two production files
 * were adjusted specifically to keep those two tests green against a harness
 * that does not represent reality (see the comments on `displayValue` and
 * `commit` in `OverlayGeometryFields.tsx`, and on `preview` in
 * `OverlayEditor.tsx`). `ControlledFields` and `ControlledOverlayEditor`
 * (below) are the fix: every test that fires a gesture or a commit and then
 * reads a field's displayed value now goes through one of them. C1, C2, C4
 * and C5b are the reviewer's own counterfactuals, pinned as tests; they are
 * expected to fail for a real reason until the two production files revert
 * to their original, simpler design (preview cleared on drag/resize stop;
 * a committed draft cleared, not kept, once the commit succeeds).
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
  /**
   * Real `OverlayEditor.tsx` wires this to `handleLabelKeyDown` (spec 149).
   * The stub below renders no DOM node at all — `Rnd: (props) => props.children`
   * — so C5b (spec 151 fix round) invokes it directly the same way the file
   * already invokes `onDrag`/`onDragStop`, rather than firing a DOM
   * `keydown` that would land on nothing. `OverlayEditorKeyboard.test.tsx`
   * is the file that exercises this handler through the real, unmocked
   * `Rnd` and real DOM events; duplicating that here would test the stub.
   */
  onKeyDown?: (event: {
    key: string;
    preventDefault: () => void;
    altKey?: boolean;
    metaKey?: boolean;
    shiftKey?: boolean;
    ctrlKey?: boolean;
  }) => void;
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
 *
 * <p>
 * <b>Phase-6 fix round.</b> Two tests in this file used to render
 * `OverlayGeometryFields` with a fixed `value` prop and a bare `vi.fn()` for
 * `onCommit` or `onChange` — a shape no real caller has.
 * `OverlayEditorDialog.tsx:134-147` renders this tree inside a React Hook
 * Form `<Controller>`, whose `onChange` flows straight back down as a new
 * `value` prop on every render. A harness that holds `value` fixed makes an
 * incorrect implementation look right (see `commit`'s comment in
 * `OverlayGeometryFields.tsx`, which currently keeps a committed draft
 * forever specifically so it kept reading right against that fixed-`value`
 * harness) and makes a correct one fail. Every test below that fires a
 * gesture or a commit and then reads a field's displayed value now goes
 * through this component (or `ControlledOverlayEditor`, below, for the
 * `OverlayEditor`-level cases) so the assertion means what it says.
 * </p>
 *
 * <p>
 * `controllerRef`, when supplied, exposes an imperative `setValue` alongside the
 * normal `onCommit` feedback loop — needed for C4 (below): an operator's
 * draft must not shadow a *later, unrelated* change to `value` that arrives
 * by some path other than this component's own commit (a parent reset, a
 * reload, a sibling field's own — differently-routed — update). No test
 * harness anywhere else in this file drives that path, because no ordinary
 * commit does either.
 * </p>
 */
interface FieldsController {
  setValue: (updater: (prev: OverlayLabel) => OverlayLabel) => void;
}

function ControlledFields({
  initial,
  onCommitSpy,
  controllerRef,
}: {
  initial: OverlayLabel;
  onCommitSpy: (field: string, normalized: number) => void;
  controllerRef?: { current: FieldsController | null };
}) {
  const [value, setValue] = useState(initial);
  // Assigned in an effect, not during render — react-hooks/refs, and the same
  // rule any real `useRef` obeys, even though `controllerRef` here is a plain
  // test-only object rather than one `useRef()` produced.
  useEffect(() => {
    if (controllerRef) {
      controllerRef.current = { setValue };
    }
  }, [controllerRef, setValue]);
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

/**
 * The `OverlayEditor`-level counterpart of `ControlledFields`, same pattern
 * as `OverlayEditorKeyboard.test.tsx`'s helper of the same name: every
 * `onChange` becomes the next `value`, so a drag settling or a field commit
 * is visible to the *next* render exactly as it is for the real, controlled
 * `OverlayEditorDialog.tsx`. C1, C2 and C5b (below) all need this — each is
 * about what the panel displays *after* something has genuinely changed
 * `value`, which a fixed-`value` render cannot represent.
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

  // "Commits an exact value on Enter, quantized to the grid, and calls
  // onCommit once (FR-004, FR-005, FR-006)" used to live here, rendering a
  // fixed `value` with a bare `vi.fn()` for `onCommit` — exactly the harness
  // shape no real caller has (phase-6 fix round; see the comment on
  // `ControlledFields`, above). Converting it to `ControlledFields` alone
  // does not discriminate anything: a single isolated commit has no later
  // event for a stale draft to shadow, so it would stay green either way,
  // proving nothing new. Its coverage — an exact typed value, correctly
  // quantized, one `onCommit`/`onChange` call — is fully subsumed by C2,
  // below, which asserts the same payload exactness *and* the display
  // correctness this version could never have proven.

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

  /**
   * C4 (phase-6 fix round, reviewer counterfactual). No drag, no keypress on
   * the label — "no gesture ever" — only this field's own commit, followed
   * by an *external* change to `value` that has nothing to do with it: a
   * parent reset, a reload, a sibling save routed some other way. A
   * committed draft must not go on shadowing `value` forever once `value`
   * has genuinely moved past it — `commit` currently keeps the draft
   * (reformatted) rather than clearing it, so `value` changing later has no
   * way back into the display.
   */
  it('C4 — a committed draft does not shadow a later, unrelated change to value (no gesture ever)', () => {
    const controllerRef: { current: FieldsController | null } = { current: null };
    render(
      <ControlledFields
        initial={buildLabel({ normalizedX: 0.25 })}
        onCommitSpy={vi.fn()}
        controllerRef={controllerRef}
      />,
    );

    // The initial value (25%) and the typed draft (10%) must differ: a
    // `fireEvent.change` to the value already on screen does not fire
    // React's `onChange` at all (its tracked-value comparison short-circuits
    // it), which would make this commit never happen and the assertions
    // below pass for no reason — exactly the class of defect this fix round
    // exists to remove. Caught empirically while writing this test.
    fireEvent.change(field('Left'), { target: { value: '10' } });
    fireEvent.keyDown(field('Left'), { key: 'Enter' });
    expect(field('Left').value).toBe('10');

    // Not this component's onCommit — an unrelated write to the same `value`
    // the real dialog could equally produce (e.g. a reset, or a save that
    // reloads the label from the server).
    act(() => {
      controllerRef.current!.setValue((prev) => ({ ...prev, normalizedX: 0.8 }));
    });

    expect(field('Left').value).toBe('80');
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

  /**
   * C1 (phase-6 fix round, reviewer counterfactual). This replaces a test
   * that used to stop at "the field reads the settled drag position" —
   * true, but against a fixed `value` it was also true for the wrong
   * reason: `OverlayGeometryFields.tsx`'s `displayValue` returns `preview`
   * whenever it is non-null, unconditionally, and `handleDragStop` sets
   * `preview` to the *release* geometry rather than clearing it back to
   * `null` (its own comment says why: "nothing here assumes that render
   * happens" — true of `onChange`'s argument, not of `preview` staying
   * stuck forever after). The two together mean a field can never show
   * anything the operator types once a drag has ever touched it. Typing
   * is the only way an operator using this panel as documented — FR-001 —
   * would ever notice.
   */
  it('C1 — after a drag settles, typing into Left is visible immediately, not the stale drag position', () => {
    const onChange = vi.fn();
    render(<ControlledOverlayEditor initial={buildLabel()} onChangeSpy={onChange} />);

    act(() => {
      lastRndProps!.onDrag?.({}, { x: 400, y: 0 });
      lastRndProps!.onDragStop({}, { x: 400, y: 0 });
    });

    expect(onChange).toHaveBeenCalledTimes(1);
    expect((onChange.mock.calls[0]![0] as OverlayLabel).normalizedX).toBe(0.5);
    expect(field('Left').value).toBe('50');

    fireEvent.change(field('Left'), { target: { value: '10' } });

    expect(field('Left').value).toBe('10');
  });

  /**
   * C2 (phase-6 fix round, reviewer counterfactual) — the worse case: the
   * commit *does* reach `onChange` with the right value (`normalizedX:
   * 0.1`), so a save right now would silently move the overlay to where
   * the field never showed it going. Escape does not rescue this either —
   * `handleKeyDown`'s Escape clears `drafts`, not `preview`, and `preview`
   * is what is pinning the display. This replaces a test that asserted the
   * same commit's `onChange` payload without ever re-reading the field —
   * the payload was always right; the display was not, and nothing checked it.
   */
  it('C2 — after a drag settles, committing Left shows the committed value, not the drag position', () => {
    const onChange = vi.fn();
    render(<ControlledOverlayEditor initial={buildLabel()} onChangeSpy={onChange} />);

    act(() => {
      lastRndProps!.onDrag?.({}, { x: 400, y: 0 });
      lastRndProps!.onDragStop({}, { x: 400, y: 0 });
    });
    expect(field('Left').value).toBe('50');

    fireEvent.change(field('Left'), { target: { value: '10' } });
    fireEvent.keyDown(field('Left'), { key: 'Enter' });

    expect(onChange).toHaveBeenCalledTimes(2);
    expect((onChange.mock.calls[1]![0] as OverlayLabel).normalizedX).toBe(0.1);
    expect(field('Left').value).toBe('10');
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

  /**
   * C5b (phase-6 fix round, reviewer counterfactual). Needs the real
   * keyboard path — spec 149's arrow-key nudge, which calls `emitNormalized`
   * directly and never touches `preview` at all — so this is `OverlayGeometryFields`'s
   * own bug in isolation from `OverlayEditor`'s stuck-`preview` one: `commit`
   * keeps the draft (reformatted) rather than clearing it, so a *later*
   * `value` change arriving through any other path — including the very
   * keyboard nudge spec 149 shipped before this panel existed — cannot reach
   * the display. `lastRndProps!.onKeyDown` is invoked directly (see the
   * comment on `RndStubProps`) rather than firing a DOM event on the stub,
   * which renders no element for one to land on.
   */
  it('C5b — after typing 10% into Left and committing, ArrowRight nudges the field to 10.5%', () => {
    const onChange = vi.fn();
    // Starts away from 10%: a `fireEvent.change` to the value already
    // displayed does not fire React's `onChange` at all (its tracked-value
    // comparison short-circuits it), which would make the commit below never
    // happen and the assertions pass for no reason (caught empirically
    // while writing this test — see the identical note on C4).
    render(<ControlledOverlayEditor initial={buildLabel({ normalizedX: 0.25 })} onChangeSpy={onChange} />);
    expect(field('Left').value).toBe('25');

    fireEvent.change(field('Left'), { target: { value: '10' } });
    fireEvent.keyDown(field('Left'), { key: 'Enter' });
    expect(field('Left').value).toBe('10');

    act(() => {
      lastRndProps!.onKeyDown?.({ key: 'ArrowRight', preventDefault: () => undefined });
    });

    expect(field('Left').value).toBe('10.5');
  });
});
