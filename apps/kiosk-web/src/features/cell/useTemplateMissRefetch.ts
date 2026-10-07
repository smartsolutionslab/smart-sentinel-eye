import { useEffect, useRef, useState } from 'react';
import { logResilienceEvent } from '@smart-sentinel-eye/shared/observability/resilienceLog';

/** 1 s / 2 s / 4 s (spec 301, #2720 US2, FR-007). */
const BACKOFF_MS: readonly number[] = [1_000, 2_000, 4_000];

export interface UseTemplateMissRefetchOptions {
  /** The overlay this miss belongs to, for the resilience log line. `null` while unbound. */
  overlayIdentifier: string | null;
  /**
   * Identifies the publish this miss episode belongs to (e.g. the published
   * revision's own number) — a new value starts a fresh 1 s/2 s/4 s cycle
   * and abandons whatever the previous value's cycle had pending.
   * `undefined` while not yet known, which — like `skip` — holds off
   * scheduling anything.
   */
  publicationKey: string | undefined;
  /** A placeholder `Text` element's own template has no entry in the snapshot. */
  hasMiss: boolean;
  /** Mirrors the snapshot query's own `skip` — never refetch a skipped query. */
  skip: boolean;
  /**
   * `useGetOverlaySnapshotQuery`'s `refetch`. Stable identity not required.
   * Review S1 (#2720): the final attempt's return value is awaited, if
   * thenable, before that attempt counts as exhausted — `unknown` rather
   * than `void` so a real RTK Query `refetch` (whose promise resolves only
   * once its answer has actually landed) can be told apart from merely
   * having been *called*.
   */
  refetch: () => unknown;
}

/**
 * Bounded retry for a placeholder `Text` element whose own raw template is
 * missing from the kiosk's resolved-text snapshot — a genuinely new
 * template SystemVariables has not indexed yet (spec 301, #2720 US2,
 * FR-007). Refetches at 1 s / 2 s / 4 s while the miss persists, stops the
 * moment it clears, and — once the third attempt is spent and the miss is
 * still outstanding — logs `resolved-text-template-miss` exactly once for
 * this episode, then stops.
 *
 * <p>
 * An "episode" is one `(overlayIdentifier, publicationKey)` pair. A new
 * `publicationKey` — a new publish — abandons whatever the previous
 * episode's backoff had pending and starts its own cycle from 1 s, timed
 * from when IT started, never from the old episode's clock.
 * </p>
 */
export function useTemplateMissRefetch({
  overlayIdentifier,
  publicationKey,
  hasMiss,
  skip,
  refetch,
}: UseTemplateMissRefetchOptions): void {
  // Read at the moment a timer fires, never captured as an effect
  // dependency — a fresh `refetch` reference every render must not restart
  // the backoff (mirrors `LayoutGrid.tsx`'s own "keep the latest in a ref"
  // idiom for `accessTokenRef`, which says why this is deliberate too).
  const latestRefetchRef = useRef(refetch);
  // eslint-disable-next-line react-hooks/refs -- see above
  latestRefetchRef.current = refetch;

  // Review S1 (#2720): which episode's final backoff attempt has both fired
  // AND received its own answer. Set only once that answer is in hand —
  // never synchronously when the final timer fires — because the fourth
  // (final) `refetch()` call only *sends* the request; the response that
  // could still clear the miss arrives later. By the time the awaited
  // promise settles, the caller's own re-render (driven by the resulting
  // cache update) has already delivered whatever fresh `hasMiss` that
  // answer implies, so the logging effect below reads a settled value,
  // never a stale one. `null` means no episode is currently exhausted.
  const [exhaustedEpisode, setExhaustedEpisode] = useState<string | null>(null);

  useEffect(() => {
    if (skip || !hasMiss || overlayIdentifier === null || publicationKey === undefined) {
      return;
    }

    const episodeKey = `${overlayIdentifier}:${publicationKey}`;
    let attempt = 0;
    let timer: ReturnType<typeof setTimeout> | undefined;
    let cancelled = false;

    const scheduleNext = (): void => {
      if (attempt >= BACKOFF_MS.length) {
        return;
      }
      const delay = BACKOFF_MS[attempt]!;
      const isFinalAttempt = attempt === BACKOFF_MS.length - 1;
      attempt += 1;
      timer = setTimeout(() => {
        const pending = latestRefetchRef.current();
        if (isFinalAttempt) {
          // Wait for the final attempt's own answer — not merely for it to
          // have been sent — before this episode counts as exhausted
          // (review S1). An errored refetch still leaves the miss
          // outstanding, so it falls through to exhaustion the same as a
          // settled-but-still-missing one.
          void Promise.resolve(pending)
            .catch(() => undefined)
            .then(() => {
              if (!cancelled) {
                setExhaustedEpisode(episodeKey);
              }
            });
        } else {
          scheduleNext();
        }
      }, delay);
    };

    scheduleNext();

    return () => {
      cancelled = true;
      if (timer !== undefined) {
        clearTimeout(timer);
      }
    };
  }, [skip, hasMiss, overlayIdentifier, publicationKey]);

  // Review S1: logging lives in its own effect, gated on `hasMiss` as read
  // on THIS render — never inside the timer callback above — so a caller
  // that re-renders with `hasMiss: false` (the final attempt's answer
  // clearing the miss) before this effect observes the exhausted episode
  // suppresses the log entirely, rather than racing it.
  const loggedEpisodeRef = useRef<string | null>(null);

  useEffect(() => {
    if (overlayIdentifier === null || publicationKey === undefined) {
      return;
    }
    const episodeKey = `${overlayIdentifier}:${publicationKey}`;
    if (exhaustedEpisode !== episodeKey || !hasMiss || loggedEpisodeRef.current === episodeKey) {
      return;
    }
    loggedEpisodeRef.current = episodeKey;
    logResilienceEvent('hub', 'resolved-text-template-miss', { overlay: overlayIdentifier });
  }, [exhaustedEpisode, hasMiss, overlayIdentifier, publicationKey]);
}
