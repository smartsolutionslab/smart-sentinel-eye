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
 * @param onHeld called with the hold that was actually achieved, once per hold,
 *   whether it ran its course or was released early. Never called for a label
 *   that was not held.
 */
export function useLabelDelay(
  text: string | undefined,
  frameAgeMilliseconds: number | null,
  onHeld?: (achievedMilliseconds: number) => void,
): string | undefined {
  const delay = labelDelayFor(frameAgeMilliseconds);

  // A tile with no label is untouched (FR-013): nothing is held and no timer is
  // scheduled. Removal is not held either — a label outliving the overlay that
  // owns it is stale text an operator would read as live, which is worse than
  // one that vanishes a frame early.
  const holding = text !== undefined && isWorthDelaying(delay);

  const [shown, setShown] = useState<string | undefined>(text);

  // **Kept in step with `text` on every render that is not holding**, using
  // React's documented pattern for adjusting state when a prop changes.
  //
  // Without this, `shown` was written only by the timer, so it still carried
  // whatever it was seeded with at mount whenever a hold *began* without an
  // intervening text change. Two ways that happens, and the first is the normal
  // path: a tile mounts before its overlay query resolves, the label arrives
  // while no lag has been sampled yet, and the tile's first measurement a
  // couple of seconds later starts a hold on the `undefined` from mount — the
  // label vanishes and never returns. The second is a label that changed while
  // the age was briefly unreadable, then reverted to its predecessor the moment
  // the age came back. Found in review.
  const [adjustedFor, setAdjustedFor] = useState<string | undefined>(text);
  if (text !== adjustedFor) {
    setAdjustedFor(text);
    if (!holding) {
      setShown(text);
    }
  }

  const delayRef = useRef(delay);
  const reportRef = useRef(onHeld);
  useEffect(() => {
    // Read inside the effect rather than named as a dependency: the age is a
    // live measurement that jitters every couple of seconds, and restarting the
    // timer on each sample would let a label be held indefinitely and never
    // arrive — the mechanism silently eating the value it exists to align.
    delayRef.current = delay;
    reportRef.current = onHeld;
  });

  // The hold in flight, so it can be settled from either end: the timer that
  // completes it, or the render that releases it early.
  const pending = useRef<{ timer: number; startedAt: number } | null>(null);

  useEffect(() => {
    const scheduled = delayRef.current;
    if (text === undefined || !isWorthDelaying(scheduled)) {
      return undefined;
    }

    // `performance.now()`, never `Date.now()`: fab clocks are PTP-stepped and an
    // epoch comparison can measure the step instead of the hold. CellPage
    // already carries that reasoning for its highlight timers.
    const startedAt = performance.now();

    const timer = window.setTimeout(() => {
      pending.current = null;
      setShown(text);
      // **The achieved hold, not the one that was asked for** (FR-015). A timer
      // fires late under load, and reporting `scheduled` would make the metric
      // agree with itself no matter what the browser actually did — a dashboard
      // that cannot show the mechanism failing.
      reportRef.current?.(performance.now() - startedAt);
    }, scheduled);

    pending.current = { timer, startedAt };

    // A later label always wins, and this cleanup is the whole of why (FR-012):
    // React runs it before re-running the effect, so the superseded timer is
    // cancelled rather than left to fire behind its replacement. A dropped
    // cleanup leaves an operator reading a stale value that looks live.
    return () => {
      window.clearTimeout(timer);
      if (pending.current?.timer === timer) {
        pending.current = null;
      }
    };
  }, [text]);

  // A hold released by the derivation above — the tile stopped reporting an age,
  // or its age jumped past the cap, both of which a live jittering measurement
  // does — is settled here rather than left running. The label is already shown;
  // what remains is to cancel the timer and report **what was actually held**.
  // Left alone, that timer still fired and reported its full scheduled delay, so
  // a tile whose statistics flicker inflated the histogram on every update.
  useEffect(() => {
    if (holding || pending.current === null) {
      return;
    }

    const { timer, startedAt } = pending.current;
    pending.current = null;
    window.clearTimeout(timer);
    reportRef.current?.(performance.now() - startedAt);
  }, [holding]);

  return holding ? shown : text;
}
