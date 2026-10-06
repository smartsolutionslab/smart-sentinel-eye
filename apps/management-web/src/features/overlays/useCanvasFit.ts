import { useEffect, useState, type RefObject } from 'react';

export interface CanvasFit {
  widthPx: number;
  heightPx: number;
}

/**
 * Spec 305 (#2350) FR-006. Measures `ref`'s content-box width with a
 * `ResizeObserver` and derives a 16:9 height from it — the canvas uses the
 * page's width instead of the dialog-imposed 800x450 default.
 *
 * `undefined` when `ResizeObserver` is unavailable (jsdom has none by
 * default) or the measured width is not yet positive (a first frame before
 * layout, or a detached ref) — the caller then passes nothing to
 * `OverlayEditor`'s own `canvasWidthPx`/`canvasHeightPx` props, which fall
 * back to its existing 800x450 default (`OverlayEditor.tsx`). This hook
 * never duplicates that fallback value itself, so the two cannot drift.
 */
export function useCanvasFit(ref: RefObject<HTMLElement | null>): CanvasFit | undefined {
  const [fit, setFit] = useState<CanvasFit | undefined>(undefined);

  useEffect(() => {
    const element = ref.current;
    // `globalThis.ResizeObserver`, not the bare identifier: this app's
    // eslint config carries no DOM-constructor globals (the same reasoning
    // `OverlayDraftForm.tsx`'s own `ComponentRef<'button'>` comment gives),
    // and widening it is the gate-weakening ADR-0144 rules out.
    const ResizeObserverCtor = globalThis.ResizeObserver;
    if (element === null || ResizeObserverCtor === undefined) {
      setFit(undefined);
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
  }, [ref]);

  return fit;
}
