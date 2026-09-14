import { useCallback, useEffect, useRef, useState } from 'react';
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
 * Field-by-field comparison of `OverlayLabel`'s six flat fields — not a
 * general deep-equal utility, because there is nothing general to compare:
 * this shape is the whole domain (Decision 3 / I7). Shared by `isEcho`
 * below and by `commit`'s own phantom-step guard (its doc comment) — the
 * two are different questions asked with the same comparison: "is this
 * incoming prop my own echo" versus "did this commit actually change
 * anything."
 *
 * <p>
 * <b>Phase 6 review finding.</b> Plan.md §5 asserted "RHF's `Controller`
 * hands `field.value` straight from form state without cloning on render" —
 * false for the installed `react-hook-form@7.86.0`. `useController` (which
 * `Controller` calls internally) sources `field.value` through `useWatch`,
 * and `useWatch` re-derives its return value via `generateWatchOutput` on
 * *every* form-state notification: deep-equal to what this hook last
 * emitted, but never the same object. A reference-only re-seed detector
 * reads every one of those echoes as an external re-seed and clears both
 * stacks the render after every single edit — confirmed against the real
 * `Controller`/`useWatch` tree in `OverlayEditorReseedRegression.test.tsx`.
 * `isEcho` below is the fix: `value` is this hook's own echo when it is
 * reference-*or*-structurally equal to `lastEmitted`.
 * </p>
 */
function sameOverlayLabel(a: OverlayLabel, b: OverlayLabel): boolean {
  return (
    a.text === b.text &&
    a.normalizedX === b.normalizedX &&
    a.normalizedY === b.normalizedY &&
    a.normalizedWidth === b.normalizedWidth &&
    a.normalizedHeight === b.normalizedHeight &&
    a.fontSizePx === b.fontSizePx
  );
}

/**
 * I6/FR-011's echo test. Compared against `lastEmitted` **only** — never
 * against `prevValue`, and never general deep equality: a genuine external
 * re-seed to genuinely different content must still clear both stacks, and
 * the six-field domain here makes a general deep-equal utility unwarranted.
 *
 * <p>
 * One accepted miss, deliberately: an external `reset()` to a label that
 * happens to be structurally identical to what this hook last emitted goes
 * undetected as a re-seed — the history is kept instead of cleared. Benign
 * — the visible content is the same either way, and the alternative (deep
 * equality alone, with no reference short-circuit) is exactly plan.md §5's
 * *other* named-wrong implementation: it would also misread an undo that
 * lands back on a value the operator visited before as a re-seed.
 * </p>
 */
