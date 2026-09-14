import { useEffect, useRef, useState } from 'react';
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

// Sites 4/5 (Decision 2): typing and the font-size slider coalesce by idle.
// Site 6 (arrows) coalesces by keydown->keyup instead — see the module
// comment on `RunKey` and spec.md's "cannot share a rule, and the reason is
// mechanical" paragraph.
function isIdleRun(key: RunKey): boolean {
  return key === 'text' || key === 'fontSize';
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
 * <b>The re-seed detector (I6/FR-011, plan.md §5).</b> Two pieces of state,
 * deliberately not refs — `eslint-plugin-react-hooks`'s `react-hooks/refs`
 * rule forbids reading or writing a ref during render, which the classic
 * ref-based version of this comparison did. `prevValue` notices only that
 * the incoming `value` *prop* changed identity since the previous render. A
 * render triggered by this hook's own `setPast`/`setFuture` — with no
 * feedback from the caller in between, e.g. a test harness whose `onChange`
 * is an inert spy — leaves `value` untouched, and must not be mistaken for
 * an external change just because `lastEmitted` has already moved on inside
 * `commit`. Only once the prop has actually moved does `lastEmitted` decide
 * whether that move was this hook's own echo (a real controlled parent
 * feeding the emitted object straight back, exactly as RHF's `Controller`
 * does) or a re-seed from outside (`OverlayEditorDialog.tsx`'s
 * `reset(defaultValues)`). Never a deep-equality check on `value`, and never
 * keyed on "did the component re-render" — plan.md §5 names both as the
 * wrong implementation.
 * </p>
 * <p>
 * The run token and the idle timer stay in refs — they are read and written
 * only from `commit`/`endRun`/`undo`/`redo` (invoked later, from event
 * handlers, never during render itself) and from the `reseedToken` effect
 * below, which closes them the render *after* a re-seed is detected. A
 * reseed's own render body stays pure: no ref access, no `clearTimeout`, no
 * other side effect — only the conditional `setState` calls React's own
 * "adjust state when a prop changes" pattern sanctions.
 * </p>
 */
export function useOverlayEditHistory(
  value: OverlayLabel,
  onChange: (next: OverlayLabel) => void,
): UseOverlayEditHistoryResult {
  const [past, setPast] = useState<OverlayLabel[]>([]);
  const [future, setFuture] = useState<OverlayLabel[]>([]);
  const [prevValue, setPrevValue] = useState(value);
  const [lastEmitted, setLastEmitted] = useState(value);
  const [reseedToken, setReseedToken] = useState(0);
  const openRunRef = useRef<RunKey | null>(null);
  const idleTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  function clearIdleTimer(): void {
    if (idleTimerRef.current !== null) {
      clearTimeout(idleTimerRef.current);
      idleTimerRef.current = null;
    }
  }

  // I6/FR-011 — see the doc comment above. Pure: only `useState` setters,
  // no ref access and no side effect, so it is safe to run unconditionally
  // in the render body — React coalesces these into the same render pass
  // rather than an extra one.
  if (value !== prevValue) {
    setPrevValue(value);
    if (value !== lastEmitted) {
      setLastEmitted(value);
      setReseedToken((token) => token + 1);
      if (past.length > 0) setPast([]);
      if (future.length > 0) setFuture([]);
    }
  }

  // The ref-based half of a re-seed (close any open run, drop its idle
  // timer) — deferred to an effect because refs cannot be touched during
  // render. `act()`/Testing Library flush effects before control returns to
  // the test, so nothing later observes the one-render gap.
  useEffect(() => {
    openRunRef.current = null;
    if (idleTimerRef.current !== null) {
      clearTimeout(idleTimerRef.current);
      idleTimerRef.current = null;
    }
  }, [reseedToken]);

  function scheduleIdleClose(key: RunKey): void {
    clearIdleTimer();
    idleTimerRef.current = setTimeout(() => {
      idleTimerRef.current = null;
      if (openRunRef.current === key) {
        openRunRef.current = null;
      }
    }, TEXT_IDLE_MS);
  }

  function commit(next: OverlayLabel, boundary: Boundary): void {
    const current = lastEmitted;
    const key = boundary === 'atomic' ? null : boundary.run;

    // I5 — a foreign commit (atomic, or a different run key) closes
    // whatever run is open and starts a new step; I4 — a commit under the
    // run already open absorbs into it instead of pushing again.
    if (key === null || openRunRef.current !== key) {
      clearIdleTimer();
      setPast((prev) => [...prev, current]);
      // I3 — a run's future was already cleared when the run opened, so an
      // absorbed commit (the `else` of this branch) must not clear it a
      // second time; only a commit that opens a new step does.
      setFuture((prev) => (prev.length > 0 ? [] : prev));
      openRunRef.current = key;
    }

    setLastEmitted(next);
    onChange(next);

    if (key !== null && isIdleRun(key)) {
      scheduleIdleClose(key);
    }
  }

  function endRun(key?: RunKey): void {
    if (key !== undefined && openRunRef.current !== key) return;
    clearIdleTimer();
    openRunRef.current = null;
  }

  function undo(): boolean {
    if (past.length === 0) return false;
    clearIdleTimer();
    openRunRef.current = null;
    // `noUncheckedIndexedAccess`: the length guard above already proves this
    // index exists.
    const previous = past[past.length - 1] as OverlayLabel;
    const currentValue = lastEmitted;
    setPast((prev) => prev.slice(0, -1));
    setFuture((prev) => [...prev, currentValue]);
    setLastEmitted(previous);
    onChange(previous);
    return true;
  }

  function redo(): boolean {
    if (future.length === 0) return false;
    clearIdleTimer();
    openRunRef.current = null;
    // `noUncheckedIndexedAccess`: the length guard above already proves this
    // index exists.
    const next = future[future.length - 1] as OverlayLabel;
    const currentValue = lastEmitted;
    setFuture((prev) => prev.slice(0, -1));
    setPast((prev) => [...prev, currentValue]);
    setLastEmitted(next);
    onChange(next);
    return true;
  }

  // I8, mirroring `OverlayEditor.tsx`'s own `announceTimerRef` cleanup.
  useEffect(() => () => clearIdleTimer(), []);

  return {
    commit,
    endRun,
    undo,
    redo,
    canUndo: past.length > 0,
    canRedo: future.length > 0,
  };
}
