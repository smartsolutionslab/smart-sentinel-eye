// @vitest-environment jsdom
import { useState } from 'react';
import { act, renderHook } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { OverlayLabel } from '@smart-sentinel-eye/shared/api/overlays.api';
import { useOverlayEditHistory } from './useOverlayEditHistory.js';
import type { Boundary } from './useOverlayEditHistory.js';

/**
 * Spec 154 (issue #2347) T004 — the hook's invariants I1–I8 (plan.md §2), in
 * isolation via `renderHook`. New behaviour, RED (ADR-0139/ADR-0144):
 * `useOverlayEditHistory.ts` lands in this same commit as a phase-4a
 * scaffold only — a real signature, a body that does nothing at all — so a
 * failure here names a missing invariant, not an unresolved import. T011–T014
 * (`OverlayEditor.tsx`) replace the scaffold body; this file's calls do not
 * change under that work, per ADR-0144's "the engineer may not edit the
 * tests to pass".
 */

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

/**
 * Wraps the hook exactly as `OverlayEditor` will: `value` is owned by this
 * harness's own state, and every `onChange` the hook fires writes it back —
 * the same controlled shape `Controller`/`OverlayEditorDialog.tsx` gives the
 * real component. `onEmit` is a side channel onto every emission, so a test
 * can assert "nothing was emitted" (the bad-request case) without confusing
 * it with "the value happens not to have changed". `reseed` bypasses
 * `commit`/`onChange` entirely — the harness's stand-in for
 * `OverlayEditorDialog.tsx:151-153`'s `reset(defaultValues)`, which replaces
 * `value` from *outside* the hook (FR-011 / I6).
 */
function useHarness(initial: OverlayLabel, onEmit?: (next: OverlayLabel) => void) {
  const [value, setValue] = useState(initial);
  const onChange = (next: OverlayLabel) => {
    setValue(next);
    onEmit?.(next);
  };
  const history = useOverlayEditHistory(value, onChange);
  return { value, reseed: setValue, ...history };
}

function setup(initial: OverlayLabel = BASE_LABEL, onEmit?: (next: OverlayLabel) => void) {
  return renderHook(() => useHarness(initial, onEmit));
}

