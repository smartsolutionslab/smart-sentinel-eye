import { type ComponentRef, useCallback, useRef } from 'react';

// `focusHeading` is stable and reads the ref when called, so it is safe as `RetryBanner`'s
// `onRecovered` (which keeps the latest callback in a ref anyway).
export function useHeadingFocus() {
  const headingRef = useRef<ComponentRef<'h1'>>(null);
  const focusHeading = useCallback(() => headingRef.current?.focus(), []);
  return { headingRef, focusHeading };
}
