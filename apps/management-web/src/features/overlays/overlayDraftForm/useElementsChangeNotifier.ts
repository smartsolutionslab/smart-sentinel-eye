import type { OverlayElement } from '@smart-sentinel-eye/shared/api/overlays.api';
import { useEffect, useRef } from 'react';

/** Spec 466. Tells a host the live element set after every edit, through the newest callback it passed. */
export function useElementsChangeNotifier(
  watchedElements: readonly unknown[] | undefined,
  onElementsChange: ((elements: OverlayElement[]) => void) | undefined,
): void {
  const onElementsChangeRef = useRef(onElementsChange);
  // eslint-disable-next-line react-hooks/refs -- the newest callback, read only inside the effect below
  onElementsChangeRef.current = onElementsChange;
  useEffect(() => {
    if (watchedElements !== undefined) onElementsChangeRef.current?.(watchedElements as OverlayElement[]);
  }, [watchedElements]);
}
