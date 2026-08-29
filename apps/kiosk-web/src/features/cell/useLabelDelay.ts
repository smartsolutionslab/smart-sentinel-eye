import { useEffect, useRef, useState } from 'react';
import { isWorthDelaying, labelDelayFor } from '@smart-sentinel-eye/shared/observability/labelDelay';

/**
 * Holds an overlay label back so it describes the same moment as the picture
 * beneath it (spec 046 US2, ADR-0129).
 *
 * <p>
 * <b>Not frame accuracy.</b> It makes the label as old as the picture; it does
 * not pair a value with the frame whose instant it belongs to — that needs a
 * clock shared between the camera and the event source, which does not exist
 * here. Calling this frame synchronisation would restate the very overclaim
 * ADR-0129 withdrew.
 * </p>
 *
 * <p>
 * <b>Fails open.</b> An unreadable frame age, an age past the cap, an overlay
 * that has gone away — every one of those shows the label immediately, with no
 * timer and no state write (FR-011, FR-013, FR-014). A mechanism that could
 * hide an operator's value is worse than the mismatch it corrects.
 * </p>
 *
 * @param text the label to show, or undefined when the tile carries no overlay
 * @param frameAgeMilliseconds how old this tile's picture is, or null if unknown
 */
export function useLabelDelay(text: string | undefined, frameAgeMilliseconds: number | null): string | undefined {
  const delay = labelDelayFor(frameAgeMilliseconds);

  // A tile with no label is untouched (FR-013): nothing is held and no timer is
  // scheduled. Removal is not held either — a label outliving the overlay that
  // owns it is stale text an operator would read as live, which is worse than
  // one that vanishes a frame early.
  const holding = text !== undefined && isWorthDelaying(delay);

  const [held, setHeld] = useState<string | undefined>(text);

  // Read inside the effect rather than named as a dependency: the age is a live
  // measurement that jitters every couple of seconds, and restarting the timer
  // on each sample would let a label be held indefinitely and never arrive —
  // the mechanism silently eating the value it exists to align.
  const delayRef = useRef(delay);
  useEffect(() => {
    delayRef.current = delay;
  });

  useEffect(() => {
    const scheduled = delayRef.current;
    if (text === undefined || !isWorthDelaying(scheduled)) {
      return undefined;
    }

    const timer = window.setTimeout(() => setHeld(text), scheduled);

    // A later label always wins, and this cleanup is the whole of why (FR-012):
    // React runs it before re-running the effect, so the superseded timer is
    // cancelled rather than left to fire behind its replacement. A dropped
    // cleanup leaves an operator reading a stale value that looks live.
    return () => window.clearTimeout(timer);
  }, [text]);

  // Derived, not assigned from an effect: a tile with no readable age, one past
  // the cap, or one with no overlay shows its label immediately, with no state
  // write and no cascading render.
  return holding ? held : text;
}
