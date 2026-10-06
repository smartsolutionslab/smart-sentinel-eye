import { useEffect, useState, type RefCallback } from 'react';

export interface CanvasFit {
  widthPx: number;
  heightPx: number;
}

export interface UseCanvasFitResult {
  fit: CanvasFit | undefined;
  /** Attach to the element to measure — `<section ref={ref}>`. */
  ref: RefCallback<HTMLElement>;
}

/**
 * Spec 305 (#2350) FR-006. Measures the attached element's content-box width
 * with a `ResizeObserver` and derives a 16:9 height from it — the canvas
 * uses the page's width instead of the dialog-imposed 800x450 default.
 *
 * `undefined` when `ResizeObserver` is unavailable (jsdom has none by
 * default) or the measured width is not yet positive (a first frame before
 * layout, or a detached element) — the caller then passes nothing to
 * `OverlayEditor`'s own `canvasWidthPx`/`canvasHeightPx` props, which fall
 * back to its existing 800x450 default (`OverlayEditor.tsx`). This hook
 * never duplicates that fallback value itself, so the two cannot drift.
 *
 * A callback ref held in state, not a plain `RefObject` the caller owns
 * (phase-6 review nit N1): the hook's own effect depends on `[element]`, so
 * it re-runs — disconnecting the old observer and attaching a new one —
 * whenever the attached DOM node actually changes, including when a
 * different element gets attached to the same JSX `ref` prop across two
 * renders (e.g. the host page swaps from a notice branch back to the
 * editor, or Back/Forward lands on a different element entirely). A plain
 * `RefObject` with `useEffect(..., [ref])` would not re-run then — `ref`
 * itself is a stable object identity for the component's whole lifetime —
 * so a stale observer would keep watching a node that already unmounted.
 */
export function useCanvasFit(): UseCanvasFitResult {
  const [element, setElement] = useState<HTMLElement | null>(null);
  const [fit, setFit] = useState<CanvasFit | undefined>(undefined);

  useEffect(() => {
    // `globalThis.ResizeObserver`, not the bare identifier: this app's
    // eslint config carries no DOM-constructor globals (the same reasoning
    // `OverlayDraftForm.tsx`'s own `ComponentRef<'button'>` comment gives),
    // and widening it is the gate-weakening ADR-0144 rules out.
    const ResizeObserverCtor = globalThis.ResizeObserver;
    // No `setFit(undefined)` here (unlike this hook's pre-N1 shape): `fit`
    // already starts `undefined`, and this effect's dependency is now
    // `[element]` rather than a stable ref object, so unconditionally
    // resetting state here on every dependency change is exactly the
    // "setState synchronously in an effect body" pattern
    // `react-hooks/set-state-in-effect` polices — `setFit` is only ever
    // called from inside the observer's own callback below, the subscribed
    // external-system update the rule asks for.
    if (element === null || ResizeObserverCtor === undefined) {
      return;
    }

    const observer = new ResizeObserverCtor((entries) => {
      const entry = entries[0];
      if (entry === undefined) return;
      const widthPx = Math.floor(entry.contentRect.width);
      if (widthPx <= 0) {
        setFit(undefined);
        return;
      }
      setFit({ widthPx, heightPx: Math.round((widthPx * 9) / 16) });
    });
    observer.observe(element);
    return () => observer.disconnect();
  }, [element]);

  return { fit, ref: setElement };
}