describe('useOverlayEditHistory (spec 154, issue #2347)', () => {
  describe('I1 — the floor is the absence of a predecessor (canUndo === past.length > 0)', () => {
    it('canUndo is false at birth', () => {
      const { result } = setup();
      expect(result.current.canUndo).toBe(false);
    });

    it('canUndo becomes true the moment a step is committed', () => {
      const { result } = setup();

      act(() => {
        result.current.commit(buildLabel({ text: 'Kiln' }), 'atomic');
      });

      expect(result.current.canUndo).toBe(true);
    });

    it('canUndo is false again after a value arrives that the hook did not emit', () => {
      const { result } = setup();
      act(() => {
        result.current.commit(buildLabel({ text: 'Kiln' }), 'atomic');
      });

      act(() => {
        result.current.reseed(buildLabel({ text: 'Reopened' }));
      });

      expect(result.current.canUndo).toBe(false);
    });
  });

  describe('I2 — an undo is not a step', () => {
    it('undo restores the value from before the step, and the step becomes redoable', () => {
      const { result } = setup();
      act(() => {
        result.current.commit(buildLabel({ text: 'Kiln' }), 'atomic');
      });

      act(() => {
        result.current.undo();
      });

      expect(result.current.value).toEqual(BASE_LABEL);
      expect(result.current.canUndo).toBe(false);
      expect(result.current.canRedo).toBe(true);
    });

    it('redo puts back exactly what undo took, and canRedo empties again', () => {
      const { result } = setup();
      const dragged = buildLabel({ normalizedX: 0.4, normalizedY: 0.3 });
      act(() => {
        result.current.commit(dragged, 'atomic');
      });
      act(() => {
        result.current.undo();
      });

      act(() => {
        result.current.redo();
      });

      expect(result.current.value).toEqual(dragged);
      expect(result.current.canRedo).toBe(false);
      expect(result.current.canUndo).toBe(true);
    });

    it('undo() reports whether it moved anything, and returns false with nothing to undo', () => {
      const onEmit = vi.fn();
      const { result } = setup(BASE_LABEL, onEmit);

      let moved = true;
      act(() => {
        moved = result.current.undo();
      });

      expect(moved).toBe(false);
      expect(onEmit).not.toHaveBeenCalled();
      expect(result.current.value).toEqual(BASE_LABEL);
    });
  });

  describe('I3 — a new step clears the future', () => {
    it('a commit made after an undo discards what redo would have restored', () => {
      const { result } = setup();
      act(() => {
        result.current.commit(buildLabel({ text: 'Kiln' }), 'atomic');
      });
      act(() => {
        result.current.undo();
      });

      act(() => {
        result.current.commit(buildLabel({ text: 'Oven' }), 'atomic');
      });

      expect(result.current.canRedo).toBe(false);

      let redone = true;
      act(() => {
        redone = result.current.redo();
      });

      expect(redone).toBe(false);
      expect(result.current.value.text).toBe('Oven');
    });

    it('a commit absorbed into an already-open run does not clear the future a second time', () => {
      // I3's own carve-out: a run's future was already cleared when the run
      // opened, so a second commit under the same key must not behave as a
      // fresh "new step" — there is nothing left to test that a first
      // commit under the run has not already exercised, so this pins the
      // observable instead: the whole run undoes as one step, never two.
      const { result } = setup();
      act(() => {
        result.current.commit(buildLabel({ text: 'K' }), { run: 'text' });
      });
      act(() => {
        result.current.commit(buildLabel({ text: 'Ki' }), { run: 'text' });
      });

      act(() => {
        result.current.undo();
      });

      expect(result.current.value.text).toBe('Furnace');
      expect(result.current.canUndo).toBe(false);
    });
  });

  describe('I4 — a run absorbs, it does not stack', () => {
    it('ten commits under the same run key collapse into exactly one undo step', () => {
      const { result } = setup();
      const run: Boundary = { run: 'text' };

      for (let i = 1; i <= 10; i += 1) {
        act(() => {
          result.current.commit(buildLabel({ text: `Furnace${'!'.repeat(i)}` }), run);
        });
      }

      expect(result.current.value.text).toBe(`Furnace${'!'.repeat(10)}`);

      act(() => {
        result.current.undo();
      });

      expect(result.current.value.text).toBe('Furnace');
      expect(result.current.canUndo).toBe(false);
    });
  });

  describe('I5 — a foreign commit closes an open run', () => {
    it('an atomic commit closes a run in progress, so each is its own undo step', () => {
      const { result } = setup();
      act(() => {
        result.current.commit(buildLabel({ text: 'Ki' }), { run: 'text' });
      });
      act(() => {
        result.current.commit(buildLabel({ text: 'Kiln' }), { run: 'text' });
      });
      act(() => {
        result.current.commit(buildLabel({ text: 'Kiln', normalizedX: 0.4 }), 'atomic');
      });

      act(() => {
        result.current.undo();
      });
      expect(result.current.value).toEqual(buildLabel({ text: 'Kiln' }));

      act(() => {
        result.current.undo();
      });
      expect(result.current.value).toEqual(BASE_LABEL);
    });

    it('a commit under a different run key closes the run open under the first key', () => {
      const { result } = setup();
      act(() => {
        result.current.commit(buildLabel({ text: 'Kiln' }), { run: 'text' });
      });
      act(() => {
        result.current.commit(buildLabel({ text: 'Kiln', fontSizePx: 40 }), { run: 'fontSize' });
      });

      // Two closed steps: undoing once must land exactly on the text step's
      // result, not skip past it to the floor.
      act(() => {
        result.current.undo();
      });
      expect(result.current.value).toEqual(buildLabel({ text: 'Kiln' }));

      act(() => {
        result.current.undo();
      });
      expect(result.current.value).toEqual(BASE_LABEL);
    });
  });

  describe('I6 — a value the hook did not emit is a new session (drag → type → undo restores the typing)', () => {
    it('a reseeded value clears both stacks and becomes the new floor', () => {
      const { result } = setup();
      act(() => {
        result.current.commit(buildLabel({ text: 'Ki' }), { run: 'text' });
      });

      act(() => {
        result.current.reseed(buildLabel({ text: 'Reopened' }));
      });

      expect(result.current.canUndo).toBe(false);
      expect(result.current.canRedo).toBe(false);

      act(() => {
        result.current.commit(buildLabel({ text: 'Reopened!' }), { run: 'text' });
      });
      act(() => {
        result.current.undo();
      });

      expect(result.current.value.text).toBe('Reopened');
      expect(result.current.canUndo).toBe(false);
    });

    it('a reseed closes an open run — the run does not silently keep absorbing into the new session', () => {
      const { result } = setup();
      act(() => {
        result.current.commit(buildLabel({ text: 'Ki' }), { run: 'text' });
      });

      act(() => {
        result.current.reseed(buildLabel({ text: 'Reopened' }));
      });

      act(() => {
        result.current.commit(buildLabel({ text: 'Reopened2' }), { run: 'text' });
      });
      act(() => {
        result.current.commit(buildLabel({ text: 'Reopened3' }), { run: 'text' });
      });

      act(() => {
        result.current.undo();
      });

      // If the pre-reseed run were still open, this single undo would have
      // to cross two absorbed commits and a reseed to reach 'Reopened' —
      // exactly one undo step must land there regardless.
      expect(result.current.value.text).toBe('Reopened');
    });

    it('a value the hook itself just emitted is not treated as an external re-seed', () => {
      // The trap named in plan.md §5: deep equality or a re-render-keyed
      // effect would both also pass this specific case, since nothing
      // *structurally* distinguishes an echo from a reseed here — this
      // assertion exists to be re-run, unmodified, once T014 wires the real
      // reference-identity detector; it is not a substitute for the
      // dialog-level "query settling does not erase the history" test that
      // is deliberately at the `OverlayEditorUndo.test.tsx` level instead,
      // because only that level has a second prop (`resolvedPreview`)
      // changing identity independently of `value`.
      const { result } = setup();
      act(() => {
        result.current.commit(buildLabel({ normalizedX: 0.4 }), 'atomic');
      });

      expect(result.current.canUndo).toBe(true);
      expect(result.current.value.normalizedX).toBe(0.4);
    });
  });

  describe('I7 — only the six OverlayLabel fields ever move through the hook', () => {
    it('a committed-then-undone value carries exactly the six OverlayLabel keys, nothing more', () => {
      const { result } = setup();
      act(() => {
        result.current.commit(buildLabel({ text: 'Kiln' }), 'atomic');
      });
      act(() => {
        result.current.undo();
      });

      expect(Object.keys(result.current.value).sort()).toEqual(
        ['fontSizePx', 'normalizedHeight', 'normalizedWidth', 'normalizedX', 'normalizedY', 'text'].sort(),
      );
    });

    // The stronger form of I7 is a compile error, not a runtime one: `commit`
    // and the hook's `value` parameter are both typed `OverlayLabel`, which
    // has no `backdrop` / `capturedFrame` / `selectedCamera` member at all —
    // there is no value of that shape to construct and pass in that would
    // even compile, so no test here can exercise "the hook was given
    // backdrop state and dropped it". `OverlayEditor.tsx`'s own three pieces
    // of authoring-session state (`backdrop`, `capturedFrame`,
    // `selectedCamera`, `:448-450`) staying in local `useState` — never
    // reaching this hook's `value`/`onChange` — is what T011–T014 must
    // preserve, and `OverlayEditorUndo.test.tsx`'s "the backdrop is not part
    // of the history" scenario is the runtime side of that: it asserts
    // through the editor's own `onChange`, where a backdrop leak would
    // actually be observable.
  });

  describe('I8 — the idle timer is cleaned up on unmount', () => {
    it('an idle run left open at unmount does not throw once its timer would have elapsed', () => {
      vi.useFakeTimers();
      try {
        const { result, unmount } = setup();
        act(() => {
          result.current.commit(buildLabel({ text: 'Ki' }), { run: 'text' });
        });

        unmount();

        expect(() => {
          act(() => {
            vi.advanceTimersByTime(1_000);
          });
        }).not.toThrow();
      } finally {
        vi.useRealTimers();
      }
    });
  });

  describe('endRun (FR-004 / FR-005 — blur, or an idle-run closer with no successor commit)', () => {
    it('endRun closes an open run so a later undo of the next step does not also revert it', () => {
      const { result } = setup();
      act(() => {
        result.current.commit(buildLabel({ text: 'Ki' }), { run: 'text' });
      });

      act(() => {
        result.current.endRun('text');
      });

      act(() => {
        result.current.commit(buildLabel({ text: 'Ki', fontSizePx: 40 }), { run: 'fontSize' });
      });

      act(() => {
        result.current.undo();
      });
      expect(result.current.value).toEqual(buildLabel({ text: 'Ki' }));

      act(() => {
        result.current.undo();
      });
      expect(result.current.value).toEqual(BASE_LABEL);
    });
  });
});