function isEcho(value: OverlayLabel, lastEmitted: OverlayLabel): boolean {
  return value === lastEmitted || sameOverlayLabel(value, lastEmitted);
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
 * `commit`. Only once the prop has actually moved does `isEcho` decide
 * whether that move was this hook's own echo (a real controlled parent
 * feeding back the emitted object or a structurally-identical clone of it —
 * RHF's `Controller`, see `isEcho`'s own doc comment) or a re-seed from
 * outside (`OverlayEditorDialog.tsx`'s `reset(defaultValues)`, to genuinely
 * different content). Never keyed on "did the component re-render" —
 * plan.md §5 names that as the wrong implementation too.
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
  // Phase 5 review finding — the floor a step/run *might* need to push, held
  // back until something in it actually diverges. See `commit`'s own doc
  // comment for why this is a separate ref rather than a plain `if` guarding
  // the push directly.
  const pendingFloorRef = useRef<OverlayLabel | null>(null);

  // Stable across every render — touches only refs, no reactive dependency —
  // so functions that close over it (below) are stable too, unless one of
  // *their own* other dependencies changes.
  const clearIdleTimer = useCallback(() => {
    if (idleTimerRef.current !== null) {
      clearTimeout(idleTimerRef.current);
      idleTimerRef.current = null;
    }
  }, []);

  // I6/FR-011 — see the doc comment above. Pure: only `useState` setters,
  // no ref access and no side effect, so it is safe to run unconditionally
  // in the render body — React coalesces these into the same render pass
  // rather than an extra one.
  if (value !== prevValue) {
    setPrevValue(value);
    if (!isEcho(value, lastEmitted)) {
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

  const scheduleIdleClose = useCallback(
    (key: RunKey) => {
      clearIdleTimer();
      idleTimerRef.current = setTimeout(() => {
        idleTimerRef.current = null;
        if (openRunRef.current === key) {
          openRunRef.current = null;
        }
      }, TEXT_IDLE_MS);
    },
    [clearIdleTimer],
  );

  // Phase 6 review finding — stated as an invariant, not just left implicit:
  // `current` reads `lastEmitted` from render scope (state, not a ref — see
  // the hook's own doc comment on why), so two `commit` calls inside one
  // synchronous handler would both read the *same* pre-batch snapshot and
  // push it onto `past` twice. No reachable call site does that today —
  // every one of the six emission sites is a discrete React event
  // (`onChange`, `onDragStop`, a keydown), each already flushed by React
  // before the next fires. A future site that emits twice in one handler
  // would break this silently; if you are adding one, read this first.
  //
  // <p>
  // <b>Phase 5 review finding — the phantom-step guard.</b> `react-rnd`/
  // `react-draggable` fires `onDragStop`/`onResizeStop` even for a plain
  // click or handle-release with zero net movement, reporting the label's
  // own, unchanged position — site 1/2 "atomic: one emission, one step"
  // (Decision 2) never said that emission has to carry a *changed* value.
  // Without this guard, that click-to-focus (the exact flow
  // `e2e/overlays.spec.ts`'s undo test uses before `Ctrl+Z`) pushes a
  // content-identical phantom step on top of whatever real step preceded
  // it, and a single `Ctrl+Z` undoes the phantom invisibly instead of the
  // real edit.
  // </p>
  // <p>
  // `pendingFloorRef` — not a plain `if (!sameOverlayLabel(next, current))
  // return` guarding the push directly — because a *run's* first commit can
  // itself be a content-identical no-op (retyping the exact same character
  // over a selection) while a **later** commit in the same run genuinely
  // changes the value; the run's floor (`current` as of when it opened)
  // must still be reachable then, not lost because the opening commit alone
  // looked like nothing happened. So a step/run's floor is recorded as
  // *pending* when it opens, and only pushed onto `past` the first time some
  // commit's value actually diverges from it — which, for an atomic
  // commit, is this same call (there is no later one to defer to). Once
  // pushed, `pendingFloorRef` is cleared to `null`, so a later commit that
  // still matches it (I3/I4's ordinary absorption) correctly pushes
  // nothing. The run itself is never broken or closed by a held-back
  // push — `openRunRef`/the idle timer are set unconditionally below,
  // exactly as before.
  // </p>
  const commit = useCallback(
    (next: OverlayLabel, boundary: Boundary) => {
      const current = lastEmitted;
      const key = boundary === 'atomic' ? null : boundary.run;

      // I5 — a foreign commit (atomic, or a different run key) closes
      // whatever run is open and starts a new step; I4 — a commit under the
      // run already open absorbs into it instead of pushing again.
      if (key === null || openRunRef.current !== key) {
        clearIdleTimer();
        openRunRef.current = key;
        pendingFloorRef.current = current;
      }

      // Pushes the still-pending floor the moment something in this
      // step/run actually diverges from it (see the doc comment above).
      if (pendingFloorRef.current !== null && !sameOverlayLabel(next, pendingFloorRef.current)) {
        const floor = pendingFloorRef.current;
        setPast((prev) => [...prev, floor]);
        // I3 — a run's future was already cleared when its floor was
        // pushed, so a later divergence within the same run (floor already
        // null by then) must not clear it a second time.
        setFuture((prev) => (prev.length > 0 ? [] : prev));
        pendingFloorRef.current = null;
      }

      setLastEmitted(next);
      onChange(next);

      if (key !== null && isIdleRun(key)) {
        scheduleIdleClose(key);
      }
    },
    [lastEmitted, onChange, clearIdleTimer, scheduleIdleClose],
  );

  const endRun = useCallback(
    (key?: RunKey) => {
      if (key !== undefined && openRunRef.current !== key) return;
      clearIdleTimer();
      openRunRef.current = null;
    },
    [clearIdleTimer],
  );

  const undo = useCallback((): boolean => {
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
  }, [past, lastEmitted, onChange, clearIdleTimer]);

  const redo = useCallback((): boolean => {
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
  }, [future, lastEmitted, onChange, clearIdleTimer]);

  // I8, mirroring `OverlayEditor.tsx`'s own `announceTimerRef` cleanup.
  useEffect(() => () => clearIdleTimer(), [clearIdleTimer]);

  return {
    commit,
    endRun,
    undo,
    redo,
    canUndo: past.length > 0,
    canRedo: future.length > 0,
  };
}
