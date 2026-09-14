import type { OverlayLabel } from '@smart-sentinel-eye/shared/api/overlays.api';

/**
 * How long a text or font-size run waits, with no further emission from
 * that site, before it closes (Decision 2 / FR-004). Chosen over
 * `DEBOUNCE_MS` (`useDebouncedValue.ts`) deliberately — plan.md §2 "The idle
 * constant" — and restated here rather than imported from
 * `OverlayEditor.tsx`, which would be a circular import (the same shape
 * `OverlayGeometryFields.tsx:34-37` documents already hitting for the axis
 * labels).
 */
export const TEXT_IDLE_MS = 500;

/**
 * The five run-coalescing keys spec 154 (issue #2347) names, spelled exactly
 * as plan.md §2's table does — the spelling *is* the coalescing design: two
 * commits under the same key absorb into one step, a commit under any other
 * key (or an `'atomic'` boundary) closes whatever run is open first.
 */
export type RunKey = 'text' | 'fontSize' | `arrow:${'move' | 'resize'}:${'x' | 'y' | 'width' | 'height'}`;

/**
 * How a `commit` relates to the undo stack (Decision 2 / FR-002–FR-005).
 * `'atomic'` is always its own step (drag, resize, a geometry-field commit).
 * `{ run }` absorbs into whatever run under that key is already open, or
 * opens one.
 */
export type Boundary = 'atomic' | { run: RunKey };

export interface UseOverlayEditHistoryResult {
  /** Records a step per `boundary` (Decision 2), then emits `next` via `onChange`. */
  commit: (next: OverlayLabel, boundary: Boundary) => void;
  /** Closes the open run, if any, named by `key` — or any run, if `key` is omitted (blur). */
  endRun: (key?: RunKey) => void;
  /** Moves one step back, emitting it. Returns whether anything moved (FR-002 "bad request"). */
  undo: () => boolean;
  /** Moves one step forward, emitting it. Returns whether anything moved. */
  redo: () => boolean;
  /** Whether `undo()` would do anything (I1 — the floor is the absence of a predecessor). */
  canUndo: boolean;
  /** Whether `redo()` would do anything. */
  canRedo: boolean;
}

/**
 * Step-wise undo/redo over one overlay-editor session (spec 154, issue
 * #2347). Lives beside `OverlayEditor.tsx`, in `apps/shared`, importing
 * nothing but `react` and the `OverlayLabel` *type* — no `react-redux`, no
 * store — because `OverlayEditorKeyboard.test.tsx:693` pins that
 * `OverlayEditor` renders with no `<Provider>` in the tree (Decision 4,
 * spec 150 FR-018).
 *
 * <p>
 * <b>Phase 4a scaffold (T002).</b> A real, exported signature with a body
 * that does nothing at all — mirrors `OverlayGeometryFields.tsx`'s "renders
 * null" from spec 151 (commit 9063ea98) and `placeholderAdvisories.ts` from
 * spec 148 before it. Without a real signature, `useOverlayEditHistory.test.ts`
 * and `OverlayEditorUndo.test.tsx` could not even resolve their import, which
 * would make every failure `ERR_MODULE_NOT_FOUND` rather than a real
 * assertion naming a missing invariant — the "weak red" ADR-0139/ADR-0144
 * rule out. T011–T014 (`OverlayEditor.tsx`, phase 4b) replace this body with
 * plan.md §2's two snapshot stacks, the open-run token and the
 * reference-identity re-seed detector (I1–I8). This file's exported
 * signature does not change under that work.
 * </p>
 */
export function useOverlayEditHistory(
  value: OverlayLabel,
  onChange: (next: OverlayLabel) => void,
): UseOverlayEditHistoryResult {
  void value;
  void onChange;
  return {
    commit: () => {},
    endRun: () => {},
    undo: () => false,
    redo: () => false,
    canUndo: false,
    canRedo: false,
  };
}
