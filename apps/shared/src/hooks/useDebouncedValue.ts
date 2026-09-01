import { useEffect, useState } from 'react';

/**
 * How long a value waits before it settles.
 *
 * <p>
 * Long enough to swallow the gaps within a typed word, short enough that a
 * deliberate pause reads as instant. Every consumer uses this rather than
 * choosing its own, so two search boxes in one app cannot feel different.
 * </p>
 */
export const DEBOUNCE_MS = 250;

/**
 * The value once it has stopped changing.
 *
 * <p>
 * <b>For keying a query off something an operator types.</b> A search box drives
 * a request per keystroke otherwise, and where the request is a multi-page walk
 * that is one round trip per page per letter — typing <c>furnace</c> into the
 * layout editor's picker issued up to thirty-five.
 * </p>
 *
 * <p>
 * It is not only a cost argument. A polite live region tied to an
 * every-keystroke query re-announces itself on every letter, which makes it
 * chatter for precisely the screen-reader user it was added for; settling the
 * value first gives one announcement per search.
 * </p>
 *
 * <p>
 * The returned value lags the argument, so the caller must not use it for the
 * input's own <c>value</c> — the field would drop characters. Drive the input
 * from the raw state and the query from this.
 * </p>
 */
export function useDebouncedValue<T>(value: T, delayMs: number = DEBOUNCE_MS): T {
  const [settled, setSettled] = useState(value);

  useEffect(() => {
    // Already settled: no timer, so a re-render caused by settling does not
    // start another one.
    if (Object.is(settled, value)) return undefined;

    const timer = setTimeout(() => setSettled(value), delayMs);

    return () => clearTimeout(timer);
  }, [value, delayMs, settled]);

  return settled;
}
