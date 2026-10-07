import { useEffect, useRef } from 'react';
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
  /** `useGetOverlaySnapshotQuery`'s `refetch`. Stable identity not required. */
  refetch: () => void;
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

  useEffect(() => {
    if (skip || !hasMiss || overlayIdentifier === null || publicationKey === undefined) {
      return;
    }

    let attempt = 0;
    let timer: ReturnType<typeof setTimeout> | undefined;

    const scheduleNext = (): void => {
      if (attempt >= BACKOFF_MS.length) {
        logResilienceEvent('hub', 'resolved-text-template-miss', { overlay: overlayIdentifier });
        return;
      }
      const delay = BACKOFF_MS[attempt]!;
      attempt += 1;
      timer = setTimeout(() => {
        latestRefetchRef.current();
        scheduleNext();
      }, delay);
    };

    scheduleNext();

    return () => {
      if (timer !== undefined) {
        clearTimeout(timer);
      }
    };
  }, [skip, hasMiss, overlayIdentifier, publicationKey]);
}
