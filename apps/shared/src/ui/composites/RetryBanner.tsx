import { useCallback, useEffect, useRef, useState } from 'react';
import { LinkButton } from '../primitives/LinkButton.js';
import { FaultNotice } from './FaultNotice.js';

export interface RetryBannerProps {
  message: string;
  /**
   * May return the refetch promise: when the read's outcome never shows in `failed`/`retrying`
   * (a fast failure batches pending and rejected into one render), its settling is the evidence.
   */
  onRetry: () => unknown;
  /** The query's `error !== undefined`. Defaults to `true`. */
  failed?: boolean;
  /** The query's `isFetching`. Defaults to `false`. */
  retrying?: boolean;
  /** Called once when a read started by Retry succeeds. */
  onRecovered?: () => void;
}

// Call sites differ only in the sentence (a failed load vs. stale data
// shown below), so `message` carries the distinction and there is no
// `variant` prop.
//
// The live region is a polite `aria-live` div with no role, not
// `role="status"`: several pages already query an unscoped `status` and
// must keep finding exactly one.
export function RetryBanner({ message, onRetry, failed = true, retrying = false, onRecovered }: RetryBannerProps) {
  const [announcement, setAnnouncement] = useState({ text: '', token: 0 });
  // Bumped only when Retry's own read is refused again, so the first
  // click -> in-flight transition keeps the same node (and Retry its focus).
  const [failureKey, setFailureKey] = useState(0);
  const retryRef = useRef<HTMLButtonElement>(null);
  const wasRetryingRef = useRef(retrying);
  const wasFailedRef = useRef(failed);
  const isFirstFailureKeyRenderRef = useRef(true);
  // Per-FETCH, never latched: an unrequested read (refetch on focus, a
  // promote refetch) must not be mistaken for the click.
  const activatedRef = useRef(false);
  const currentFetchStartedByRetryRef = useRef(false);
  const liveRegionRef = useRef<HTMLDivElement>(null);
  const [settledCount, setSettledCount] = useState(0);
  const onRecoveredRef = useRef(onRecovered);
  const settledRefusedRef = useRef(false);
  useEffect(() => {
    onRecoveredRef.current = onRecovered;
  });

  // Never pull focus out of something the operator opened while the read was in flight.
  const focusIsFree = useCallback(() => {
    const active = document.activeElement;
    return (
      active === null || active === document.body || active === retryRef.current || active === liveRegionRef.current
    );
  }, []);
  const recover = useCallback(() => {
    if (focusIsFree()) onRecoveredRef.current?.();
  }, [focusIsFree]);

  useEffect(() => {
    const wasRetrying = wasRetryingRef.current;
    wasRetryingRef.current = retrying;
    const wasFailed = wasFailedRef.current;
    wasFailedRef.current = failed;

    if (!wasRetrying && retrying) {
      currentFetchStartedByRetryRef.current = activatedRef.current;
      activatedRef.current = false;
      if (currentFetchStartedByRetryRef.current) {
        setAnnouncement((previous) => ({ text: 'Retrying…', token: previous.token + 1 }));
      }
      return;
    }

    // React can batch the whole pending -> fulfilled round trip into one render
    // (a fast response), so the click is then the only evidence the read was ours.
    if (!wasRetrying && !retrying && activatedRef.current && wasFailed && !failed) {
      activatedRef.current = false;
      setAnnouncement((previous) => ({ text: 'Loaded.', token: previous.token + 1 }));
      recover();
      return;
    }

    if (!wasRetrying || retrying) return;

    const startedByRetry = currentFetchStartedByRetryRef.current;
    currentFetchStartedByRetryRef.current = false;
    if (!startedByRetry) return;

    if (failed) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- reacting to an external query's fetch lifecycle
      setAnnouncement((previous) => ({ text: '', token: previous.token + 1 }));
      setFailureKey((previous) => previous + 1);
    } else {
      setAnnouncement((previous) => ({ text: 'Loaded.', token: previous.token + 1 }));
      recover();
    }
  }, [retrying, failed, recover]);

  // The click's own promise settled with a refusal and no effect pass has consumed the click:
  // pending and rejected shared one render, so props never showed the retry. The settled value
  // (an RTK Query result) is the evidence; props can still lag it, so only a refusal is read here.
  useEffect(() => {
    if (settledCount === 0 || !settledRefusedRef.current || !activatedRef.current) return;
    activatedRef.current = false;
    setAnnouncement((previous) => ({ text: '', token: previous.token + 1 }));
    setFailureKey((previous) => previous + 1);
  }, [settledCount]);

  useEffect(() => {
    if (isFirstFailureKeyRenderRef.current) {
      isFirstFailureKeyRenderRef.current = false;
      return;
    }
    if (focusIsFree()) retryRef.current?.focus();
  }, [failureKey, focusIsFree]);

  const activate = () => {
    if (retrying) return;
    activatedRef.current = true;
    const outcome = onRetry();
    if (isThenable(outcome)) {
      const settled = (value: unknown) => {
        settledRefusedRef.current = isRefusal(value);
        setSettledCount((previous) => previous + 1);
      };
      outcome.then(settled, () => settled({ isError: true }));
    }
  };

  return (
    <>
      {failed && (
        <FaultNotice key={failureKey}>
          {message}{' '}
          <LinkButton
            ref={retryRef}
            aria-disabled={retrying}
            className="aria-disabled:cursor-progress"
            onClick={activate}
          >
            Retry
          </LinkButton>
        </FaultNotice>
      )}
      <div ref={liveRegionRef} aria-live="polite" className="sr-only" data-testid="retry-banner-live-region">
        <span key={announcement.token}>{announcement.text}</span>
      </div>
    </>
  );
}

function isThenable(value: unknown): value is PromiseLike<unknown> {
  return typeof value === 'object' && value !== null && typeof (value as PromiseLike<unknown>).then === 'function';
}

function isRefusal(value: unknown): boolean {
  return typeof value === 'object' && value !== null && (value as { isError?: unknown }).isError === true;
}
